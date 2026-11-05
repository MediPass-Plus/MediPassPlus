# ADR-0011: Access grant lifecycle and status derivation

- **Status:** Proposed (awaiting module review; MP-1301 step 4)
- **Date:** 2026-11-05
- **Affects:** Visit Access module, `access.AccessGrants` schema, the future access checker, class diagram (D4)

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
6. **Uniqueness.** At most one grant exists per access request and per appointment; both foreign keys are unique.
7. **Request lifecycle.** `AccessRequest.Status` is `Pending`, `Approved` or `Rejected`, and only the patient moves it
   out of `Pending`. Requests do not expire: the credentials do, and US-22 replaces a code under the same pending
   request by revoking the previous credential (ADR-0001).
8. **Scope consistency.** The request and grant store `AppointmentId`, `DoctorId` and `PatientId`. The database cannot
   enforce that the doctor and patient match the appointment without a composite key on `Appointment`, so the visit
   access service must verify it before creating a request or grant and must audit both events (ADR-0004).

## Consequences

- No status column and no scheduled job: every checker computes the same answer from the same timestamps.
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
