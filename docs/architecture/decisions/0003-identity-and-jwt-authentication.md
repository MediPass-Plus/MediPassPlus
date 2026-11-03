# ADR-0003: ASP.NET Core Identity with JWT authorization

- **Status:** Proposed (blocked: the Identity user-model shape — TPH subclasses vs profile tables — and the
  refresh-token storage/rotation design are not confirmed)
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
     `RequireUppercase = false`, `RequireNonAlphanumeric = false` (spec: "8 characters with a letter and a digit").
   - `User.RequireUniqueEmail = true` (FR-ACC-02).
   - `Lockout.MaxFailedAccessAttempts = 5` (FR-ACC-05); `Lockout.DefaultLockoutTimeSpan = 15 min` (assumption — the
     requirement only says "for a period"); login uses `CheckPasswordSignInAsync(..., lockoutOnFailure: true)`.
3. Roles via Identity roles (`Patient`, `Doctor`, `ClinicStaff`, `Admin`), issued as JWT role claims. Server-side
   `[Authorize(Roles = ...)]`/policies on every endpoint (FR-ACC-09); the medical-grant check is separate (ADR-0004).
4. Access token: 15 minutes (NFR-SEC-03). Refresh: dedicated hashed token row with rotation and revocation (FR-ACC-06)
   — exact table design is an open sub-decision for the Accounts owner.
5. Deactivated accounts (FR-STF-07) block login (Identity lockout) **and** carry an `IsActive` flag; because JWT bearer
   validation does not hit the database, deactivation takes effect at latest when the 15-minute access token expires.
6. JWT signing key comes from `Jwt:Key` (user-secrets/env); it is never the Data Protection key (ADR-0007).

## Open sub-decisions (block the Accounts PR, not the ADR)

- **User model shape:** TPH subclasses (`Patient`, `Doctor`, `ClinicStaff`, `Admin` inheriting `ApplicationUser`, one
  `AspNetUsers` table with a discriminator) — matches the existing class diagram and needs a diagram field rename; or
  `ApplicationUser` + 1:1 profile tables (`PatientProfile.DateOfBirth/Phone`, `DoctorProfile.Specialty/ClinicId`,
  `StaffProfile.ClinicId`) — cleaner for Identity, but redraws part 1. **Recommendation: TPH**, with tests asserting the
  subtype and the Identity role never disagree.
- **Refresh-token storage:** table with rotation vs security-stamp revalidation only. **Recommendation:** table.

## Consequences

- `passwordHash`, `failedLogins`, `lockoutEnd` disappear from the diagram; Identity's `PasswordHash`,
  `AccessFailedCount`, `LockoutEnd` take their place.
- `Patient`'s duplicate phone fields (`phoneNumber` inherited + `phone`) must be resolved to one source.
- JWT bearer tokens are self-contained, so role changes and deactivation lag by up to one token lifetime — accepted
  because tokens are short (NFR-SEC-03) and documented in the security notes.

## Conflict check

No conflict with requirements; the proposal aligns with the planning document and NFR-SEC-02.
Conflict only with the class diagram's hand-rolled identity fields (design artifact to update).
