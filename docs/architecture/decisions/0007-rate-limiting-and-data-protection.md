# ADR-0007: Rate limiting and Data Protection key management

- **Status:** Accepted (2026-10-29), with a standing caveat: the numeric limits and their scoping are subject to
  security review and may be tuned without a new ADR, as long as the decisions below (where the limit applies, key
  separation) stay unchanged.
- **Date:** 2026-10-29
- **Affects:** Platform/Infrastructure, Visit Access, Accounts, Medical Record

## Context

FR-PRV-07 requires rate limiting on **login attempts, QR scans and code entries**. FR-ACS-05 additionally locks a code
after 5 wrong attempts. NFR-SEC-05 requires diagnosis text encrypted with Data Protection and keys kept outside the
repository; the planning risk register (R6) names lost/leaked keys as high impact. Acceptance criterion: the
proposal's numeric limits (5 verifications / 15 min, 3 generations / 15 min).

## Decision

1. **Two independent layers.** Per-credential attempt lockout (5 wrong → locked) is the authoritative FR-ACS-05 rule.
   Rate limiting is defence-in-depth and must not replace or relax it.
2. **Configurable limits** bound from `appsettings`/env through `IOptions`, validated at startup:
   - `CodeVerification`: 5 permits / 15 min, keyed per **(doctorId, appointmentId)**.
   - `CodeGeneration`: 3 permits / 15 min, keyed per **patientId**.
   - `Login`: 5 permits / 15 min per **email + IP** (required by FR-PRV-07, missing from the original proposal).
   - `QrApprove`: limit on approve/reject attempts per patient (required by FR-PRV-07).
3. Rejections return **429** with `Retry-After`, and a body that contains no medical content or identifiers beyond the
   request scope.
4. Limits apply to the specific endpoint group via `RequireRateLimiting(policy)`; a named policy per group; the
   in-memory limiter is per instance (acceptable for the single-instance demo, recorded as a scaling limitation).
5. **Data Protection:** `PersistKeysToFileSystem` to a path from configuration **outside the repository**
   (`uploads/`-style ignored location for dev, a mounted volume or DB in the demo/production host),
   protected at rest for production. Purpose strings are versioned, e.g. `MediPass.Diagnosis.v1`.
6. **Key separation:** the diagnosis cipher key ring, the JWT signing key (`Jwt:Key`) and the code-HMAC key
   (ADR-0001) are three distinct secrets; none is committed; all are documented in the deployment notes.

## Consequences

- If the key ring is lost, existing diagnosis ciphertext is unreadable — the demo reset procedure (drop DB + reseed) is
  the documented recovery.
- Limits keyed per appointment avoid punishing a doctor who sees several patients in a row; the proposal's ambiguous
  "5 per 15 minutes" scope is therefore fixed explicitly.
- Making the 2 h grant cap or the 2/5 min credential TTLs configurable is **not** included: they are requirement
  constants (NFR-PRI-02, FR-ACS-02/05). Only rate limits are configurable.
- Rate limiting tests belong in ADR-0006's project (assert 429 and that the lockout still triggers).

## Conflict check

No conflict. It closes FR-PRV-07 gaps (login and QR limits) that the original proposal did not cover, and keeps
FR-ACS-05 as the authoritative lockout rule.
