# ADR-0005: Diagnosis relationships (record, appointment, prescriptions)

- **Status:** Accepted (2026-11-11; revised 2026-10-29 for required `AppointmentId`; amended 2026-11-11 after review)
- **Date:** 2026-10-29 (amended 2026-11-11)
- **Affects:** Medical Record module, ERD/schema in D4, record queries

## Context

The requirements and diagrams describe the link differently:

- FR-REC-01 / US-30: one permanent record per patient; diagnoses are part of that record.
- FR-REC-03 / US-27: a doctor records a diagnosis **for the current appointment**, and it cannot be deleted.
- FR-REC-09: diagnoses and prescriptions are permanent; corrections are added as new entries.
- Class diagram part 3: `MedicalRecord` *contains* 0..* `Diagnosis` (composition) and `Appointment` *results in*
   0..* `Diagnosis`. The `0..*` multiplicity sits on the **Diagnosis** end: it says an appointment can result in zero
   diagnoses, not that a diagnosis may exist without an appointment.

The 2026-10-29 revision made `AppointmentId` required. The 2026-11-11 review accepted that core and closed the gaps
left open by the first revision: prescription cardinality, correction traceability, report attribution, the encryption
boundary and the write invariants that make the relationships safe.

## Decision

### Record and diagnosis

- `MedicalRecord.PatientId` — **required** FK and **unique**: one permanent record per patient (FR-REC-01). The row is
  created in the same unit of work as patient registration, and a backfill covers users who registered before the
  Record module shipped. The record holds no duplicated demographics; Identity stays the single source of truth.
- `Diagnosis.MedicalRecordId` — **required** FK. Every diagnosis belongs to exactly one permanent record.
- `Diagnosis.AppointmentId` — **required** FK (`NOT NULL`). A diagnosis is always recorded during a visit, through the
  grant that authorized that visit, so the appointment is part of what the row means (FR-REC-03, US-27). The `0..*`
  in the diagram is a cardinality of the appointment end, not a nullability of the column.
- `Diagnosis.DiagnosedAtUtc` — server-set UTC. History ordering is by this column (FR-REC-02); the index
  `(MedicalRecordId, DiagnosedAtUtc)` supports the newest-first page.
- Both foreign keys are derived server-side from the grant's appointment. The request never supplies
  `MedicalRecordId`, `PatientId` or `AppointmentId`, so a row that points at another patient's record is a service bug,
  not a client input. A negative test covers the mismatch.

### Prescriptions

- `Prescription.DiagnosisId` — **required** FK. A prescription belongs to exactly one diagnosis.
- A diagnosis has **zero or more** prescriptions, matching the class diagram's `0..*`. FR-REC-04's "one or more"
  applies to a request that adds prescriptions, not to the diagnosis as a whole: not every diagnosis needs medication,
  and a mandatory minimum would force dummy rows.
- Fields: medicine name, dosage, optional notes (FR-REC-04).

### Permanence and corrections

- **No deletion path anywhere:** no delete endpoints, no cascade delete from record → diagnosis/prescription/report.
  FKs use `Restrict` (`NO ACTION` in SQL Server).
- **No update path:** a diagnosis or prescription is never edited in place. FR-REC-09 says corrections are added as
  new entries.
- `Diagnosis.SupersedesDiagnosisId` — optional self-FK (`Restrict`, no cascade) pointing at the row this one corrects.
  The corrected row stays visible; the UI follows the chain to show which entry is current. A unique filtered index
  prevents two rows from superseding the same diagnosis.
- `Prescription.SupersedesPrescriptionId` — optional self-FK with the same semantics, for a medicine or dose correction
  that does not change the diagnosis. It stays inside the same diagnosis; when the diagnosis itself is corrected, the
  new diagnosis's prescriptions are covered by `SupersedesDiagnosisId`.
- The service enforces that a supersedes link stays inside the same medical record (diagnosis) or the same diagnosis
  (prescription). A composite FK may enforce the diagnosis case at the database level; the service check and its test
  remain required either way.
- An EF `SaveChanges` interceptor rejects `Modified` and `Deleted` entries for `Diagnosis`, `Prescription` and
  `Report`, mirroring ADR-0004 decision 6. A bug cannot silently rewrite history.
- Consequence: a correction needs an active grant (FR-REC-10), so it happens during a later visit or during the
  15-minute post-completion window of the current one. There is no post-hoc correction outside a visit in this scope.

### Reports

- `Report.MedicalRecordId` — **required**; reports are held by the record, not by a visit (diagram).
- `Report.UploadedByUserId` — **required** `Guid` FK to the uploading user account (no domain navigation), consistent
  with `Appointment.PatientId`. `Report.UploadedAtUtc` is server-set. The report stays record-level: no
  `AppointmentId`, and no visit attribution is reconstructed from the audit log. The audit log is an accountability
  record, not a business read model; `AuditEntry` has no appointment column, and `AccessGrantId` is optional and
  absent for patient uploads.
- If a future requirement needs "reports from this visit", add a nullable `Report.AppointmentId` through a new ADR and
  update the class diagram. Do not infer it from audit rows.

### Encryption

- `Diagnosis.Description` is stored encrypted (NFR-SEC-05, ADR-0007 decision 5): the column stores the Base64 of the
  Data Protection payload under the versioned purpose `MediPass.Diagnosis.v1`. It is not searchable or sortable in
  SQL; ordering is by `DiagnosedAtUtc`.
- `Prescription.MedicineName`, `Dosage` and `Notes` are also stored encrypted under `MediPass.Prescription.v1`.
  NFR-SEC-05 only names diagnosis text, but leaving the medicine name in clear leaks the condition the diagnosis
  describes. The fields are display-only, so losing SQL search on them costs nothing.
- Plaintext limits are validated before encryption: `Description` 4,000 characters, `MedicineName` 200, `Dosage` 200,
  `Notes` 1,000. Ciphertext columns are `nvarchar(max)`; a test round-trips the maximum plaintext so a future column
  change cannot silently truncate data.
- The key ring lives outside the repository per ADR-0007 decision 5. Losing it makes existing clinical text unreadable;
  that is the accepted consequence. Bumping a purpose string is a data migration, not a configuration change.

### Write invariants

- A diagnosis is created only through an appointment-scoped endpoint that re-checks the active grant **in the database
  on that request** (FR-PRV-01, FR-REC-10). A JWT claim is not proof of access.
- The appointment must be `InVisit`, or `Completed` while the grant is still inside its 15-minute post-completion
  window (ADR-0011). The grant checker is the authority; the status check is a second, cheap guard.
- The diagnosis, its prescriptions and their audit rows (`DiagnosisCreated`, one `PrescriptionCreated` per
  prescription — ADR-0004) commit in one transaction. A refused grant writes no medical row.
- Doctor reads stay appointment-scoped; there is no doctor route that takes a patient id (FR-ACS-10). The patient's own
  read route is separate and always scoped to the authenticated user.

## Consequences

- Queries for "history of a patient" filter by `MedicalRecordId`; queries for "this visit" use `AppointmentId`.
- The database itself rejects an orphan diagnosis; no application-layer invariant test is needed to compensate for a
  nullable column.
- Statistics such as "diagnoses per visit" and the audit trail stay joinable without null handling.
- Encrypted clinical text means no server-side sorting or filtering on descriptions, medicine names or notes; ordering
  is by `DiagnosedAtUtc`, and history paging uses the `(MedicalRecordId, DiagnosedAtUtc)` index.
- A correction is a new row with a supersedes link, so the record grows with every correction. That is the intended
  cost of permanence (FR-REC-09), not a defect.
- Reports have no visit FK. Showing "which visit produced this scan" is out of scope; the uploader and upload time are
  stored, and the audit log records the event for accountability.

## Alternatives considered

- `AppointmentId` **nullable** (the earlier draft of this ADR) — would allow importing history that has no appointment
  in the system. Nothing in the requirements asks for an import, and a nullable FK permits orphan medical data, which
  is the opposite of the project's data-minimisation stance. Rejected. General healthcare models (for example FHIR
  `Condition.encounter`) make the encounter optional because problem lists and imports exist; that is a different
  scope, and relaxing the column later is a new ADR.
- Diagnosis attached only to `Appointment` (record derived through patient) — loses the "one record" composition that
  FR-REC-01/07 rely on. Rejected.
- Updating a diagnosis in place, or versioning a mutable row — contradicts FR-REC-09's "corrections are added as new
  entries" and hides history. Rejected in favour of append-only rows with explicit supersedes links.
- Recovering a report's appointment from the audit log — `AuditEntry` has no appointment column, `AccessGrantId` is
  optional, and patient uploads have no grant. It also turns the accountability log into a hidden read model. Rejected.
- Encrypting only `Diagnosis.Description` — leaves the medicine name in clear, which leaks the same condition.
  Rejected in favour of encrypting the prescription text fields too.

## Conflict check

No conflict with a Must requirement. The `0..*` notation in the class diagram is consistent with a required FK: it
constrains how many diagnoses an appointment may have, not whether a diagnosis may lack an appointment. The
prescription cardinality is corrected to 0..*, which is what the diagram already said. Encrypting prescription text
goes beyond NFR-SEC-05 but does not contradict it; NFR-SEC-05 remains the minimum, not a ceiling. ADR-0004 already
defines `DiagnosisCreated`, `PrescriptionCreated`, `ReportUploaded` and `ReportDownloaded`, so no audit amendment is
needed.

## Amendment 2026-11-11 — review outcome

Accepted after review. The required-FK core is unchanged. The review:

- corrected the prescription cardinality from `1..*` to `0..*`;
- added `SupersedesDiagnosisId` / `SupersedesPrescriptionId` and the no-update interceptor so FR-REC-09 has a model;
- replaced the "recover the report's appointment from audit" claim with explicit `UploadedByUserId` attribution and a
  record-level report, leaving a future nullable `AppointmentId` to a new ADR;
- fixed the encryption boundary and sizing: diagnosis and prescription text, named purpose strings, plaintext caps and
  `nvarchar(max)` ciphertext;
- recorded the write invariants (database grant re-check, `InVisit`/post-completion status guard, same-transaction
  audit, server-derived keys) and the registration-time record creation with its backfill;
- refreshed the Part 3 class diagram and added its reproducible source
  (`Diagrams/src/render_class_diagram_part3.py`), which shows the new fields, encryption markers and append-only rules.
