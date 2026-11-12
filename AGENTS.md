# AGENTS.md

MediPass+ — DEPI graduation project. ASP.NET Core 10 + EF Core + SQL Server API with an Angular 22 frontend.
Privacy-first medical history: a doctor can read a patient's record only while a visit-scoped access grant is active.
This file is the shared entry point for every developer and every AI agent. Read it before you change anything.

## 1. Read these first

| What | Where | Why |
|---|---|---|
| ADR index | `docs/architecture/decisions/README.md` | Binding decisions per area; read the one that governs what you touch |
| Requirements | `Documentations/MediPass_Requirements_Gathering.docx` | FR/NFR/US/UC IDs and the grant rules |
| Planning | `Documentations/MediPass+_Project_Planning_and_Management.docx` | Scope, sprints, module owners, risks |
| Process | `CONTRIBUTING.md`, `.github/pull_request_template.md`, `.github/workflows/ci.yml` | Branch, commit, review, CI |
| Design | `Diagrams/*.png` | Class, sequence and component diagrams (D4) |

Two kinds of requirement documents live in `Documentations/`:

- The `.docx` files are the submitted deliverables and are tracked in Git. **They are the source of truth.**
- `MediPass_Requirements_Gathering.md` and `MediPass+_Project_Planning_and_Management.md` are a local text copy of each
  document for reading and grepping. They are gitignored, may be missing from your clone, and never count as the
  source of truth in a PR. If a copy and the `.docx` disagree, the `.docx` wins and the copy is wrong.

This file, `AGENTS.md`, is tracked and shared with the team. `scripts/create-solution.ps1` is the one-time bootstrap: it
is gitignored, it exists only on some machines, and it must not be re-run — it would create a stale `.sln` and overwrite
the projects.

## 2. Repository status

This is an early implementation, not a product. The Accounts slice has `ApplicationUser : IdentityUser<Guid>` with
register, login and password-reset endpoints backed by JWT (ACC-01); the Booking slice has `Clinic`, `Doctor`,
`DoctorSlot` and `Appointment` (CLB-01); B1 exposes read-only clinic search, clinic-doctor search and doctor-slot
listing endpoints. MP-27 adds the patient booking slice: `POST api/appointments` books a free future slot for the
authenticated patient, `POST api/appointments/{appointmentId}/cancel` cancels a booked appointment, and
`POST api/appointments/{appointmentId}/reschedule` moves it to another free slot of the same doctor. Every refusal is
the shared `ProblemDetails` envelope (`400` invalid state, `404` out of the patient's scope, `409` slot/state
conflict), and a filtered unique index (`IX_Appointments_DoctorSlotId_Active`) plus slot and appointment concurrency
tokens make a concurrent double booking impossible. Clinic and doctor searches default to 20 results per page,
accept at most 50, and return 400 for
invalid page or page-size values. MP-1301 added `AccessRequest`, `AccessGrant` and `VisitAccessCredential`.
A read-only visit-access scope service now verifies that the acting doctor or patient matches the appointment, and
`POST api/appointments/{appointmentId}/start-visit` lets a doctor start a today, checked-in appointment, reuse its
pending request or create a new one, and receive a fresh two-minute QR credential whose SHA-256 hash is the only
stored form. A filtered unique index allows at most one pending request per appointment, so a concurrent start
returns `409`. A newly created request is audited with `AccessRequestCreated` in the same unit of work (ADR-0004,
ADR-0011 decision 8). `POST api/access-requests/{requestId}/approve` and `/reject` let the authenticated owner of the
appointment answer a pending request: approval creates the visit's grant (two-hour cap) and its audit row, rejection
creates no grant, and both answer the request and change its credentials in one unit of work. `AccessRequest.Status`
is an EF concurrency token, so only the first decision wins and the other returns `409`. Both decisions also share the
named `QrApprove` fixed-window rate limit (default 5 attempts / 15 minutes per patient, configurable and validated at
startup); a rejection is `429` with `Retry-After` (ADR-0007). MP-34 adds the appointment status flow: the domain
`Appointment` owns the only allowed transitions (`Booked -> CheckedIn -> InVisit -> Completed`, plus
`Booked -> Cancelled`) and records each state's UTC timestamp, refusing every skipped or backward transition.
`POST api/appointments/{appointmentId}/check-in` lets staff in the appointment's clinic check in a booked appointment
for today, `POST api/appointments/{appointmentId}/complete` lets the owning doctor complete an in-visit appointment,
and `start-visit` now moves `CheckedIn -> InVisit` before it issues the QR (re-issuing while `InVisit` is allowed
until the patient approves; once a grant exists, a restart returns `409` instead of a QR that could never be
approved). MP-36 adds the six-digit fallback (FR-ACS-05, ADR-0002): `POST
api/appointments/{appointmentId}/access-code` lets the authenticated patient generate a code for today's
checked-in or in-visit appointment with a pending request, and `POST
api/appointments/{appointmentId}/verify-code` lets the owning doctor verify it. The code lives five minutes, is
stored only as HMAC-SHA-256 under the separate `AccessCode:HmacKey` secret, is consumed on success, and locks after
five wrong entries. A successful verification records the patient's approval, creates the two-hour grant, moves the
appointment to `InVisit`, revokes the QR, and writes `AccessGrantCreated` in one unit of work (ADR-0011 amendment
2026-11-12). Generation is limited to 3 per 15 minutes per patient and verification to 5 per 15 minutes per doctor
and appointment; both answer `429` with `Retry-After` beyond the budget. No migration was needed.
MP-37 adds the remaining grant-ending paths (FR-ACS-07, FR-ACS-09, FR-ACS-11). `POST
api/access-grants/{grantId}/end` lets the authenticated patient end a live grant immediately; it answers `200` with
the ended grant, `403` for another patient's grant, `404` for an unknown grant, and `409` once the grant has already
ended or reached its two-hour limit. The grant end and its `AccessEnded` audit row commit in one unit of work.
Completion now loads the appointment's grant and, while it is still active, schedules its end 15 minutes after
completion capped by the two-hour limit and writes `AccessEnded` in the same unit of work; a visit with no grant, or
with a grant that already ended or expired, completes without extending it or writing a second audit. The absolute
two-hour cap itself is already enforced on every read by `AccessGrantChecker`, so no production change was needed
there. Reaching the cap is time-only: `StatusAt` derives `Expired` from the stored timestamps, there is no stored
change, no background job and no audit row (ADR-0011 decision 3). Completion vs patient-end that are truly concurrent
can still overwrite each other because `AccessGrant` has
no concurrency token; sequential behaviour is correct, and the race is marked with a `TODO` in the completion
service. No migration was needed.
`GET api/doctors/me/schedule?date=` returns only the authenticated doctor's non-cancelled appointments for one
clinic-local day, ordered by start, with only the patient name. "Today" and the schedule date use the Africa/Cairo
calendar while every stored timestamp stays UTC. Eleven migrations live in
`src/MediPassPlus.Infrastructure/Migrations/`, including `AddClinicStaffAndAdmin`, `AddAuditLog`,
`EnforceSinglePendingAccessRequest`, `EnforceSingleActiveAppointmentPerSlot` and `AddAppointmentStatusTimestamps`.
The audit store (`AuditLog`, `IAuditWriter` and an append-only EF interceptor) now exists. HTTP integration tests live
in `tests/MediPassPlus.Api.IntegrationTests`; they boot the real pipeline on Testcontainers SQL Server (or a local
server through `MEDIPASS_TEST_SQLSERVER`) and share one `MediPassApiFactory` collection fixture. They cover the
approve/reject matrix, the start-visit→approve flow, the `QrApprove` 429, the booking `400`/`404`/`409` matrix
(including a concurrent double-booking test), the MP-29 admin 401/403 matrix, the MP-34 check-in/complete
authorization and state matrix (including the check-in → start-visit → complete flow and the Cairo schedule
boundary), and the MP-36 code generation/verification matrix (wrong doctor, expiry, replay, lockout, both rate
limits, and the HMAC-only storage proof). The MP-37 tests add the completion grant-end/cap/already-ended matrix, the
patient-end `401`/`403`/`404`/`409` matrix, both audit proofs, and a test-only `[RequireActiveGrant]` controller that
shows the doctor's next gated request returns `403` after the patient ends the grant. There is still no record-read
endpoint, no QR-verification endpoint,
no medical-record module and no audit endpoint, and the
frontend is the untouched Angular welcome page. Everything the README, planning document or an ADR describes
as planned is a target, not a fact. Trust the code, the `.csproj` files, `package.json` and the ADRs over prose.

Installed dependencies include Identity, JWT bearer, FluentValidation and the shared ProblemDetails pipeline
(ADR-0010); Swashbuckle generates the Swagger document and UI (ADR-0012). Serilog is not installed.

This section is a snapshot, not a permanent fact: update it in the same PR that changes the status. Once a capability
exists, the sentence that says it does not is a bug, and an agent that trusts it will make wrong assumptions.

## 3. ADR rule

- An **Accepted** ADR is binding. Do not contradict it.
- A **Proposed** ADR is not binding. If your task depends on the decision it leaves open, stop and ask for that
  decision. If your task is independent of it and does not contradict it, proceed normally.
- Write a new ADR and get team approval **before** implementing an architectural decision that:
  - introduces a new architectural choice no existing ADR covers;
  - changes or supersedes an Accepted ADR;
  - conflicts with an approved requirement or with the established architecture (§4).
- Normal implementation that follows existing Accepted ADRs needs no new ADR — adding a table, endpoint, service or
  test that an ADR already describes is just doing the task.
- Never make an architectural decision silently, and never mark an ADR Accepted because it contains a good
  recommendation — acceptance is a team decision.

## 4. Architecture boundaries

- Project references define the layering. Checked against the `.csproj` files (verified 2026-11-05):
  - `Domain` — no project references.
  - `Application` → `Domain`.
  - `Infrastructure` → `Application` + `Domain` (direct). If a project reference must change, ask first and update
    this list in the same PR.
  - `Api` → `Application` + `Infrastructure`.
  - No reference may point upward: `Domain` must never reference `Application`, `Infrastructure` or `Api`. Never put
    EF Core types in `Domain`.
- `MediPassDbContext` calls `ApplyConfigurationsFromAssembly`, so `IEntityTypeConfiguration` classes are discovered
  automatically. Do not register configurations by hand.
- Seeders implement `ISeeder` (`Order`, `SeedAsync`) under `Infrastructure/Persistence/Seeding`, are registered in
  `Program.cs` as `AddScoped<ISeeder, XSeeder>()` (placeholders are commented there), and must be idempotent.
  `DatabaseSeeder` runs them at startup in Development only, ordered by `Order`.
- Nothing calls `Database.Migrate()` or `EnsureCreated()`. Tables exist only after `dotnet ef database update`.
- Identity owns authentication; MediPass+ owns healthcare authorization. Roles are never trusted from a request body,
  a client, or a token claim alone.
- The grant check, the audit writer, encryption and file streaming are shared services, not per-controller copies.

## 5. Security and privacy invariants (non-negotiable)

- Every medical read/write re-checks the active access grant **in the database on that request**. A JWT claim is never
  proof of access.
- Every medical read/write writes an audit entry. Audit rows are never updated or deleted.
- Clinic staff and admins never reach medical endpoints; doctors cannot list or search patients.
- Diagnosis text is stored encrypted. Files are only served through an endpoint that checks ownership or an active
  grant. Uploads are PDF/JPEG/PNG up to 10 MB, stored under random names.
- Encryption keys: the Data Protection key ring is persisted to a filesystem path taken from configuration **outside the
  repository** and protected at rest in a deployment; purpose strings are versioned (`MediPass.Diagnosis.v1`) —
  ADR-0007, decision 5. Losing the ring makes existing diagnosis text unreadable; that is the accepted consequence, not
  a reason to invent a second mechanism.
- QR token: 128-bit CSPRNG value, 2-minute life, single use, stored only as a **SHA-256** hash (ADR-0001).
- 6-digit code: 5-minute life, single use, entered by the doctor only (ADR-0002), stored only as **HMAC-SHA-256 with a
  protected server-side key** — a plain SHA-256 over a 6-digit keyspace is precomputable. Compare in fixed time
  (ADR-0001). The key is `AccessCode:HmacKey` (Base64 of at least 32 random bytes), validated at startup and separate
  from the JWT signing key and the Data Protection ring.
- Attempts: 5 wrong code entries lock that credential (FR-ACS-05). ADR-0007, decision 1 keeps the per-credential
  lockout authoritative and treats rate limiting as defence-in-depth (5 verifications / 15 min per doctor+appointment,
  3 generations / 15 min per patient; the numbers are tunable). The **lockout duration itself is not settled**: ADR-0003
  records 15 minutes as an assumption, so do not present it as an approved value.
- A grant ends in three independent ways (FR-ACS-07):
  - completing the visit sets its end to **15 minutes after completion** (UC-18, US-25);
  - it is never valid for **more than 2 hours from creation**, even if the visit is never completed (UC-24, US-25);
  - the patient can end it **immediately**, and the doctor's next request is refused (UC-19, US-25).
- A new visit needs a new approval, and an ended grant is never reused (FR-ACS-11).
- Never log or print medical content, passwords, tokens, codes or file bytes — identifiers only.
- Never commit secrets, connection strings, keys, certificates or real patient data. Use user-secrets or environment
  variables. An API key pasted into a chat or a file is a leaked key: rotate it, do not "clean it up later".

## 6. Swagger UI and OpenAPI

- Swashbuckle (`Swashbuckle.AspNetCore`) is the single Swagger document generator and UI; `Microsoft.AspNetCore.OpenApi`
  is not used. Do not add a second generator (ADR-0012).
- Development URLs: UI at `http://localhost:5114/swagger`, JSON at
  `http://localhost:5114/swagger/v1/swagger.json` (document name `v1`).
- Both are mapped only when the app runs in Development **or** `OpenApi:ExposeOutsideDevelopment` is `true`.
  The default in `appsettings.json` is `false`; keep it that way. Never expose the document publicly in a deployment
  that holds real data.
- The `Bearer` scheme and the per-operation security requirement come from `Swagger/AuthorizeOperationFilter.cs`.
  It marks operations that carry `[Authorize]` automatically, so do not hand-write security requirements.
- Schema ids are namespace-qualified by `Swagger/SwaggerSchemaId.cs`; two DTOs may share a simple name without
  breaking the document.
- **Current state:** Swagger UI works and the Accounts endpoints (`register`, `login`, `forgot-password`,
  `reset-password`) exist. The `[Authorize]` operations are `POST api/Auth/create-admin` (Admin, and it requires an
  existing admin account), `POST api/appointments/{appointmentId}/start-visit` (Doctor),
  `POST api/appointments/{appointmentId}/access-code` (Patient),
  `POST api/appointments/{appointmentId}/verify-code` (Doctor),
  `POST api/appointments/{appointmentId}/check-in` (Staff), `POST api/appointments/{appointmentId}/complete`
  (Doctor), `GET api/doctors/me/schedule?date=` (Doctor),
  `POST api/access-requests/{requestId}/approve` / `/reject` (Patient),
  `POST api/access-grants/{grantId}/end` (Patient), and the booking actions
  `POST api/appointments` / `{appointmentId}/cancel` / `{appointmentId}/reschedule` (Patient). Start-visit exercises
  the scope check and issues a one-time QR credential; check-in and completion enforce the appointment state machine
  and the staff-clinic or doctor-ownership scope; the doctor schedule reads one Cairo-local day; the patient
  endpoints answer a request by ID, consume or revoke its credentials, and return `429` with `Retry-After` after the
  per-patient `QrApprove` limit. The access-code endpoints are patient generation and owning-doctor verification,
  with their own `CodeGeneration`/`CodeVerification` limits and the FR-ACS-05 lockout. Completion also ends the
  visit's active grant after the 15-minute grace, capped by the two-hour limit, and the patient end takes effect
  immediately. The booking actions stay inside the authenticated patient's scope and return `400`/`404`/`409`
  `ProblemDetails` for every refused rule. No endpoint yet verifies a presented QR, and there is still no
  medical-record endpoint.
- **To call an authorized endpoint:** log in with `POST api/Auth/login`, copy the `accessToken` value, click
  **Authorize**, paste the raw token (no `Bearer ` prefix) and execute. The token lives in the browser tab only.
- When you add an authorized endpoint, document its real auth contract: `[ProducesResponseType]` plus `<response>` for
  `401` (missing, invalid or expired token) and `403` (wrong role, no active grant, expired or ended grant), along with
  every other status it can return. The operation filter supplies the security requirement; you still own the response
  metadata.
- XML documentation is mandatory on public controllers/actions and on public contracts whose shape is not obvious.
  `<GenerateDocumentationFile>` is on for `MediPassPlus.Api` and the comments are included in the document by
  Swashbuckle.
- Every action declares `[ProducesResponseType]` for each status code it can return, plus a `<response code="...">`
  description. A `<response>` tag alone does not add a status code.

## 7. Conventions

- C#: 4 spaces, file-scoped namespaces, `System` usings first, nullable enabled (`.editorconfig`).
- Frontend: 2 spaces, Prettier `singleQuote` + width 100, standalone components, zoneless. Specs settle rendering
  with `await fixture.whenStable()`, not `detectChanges()`.
- Paging everywhere: default 20, maximum 50 (NFR-PRF-04). Validate the page size instead of trusting it. The
  doctor's daily schedule (`GET api/doctors/me/schedule`) is the one documented exception: one clinic-local day for
  one doctor is a bounded set, so it returns the whole day ordered by start.
- Return `ProblemDetails` for errors; do not invent a second error envelope.
- Names of requirement IDs are the shared vocabulary: quote `FR-ACS-02` / `NFR-PRI-02` in code comments, tests and PR
  descriptions when they justify a rule.

## 8. Commands

The commands below were last verified on the reference machine on 2026-10-29 with .NET SDK 10.0.400, Node 24.20.0 and
npm 11.19.0. That is a record of one run, not a guarantee for every machine — check your own versions first
(onboarding §1) before concluding that something is broken.

Backend, from the repo root — this is also the CI sequence:

```powershell
dotnet build --configuration Release        # 0 errors; 1 pre-existing warning (JwtSettings.Key)
dotnet test --no-build --configuration Release
dotnet test tests/MediPassPlus.Domain.Tests --filter FullyQualifiedName~MyTest   # single test
dotnet run --project src/MediPassPlus.Api --launch-profile http   # http://localhost:5114
```

Frontend, from `frontend/`:

```powershell
npm ci
npm start                                   # http://localhost:4200
npm run build
npm test -- --watch=false                   # Vitest; without --watch=false it stays in watch mode
npm test -- --watch=false src/app/app.spec.ts
```

Migrations — **only with approval** (ADR-0009):

```powershell
dotnet ef migrations add <Name> --project src/MediPassPlus.Infrastructure --startup-project src/MediPassPlus.Api
dotnet ef database update --project src/MediPassPlus.Infrastructure --startup-project src/MediPassPlus.Api
```

- The solution is `.slnx`, not `.sln`. Keep the real casing `MediPassPlus.Infrastructure` (Linux CI is
  case-sensitive).
- `dotnet-ef` 10.0.11 is installed as a global tool. Migrations live in `src/MediPassPlus.Infrastructure/Migrations/`;
  do not pass `--output-dir`, or EF creates a second snapshot. The tools print a version-mismatch warning against the
  10.0.14 runtime; it is not a build failure.
- `ng test` accepts only Angular builder flags. Vitest flags fail (`--pool` → "Unknown argument"); `--watch=false` and
  a spec path are the supported ones. A cold run can also die with `Failed to start forks worker` before any test
  executes. Do not treat a passing rerun as the fix: record the original failure, check that no stale Node/Vitest worker
  is holding the workspace and that `node_modules` is complete, then rerun and report the rerun as a separate result.
  An intermittent failure that disappears is still a finding, not a green test run.
- No linter/formatter job exists; `.editorconfig` and `frontend/.prettierrc` are the intended style source.

## 9. Testing

- Backend xUnit; frontend Vitest + jsdom.
- New or changed logic gets unit tests. Endpoint changes get HTTP tests in `tests/MediPassPlus.Api.IntegrationTests`
  against Testcontainers SQL Server (ADR-0006, Accepted 2026-11-09). SQLite stays the fast persistence-test provider.
  On a machine without Docker, set `MEDIPASS_TEST_SQLSERVER` to an existing SQL Server connection string; the
  suite creates and drops its own database. See the project README.
- Every medical endpoint needs negative tests: no token (401), wrong role (403), no grant (403), expired grant (403),
  ended grant (403), wrong doctor (403) — NFR-SEC-10.
- Never weaken, skip or delete a test to get a green build. Report the failure instead.

## 10. Git and pull requests

- `main` is protected. Branches: `feature|fix/{module}-{JIRA-KEY}-{slug}`, `docs/{JIRA-KEY}-{slug}`, `chore/{slug}`.
  Modules: `accounts`, `booking`, `staff-admin`, `visit-access`, `record`, `privacy`, `chat`, `infra`.
- Commit `MP-<n> <present-tense sentence>`, e.g. `MP-14 add appointment booking endpoint`.
- One PR per Jira ticket, one migration per PR, never edit a migration that is already merged, squash-merge.
- Fill `.github/pull_request_template.md`; its checklist is the review bar. Match the CI job locally before pushing.
- Module owner reviews their module; the team leader approves every PR (planning document §3.2).

## 11. Agent operating rules

- **Plan before code.** For a Jira task, first report: the key and acceptance criteria, the requirement IDs, the ADRs
  and modules involved, the plan, the files you expect to create or modify, the tests you will run, and anything
  ambiguous. Wait for explicit approval, then implement.
- **Approval gates.** Do not create or apply migrations, add or upgrade packages, change a public API contract,
  rename or delete entities, or commit, push, merge, rebase or force-push without explicit approval for that specific
  action.
- **Preserve unrelated work.** If a file is already modified and is not part of your task, leave it exactly as it is
  and mention it in your report. Never `git reset`, `git checkout --`, `git clean`, `git stash` or overwrite it.
- **Report what actually happened.** Paste the real commands and their real results. Never claim a build, test,
  review, commit or push that did not run.
- **Stay in scope.** No application features, no schema changes, no "while I was there" refactors during a
  documentation or infrastructure task.
- If an ADR or requirement blocks the task, stop and say which one and what decision is needed. Do not guess.
