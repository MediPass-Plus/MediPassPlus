# Architecture Decision Records

One file per decision. Status values: **Proposed** (awaiting a decision), **Accepted**, **Superseded**, **Rejected**.

These ADRs were validated against the requirements and planning documents in `Documentations/` (the tracked `.docx`
is the submitted deliverable; a local Markdown copy may exist next to it), the PNGs in `Diagrams/`, and the code as of
2026-10-29. Where a decision conflicts with a documented requirement, the conflict is recorded inside the ADR and must
be resolved by the team, not silently.

| ADR | Title | Status | Blocks |
|---|---|---|---|
| [0001](0001-separate-visit-access-credentials.md) | Separate QR token and 6-digit code credentials | **Accepted** | Visit Access entities |
| [0002](0002-code-generation-and-verification-actors.md) | Code generation and verification actors (doctor-only) | **Accepted** | Visit Access endpoints |
| [0003](0003-identity-and-jwt-authentication.md) | ASP.NET Core Identity with JWT authorization | Proposed — user model + refresh tokens | Accounts, every authorized endpoint |
| [0004](0004-audit-logging.md) | Audit logging scope and controlled values | **Accepted** | Record, Privacy, Visit Access |
| [0005](0005-diagnosis-relationships.md) | Diagnosis → record and appointment relationships | Proposed — revised, awaiting review | Medical Record entities |
| [0006](0006-api-integration-tests.md) | HTTP integration tests with WebApplicationFactory | Proposed — Testcontainers not approved | Authorization test matrix (NFR-SEC-10) |
| [0007](0007-rate-limiting-and-data-protection.md) | Rate limiting and Data Protection keys | **Accepted** (values under security review) | Platform, Visit Access, Record |
| [0008](0008-openapi-and-xml-documentation.md) | OpenAPI, Swagger UI and XML documentation | **Accepted** | Every endpoint PR |
| [0009](0009-database-baseline-strategy.md) | Coordinated database baseline, then serialized migrations | Proposed — owner not named | All schema work |

## How to use these records

- Read the ADR that governs the area before you change architecture, entities, authorization, audit, rate limiting,
  OpenAPI or the database.
- An **Accepted** ADR is binding. Changing it means writing a new ADR that supersedes it, in the same PR as the code.
- A **Proposed** ADR is not binding yet. If your task depends on it, stop and ask for the decision; if your task is
  independent of it and does not contradict it, proceed.
- Never contradict an ADR silently, and never mark one Accepted because it contains a recommendation — acceptance is a
  team decision, recorded here by the person who took it.

## Decisions still needed outside these ADRs

- User model shape: TPH subclasses of `ApplicationUser` vs profile tables (ADR-0003, open).
- Refresh-token storage and rotation (ADR-0003, open).
- Integration-test database: Testcontainers SQL Server vs SQLite in-memory (ADR-0006, open).
- Lockout duration, since the requirements only say "for a period" (ADR-0003, assumption: 15 minutes).
- Named owner for the baseline migration and for the serialized-migration rule (ADR-0009, open).

Resolved on 2026-10-29: code verification is doctor-only (ADR-0002); `Diagnosis.AppointmentId` is required
(ADR-0005 revision); Swagger UI is a Development tool and the document is off in Production by default (ADR-0008).
