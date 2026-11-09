# ADR-0006: HTTP integration tests with WebApplicationFactory

- **Status:** Accepted (2026-11-09) — Testcontainers SQL Server is the integration-test database.
- **Date:** 2026-10-29
- **Affects:** `tests/`, CI, every endpoint PR

## Context

NFR-SEC-10 requires a negative test for **100% of medical endpoints**, and the KPIs measure "0 successful medical
requests without an active grant". The repo currently has two placeholder unit tests and no way to exercise HTTP,
authorization or the database. Unit tests cannot cover 401/403/grant behaviour.

## Decision

1. Add `tests/MediPassPlus.Api.IntegrationTests` (xUnit, `Microsoft.AspNetCore.Mvc.Testing`) and register it in
   `MediPassPlus.slnx` so the existing `dotnet test` CI step picks it up.
2. `WebApplicationFactory<Program>` with real HTTP through `HttpClient` — no controller unit-mocking for authorization.
   This requires `public partial class Program { }` at the end of `Program.cs` (top-level statements).
3. Each test class gets an isolated database and a test configuration: `Jwt:Key`, an ephemeral Data Protection ring,
   and rate limits raised or disabled except in the limiter tests.
4. **Mandatory authorization matrix** per medical endpoint: no token → 401; wrong role (staff/admin, other doctor) →
   403; no grant → 403; expired grant → 403; revoked/ended grant → 403; consumed credential → 400/403;
   foreign appointment id → 403/404 without leaking existence.
5. **Database strategy:** Testcontainers SQL Server is the integration-test database. It gives provider parity with
   development and production, covers filtered indexes, column types, provider error behavior and migration SQL, and
   works on the Ubuntu CI runner where Docker is available.
   - SQLite remains the fast persistence-test provider in `MediPassPlus.Infrastructure.Tests`; it is not used for the
     HTTP integration matrix.
   - *Rejected:* the EF InMemory provider — it does not enforce relational constraints.
   - *Rejected:* LocalDB — Windows-only, breaks the Ubuntu CI job.
6. Assertions must prove what left the server: no medical content in error bodies, problem details shaped consistently,
   and audit rows created for the successful read/write paths.

## Consequences

- The CI backend job stays `dotnet build/test -c Release`; no workflow change is required beyond the project joining
  the solution. Docker is available on GitHub-hosted Ubuntu runners for Testcontainers.
- Database-backed tests become the slowest step; keep the matrix focused on authorization, not CRUD breadth.
- The SQLite persistence tests stay as the fast layer. They are not evidence of SQL Server behavior; the Testcontainers
  HTTP suite is the provider-parity gate.
- A test-only factory must not weaken production code paths (no special-cased auth bypass in `Program.cs`).

## Conflict check

No conflict. This decision is required by NFR-SEC-10 and the "0 requests without a grant" KPI, and it fills the biggest
testing gap in the current repo.
