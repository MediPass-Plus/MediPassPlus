# MediPassPlus.Api.IntegrationTests

HTTP integration tests that boot the real API pipeline with `WebApplicationFactory<Program>` and run against SQL
Server, as required by [ADR-0006](../../docs/architecture/decisions/0006-api-integration-tests.md) and NFR-SEC-10.

The suite creates a unique database, applies every EF Core migration with `Database.Migrate`, seeds its own
users and visit data, and drops the database when the run ends. It never touches the application database.

`MediPassApiFactory` is the collection fixture for `IntegrationTestCollection` (`api-integration`). Every HTTP test
class in the collection shares one host and one database, and the classes run serially so the fixed-window rate
limiter stays deterministic. Admin authorization (`MP-29`) and the visit-access approve/reject endpoints (`MP-4`)
both use that harness.

## Run with Testcontainers (default)

Docker must be running. The suite starts
`mcr.microsoft.com/mssql/server:2022-latest` once per test run.

```powershell
dotnet test tests/MediPassPlus.Api.IntegrationTests
```

## Run against an existing SQL Server

Use this on a machine without Docker. The login must be allowed to `CREATE DATABASE`.

```powershell
$env:MEDIPASS_TEST_SQLSERVER = "Server=.;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False"
dotnet test tests/MediPassPlus.Api.IntegrationTests
```

The connection string points at the server and a database used only to reach it; the suite replaces the database name
with its own unique name.

## Notes

- The JWT signing key is generated at runtime; no secret is committed (NFR-SEC-08).
- The host runs as the `Testing` environment, so the development demo seeder never runs. Default roles are still seeded
  for every environment, and each test creates only the accounts it needs.
- SQLite stays the fast persistence-test provider in `MediPassPlus.Infrastructure.Tests`; these HTTP tests use SQL
  Server for provider parity.
