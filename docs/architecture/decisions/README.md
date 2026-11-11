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
| [0003](0003-identity-and-jwt-authentication.md) | ASP.NET Core Identity with JWT authorization | Proposed — user model + refresh tokens; see implementation review | Accounts, every authorized endpoint |
| [0004](0004-audit-logging.md) | Audit logging scope and controlled values | **Accepted** | Record, Privacy, Visit Access |
| [0005](0005-diagnosis-relationships.md) | Diagnosis → record and appointment relationships | Proposed — revised, awaiting review | Medical Record entities |
| [0006](0006-api-integration-tests.md) | HTTP integration tests with WebApplicationFactory | **Accepted** (2026-11-09; Testcontainers SQL Server) | Authorization test matrix (NFR-SEC-10) |
| [0007](0007-rate-limiting-and-data-protection.md) | Rate limiting and Data Protection keys | **Accepted** (values under security review) | Platform, Visit Access, Record |
| [0008](0008-openapi-and-xml-documentation.md) | OpenAPI, Swagger UI and XML documentation | **Superseded** by ADR-0012 | Every endpoint PR |
| [0009](0009-database-baseline-strategy.md) | Coordinated database baseline, then serialized migrations | **Accepted** (amended 2026-11-05) | All schema work |
| [0010](0010-validation-and-error-format.md) | Input validation with FluentValidation and one ProblemDetails error format | **Accepted** (2026-11-05) | Every endpoint PR, error contract |
| [0011](0011-access-grant-lifecycle.md) | Access grant lifecycle and status derivation | **Accepted** (2026-11-05; amended 2026-11-05 for composite scope enforcement) | Visit Access checker, grant endpoints |
| [0012](0012-swagger-single-document-generator.md) | Swashbuckle as the single Swagger document generator | **Accepted** (2026-11-11) | Every endpoint PR, Swagger configuration |

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
- Lockout duration, since the requirements only say "for a period" (ADR-0003, assumption: 15 minutes).

Resolved on 2026-10-29: code verification is doctor-only (ADR-0002); `Diagnosis.AppointmentId` is required
(ADR-0005 revision); Swagger UI is a Development tool and the document is off in Production by default (ADR-0008).
Resolved on 2026-11-05: the baseline is closed and migrations are serialized (ADR-0009); the validation/error format
(ADR-0010) and the access-grant lifecycle (ADR-0011) are accepted. Resolved on 2026-11-09: HTTP integration tests use
Testcontainers SQL Server (ADR-0006). Resolved on 2026-11-11: Swashbuckle is the single Swagger document generator
(ADR-0012). ADR-0003 remains open on the implementation gaps listed in that file.
