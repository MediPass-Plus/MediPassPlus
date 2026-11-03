# ADR-0005: Diagnosis relationships (record, appointment, prescriptions)

- **Status:** Proposed (revised 2026-10-29: `AppointmentId` is required; awaiting review)
- **Date:** 2026-10-29
- **Affects:** Medical Record module, ERD/schema in D4, record queries

## Context

The requirements and diagrams describe the link differently:

- FR-REC-01 / US-30: one permanent record per patient; diagnoses are part of that record.
- FR-REC-03 / US-27: a doctor records a diagnosis **for the current appointment**, and it cannot be deleted.
- FR-REC-09: diagnoses and prescriptions are permanent; corrections are added as new entries.
- Class diagram part 3: `MedicalRecord` *contains* 0..* `Diagnosis` (composition) and `Appointment` *results in*
   0..* `Diagnosis`. The `0..*` multiplicity sits on the **Diagnosis** end: it says an appointment can result in zero
   diagnoses, not that a diagnosis may exist without an appointment.

## Decision

- `Diagnosis.MedicalRecordId` — **required** FK. Every diagnosis belongs to exactly one permanent record.
- `Diagnosis.AppointmentId` — **required** FK (`NOT NULL`). A diagnosis is always recorded during a visit, through the
  grant that authorized that visit, so the appointment is part of what the row means (FR-REC-03, US-27). The `0..*`
  in the diagram is a cardinality of the appointment end, not a nullability of the column.
- `Prescription.DiagnosisId` — required (composition, 1..*), fields: medicine name, dosage, optional notes (FR-REC-04).
- `Report.MedicalRecordId` — required; reports are held by the record, not by a visit (diagram). The owning appointment,
  when one exists, is recovered from the audit entry / grant rather than a second FK.
- **No deletion path anywhere:** no delete endpoints, no cascade delete from record → diagnosis/prescription/report,
  corrections are new diagnoses (FR-REC-09).
- `Diagnosis.Description` is stored **encrypted** (ADR-0007 keys): the column stores ciphertext (Base64 of the Data
  Protection payload), so its length is sized for ciphertext, it is not searchable in SQL, and "search inside history"
  stays a stretch item.

## Consequences

- Queries for "history of a patient" filter by `MedicalRecordId`; queries for "this visit" use `AppointmentId`.
- The database itself rejects an orphan diagnosis; no application-layer invariant test is needed to compensate for a
  nullable column.
- Statistics such as "diagnoses per visit" and the audit trail stay joinable without null handling.
- Encrypted text means no server-side sorting/filtering on description — ordering is by `DiagnosedAt`.

## Alternatives considered

- `AppointmentId` **nullable** (the earlier draft of this ADR) — would allow importing history that has no appointment in
  the system. Nothing in the requirements asks for an import, and a nullable FK permits orphan medical data, which is
  the opposite of the project's data-minimisation stance. Rejected.
- Diagnosis attached only to `Appointment` (record derived through patient) — loses the "one record" composition that
  FR-REC-01/07 rely on.

## Conflict check

No conflict with a Must requirement. The `0..*` notation in the class diagram is consistent with a required FK: it
constrains how many diagnoses an appointment may have, not whether a diagnosis may lack an appointment.
