# ADR-0002: Code generation and verification actors

- **Status:** Accepted (2026-10-29), doctor-only verification. The conflict recorded below is resolved in favour of the
  requirements (US-23 / FR-ACS-05 / UC-13), so no requirement change is needed.
- **Date:** 2026-10-29
- **Affects:** Visit Access endpoints, authorization rules, audit values

## Context

The proposal says the patient requests the 6-digit code and **clinic staff** verify it. Three requirements and one
sequence diagram disagree about who enters the code:

- US-23: "As a **doctor**, I want to type the code the patient tells me" — acceptance: locked after 5 wrong attempts.
- FR-ACS-05: "the patient requests the code in the app, it is valid for 5 minutes, **the doctor enters it**".
- UC-13 primary actors: Patient, **Doctor** (not clinic staff).
- Sequence diagram 1: `enterCode(appointmentId, code)` is sent from the **Doctor** lifeline.
- FR-PRV-06 / §1.5 scope table: clinic staff must be refused on every medical endpoint; staff see only name, phone,
  time and status.

Verifying a code mints an `AccessGrant`, i.e. it is the act that grants medical access. Letting staff perform it would
put the scheduling role inside the privacy boundary the project is built on.

## Decision

- **Generation: patient only.** `POST` on the patient's own in-progress appointment with a live `AccessRequest`.
  Preconditions: authenticated patient owns the appointment; appointment is today; status `CheckedIn` or `InVisit`;
  request not consumed; no active/unexpired code (or replace it, per US-22).
- **Verification: doctor only**, and only for their own appointment, matching `AccessRequest.DoctorId`.
  Preconditions: request pending, credential `NumericCode` not consumed/revoked/expired/locked; each failure
  increments `FailedAttempts`; the 5th failure sets `LockedAt` and the credential becomes unusable.
- Staff and admin never receive a route, permission or token that can create a grant.

## Consequences

- No requirement changes, no diagram changes.
- The staff role keeps exactly one medical-adjacent action: none. Their schedule views stay metadata-only (FR-STF-04).
- If the team wants staff help with the fallback, that is a **requirements change** (US-23, UC-13, FR-ACS-05, FR-PRV-06,
  scope table, D4) and would need a new audit value; recommend rejecting it for this project.

## Conflict check

**Conflict:** proposed "clinic staff verify" vs US-23 / FR-ACS-05 / UC-13 / sequence diagram 1 / FR-PRV-06.
**Recommendation:** doctor-only, keep requirements unchanged.
