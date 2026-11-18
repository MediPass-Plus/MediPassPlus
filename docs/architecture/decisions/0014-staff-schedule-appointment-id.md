# ADR-0014: Staff schedule returns the appointment id with the four FR-STF-04 fields

- **Status:** Accepted (2026-11-18). Clarifies FR-STF-04 and US-15 for the clinic staff schedule.
- **Date:** 2026-11-18
- **Affects:** `StaffScheduleItemDto`, `GET api/staff/schedule`, the staff check-in surface, MP-41 refusal tests

## Context

FR-STF-04 and US-15 allow clinic staff to see only the patient's name, phone, appointment time and status.
`StaffScheduleItemDto` (MP-40) also returns `AppointmentId`. The MP-41 review asked whether that extra field was an
approved exception; the team confirmed on 2026-11-18 that it is.

`AppointmentId` is an opaque technical key, not patient or medical data. The staff surface needs it because the
check-in route is `POST api/staff/appointments/{appointmentId}/check-in`; without the id the schedule cannot drive the
one staff action it supports. Removing it would not improve the privacy boundary and would make the staff UI unable to
use the schedule.

## Decision

1. `GET api/staff/schedule?date=` returns exactly these five properties per item: `appointmentId`, `patientName`,
   `patientPhone`, `startTimeUtc`, `status`.
2. The four fields in FR-STF-04 and US-15 are the patient-data fields; `appointmentId` is the transport identifier for
   the staff check-in action and is not an additional patient-data field.
3. No diagnosis, prescription, report, history or other medical field may be added. Any further field needs a new ADR.

## Consequences

- `StaffScheduleItemDto` keeps `AppointmentId`; the MP-41 test asserts the exact five-property set, so a missing or
  added field fails instead of being silently accepted.
- FR-STF-04's privacy intent is unchanged: staff still see metadata only, and clinic isolation is enforced inside the
  query, never from the request.
