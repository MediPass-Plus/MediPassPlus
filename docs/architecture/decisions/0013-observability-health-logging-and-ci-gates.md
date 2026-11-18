# ADR-0013: Platform observability — built-in logging, correlation ids, `/health` and CI gates

- **Status:** Proposed (2026-11-17). Proposed in MP-54; acceptance is a team decision taken in review. Two parts need
  an explicit sign-off from the team: built-in logging (decision 1) and host-captured stdout as the interpretation of
  "console/file logging" (decision 2). The rest follows the requirements directly.
- **Date:** 2026-11-17
- **Affects:** `MediPassPlus.Api` (logging configuration, request pipeline, `/health`), `.github/workflows/ci.yml`,
  `scripts/check-coverage.ps1`, every pull request

## Context

The scope table asks for the platform observability that the requirements already name, without ever writing medical
content into a log:

- **NFR-REL-06** — the system exposes its health; a `/health` endpoint reports the state of the API and the database.
- **NFR-REL-02** — the system is free of server errors in normal use (HTTP 5xx under 1% of requests).
- **FR-PRV-08** — no medical content in logs; logs contain identifiers only.
- **NFR-SEC-08** — secrets never reach source control; a repository scan finds zero secrets.
- **NFR-MNT-02** — unit test coverage of 50% or more in the Domain and Application layers.

Today the API has no `/health` endpoint, no correlation id, no structured log format and no CI gate beyond
restore/build/test. `AGENTS.md` §2 records that Serilog is **not installed**, and the ticket forbids adding Serilog or
another package without approval. `AGENTS.md` §5 already forbids logging medical content, passwords, tokens, codes or
file bytes.

No accepted decision covers application logging or the health endpoint. ADR-0004 covers audit rows in the database,
which is a different concern: an audit row is a business record, a log line is operational telemetry.

## Decision

1. **Built-in logging only.** `Microsoft.Extensions.Logging` with the framework's console formatters. **Serilog is not
   added**, and no logging package is added. The JSON console formatter is the production format; the simple console
   formatter is the development format. Both include scopes, so structured state (not interpolated strings) reaches
   the output.
2. **File logging is the host's responsibility.** The framework has no file provider, and writing our own rotation,
   retention and concurrency code is infrastructure we would then own and test. The API writes structured output to
   standard output; the deployment (IIS, Docker, systemd) captures it to a file. `docs/development/logging.md` states
   this contract and the identifiers-only rule.
   **Interpretation (MP-54):** host-captured stdout is the accepted reading of "console/file logging" for this ticket.
   The repository ships no deployment manifest, so this decision adds no capture configuration; IIS, Docker and systemd
   capture stdout once a deployment exists. If the acceptance criteria require the application itself to write a file,
   that is a separate decision and a separate ticket, not an unapproved package.
3. **Correlation id.** A middleware reads `X-Correlation-ID`, accepts it only when it is at most 64 characters of
   `[A-Za-z0-9._-]`, otherwise generates a 128-bit value, echoes the resolved value in the response header, stores it
   in `HttpContext.Items` and opens a log scope named `CorrelationId`. The header is written from
   `Response.OnStarting`, because the exception handler clears the response before it writes `ProblemDetails`; an
   eagerly written header would be missing on every `500`. Rejecting hostile values is deliberate: an unvalidated
   header lets a client inject newlines into the log stream.
4. **`/health` is anonymous and minimal.** `GET /health` runs an `api` check (the process answered) and a `database`
   check (`Database.CanConnectAsync`), returns `200` when every check is healthy and `503` otherwise, sends
   `Cache-Control: no-store`, and writes **statuses only** — never a description, exception message, connection string,
   server name or duration. `Degraded` is treated as `503` on purpose: a probe that receives `200` cannot tell a
   degraded service from a healthy one. The endpoint is not part of the Swagger document: it is operational surface,
   not a product contract. It is exempt from the HTTPS redirect so a plain HTTP probe receives the status instead of a
   `307`.
5. **Exception logging keeps the type, not the message.** `ApiExceptionHandler` logs the exception type and stack trace
   with the trace and correlation ids. Outside Development it does not log the exception message, because a message can
   carry a patient name, a connection string or a parameter value — an EF Core or SQL Server failure often does.
   Development logs the whole exception (FR-PRV-08).
6. **CI gates.**
   - `gitleaks` (pinned CLI, no action license) scans the working tree and the pull request's own commits. A push to
     `main` additionally scans the whole history, so a secret merged before this job existed is still found. A pull
     request does not scan all history: an old secret would turn every PR red for a reason that PR cannot fix.
   - `scripts/check-coverage.ps1` reads the Cobertura report of `MediPassPlus.Domain.Tests` and
     `MediPassPlus.Application.Tests` and fails below **50% line coverage** for `MediPassPlus.Domain` and
     `MediPassPlus.Application` (NFR-MNT-02). It compares the raw rate, not the rounded one, and fails unless it finds
     exactly one report per project.
   - A negative control proves the pipeline actually fails on a failing test: an environment-gated test fails only
     when `MEDIPASS_CI_NEGATIVE=1`, the CI step fails when that test **passes**, and it also asserts that the failure
     is that test's own failure rather than a broken build or an empty filter.

## Consequences

- Zero new packages and zero migrations. The health check uses the shared framework and the existing `DbContext`.
- Logs are machine-readable and every request can be followed by one identifier. The team must use the
  `CorrelationId` scope when reading them.
- The application does not rotate or retain files; that moves to the deployment documentation. A team that needs
  in-process files must write and test a provider, which is a new decision.
- An unhandled failure is diagnosable by its type, stack trace and correlation id, not by its message. A message that
  must be recorded to understand a bug is a bug to fix, not a log line to add.
- **Open conflict:** NFR-REL-02 states that the 5xx rate is inspected through **Serilog logs**. This decision does not
  install Serilog, so the measurement is taken from the structured built-in logs instead. The requirement text and the
  repository status disagree; the team must resolve the wording, not this PR silently.
- The coverage gate measures the two projects that own the core logic. Coverage of `Api` and `Infrastructure` is not
  gated, because HTTP integration tests cover them through the real pipeline.

## Alternatives considered

- **Serilog with a console and file sink.** Rejected for this ticket: it is a package, `AGENTS.md` §2 records that it is
  not installed, and the ticket requires approval before adding it. Revisit if the team decides the built-in output is
  not enough.
- **`Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`.** Rejected: a package for one call to
  `Database.CanConnectAsync`, which the existing `DbContext` already exposes.
- **`gitleaks/gitleaks-action`.** Rejected: the action requires a licence for organizations. The pinned CLI has the
  same rules and no licence.
- **A separate CI probe test project.** Rejected: an environment-gated test in the existing unit test project proves
  the same property without a project that only CI builds.
