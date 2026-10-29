# ADR-0004: Audit logging — scope, values and immutability

- **Status:** Accepted (2026-10-29). The value list below is the agreed one; additions need a new ADR amendment.
- **Date:** 2026-10-29
- **Affects:** Privacy/Audit module, Medical Record, Visit Access, `AuditEntry` schema

## Context

FR-PRV-02 requires an audit entry for **every read and write of medical data** (user, patient, action, data type,
time, IP address). NFR-PRI-05 requires 100% of reads and writes to appear in "Who viewed my record". The class diagram's
`AuditLog.action` and `entityType` are free strings, which would make queries and tests fragile.

## Decision

1. **Audit targets:** every medical read and write, **including the patient viewing their own record** (FR-PRV-02
   wins over the narrower UC-20 narrative). Also audit access-control events that create or destroy access:
   grant created, request rejected, access ended.
2. **Controlled values** (enums persisted as strings, no free text):
   - `AuditAction`: `RecordRead`, `RecordWritten`, `DiagnosisCreated`, `PrescriptionCreated`, `ReportUploaded`,
     `ReportDownloaded`, `AccessGrantCreated`, `AccessRequestRejected`, `AccessEnded`.
   - `AuditEntityType`: `MedicalRecord`, `Diagnosis`, `Prescription`, `Report`, `AccessRequest`, `AccessGrant`.
3. **Never audited/logged:** medical content, passwords, token or code hashes, plaintext codes, file bytes.
   Auth events (login failures, lockout) go to application logs, not the patient-visible audit.
4. **Reading the audit list is not itself audited** (avoids recursion); document this in the API docs.
5. **Entry fields:** `Id`, `UserId`, `PatientId`, `Action`, `EntityType`, `EntityId`, `OccurredAt` (UTC,
   `DateTimeOffset`), `IpAddress`, optional `AccessGrantId` for traceability.
6. **Immutability:** no update/delete endpoints; an EF `SaveChanges` interceptor throws on `Modified`/`Deleted` audit
   entries; production DB permissions deny `UPDATE`/`DELETE` on the audit table (NFR-PRI-03).
7. **Transactionality:** the audit row for a medical **write** commits in the same transaction as the write; a read is
   audited in its own committed insert before the response is returned.
8. **IP capture:** rely on the connection IP; behind a proxy the API enables `ForwardedHeaders` with explicit known
   proxies so the recorded address is meaningful.

## Consequences

- The patient's own reads appear in "Who viewed my record"; the UI should label them (`RecordRead` by self) instead of
  hiding them, because NFR-PRI-05 is measured by test.
- Enums stored as strings keep the DB readable; changing a value later is a breaking change for existing rows.
- A `Diagnosis` write produces one `DiagnosisCreated` entry; prescriptions are recorded as `PrescriptionCreated`
  (one per created prescription) so "what changed" stays answerable.

## Conflict check

No conflict with requirements. It **resolves** the open ambiguity about auditing patient self-reads in favour of
FR-PRV-02 / NFR-PRI-05. It adds audit values for access-control events, which UC-12 (A2 rejection) and UC-19 (end
access) already require.
