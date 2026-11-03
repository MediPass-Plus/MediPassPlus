# Validation and the shared error format

Every module answers a bad request the same way. This page is the short guide MP-1306 asks for; the reasoning lives in
[ADR-0010](../architecture/decisions/0010-validation-and-error-format.md).

## The two rules

- **NFR-SEC-06** — every write endpoint validates its input.
- **NFR-REL-04** — errors are handled in one place, in one format, with no stack traces.

## Add a validator (one example)

1. Put the validator next to the contract it validates, usually in `src/MediPassPlus.Application`:

```csharp
public sealed class SampleRequestValidator : AbstractValidator<SampleRequest>
{
    public SampleRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(100);
        RuleFor(request => request.Email).NotEmpty().EmailAddress();
    }
}
```

2. If your contract lives in a different assembly, register that assembly in `Program.cs`:

```csharp
builder.Services.AddValidatorsFromAssemblyContaining<SampleRequestValidator>();
```

3. Done. The global `ValidationFilter` picks up every `IValidator<T>` automatically; no controller code is needed.

> A **missing or `null` body** never reaches the action when a validator is registered for its declared type: the
> filter returns **400** with `"{ \"request\": [\"'request' must not be empty.\"] }"`.

## What the client receives

A failing rule returns **400** with `ValidationProblemDetails`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Email": ["'Email' is not a valid email address."]
  },
  "traceId": "0HN..."
}
```

An unhandled exception returns **500** with a generic `ProblemDetails` — no exception type, message, or stack trace:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.6.1",
  "title": "An unexpected error occurred.",
  "status": 500,
  "traceId": "0HN..."
}
```

## Status codes

| Code | When |
|---|---|
| 400 | Validation failure, malformed body |
| 401 | Missing, invalid or expired token (once ADR-0003 lands) |
| 403 | Wrong role, no active grant, expired or ended grant |
| 404 | Unknown resource |
| 429 | Rate limited (ADR-0007) |
| 500 | Unhandled exception, generic body only |

## Rules for the message text

- Describe the **field and the rule**, never medical content, tokens, codes, passwords or file bytes
  (AGENTS.md §5, ADR-0004 §3).
- Do not return stack traces or exception messages.
- Do not invent a second error envelope: everything is `ProblemDetails`.
