# ADR-0011: Access grant lifecycle and status derivation

- **Status:** Accepted (2026-11-05, module review completed by Ahmed; amended 2026-11-05 for composite scope enforcement;
  amended 2026-11-12 for code-verification approval; amended 2026-11-13 for QR scanning and the access-state read model)
- **Date:** 2026-11-05 (amended 2026-11-05, amended 2026-11-12, amended 2026-11-13)
- **Affects:** Visit Access module, `access.AccessRequests`/`access.AccessGrants`/`clinical.Appointments` schema,
  the future access checker, class diagram (D4)

## Context

FR-ACS-07 and US-25 require an access grant to stop in three independent ways: 15 minutes after the visit is
completed (UC-18), at most 2 hours after it was created (UC-24), or immediately when the patient ends it (UC-19).
FR-ACS-11 requires a new approval for a new visit and forbids reusing an ended grant.

ADR-0001 already separated the request from its single-use credentials and named the request lifecycle. It did not
say how the grant records its end or how the three states are produced, which leaves the access checker and the
status list in MP-1301 undefined.

## Decision

1. `AccessGrant` stores `GrantedAtUtc`, `ExpiresAtUtc = GrantedAtUtc + 2 hours` (immutable), `EndedAtUtc?` and
   `EndReason?` (`VisitCompleted` | `PatientEnded` | `TimeLimit`). All timestamps are UTC.
2. The effective end is `EndedAtUtc ?? ExpiresAtUtc`. A grant is active while `now < effective end`.
3. The status is **derived, never stored**: `StatusAt(now)` returns `Active`, `Ended` or `Expired`.

| Status | Condition | Reached by | Actor |
|---|---|---|---|
| **Active** | `now < EndedAtUtc ?? ExpiresAtUtc` | The patient approves the request; a grant row is inserted | Patient (approval), system (insert) |
| **Ended** | effective end reached and `EndReason` is `VisitCompleted` or `PatientEnded` | Visit completion sets `EndedAtUtc = min(now + 15 min, ExpiresAtUtc)` and reason `VisitCompleted`; the grant stays **Active** during that grace window. Patient end sets `EndedAtUtc = now` and reason `PatientEnded` | Doctor (own appointment, completion only) and Patient (immediate end) |
| **Expired** | `now >= ExpiresAtUtc` with no end reason | The 2-hour cap is reached; no stored change and no background job | Time only |

4. **Transition authority.** Nobody creates a grant except through an approved request; the doctor cannot create,
   extend, reactivate or shorten a grant. A doctor's only write is *Complete visit*, which may only move the end
   earlier. The patient may end the grant immediately at any time while it is Active, including during the
   post-completion grace window; that overrides the scheduled end.
5. **Terminal states.** `Ended` and `Expired` are final. No transition leaves them, and an ended or expired grant is
   never reused. A new visit requires a new request, a new approval and a new grant (FR-ACS-11).
6. **Uniqueness.** At most one grant exists per access request and per appointment. The request foreign key is unique,
   and the appointment scope `(AppointmentId, DoctorId, PatientId)` is a unique foreign key.
7. **Request lifecycle.** `AccessRequest.Status` is `Pending`, `Approved` or `Rejected`, and only the patient moves it
   out of `Pending`. Requests do not expire: the credentials do, and US-22 replaces a code under the same pending
   request by revoking the previous credential (ADR-0001).
8. **Scope consistency.** The request and grant store `AppointmentId`, `DoctorId` and `PatientId`. The database
   enforces that the stored triple matches the appointment through an alternate key on
   `Appointment (Id, DoctorId, PatientId)` and composite foreign keys from `AccessRequest` and `AccessGrant`. The
   visit access service still verifies the authenticated actor before creating a request or grant and must audit both
   events (ADR-0004); the database cannot know which user is acting and must not be the first line that rejects a
   request.

### Amendment 2026-11-05 — database scope key

The original decision 8 said the database could not enforce this rule without a composite key. The MP-1301
follow-up adds that key and the composite foreign keys in migration `EnforceVisitAccessScopeMatch`, so a row whose
stored doctor or patient does not match the appointment is rejected by the database. The service check remains mandatory: it resolves `Doctor.UserId` and the
authenticated patient and returns the `403`/`404` decision, so a constraint violation is a last-resort integrity
check rather than the caller-facing authorization response. Both the key and the service are required.

### Amendment 2026-11-12 — numeric-code verification is the patient's approval

MP-36 adds the six-digit fallback (FR-ACS-05, UC-13, ADR-0001, ADR-0002). A successful verification of the patient's
live numeric code by the doctor who owns the request's appointment is the patient's approval expressed out of band:
the patient generated the code and read it to that doctor. The application service may therefore move the request
`Pending -> Approved` and create the grant through `AccessGrant.Create`, exactly as a direct approval does.

Decision 4 is unchanged. A grant is still created only through an approved request, and the doctor still cannot create
access unilaterally: a live, unconsumed, unrevoked, unlocked, unexpired code generated by the patient must exist and be
presented by the owning doctor. A wrong code never changes the request. Decision 7 keeps its default: the patient
answers a request directly, and the code path is the one additional way the patient's approval is recorded.

The `AccessGrantCreated` audit row records the doctor as the acting user, the patient as the record owner, and the
grant id as the affected entity.

The attempt counter is authoritative in the database, not in the API instance: a wrong entry is one conditional
`UPDATE` that increments `FailedAttempts` and sets `LockedAtUtc` on the fifth, guarded by the credential's usable
state. Concurrent entries serialize on that row, so no increment is lost and the fifth entry always locks even when
several wrong codes arrive at once. The rate limit remains defence-in-depth (ADR-0007).

### Amendment 2026-11-13 — QR scanning and the access-state read model

MP-97 closes the presentation gap between `start-visit` and the patient's decision without changing this ADR's
lifecycle. The doctor's response now also carries `qrCodeDataUri`, the same plaintext token rendered as a PNG data
URI at response time; the token itself is still never stored, and only its SHA-256 hash is (ADR-0001).

A scan is read-only. `POST api/access-requests/scan` (token text) and `POST api/access-requests/scan-image` (a camera
frame decoded in memory) resolve the token by its stored hash and return the doctor, clinic and appointment details
the patient needs. Neither endpoint consumes, revokes or writes anything, so the patient can look before deciding
and a retry is harmless. Consumption stays where decision 7 and ADR-0001 put it: the approve or reject action.

The token decision paths (`POST api/access-requests/token/approve` and `/token/reject`) answer the request the token
proves. An unknown token and another patient's token both answer `403`, so the response never confirms that a token
exists or who owns it. An expired, consumed, revoked or locked token, an already-answered request, or a visit that is
no longer `CheckedIn`/`InVisit` answers `410` on these paths. The request-id paths keep their existing vocabulary:
`404` for an unknown request and `409` for an already-answered request or a closed visit. Approval remains refused
once the visit is no longer in progress, and rejection remains available because it creates no grant. A decision
race still answers `409` through the `AccessRequest.Status` concurrency token.

`GET api/appointments/{appointmentId}/access-state` returns the appointment's newest request and its grant to the
appointment's doctor or patient. Every status and the remaining seconds are derived from the stored timestamps at
read time with `StatusAt`/`EffectiveEndUtc`; nothing is written, no status is stored, and no background job exists.
The endpoint adds no table and no column, and MP-97 needed no migration. Scanning is not an access-control event and
adds no audit action; approve and reject continue to write `AccessGrantCreated` and `AccessRequestRejected` in their
own unit of work.

## Consequences

- No status column and no scheduled job: every checker computes the same answer from the same timestamps.
- The composite scope key makes a mismatched request or grant fail at `SaveChanges` as well as at the service; a bug
  cannot silently create access for another doctor or patient.
- `EndReason` is what separates `Ended` from `Expired`; a completion capped at the 2-hour limit is still `Ended`
  because a deliberate reason exists.
- Completing a visit never extends access past `GrantedAtUtc + 2 hours`.
- The status vocabulary is stable for the checker built by the module reviewer and for the future API contract.
- The class diagram must add `EndReason` and show the status as derived, not stored.

## Alternatives considered

- **Store a `Status` column and update it with a background job.** Rejected: the row is stale between job runs, the
  job races with the checker, and the 2-hour cap is deterministic without it.
- **Make the 2-hour cap or the grace window configurable.** Rejected by ADR-0007: they are requirement constants,
  unlike rate limits.
- **Keep both credentials and their expiries on `AccessRequest`.** Already rejected by ADR-0001.

## Conflict check

No conflict with an Accepted ADR. ADR-0009 is still Proposed and is not relied on: four migrations were merged before
this one, so MP-1301 follows the established per-module migration practice. ADR-0003 (Identity) and ADR-0006
(integration tests) are not changed by this decision.
