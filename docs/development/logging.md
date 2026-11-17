# Logging

The API uses the logging that ships with .NET. **Serilog is not installed**, and adding it needs approval
(`AGENTS.md` §2, ADR-0013). Every line written by the application goes through `ILogger<T>`, so structured state is
passed as named properties, never as an interpolated string.

## The correlation id

Every request gets one `X-Correlation-ID`:

- If the client sends a value of at most 64 characters from `[A-Za-z0-9._-]`, it is used as-is.
- Anything else — missing, too long, a comma-joined pair, a newline — is replaced with a 128-bit value written as
  32 hexadecimal characters.
- The resolved value is echoed in the response header, stored in `HttpContext.Items["CorrelationId"]`, and opened as
  a log scope named `CorrelationId` for the whole request.

The middleware is registered before the exception handler, so even an unhandled failure is logged inside the scope.
The value is validated on purpose: echoing an unchecked header would let a client inject newlines into the log stream.

## What may be logged

- Identifiers: `PatientId`, `DoctorId`, `AppointmentId`, `DiagnosisId`, `GrantId`, `RequestId`, `ClinicId`.
- The operation: HTTP method and route template, status code, the check or seeder name.
- Configuration state that is not a secret, for example "Smtp:Host is not configured".
- Counts, durations, and the outcome of a rule.

## What must never be logged

- Names, emails, phone numbers, addresses, or any other patient or staff detail (FR-PRV-08).
- Diagnosis text, prescriptions, dosages, notes, report content or file bytes.
- Passwords, access or refresh tokens, QR tokens, six-digit codes, reset tokens.
- Connection strings, JWT keys, the access-code HMAC key, the Data Protection ring.
- Request or response bodies, query strings, and headers. Log the id you resolved from the body instead.

The rule is the same one in `AGENTS.md` §5: **identifiers only**. A log line is operational telemetry, not a record
of the data.

## Format and levels

| Environment | Formatter | Why |
|---|---|---|
| Production (and `Testing`) | `json` with `IncludeScopes` | Machine-readable, one JSON object per line, searchable by property |
| Development | `simple` with `IncludeScopes` | Readable in the console |

The default level is `Information`; `Microsoft.AspNetCore` and `Microsoft.EntityFrameworkCore` are `Warning` so the
application's lines are not buried. A log scope is rendered only when the provider includes scopes, which is why both
formatters set `IncludeScopes` to `true`.

## Where the files are

The application writes to standard output. It does **not** create or rotate files: the .NET logging stack has no file
provider, and writing one means owning rotation, retention and concurrent-writer behaviour. The deployment captures the
output:

- Docker or Kubernetes: the container log driver.
- IIS: stdout logging or a log collector.
- systemd: `StandardOutput=journal`.

If the team needs in-process files, that is a new decision, not a configuration change (ADR-0013).

## Finding a request

Ask the user for the `X-Correlation-ID` of the failing request, then filter on `CorrelationId`. Every line from that
request — the endpoint, the refusal, the exception — carries the same value. The `traceId` in a `ProblemDetails` body
is the framework's `HttpContext.TraceIdentifier` and is a different value; the correlation id is the one to quote.

## The review checklist

Before a pull request that adds or changes a log call:

- [ ] The message is a template with named properties, not an interpolated string.
- [ ] Every property is an identifier, a count, a duration or a status.
- [ ] No patient detail, medical text, token, code, connection string or request body.
- [ ] The line is useful without the request body; if it is not, log the id and look the row up.
- [ ] The level is honest: `Information` for a completed step, `Warning` for a degraded but handled case, `Error` for
      an unhandled failure.

## What the tests enforce

- `LogRedactionTests` runs a real password-reset request through the pipeline, captures every line, and asserts that
  each one carries the correlation id and that none repeats the email, password or patient name.
- `CorrelationIdMiddlewareTests` covers the header contract: generated value, accepted value, over-long value,
  control characters, comma-joined values.
- `CorrelationIdEndpointTests` proves the header survives success, `401`, `404` and the exception handler.
