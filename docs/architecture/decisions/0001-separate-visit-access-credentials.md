# ADR-0001: Separate QR token and 6-digit code credentials

- **Status:** Accepted (2026-10-29)
- **Date:** 2026-10-29
- **Affects:** Visit Access module, class diagram (D4), database baseline

## Context

FR-ACS-02 requires a QR token valid for **2 minutes**; FR-ACS-05 requires a 6-digit fallback valid for **5 minutes**.
The current class diagram (`Diagrams/MediPass_Class_Diagram_Part2_Booking_Access.png`) models one `AccessRequest`
with `tokenHash`, `codeHash`, a single `expiresAt`, `failedAttempts` and `usedAt`. One expiry field cannot satisfy two
different lifetimes, and mixing both secrets in one row makes single-use and lockout semantics ambiguous.

## Decision

Split the credential from the request:

- `AccessRequest`: `Id`, `AppointmentId`, `DoctorId`, `PatientId`, `CreatedAt`, lifecycle state. One per visit start.
- `VisitAccessCredential`: `Id`, `AccessRequestId` (required FK), `Type` (`QrToken` | `NumericCode`), `SecretHash`,
  `CreatedAt`, `ExpiresAt`, `ConsumedAt?`, `RevokedAt?`, `FailedAttempts` (code only), `LockedAt?`.
- One *active* credential per `(AccessRequestId, Type)`: generating a new code revokes the previous one (US-22 "a new
  code replaces the old one").
- QR: 128-bit CSPRNG value, `ExpiresAt = CreatedAt + 2 min`, single use.
- Code: 6 digits from a CSPRNG, `ExpiresAt = CreatedAt + 5 min`, single use, locked after 5 failed attempts.
- **Hashing:** QR token → SHA-256 (input is already 128-bit random). 6-digit code → **HMAC-SHA256 with a server-side
  key**, because a plain hash of a 10^6 keyspace is precomputable. Compare with
  `CryptographicOperations.FixedTimeEquals`.
- Approving a request consumes the used credential and revokes the other one for that request.

## Consequences

- Requirements are met exactly (2 min / 5 min, single use, lockout, hash-only storage).
- TTLs live on the credential row, so tests can assert both lifetimes without a special case.
- The class diagram and D4 document (due 6 Nov) must be redrawn: `AccessRequest` loses `tokenHash`, `codeHash`,
  `expiresAt`, `failedAttempts`, `usedAt`; `VisitAccessCredential` gains them. Sequence diagram 1 stays valid.
- A new secret (code HMAC key) joins `Jwt:Key` and the Data Protection ring; it must live outside the repository.

## Alternatives considered

- Keep both hashes on `AccessRequest` with two expiry columns — fewer tables, but lockout/consumption state still
  collides between the two credential types.
- Store plaintext codes — rejected by NFR-SEC-04.

## Conflict check

No conflict with a documented requirement. Conflict with the **class diagram**, which is a design artifact to update,
not a requirement.
