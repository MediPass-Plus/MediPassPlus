# ADR-0015: Record-scoped report routes and record-level grant scope

- **Status:** Proposed (2026-11-20). Needs a team decision before the MP-47/MP-48 routes are written. On acceptance it
  also amends the doctor-read rule in ADR-0005 (§ "Write invariants"), so both changes land in the same PR as the
  route code.
- **Date:** 2026-11-20
- **Affects:** MP-47 `POST api/records/{recordId}/reports`, MP-48 `GET api/reports/{reportId}`,
  `IAccessGrantChecker`, the report storage boundary, ADR-0005

## Context

The requirements fix the behaviour but not the route shape:

- **FR-REC-05** (Must, US-29/UC-16): "The system shall let a patient, and a doctor with an active grant, upload
  reports and scans as PDF, JPEG or PNG up to 10 MB."
- **FR-REC-06** (Must): "The system shall check the type and size of every uploaded file and store it privately under
  a random name."
- **FR-REC-08** (Should, US-31): "The system shall let a patient download their own reports, and a doctor open reports
  only with an active grant."
- **FR-PRV-05** (Must): "The system shall serve report files only through an endpoint that checks ownership or an
  active grant. There shall be no public file links."
- **NFR-SEC-09** (Must): allowed types PDF, JPEG and PNG; at most 10 MB; random stored names; proven by integration
  tests.

MP-47 and MP-48 in Jira fix the routes:

| Ticket | Route | Who |
|---|---|---|
| MP-47 | `POST api/records/{recordId}/reports` (multipart, field `file`) | Patient on their own record, doctor with an active grant |
| MP-48 | `GET api/reports/{reportId}` | Patient on their own report, doctor with an active grant |

Three existing decisions stand in the way, and each one has to be resolved here rather than in a controller:

1. **ADR-0005 (Accepted)** ends with: "Doctor reads stay appointment-scoped; there is no doctor route that takes a
   patient id (FR-ACS-10)."
2. **FR-ACS-10 (Must)** is stricter than that sentence: "The system shall give doctors no way to list or search
   patients. **A record can be reached only through an appointment.**"
3. **The only grant check today is appointment-scoped.** `IAccessGrantChecker.RequireActiveGrantAsync(doctorUserId,
   appointmentId, ct)` resolves the patient *from the appointment* and verifies the stored grant for that exact
   `(AppointmentId, DoctorId, PatientId)` triple (ADR-0011 decision 8). A report route carries no appointment, so the
   method cannot be reused unchanged.

What a grant already authorizes is not in doubt: **FR-REC-02** (Must) requires a doctor with an active grant to see
"the complete history (diagnoses, prescriptions, reports) from all earlier visits and doctors". MP-39 implements
exactly that — `GET api/appointments/{appointmentId}/history` verifies the grant for that appointment and then reads
the patient's *whole* record, not only the current visit's entries. So the grant is already record-wide in effect;
only the route that proves it is appointment-shaped. `RecordHistoryResponse` also returns `MedicalRecordId`, so a
caller who has already passed the grant check is the one who learns the record id.

## Decision

1. **Two routes, as fixed by MP-47/MP-48**, with the record as the resource:
   `POST api/records/{recordId}/reports` for the upload and `GET api/reports/{reportId}` for the MP-48 download. Both
   are `[Authorize(Roles = "Doctor,Patient")]`; clinic staff and admins never reach them (FR-PRV-06).

2. **A doctor's record access is authorized by a patient-scoped grant re-check.** `IAccessGrantChecker` gains a
   second method - `RequireActiveGrantForPatientAsync(doctorUserId, patientUserId, ct)` - that loads that doctor's
   grants for that patient **from the database on that request** and succeeds when at least one of them is active
   under ADR-0011's lifecycle rules (`now < EndedAtUtc ?? ExpiresAtUtc`, never past `GrantedAtUtc + 2 hours`,
   terminal states never reused by FR-ACS-11). The doctor binding lives in the store's read: `AccessGrant` stores a
   `DoctorId` profile, and the lookup joins `Doctors` on `UserId`, so a caller cannot ask for another doctor's
   grants without resolving a profile first. The checker re-verifies the patient on every candidate row, so a grant
   whose `PatientId` does not match the record owner authorizes nothing (defence in depth, NFR-PRI-01). It returns
   the grant it used, so the audit row can carry its id. The store needs one additional read - grants by
   `(doctor user account, patient)` - and the status rules stay in the checker, so there is no schema change and no
   migration.

3. **The record id is a capability, not a lookup key.** There is no route that lists records, no route that takes a
   patient id, and no way to search by patient (FR-ACS-10's first sentence). The only way to obtain a record id is a
   response that already passed the grant or ownership check (`RecordHistoryResponse.MedicalRecordId`). Guessing is
   not a path: the id is a `Guid` (v7, 74 random bits) and a wrong id costs exactly what a forbidden id costs, because
   both answer the same 404 (decision 4).

4. **One refusal answer for record-level refusals: `404`** for an unknown record, another patient's record, a report
   that belongs to another patient, and a doctor without an active grant. The response never confirms that a record
   exists (NFR-PRI-01). `403` is reserved for a role that may never call the endpoint, which is decided by the
   authorization policy before the action runs.

5. **Order of the upload pipeline**, so an unauthorized caller learns nothing and a bad file leaves nothing behind:
   authenticate → resolve the record and authorize it (404) → validate the file (413/415/400) → write the file → stage
   the `Report` row and its audit row in one `SaveChanges`. A failed `SaveChanges` deletes the stored file before the
   failure is returned; a failed file write stages no row.

6. **Storage boundary.** Files live under `Reports:Storage:RootPath`, which must be absolute and outside the
   repository **and** outside `wwwroot` — the same rule ADR-0007 decision 5 applies to the key ring. The stored name is
   `{Guid v7:N}{ext}` with the extension derived from the *validated* content type; the client file name is never
   stored, never logged and never used as a path. Nothing maps the root as static content, so no public link exists
   (FR-PRV-05) and a leaked name is still useless without a token.

7. **Audit.** The upload writes `AuditAction.ReportUploaded` against `AuditEntityType.Report`, with the patient as the
   record owner, the caller as the actor, and `AccessGrantId` set to the grant that authorized a doctor's upload and
   `null` for a patient's own upload — the same attribution ADR-0005 already fixed for reports (ADR-0004).

8. **Amendment to ADR-0005** (applied on acceptance, in the PR that adds the routes). The final bullet of
   "Write invariants" becomes:

   > A doctor route never takes a patient id and no endpoint lists or searches patients (FR-ACS-10). A doctor route may
   > take an opaque medical record id only when (a) the id was returned by a response that already passed the grant
   > check and (b) a patient-scoped grant re-check succeeds on that request. MP-47 and MP-48 are the only such routes
   > today.

## Consequences

- A doctor with one active grant can upload a report to, and download a report from, any entry of that patient's
  record. That is the same reach FR-REC-02 already gives a doctor for history, and it does not widen the grant's
  lifetime: the ended/expired rules are unchanged.
- The new method is a second way to prove the same grant, not a second kind of grant. It reads the existing
  `access.AccessGrants` rows through `IAccessGrantStore`; no schema change and no migration.
- One shared 404 keeps the privacy promise cheap: there is no branch where a forbidden id is distinguishable from a
  missing one, and the negative matrix (NFR-SEC-10) asserts exactly that.
- The safety net is the route list, not a filter: adding a third record-scoped route later needs this ADR to be
  updated, otherwise a reviewer has no rule to check it against.
- **Cost / residual risk:** FR-ACS-10 says "a record can be reached only through an appointment", and a record-id
  route reads that sentence against its stated intent. The intent (no enumeration, no patient search) is preserved;
  the literal sentence is not. The team accepts that reading or chooses the alternative below — this is the decision
  this ADR asks for.

## Alternatives considered

- **Appointment-scoped upload:** `POST api/appointments/{appointmentId}/reports` for a doctor plus
  `POST api/me/reports` for a patient, mirroring `DiagnosesController` and `MyHistoryController`. This is the safest
  reading of FR-ACS-10 and reuses `RequireActiveGrantAsync` unchanged. Rejected as the primary design because MP-47,
  MP-48 and the frontend contract name a record-level resource, and because MP-48's `api/reports/{reportId}` needs the
  patient-scoped check anyway — the extra method is not avoided, only moved. It stays the fallback if the team prefers
  the literal reading; swapping the upload route before any code exists costs one route attribute.
- **Add `Report.AppointmentId`** so every report route is appointment-scoped. Rejected by ADR-0005: a report is
  record-level, and adding a visit FK is a new ADR plus a migration, not a side effect of MP-47.
- **Serve the files from `wwwroot` under unguessable names.** Rejected: FR-PRV-05 forbids public links outright, and a
  URL that leaks past the app (logs, browser history, a shared screenshot) would bypass the grant completely.
- **Trust the `Content-Type` header and the reported length.** Rejected: NFR-SEC-09 is verified by tests, and a client
  controls both values. The signature is read from the bytes and the size is counted while copying, so a lying
  declaration fails.

## Conflict check

- **ADR-0005** — amended as decision 8, not contradicted; the report stays record-level with `UploadedByUserId`
  attribution.
- **ADR-0004** — `ReportUploaded` and `ReportDownloaded` already exist as audit actions; no new action, no new entity
  type.
- **ADR-0011** — unchanged. The new lookup only *finds* grants; the lifecycle, the stored triple and the terminal
  states are the same code path.
- **ADR-0007 decision 5** — the storage root follows the key-ring rule (absolute, outside the repository) with
  `wwwroot` added, because a file under the static root is a public link.
- **FR-ACS-10** — the second sentence conflicts with a record-id route. Recorded in Consequences; the team decides.
- **FR-ACS-11** — respected: the lookup returns active grants only, so a new visit still needs a new approval and an
  ended grant never authorizes an upload.
