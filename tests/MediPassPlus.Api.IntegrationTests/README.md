# MediPassPlus.Api.IntegrationTests

HTTP integration tests that boot the real API pipeline with `WebApplicationFactory<Program>` and run against SQL
Server, as required by [ADR-0006](../../docs/architecture/decisions/0006-api-integration-tests.md) and NFR-SEC-10.

Every test class gets its own database: the suite creates a unique catalog per class, applies every EF Core migration
with `Database.Migrate`, seeds that class's own users and visit data, and drops the catalog when the class finishes. It
never touches the application database and no two classes share one (ADR-0006 decision 3).

`MediPassApiFactory` is therefore an xUnit class fixture: each HTTP test class declares
`IClassFixture<MediPassApiFactory>`, so the class owns its own catalog, API host and ephemeral Data Protection ring.
Classes stay in the `IntegrationTestCollection` (`api-integration`) so the fixed-window rate limiter and the
process-wide test configuration stay deterministic while one class runs at a time.

`DatabaseIsolationTests` proves the boundary directly: two live factory instances resolve two distinct catalogs and a
row written through one context is invisible to the other. `IsolationFirstClassTests` and `IsolationSecondClassTests`
prove the same boundary through real class fixtures: each class's catalog holds only its own marker row and a query
through its context returns zero rows for the other class's marker. The isolation classes fail if two classes are ever
pointed at one database.

## Run with Testcontainers (default)

Docker must be running. The suite starts
`mcr.microsoft.com/mssql/server:2022-latest` once per test run and each test class creates its own database on it.

```powershell
dotnet test tests/MediPassPlus.Api.IntegrationTests
```

## Run against an existing SQL Server

Use this on a machine without Docker. The login must be allowed to `CREATE DATABASE` and `DROP DATABASE`.

```powershell
$env:MEDIPASS_TEST_SQLSERVER = "Server=.;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False"
dotnet test tests/MediPassPlus.Api.IntegrationTests
```

The connection string points at the server and a database used only to reach it; each test class replaces the database
name with its own unique name.

## Notes

- `SmokeTests` calls the test-only `GET api/test/probe` endpoint (`ProbeTestController`) to prove 401 without a token
  and 200 for Patient, Doctor, Staff and Admin.
- Test-only controllers live in this assembly only and are excluded from the generated Swagger document.
- The JWT signing key and the access-code HMAC key are generated at runtime and set only in the test process; no secret
  is committed (NFR-SEC-08).
- Each factory writes an ephemeral Data Protection key ring under the temp path, separate from the JWT and access-code
  keys, and deletes it when the class finishes (ADR-0006 decision 3, ADR-0007 decision 5).
- Rate limits keep the shipped configuration: every test creates GUID-unique accounts, so no partition is shared
  across tests, and the limiter tests are the only ones that exercise the budget explicitly.
- The host runs as the `Testing` environment, so the development demo seeder never runs. Default roles are still seeded
  for every environment, and each test creates only the accounts it needs.
- SQLite stays the fast persistence-test provider in `MediPassPlus.Infrastructure.Tests`; these HTTP tests use SQL
  Server for provider parity.
