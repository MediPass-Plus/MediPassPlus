# ADR-0003: ASP.NET Core Identity with JWT authorization

- **Status:** Proposed (blocked on the user-model shape only; see the open sub-decision at the end. The password
  policy, the 15-minute lockout and the refresh-token design are settled — the last by MP-31 on 2026-11-16 and
  recorded in decision 4.)
- **Date:** 2026-10-29
- **Affects:** Accounts module, every authorized endpoint, class diagram (D4), database baseline

## Context

The planning document (§1.8) and NFR-SEC-02 require ASP.NET Core Identity for password storage, and FR-ACC-04/06
require short-lived JWT access tokens plus revocable refresh tokens. The class diagram instead models
`ApplicationUser` with `passwordHash`, `failedLogins` and `lockoutEnd`, i.e. a hand-rolled identity — that is not
what the documents require, and it would duplicate Identity.

## Decision

1. `ApplicationUser : IdentityUser<Guid>` is the single identity entity. Identity owns credential storage, lockout,
   email uniqueness and password rules; MediPass+ owns healthcare authorization on top of it.
2. Explicit Identity configuration (defaults do **not** match FR-ACC-02):
   - `Password.RequiredLength = 8`, `RequireDigit = true`, `RequireLowercase = true`,
     `RequireUppercase = true`, `RequireNonAlphanumeric = true`. The requirement states a minimum ("8 characters with
     a letter and a digit"); the team kept the stricter policy, which satisfies it and relaxes no rule (amended
     2026-11-13).
   - `User.RequireUniqueEmail = true` (FR-ACC-02).
   - `Lockout.MaxFailedAccessAttempts = 5` (FR-ACC-05); `Lockout.DefaultLockoutTimeSpan = 15 min`, confirmed by the
     Accounts module owner on 2026-11-13 (the requirement only says "for a period"); login uses
     `CheckPasswordSignInAsync(..., lockoutOnFailure: true)`.
3. Roles via Identity roles (`Patient`, `Doctor`, `Staff`, `Admin`), issued as JWT role claims. `Staff` is the seeded
   and enforced name for the requirements' `ClinicStaff` label. Server-side `[Authorize(Roles = ...)]`/policies on
   every endpoint (FR-ACC-09); the medical-grant check is separate (ADR-0004).
4. Access token: 15 minutes (NFR-SEC-03). Refresh (FR-ACC-06) is a dedicated hashed token row with rotation and
   revocation. The design below was confirmed on 2026-11-16 by MP-31 and is what the code runs:
   - **Value:** 256 bits of CSPRNG output, handed to the client once as an opaque Base64Url string. It is never
     stored: the row keeps only its SHA-256 hash in upper-case hex (64 characters) under a unique index.
   - **Table:** `identity.RefreshTokens` with `Id` (UUIDv7), `UserId` (foreign key to `identity.Users`, cascade),
     `TokenHash`, `CreatedAtUtc`, `ExpiresAtUtc`, `SessionStartedAtUtc`, `RevokedAtUtc`, `ReplacedByTokenId` and an
     optional `DeviceId`. Indexes on the unique `TokenHash`, on `UserId` (replay and session reads) and on
     `ExpiresAtUtc`, which is what a retention job would scan.
   - **Lifetime:** one token lives `Jwt:RefreshTokenDays` (7 days) and no session outlives `Jwt:SessionDays`
     (30 days) counted from the first login. Every rotation carries the original `SessionStartedAtUtc` forward, so a
     replacement expires at `min(now + 7 days, sessionStart + 30 days)`. Both lifetimes are validated at startup, so a
     misconfiguration cannot turn the first login into a `500`.
   - **Rotation:** a refresh retires the presented row (`RevokedAtUtc`, `ReplacedByTokenId`) and inserts its
     replacement in one unit of work, so no value can be exchanged twice. `RevokedAtUtc` is the EF concurrency token:
     of two concurrent refreshes exactly one wins and the loser answers `401`.
   - **Replay:** presenting a revoked row that has a successor proves a copy leaked, so every live token of the
     account is revoked with one `ExecuteUpdate`. A row revoked by a logout has no successor and ends only itself, so
     replaying a logged-out token cannot end the account's other sessions.
   - **Logout:** `POST api/Auth/logout` is anonymous, idempotent and always answers `204`, so it reveals nothing about
     which tokens exist.
   - **Other ways a session ends:** deactivating the account (FR-STF-07) and a successful password reset both revoke
     every live token of that account, so reactivating an account or resetting a password cannot revive an old session.
   - **Transport:** the login and refresh responses carry `Cache-Control: no-store` and `Pragma: no-cache`
     (RFC 6749 section 5.1).
   - **Refusals:** every failed refresh answers the same `401` `ProblemDetails`, whatever the reason.
5. Deactivated accounts (FR-STF-07) block login (Identity lockout) **and** carry an `IsActive` flag; because JWT bearer
   validation does not hit the database, deactivation takes effect at latest when the 15-minute access token expires.
   A deactivated account cannot extend a session either: every live refresh token of the account is revoked with the
   flag (decision 4).
6. JWT signing key comes from `Jwt:SecretKey` (user-secrets/env); it is never the Data Protection key (ADR-0007).

## Implementation review (2026-11-05, superseded by the review dated 2026-11-13)

The merged Accounts code follows the Identity/JWT direction but does not yet satisfy this ADR. Do not mark the ADR
Accepted until the gaps are closed or the decision is amended by the team.

Aligned:

- `ApplicationUser : IdentityUser<Guid>`; Identity owns the password hash and user record.
- JWT access tokens carry the user id and role claims, with a 15-minute lifetime.
- `IsActive` is checked at login, so a deactivated account cannot obtain a token.

Gaps:

- **Password policy:** the code requires an uppercase letter and a non-alphanumeric character; this ADR and FR-ACC-02
  say `false`. Decide whether to relax the code or amend the ADR/requirement.
- **Lockout:** login uses `CheckPasswordAsync`, not `CheckPasswordSignInAsync(..., lockoutOnFailure: true)`, and
  `MaxFailedAccessAttempts` / `DefaultLockoutTimeSpan` are not configured. FR-ACC-05 is not implemented.
- **Refresh tokens:** there is no refresh-token table, rotation, revocation or endpoint; `RefreshTokenDays` is unused.
  FR-ACC-06 is not implemented.
- **User model:** the code uses one `ApplicationUser` plus a `Doctor` profile table and a duplicate `Role` string. The
  TPH-vs-profile sub-decision and the single source of truth for roles remain unresolved.
- **Role assignment:** registration accepts a role from the request body and allows `Doctor`/`Staff` self-assignment.
  AGENTS.md §4 forbids trusting a role from a request body; administration must assign privileged roles.
- **Role names:** the seeder creates `Staff`; this ADR and the requirements use `ClinicStaff`.
- **JWT key name:** the code binds `Jwt:SecretKey`; this ADR names `Jwt:Key`. Standardize one name and keep the value
  outside the repository.

## Implementation review (2026-11-13)

Rewritten after MP-21 was merged. Decision 2 now states the policy the code runs with, which closes the password and
lockout gaps the 2026-11-05 review opened. The ADR stays Proposed for the two sub-decisions below, not for the Accounts
implementation.

Aligned:

- `ApplicationUser : IdentityUser<Guid>`; Identity owns the password hash and user record.
- JWT access tokens carry the user id and role claims, with a 15-minute lifetime.
- `IsActive` is checked at login, so a deactivated account cannot obtain a token.
- **Password policy:** 8 characters with upper, lower, digit and symbol, enforced by Identity and mirrored by the DTO
  validators, so a password cannot pass validation and then fail inside `UserManager` with a different message.
- **Lockout:** login calls `CheckPasswordSignInAsync(..., lockoutOnFailure: true)`. Five failures lock the account for
  the configured 15 minutes, a locked account is refused even with the correct password, every refusal returns one
  identical `401` `ProblemDetails`, and a successful password reset clears the lockout.
- **JWT key name:** the code binds `Jwt:SecretKey`, the name decision 6 uses.
- **Role names:** the seeder and the authorization checks use `Staff`, the name decision 3 uses.
- **Role casing:** registration resolves the requested role through `RoleManager.FindByNameAsync` and branches on
  `IdentityRole.NormalizedName`, so `admin`/`Admin`/`ADMIN` reach the same branch and the Admin role cannot be
  self-assigned by casing. Covered by HTTP tests per casing variant.

Gaps:

- **Refresh tokens:** there is no refresh-token table, rotation, revocation or endpoint; `RefreshTokenDays` is unused.
  FR-ACC-06 is not implemented. **Closed by MP-31 on 2026-11-16; see decision 4 and the confirmation below.**
- **User model:** the code uses one `ApplicationUser` plus a `Doctor` profile table and a duplicate `Role` string. The
  TPH-vs-profile sub-decision and the single source of truth for roles remain unresolved.
- **Role assignment:** registration still accepts `Patient`, `Doctor` and `Staff` from the request body. `Admin` is
  refused, and a self-registered `Doctor` or `Staff` account is inactive until an admin activates it, so a role from
  the body never grants access on its own. Restricting the public endpoint to `Patient` (FR-ACC-03) remains a product
  decision.

## Refresh-token design confirmed (2026-11-16)

MP-31 closed the refresh-token sub-decision decision 4 used to leave open for the Accounts owner. Decision 4 now
records the lifecycle the code runs, so the remaining gap from the 2026-11-13 review is closed.

The HTTP contract is covered by
`tests/MediPassPlus.Api.IntegrationTests/RefreshTokenEndpointTests.cs`: rotation and single use, a replay ending every
live session of the account, logout idempotence and its `204`, the seven-day expiry, the thirty-day session cap,
revocation on deactivation and on password reset, and `no-store` on both token responses.
`RefreshTokenTests` (Domain) and `RefreshTokenPersistenceTests` (SQLite) cover the entity rules and the stored
columns. Nothing in this design contradicts FR-ACC-04, FR-ACC-06 or NFR-SEC-03.

## Open sub-decision (blocks the Accounts PR, not the ADR)

- **User model shape:** TPH subclasses (`Patient`, `Doctor`, `ClinicStaff`, `Admin` inheriting `ApplicationUser`, one
  `AspNetUsers` table with a discriminator) — matches the existing class diagram and needs a diagram field rename; or
  `ApplicationUser` + 1:1 profile tables (`PatientProfile.DateOfBirth/Phone`, `DoctorProfile.Specialty/ClinicId`,
  `StaffProfile.ClinicId`) — cleaner for Identity, but redraws part 1. **Recommendation: TPH**, with tests asserting the
  subtype and the Identity role never disagree.

## Consequences

- `passwordHash`, `failedLogins`, `lockoutEnd` disappear from the diagram; Identity's `PasswordHash`,
  `AccessFailedCount`, `LockoutEnd` take their place.
- `Patient`'s duplicate phone fields (`phoneNumber` inherited + `phone`) must be resolved to one source.
- `identity.RefreshTokens` gains one row per login and per rotation, and the API never deletes them. A retention job
  must remove expired and revoked rows; the `ExpiresAtUtc` index exists so that job can scan instead of reading the
  table whole.
- JWT bearer tokens are self-contained, so role changes and deactivation lag by up to one token lifetime — accepted
  because tokens are short (NFR-SEC-03) and documented in the security notes.

## Conflict check

No conflict with requirements; the proposal aligns with the planning document and NFR-SEC-02.
Conflict only with the class diagram's hand-rolled identity fields (design artifact to update).
