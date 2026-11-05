# ADR-0009: One coordinated database baseline, then serialized migrations

- **Status:** Accepted (2026-11-05, amended). The initial baseline has already landed; decisions 1-8 are binding.
- **Date:** 2026-10-29 (amended 2026-11-05)
- **Affects:** all modules, CONTRIBUTING/README migration rules, CI

## Context

This ADR was written on 2026-10-29, when the repository had **no migrations**. It proposed an entity-only freeze
followed by one coordinated `InitialSchema` migration, so six parallel modules would not generate six competing
"first" migrations and snapshots.

That window has closed. At amendment time the repository has five merged migrations:

`InitialCreate`, `AddRoleInApplicationUser`, `AddClinicalSchema`, `EnforceAppointmentDoctorSlotMatch`, and
`AddVisitAccessSchema`.

Retrofitting the original single-baseline plan would require squashing or replaying already merged migrations and
would break every developer database. The durable part of the decision is the rule that keeps parallel modules from
fighting over the shared snapshot after a baseline exists.

## Decision

1. **The baseline is closed.** The merged chain above is the shared baseline. Do not create a second `InitialSchema`,
   do not squash or replay merged migrations, and do not use `EnsureCreated()`.
2. **One migration per PR.** Every PR that changes the model includes exactly one migration and the updated
   `MediPassDbContextModelSnapshot.cs`. Entity and `IEntityTypeConfiguration` changes stay in the same PR as their
   migration. Never edit a migration that is already merged; a mistake is corrected with a new migration.
3. **Serialize migrations.** Announce a migration PR in the weekly technical sync. Do not keep two open migration PRs
   from different modules. Rebase on `main` before regenerating. If the snapshot conflicts, regenerate it from the
   model after the rebase; never hand-merge the snapshot. The team leader coordinates this serialization; a separate
   baseline owner is no longer needed.
4. **Schema layout.** Use the documented separations: `identity`, `clinical`, `access`, `records`, `audit`, configured
   per entity with `ToTable(name, schema)`. Table names stay scoped and cross-module queries stay explicit.
5. **Drift guard.** Run `dotnet ef migrations has-pending-model-changes` before opening a PR and add it to CI when the
   pipeline supports it, so a merged entity change cannot slip past the snapshot.
6. **Seed data.** Seeders implement the existing `ISeeder` contract, run after the schema exists, and stay idempotent.
   Never seed real patient data, secrets or production identifiers.
7. **No automatic migration.** `Program.cs` does not call `Migrate()` or `EnsureCreated()`. Developers and CI apply
   migrations explicitly:
   `dotnet ef database update --project src/MediPassPlus.Infrastructure --startup-project src/MediPassPlus.Api`.
8. **Safety review.** Read the generated SQL before applying it. Test the migration on an empty database and on a copy
   of an existing database. Prefer additive changes. A destructive change - drop, rename, type narrowing or data
   rewrite - needs a backup and explicit approval for that specific migration.

## Consequences

- The baseline is a fact, not a future step; new modules add migrations to the existing chain.
- The team still shares one snapshot, so migration PRs must be short-lived and serialized.
- The "one migration per PR" rule in CONTRIBUTING is now unconditional and applies to every module.
- A bad merged migration is fixed forward with a new migration, never by editing history.
- The first migration after a model change is cheap to review; a long migration PR is a warning sign.

## Alternatives considered

- **Retrofit the original single-baseline plan.** Rejected: it would require rewriting five merged migrations and
  invalidating every developer database.
- **`EnsureCreated` in development, migrations later.** Rejected: it drifts from the production schema and breaks
  NFR-REL-03 checks (unique constraints, filtered indexes).
- **Let each module generate migrations independently without serialization.** Rejected: the shared snapshot produces
  merge conflicts and broken migration chains.

## Conflict check

No conflict with a requirement. The amendment records the baseline that actually landed and keeps the original
serialization intent. It does not change ADR-0003 (Identity), ADR-0006 (integration tests) or ADR-0011 (grant
lifecycle).
