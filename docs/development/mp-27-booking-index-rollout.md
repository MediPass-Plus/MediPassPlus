# MP-27 booking index rollout

The merged migration `20261109165310_EnforceSingleActiveAppointmentPerSlot` adds a filtered unique index:

- name: `IX_Appointments_DoctorSlotId_Active`
- table: `clinical.Appointments`
- column: `DoctorSlotId`
- filter: `[Status] <> 'Cancelled'`

The filter includes every status except `Cancelled` (`Booked`, `CheckedIn`, `InVisit`, `Completed`), so the index
makes a second non-cancelled appointment for the same doctor slot impossible. It also makes
`dotnet ef database update` fail with a duplicate-key error on any existing database that already contains two or more
non-cancelled appointments for the same `DoctorSlotId`. A fresh database is not affected, and the integration-test
database is created fresh for every run.

## Why this is a runbook, not a migration

`EnforceSingleActiveAppointmentPerSlot` is already merged. ADR-0009 forbids editing a merged migration, and a later
migration cannot repair a failure in an earlier one: `dotnet ef database update` stops at the failing `CreateIndex`
and never reaches the next migration. The safe fix is to clean the data before rerunning the migration.

## Before updating

Run this read-only query against every database that has appointments:

```sql
SELECT DoctorSlotId, COUNT(*) AS ActiveCount
FROM clinical.Appointments
WHERE Status <> 'Cancelled'
GROUP BY DoctorSlotId
HAVING COUNT(*) > 1;
```

- No rows: run the normal update.
- One or more rows: inspect and resolve each group before updating.

Use this query to see the rows that need a decision:

```sql
SELECT a.Id, a.DoctorSlotId, a.PatientId, a.DoctorId, a.Status, a.CreatedAtUtc
FROM clinical.Appointments AS a
WHERE a.DoctorSlotId IN (
    SELECT DoctorSlotId
    FROM clinical.Appointments
    WHERE Status <> 'Cancelled'
    GROUP BY DoctorSlotId
    HAVING COUNT(*) > 1
)
ORDER BY a.DoctorSlotId, a.CreatedAtUtc, a.Id;
```

## Resolving duplicates

The right decision depends on the data. Cancelling or rescheduling the extras is the safest option: visit-access
rows (`AccessRequest`, `AccessGrant`) use `DeleteBehavior.Restrict`, so deleting an appointment that already has a
request or grant fails. For disposable developer rows with no visit-access history, deleting is also acceptable. Do
not change a row blindly, and take a backup before changing data in any shared database.

The template below is the less destructive option: it keeps the earliest-created appointment per slot and cancels
the rest.

```sql
-- Review the detail query first. This changes Status; it does not delete rows.
WITH Ranked AS (
    SELECT Id,
           ROW_NUMBER() OVER (PARTITION BY DoctorSlotId ORDER BY CreatedAtUtc, Id) AS rn
    FROM clinical.Appointments
    WHERE Status <> 'Cancelled'
)
UPDATE a
SET Status = 'Cancelled'
FROM clinical.Appointments AS a
INNER JOIN Ranked AS r ON r.Id = a.Id
WHERE r.rn > 1;
```

`Status` is stored as text, so `'Cancelled'` must match the EF conversion exactly. After cleanup, rerun the duplicate
query; it must return no rows.

## Apply and verify

From the repository root:

```powershell
dotnet ef database update --project src/MediPassPlus.Infrastructure --startup-project src/MediPassPlus.Api
```

Verify the index exists:

```sql
SELECT COUNT(*) AS IndexCount
FROM sys.indexes
WHERE name = 'IX_Appointments_DoctorSlotId_Active'
  AND object_id = OBJECT_ID('clinical.Appointments');
```

`IndexCount` must be `1`. Do not edit the merged migration or add a later migration to repair this failure; a later
migration cannot run while this one is failing.
