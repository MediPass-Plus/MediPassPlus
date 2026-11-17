# MediPass+ engineering guide

MediPass+ — DEPI graduation project. ASP.NET Core 10 + EF Core + SQL Server API with an Angular 22 frontend.
Privacy-first medical history: a doctor can read a patient's record only while a visit-scoped access grant is active.
This file is the shared entry point for every developer. Read it before you change anything.

## 1. Read these first

| What | Where | Why |
|---|---|---|
| Architecture decisions | `docs/architecture/decisions/README.md` | Binding decisions per area; read the one that governs what you touch |
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
listing endpoints. MP-27 adds the patient booking rules, and MP-35 exposes them on the patient surface:
`POST api/me/appointments` books a free future slot for the authenticated patient,
`PUT api/me/appointments/{appointmentId}/cancel` cancels a booked appointment,
`PUT api/me/appointments/{appointmentId}/reschedule` moves it to another free slot of the same doctor, and
`GET api/me/appointments?page=&pageSize=` returns the authenticated patient's own upcoming, past and cancelled
appointments, newest slot first, 20 per page by default and at most 50 (FR-BKG-06, NFR-PRF-04). Every refusal is
the shared `ProblemDetails` envelope (`400` invalid state, `404` out of the patient's scope, `409` slot/state
conflict), and a filtered unique index (`IX_Appointments_DoctorSlotId_Active`) plus slot and appointment concurrency
tokens make a concurrent double booking impossible. Clinic and doctor searches default to 20 results per page,
accept at most 50, and return 400 for
invalid page or page-size values. MP-1301 added `AccessRequest`, `AccessGrant` and `VisitAccessCredential`.
A read-only visit-access scope service now verifies that the acting doctor or patient matches the appointment, and
`POST api/appointments/{appointmentId}/start-visit` lets a doctor start a today, checked-in appointment, reuse its
pending request or create a new one, and receive a fresh two-minute QR credential whose SHA-256 hash is the only
stored form. A filtered unique index allows at most one pending request per appointment, so a concurrent start
returns `409`. A newly created request is audited with `AccessRequestCreated` in the same unit of work.
`POST api/access-requests/{requestId}/approve` and `/reject` let the authenticated owner of the
appointment answer a pending request: approval creates the visit's grant (two-hour cap) and its audit row, rejection
creates no grant, and both answer the request and change its credentials in one unit of work. `AccessRequest.Status`
is an EF concurrency token, so only the first decision wins and the other returns `409`. Both decisions also share the
named `QrApprove` fixed-window rate limit (default 5 attempts / 15 minutes per patient, configurable and validated at
startup); a rejection is `429` with `Retry-After`. MP-34 adds the appointment status flow: the domain
`Appointment` owns the only allowed transitions (`Booked -> CheckedIn -> InVisit -> Completed`, plus
`Booked -> Cancelled`) and records each state's UTC timestamp, refusing every skipped or backward transition.
`POST api/appointments/{appointmentId}/check-in` lets staff in the appointment's clinic check in a booked appointment
for today, `POST api/appointments/{appointmentId}/complete` lets the owning doctor complete an in-visit appointment,
and `start-visit` now moves `CheckedIn -> InVisit` before it issues the QR (re-issuing while `InVisit` is allowed
until the patient approves; once a grant exists, a restart returns `409` instead of a QR that could never be
approved). MP-36 adds the six-digit fallback (FR-ACS-05): `POST
api/appointments/{appointmentId}/access-code` lets the authenticated patient generate a code for today's
checked-in or in-visit appointment with a pending request, and `POST
api/appointments/{appointmentId}/verify-code` lets the owning doctor verify it. The code lives five minutes, is
stored only as HMAC-SHA-256 under the separate `AccessCode:HmacKey` secret, is consumed on success, and locks after
five wrong entries. A successful verification records the patient's approval, creates the two-hour grant, moves the
appointment to `InVisit`, revokes the QR, and writes `AccessGrantCreated` in one unit of work.
Generation is limited to 3 per 15 minutes per patient and verification to 5 per 15 minutes per doctor
and appointment; both answer `429` with `Retry-After` beyond the budget. No migration was needed.
MP-37 adds the remaining grant-ending paths (FR-ACS-07, FR-ACS-09, FR-ACS-11). `POST
api/access-grants/{grantId}/end` lets the authenticated patient end a live grant immediately; it answers `200` with
the ended grant, `403` for another patient's grant, `404` for an unknown grant, and `409` once the grant has already
ended or reached its two-hour limit, or when another request ended it first. The grant end and its `AccessEnded`
audit row commit in one unit of work.
Completion now loads the appointment's grant and, while it is still active, schedules its end 15 minutes after
completion capped by the two-hour limit and writes `AccessEnded` in the same unit of work; a visit with no grant, or
with a grant that already ended or expired, completes without extending it or writing a second audit. The absolute
two-hour cap itself is already enforced on every read by `AccessGrantChecker`, so no production change was needed
there. Reaching the cap is time-only: `StatusAt` derives `Expired` from the stored timestamps, there is no stored
change, no background job and no audit row. Completion and a patient end that are truly
concurrent cannot overwrite each other: `AccessGrant.EndedAtUtc` is an EF concurrency token, so the first end wins and
the stale writer updates zero rows and answers `409` instead. A completion that loses the race does not extend the
grant and writes no second audit; the doctor retries and the retry completes the visit against the already-ended
grant. The token changes no column and needed no migration.
MP-97 closes the QR loop. `POST api/appointments/{appointmentId}/start-visit` now also returns `qrCodeDataUri`, the
same one-time token rendered as a PNG data URI by QRCoder; the plaintext is still never stored and only its SHA-256
hash is. `POST api/access/scan` resolves a token presented as text and `POST
api/access-requests/scan-image` decodes one from a camera frame in memory with ZXing.Net and ImageSharp (5 MB
limit); neither consumes the token. `POST api/access-requests/token/approve` and `/token/reject` answer the request
the token proves, and `GET api/appointments/{appointmentId}/access-state` returns the derived request and grant state
to the appointment's doctor or patient. An unknown or foreign token answers `403`; an expired, consumed, revoked or
already-answered token, or a visit that is no longer checked in/in visit, answers `410` on the token paths, while the
request-id approve path keeps its `409` for a closed visit. A decision race answers `409`, and every scan and token
decision shares the per-patient `QrApprove` limit. No new audit action was added: scanning is read-only and the
decisions reuse `AccessGrantCreated`/`AccessRequestRejected`. Swagger UI links to a development-only camera helper at
`/visit-access-tool.html` (served only when the API reference is exposed). No migration was needed.
MP-40 adds the clinic staff workflow (FR-STF-01..05, FR-PRV-06), all under `[Authorize(Roles = "Staff")]` and scoped to
the staff member's own clinic, which is read from the `ClinicStaff` row of the token's user and applied inside every
query, never taken from the request. `GET api/staff/schedule?date=` returns the clinic's non-cancelled appointments for
one Africa/Cairo day, ordered by start then id, with only the appointment id, patient name, phone, start time and
status. `POST api/staff/doctors/{doctorId}/slots` adds a slot for a doctor of the clinic and `DELETE
api/staff/doctors/{doctorId}/slots/{slotId}` removes one; the overlap check and the insert run in one Serializable
transaction, so concurrent creates cannot overlap (`409`), and any appointment that references a slot, even a
cancelled one, blocks its deletion (`409`). `POST api/staff/appointments` books a free future slot for a patient
identified by email, resolved on the server and required to have the Patient role, and delegates every booking rule to
`IBookingService`. `POST api/staff/appointments/{appointmentId}/check-in` checks in a booked appointment of today in
the clinic through `IAppointmentStatusService.CheckInAsync`, the same transition as `api/appointments/{id}/check-in`.
A doctor, slot or appointment outside the clinic answers `404`, a staff account without a profile answers `403`, and
no staff response carries diagnosis, prescription or report data. No migration was needed.
`GET api/doctors/me/schedule?date=` returns only the authenticated doctor's non-cancelled appointments for one
clinic-local day, ordered by start, with only the patient name. "Today" and the schedule date use the Africa/Cairo
calendar while every stored timestamp stays UTC. MP-31 adds the refresh-token lifecycle (FR-ACC-04, FR-ACC-06):
`POST api/Auth/login` answers the access token plus an opaque 256-bit refresh token and its expiry, `POST
api/Auth/refresh` rotates the pair, and `POST api/Auth/logout` ends the session and always answers `204`. Only the
SHA-256 hash of a token is stored, in `identity.RefreshTokens`; one token lives `Jwt:RefreshTokenDays` (7 days) and no
session outlives `Jwt:SessionDays` (30 days) from the first login. Presenting an already-rotated token is treated as a
leak and revokes every live token of the account, `RevokedAtUtc` is the concurrency token that makes a double rotation
impossible, deactivating an account or resetting its password also revokes every live token, and both token responses
carry `Cache-Control: no-store`. Thirteen migrations live in
`src/MediPassPlus.Infrastructure/Migrations/`, including `AddClinicStaffAndAdmin`, `AddAuditLog`,
`EnforceSinglePendingAccessRequest`, `EnforceSingleActiveAppointmentPerSlot`, `AddAppointmentStatusTimestamps`,
`AddMedicalRecordSchema` and `AddRefreshTokens`. MP-28 adds the `records` schema (`MedicalRecords`, `Diagnoses`, `Prescriptions`, `Reports`):
one record per patient, diagnoses always scoped to a visit, prescriptions inside a diagnosis, record-level reports
with an explicit uploader, append-only rows linked by `SupersedesDiagnosisId` / `SupersedesPrescriptionId` for
corrections, ciphertext columns for diagnosis and prescription text, required owning foreign keys with `Restrict` and
no cascade path, plus an EF interceptor that refuses updates and deletes. Patient registration creates the record in
the same transaction and the migration backfills every existing patient. The audit store (`AuditLog`, `IAuditWriter`
and an append-only EF interceptor) now exists. MP-38 adds the record write path: `POST
api/appointments/{appointmentId}/diagnoses` records a diagnosis for the grant's appointment and `POST
api/diagnoses/{diagnosisId}/prescriptions` adds one append-only prescription to a diagnosis the grant owns. Both
derive the record, appointment and patient from the verified grant and resolve the diagnosis inside that scope,
require the visit to be `InVisit` or `Completed` inside the 15-minute grace window, encrypt diagnosis and prescription
text through
`IClinicalTextProtector`, and commit the medical rows and their `DiagnosisCreated`/`PrescriptionCreated` audit rows in
one unit of work (FR-REC-03, FR-REC-04, FR-PRV-01, FR-PRV-02, FR-PRV-04). MP-39 adds the read
path: `GET api/appointments/{appointmentId}/history` returns a patient's whole permanent record to the authenticated
doctor only while the appointment's grant is active, and `GET api/me/history` returns the authenticated patient's own
record with no grant. Both return the record header, one page of diagnoses newest first with their prescriptions and
one page of report metadata, defaulting to 20 rows and capped at 50; invalid paging is `400`, every successful read
writes a `RecordRead` audit row before decryption (including a patient self-read), and the store returns ciphertext
only (FR-REC-01, FR-REC-02, FR-REC-07, FR-PRV-01, FR-PRV-02, NFR-PRF-04). Neither PR needed a
migration. HTTP integration tests live
in `tests/MediPassPlus.Api.IntegrationTests`; they boot the real pipeline on Testcontainers SQL Server (or a local
server through `MEDIPASS_TEST_SQLSERVER`) and give every test class its own database through a `MediPassApiFactory`
class fixture, with a test-only probe endpoint, smoke tests for the four roles, and isolation
tests that prove two classes never share a catalog. They cover the
approve/reject matrix, the start-visit→approve flow, the `QrApprove` 429, the booking `400`/`404`/`409` matrix
(including a concurrent double-booking test), the MP-29 admin 401/403 matrix, the MP-40 staff matrix (clinic scope, Cairo schedule day, slot overlap including a concurrent create, delete conflicts, booking and check-in, and the schedule response shape), the MP-34 check-in/complete
authorization and state matrix (including the check-in → start-visit → complete flow and the Cairo schedule
boundary), the MP-28 registration record matrix and the applied record migration, the MP-36 code
generation/verification matrix (wrong doctor, expiry, replay, lockout, both rate limits, and the HMAC-only storage
proof), and the MP-37 completion grant-end/cap/already-ended matrix, the patient-end `401`/`403`/`404`/`409` matrix,
both audit proofs, a two-context persistence test proving a stale completion cannot overwrite a patient end, and a
test-only `[RequireActiveGrant]` controller that shows the doctor's next gated request returns `403` after the
patient ends the grant. The MP-97 tests add the rendered-QR round trip, scan as text and as a camera image, the
token approve/reject matrix, the closed-visit `410`/`409` split, the wrong-patient `403`, the `415` upload, the
shared `QrApprove` `429`, and the doctor/patient access-state authorization matrix. The MP-31 tests add rotation and
single use, a replay ending every live session, idempotent logout and its `204`, the seven-day expiry and the
thirty-day session cap, revocation on deactivation and on password reset, and `no-store` on both token responses.
The MP-38 tests add the diagnosis/prescription write authorization, appointment-state, encryption and audit matrix;
the MP-39 tests add the doctor history grant/expiry/ended/wrong-doctor matrix, the patient self-read, the
forged-patient-id case, paging boundaries, real-protector decryption and both audit proofs. There is still no report
upload/download endpoint and no audit endpoint, and the frontend is the untouched Angular welcome page.
Everything the README or the planning document describes
as planned is a target, not a fact. Trust the code, the `.csproj` files and `package.json` over prose.

MP-50 configures the Data Protection key ring from `DataProtection:KeysPath`: the directory must be absolute and
outside the repository, Development and Testing fall back to a directory under the operating system's temporary path,
and Production refuses to start without it. It also adds `IDiagnosisProtector`, the only
component allowed to encrypt diagnosis text, under the versioned purpose `MediPass.Diagnosis.v1` (FR-PRV-04,
NFR-SEC-05); the MP-38 write endpoints encrypt through it and the MP-39 read path decrypts through it. The
three secrets — the key ring, `Jwt:SecretKey` and
`AccessCode:HmacKey` — are documented in `docs/operations/deployment-keys.md`, and
`DiagnosisProtectorTests`, `DataProtectionOptionsValidatorTests` and `DataProtectionRegistrationTests` cover the
round trip, the outside-repository rule and the Production startup failure. No migration was needed.

MP-54 adds the platform observability (decision record 0013). `GET /health` is anonymous and reports the `api`
and `database` checks as a status-only JSON body (`200` when every check is healthy, `503` otherwise); the database
check is a plain `IHealthCheck` over the existing `DbContext`, so no package was added. Every request carries an
`X-Correlation-ID` that is validated, echoed in the response and opened as a log scope, and the API writes structured
JSON to standard output in every environment except Development, where it uses the readable console format. The
identifiers-only contract is in `docs/development/logging.md`. CI now runs a gitleaks scan, a Domain/Application
coverage gate at 50% (NFR-MNT-02) and a negative control that proves a failing test fails the job.

Installed dependencies include Identity, JWT bearer, FluentValidation and the shared ProblemDetails pipeline;
Swashbuckle generates the Swagger document and UI. MP-97 adds QRCoder, ZXing.Net,
ZXing.Net.Bindings.ImageSharp.V3 and SixLabors.ImageSharp to Infrastructure. Serilog is not installed; the platform
uses the built-in structured logging (decision record 0013).

This section is a snapshot, not a permanent fact: update it in the same PR that changes the status. Once a capability
exists, the sentence that says it does not is a bug, and a reader who trusts it will make wrong assumptions.

## 3. Architecture decisions

- An **accepted** decision in `docs/architecture/decisions/` is binding. Do not contradict it.
- A **proposed** decision is not binding. If your task depends on the decision it leaves open, stop and ask for that
  decision. If your task is independent of it and does not contradict it, proceed normally.
- Write a new decision record and get team approval **before** implementing an architectural decision that:
  - introduces a new architectural choice no existing record covers;
  - changes or supersedes an accepted decision;
  - conflicts with an approved requirement or with the established architecture (§4).
- Normal implementation that follows existing accepted decisions needs no new record — adding a table, endpoint,
  service or test that a decision already describes is just doing the task.
- Never make an architectural decision silently, and never mark a decision accepted because it contains a good
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
  repository** and protected at rest in a deployment; purpose strings are versioned (`MediPass.Diagnosis.v1`).
  Losing the ring makes existing diagnosis text unreadable; that is the accepted consequence, not a reason to invent a
  second mechanism.
- QR token: 128-bit CSPRNG value, 2-minute life, single use, stored only as a **SHA-256** hash.
- 6-digit code: 5-minute life, single use, entered by the doctor only, stored only as **HMAC-SHA-256 with a
  protected server-side key** — a plain SHA-256 over a 6-digit keyspace is precomputable. Compare in fixed time.
  The key is `AccessCode:HmacKey` (Base64 of at least 32 random bytes), validated at startup and separate from the
  JWT signing key and the Data Protection ring.
- Attempts: 5 wrong code entries lock that credential (FR-ACS-05). The per-credential lockout is authoritative and
  rate limiting is defence-in-depth (5 verifications / 15 min per doctor+appointment, 3 generations / 15 min per
  patient; the numbers are tunable). The **lockout duration itself is not settled**: the decision record treats
  15 minutes as an assumption, so do not present it as an approved value.
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
  is not used. Do not add a second generator.
- Development URLs: UI at `http://localhost:5114/swagger`, JSON at
  `http://localhost:5114/swagger/v1/swagger.json` (document name `v1`).
- Both are mapped only when the app runs in Development **or** `OpenApi:ExposeOutsideDevelopment` is `true`.
  The default in `appsettings.json` is `false`; keep it that way. Never expose the document publicly in a deployment
  that holds real data.
- The `Bearer` scheme and the per-operation security requirement come from `Swagger/AuthorizeOperationFilter.cs`.
  It marks operations that carry `[Authorize]` automatically, so do not hand-write security requirements.
- Schema ids are namespace-qualified by `Swagger/SwaggerSchemaId.cs`; two DTOs may share a simple name without
  breaking the document.
- Exposure and security are covered by `ApiReferenceExposureTests` (Development + flag `false`, Production + flag
  `false`, Production + flag `true`; wherever Swagger is served, `/openapi/v1.json` still answers 404) and
  `SwaggerDocumentTests` (the UI configuration points at the document it serves, every operation has a summary and a
  described response, `login` documents the validation `400` it can return, and every `[Authorize]` operation carries
  `Bearer` while anonymous operations stay unmarked).
- **Current state:** Swagger UI works and the Accounts endpoints (`register`, `login`, `refresh`, `logout`,
  `forgot-password`, `reset-password`) exist. The `[Authorize]` operations are `POST api/Auth/create-admin` (Admin, and it requires an
  existing admin account), `POST api/appointments/{appointmentId}/start-visit` (Doctor),
  `POST api/appointments/{appointmentId}/access-code` (Patient),
  `POST api/appointments/{appointmentId}/verify-code` (Doctor),
  `POST api/appointments/{appointmentId}/check-in` (Staff), `POST api/appointments/{appointmentId}/complete`
  (Doctor), `POST api/appointments/{appointmentId}/diagnoses` (Doctor),
  `POST api/diagnoses/{diagnosisId}/prescriptions` (Doctor), `GET api/doctors/me/schedule?date=` (Doctor),
  `POST api/access-requests/{requestId}/approve` / `/reject` (Patient),
  `POST api/access/scan` / `/scan-image` (Patient),
  `POST api/access-requests/token/approve` / `/token/reject` (Patient),
  `GET api/appointments/{appointmentId}/access-state` (Doctor or Patient),
  `POST api/access-grants/{grantId}/end` (Patient), `GET api/appointments/{appointmentId}/history` (Doctor),
  `GET api/me/history` (Patient), and the patient appointment actions
  `GET api/me/appointments` / `POST api/me/appointments` / `PUT {appointmentId}/cancel` /
  `PUT {appointmentId}/reschedule` (Patient). The record write endpoints derive the patient, record and diagnosis
  from the verified grant, encrypt clinical text and commit each medical row with its audit row; the history
  endpoints return the record header, paged diagnoses with their prescriptions and paged report metadata, with the
  doctor's route gated by an active grant and the patient's route self-scoped. Start-visit exercises
  the scope check and issues a one-time QR credential plus its rendered `qrCodeDataUri`; the scan endpoints resolve
  that token as text or as a camera image without consuming it, and the token decision endpoints approve or reject
  the request it proves; check-in and completion enforce the appointment state machine
  and the staff-clinic or doctor-ownership scope; the doctor schedule reads one Cairo-local day; the patient
  endpoints answer a request by ID, consume or revoke its credentials, and return `429` with `Retry-After` after the
  per-patient `QrApprove` limit. The access-code endpoints are patient generation and owning-doctor verification,
  with their own `CodeGeneration`/`CodeVerification` limits and the FR-ACS-05 lockout. Completion also ends the
  visit's active grant after the 15-minute grace, capped by the two-hour limit, and the patient end takes effect
  immediately. The access-state endpoint derives the request/grant status and remaining seconds from the stored
  timestamps for the appointment's doctor or patient. The booking actions stay inside the authenticated patient's
  scope and return `400`/`404`/`409` `ProblemDetails` for every refused rule. Swagger UI adds a topbar link to
  `/visit-access-tool.html`, the development-only camera helper, whenever the document is exposed; there is still no
  report upload/download endpoint and no audit endpoint.
- **To call an authorized endpoint:** log in with `POST api/Auth/login`, copy the `accessToken` value, click
  **Authorize**, paste the raw token (no `Bearer ` prefix) and execute. The token lives in the browser tab only.
- When you add an authorized endpoint, document its real auth contract: `[ProducesResponseType]` plus `<response>` for
  `401` (missing, invalid or expired token) and `403` (wrong role, no active grant, expired or ended grant), along with
  every other status it can return. The operation filter supplies the security requirement; you still own the response
  metadata.
- XML documentation is mandatory on public controllers/actions and on public contracts whose shape is not obvious.
  `<GenerateDocumentationFile>` is on for `MediPassPlus.Api` and the comments are included in the document by
  Swashbuckle.
- `GET /health` is deliberately **not** in the document: it is operational surface, not a product contract. It is
  documented in `README.md` and `docs/development/logging.md` (decision record 0013).
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
- Logging: `ILogger<T>` with message templates, identifiers only (FR-PRV-08). Never log bodies, headers, tokens, codes,
  connection strings or medical text, and never build a message with string interpolation. The request's
  `CorrelationId` scope and the full checklist are in `docs/development/logging.md`.
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

Coverage gate (NFR-MNT-02), from the repo root — this is also what CI runs:

```powershell
dotnet test tests/MediPassPlus.Domain.Tests --no-build --configuration Release --collect:"XPlat Code Coverage" --results-directory artifacts/coverage/domain
dotnet test tests/MediPassPlus.Application.Tests --no-build --configuration Release --collect:"XPlat Code Coverage" --results-directory artifacts/coverage/application
pwsh -NoProfile -File scripts/check-coverage.ps1          # fails below 50% in either project
```

On a Windows machine without `pwsh`, run the same file with `powershell -NoProfile -File scripts/check-coverage.ps1`.

The negative control, to prove a red test really fails the run:

```powershell
$env:MEDIPASS_CI_NEGATIVE="1"
dotnet test tests/MediPassPlus.Api.UnitTests --no-build --configuration Release --filter "FullyQualifiedName~CiNegativeControlTests"
"exit=$LASTEXITCODE"                                      # must be 1
Remove-Item Env:MEDIPASS_CI_NEGATIVE
```

Frontend, from `frontend/`:

```powershell
npm ci
npm start                                   # http://localhost:4200
npm run build
npm test -- --watch=false                   # Vitest; without --watch=false it stays in watch mode
npm test -- --watch=false src/app/app.spec.ts
```

Migrations — **only with approval**:

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
  against Testcontainers SQL Server. SQLite stays the fast persistence-test provider.
  On a machine without Docker, set `MEDIPASS_TEST_SQLSERVER` to an existing SQL Server connection string; the
  suite creates and drops its own database. See the project README.
- Every medical endpoint needs negative tests: no token (401), wrong role (403), no grant (403), expired grant (403),
  ended grant (403), wrong doctor (403) — NFR-SEC-10.
- Never weaken, skip or delete a test to get a green build. Report the failure instead.
- CI also runs the coverage gate, the gitleaks scan and the negative control (`CiNegativeControlTests` with
  `MEDIPASS_CI_NEGATIVE=1`). The negative control fails on purpose there; the step fails when it passes. Match all
  three locally before pushing (`AGENTS.md` §8).

## 10. Git and pull requests

- `main` is protected. Branches: `feature|fix/{module}-{JIRA-KEY}-{slug}`, `docs/{JIRA-KEY}-{slug}`, `chore/{slug}`.
  Modules: `accounts`, `booking`, `staff-admin`, `visit-access`, `record`, `privacy`, `chat`, `infra`.
- Commit `MP-<n> <present-tense sentence>`, e.g. `MP-14 add appointment booking endpoint`.
- One PR per Jira ticket, one migration per PR, never edit a migration that is already merged, squash-merge.
- Fill `.github/pull_request_template.md`; its checklist is the review bar. Match the CI job locally before pushing.
- Module owner reviews their module; the team leader approves every PR (planning document §3.2).

## 11. Working rules

- **Plan before code.** For a Jira task, first report: the key and acceptance criteria, the requirement IDs and
  modules involved, the plan, the files you expect to create or modify, the tests you will run, and anything
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
- If an architecture decision or a requirement blocks the task, stop and say which one and what decision is
  needed. Do not guess.
