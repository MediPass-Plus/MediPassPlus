# ADR-0009: One coordinated database baseline, then serialized migrations

- **Status:** Proposed (blocked: no database migration owner has been named yet)
- **Date:** 2026-10-29
- **Affects:** all modules, CONTRIBUTING/README migration rules, CI

## Context

There are **no migrations** today (verified 2026-10-29). CONTRIBUTING.md and README both say "one migration per pull
request, never edit a merged migration". Six modules are developed in parallel, and every EF migration embeds a full
model snapshot, so six modules generating their first migration at the same time guarantees snapshot merge conflicts.
The planning document already assigns the database context and seed script to one owner (Nagham, §3.3) and calls for
"database schema agreed" in Sprint 1.

## Decision

1. **Contract-first entity freeze.** Each module merges its entities + `IEntityTypeConfiguration` classes **without a
   migration** (entity-only PRs, reviewable in isolation).
2. **One coordinated `InitialSchema` migration** is created once, after the Must entities exist (Identity/users,
   clinic/doctor/slot/appointment, access request/credential/grant, medical record/diagnosis/prescription/report,
   audit), by the named migration owner (Nagham), with all module owners reviewing the generated SQL. It lands in its
   own PR, contains only the migration + snapshot, and is the team's shared baseline.
3. **After the baseline: serialize.** One migration per PR, announced in the weekly technical sync, never two open
   migration PRs from different modules; rebase on `main` before regenerating.
4. **Schema layout** follows the documented separation: `identity`, `clinical`, `access`, `records`, `audit`
   (configured per entity with `ToTable(name, schema)`), so table names stay scoped and cross-module queries stay explicit.
5. **Drift guard:** CI (or a pre-PR local step) runs `dotnet ef migrations has-pending-model-changes`
   (EF Core 10 command, verified) so a merged entity change cannot slip past the snapshot.
6. **Seed data** is added by seeders (existing `ISeeder` contract), after the baseline, and stays idempotent.
7. `Program.cs` does not call `Migrate()`: developers and CI apply migrations explicitly
   (`dotnet ef database update --project src/MediPassPlus.Infrastructure --startup-project src/MediPassPlus.Api`).

## Consequences

- One large baseline PR, but a single snapshot that no module has to merge.
- Modules can implement endpoints before the baseline only if they do not need a real database; otherwise the baseline
  is a hard dependency for end-to-end runs — plan it before the first feature demo.
- The "one migration per PR" rule in CONTRIBUTING stays true for everything after the baseline; the baseline PR
  documents itself as the exception.

## Alternatives considered

- *Per-module first migrations* — rejected: parallel snapshot conflicts, six "first" migrations, unmanageable review.
- *`EnsureCreated` in development, migrations later* — rejected: drifts from production schema and breaks NFR-REL-03
  checks (unique constraints, filtered indexes).

## Conflict check

No conflict. It operationalizes the existing "one migration per PR" rule and the Sprint-1 "schema agreed" milestone.
