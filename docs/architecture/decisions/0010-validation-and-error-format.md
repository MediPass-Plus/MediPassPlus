# ADR-0010: Input validation with FluentValidation and one ProblemDetails error format

- **Status:** Accepted (2026-11-05). Merged in MP-1306 (#12); the open questions below are follow-ups, not blockers.
- **Date:** 2026-11-02
- **Affects:** every endpoint PR, `MediPassPlus.Api`, `MediPassPlus.Application`, the API error contract

## Context

NFR-SEC-06 requires input validation on **every write endpoint** and safe queries. NFR-REL-04 requires errors to be
handled **in one place**, with a consistent error format and **no stack traces** shown to users. `AGENTS.md` §7
already fixes the envelope: "Return `ProblemDetails` for errors; do not invent a second error envelope."

The repository has no validation mechanism and no central error handling today: `Program.cs` only calls
`AddControllers()` and `UseAuthorization()`, and an unhandled exception currently produces the framework default
response. No Accepted ADR covers how validation is expressed.

MP-1306 asks the team to **pick the validation approach**, write one example, add the shared error handler, never
return stack traces, and document how to use it.

## Decision

1. **Validation library: FluentValidation 12.2.0** (MIT-free, Apache-2.0), with
   `FluentValidation.DependencyInjectionExtensions` 12.2.0 for assembly scanning. Both target `net8.0` and run on
   `net10.0`.
   - `FluentValidation.AspNetCore` is **not** used: its automatic validation is legacy and duplicates the filter below.
2. **Validators live next to the contract they validate** (typically `MediPassPlus.Application`), and are registered
   with `AddValidatorsFromAssembly`. Each module registers its own assembly in `Program.cs`.
3. **One global MVC filter** (`MediPassPlus.Api/Validation/ValidationFilter.cs`) runs before every action. For each
   declared action parameter it resolves `IValidator<T>`; if a validator exists and fails, the request short-circuits
   with **400** and a `ValidationProblemDetails` body. Arguments without a validator are untouched, so the filter is a
   no-op for endpoints that do not opt in. A **missing or null** argument is rejected the same way (`'{name}' must not
   be empty.`) instead of silently reaching the action.
4. **One exception handler** (`MediPassPlus.Api/Errors/ApiExceptionHandler.cs`, `IExceptionHandler`) turns any
   unhandled exception into **500** with a generic `ProblemDetails`. The response contains no exception type, message
   or stack trace; the exception is logged server-side with a trace identifier only.
5. **Single error envelope** — RFC 9110 `ProblemDetails` everywhere:
   ```json
   {
     "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
     "title": "One or more validation errors occurred.",
     "status": 400,
     "errors": { "Email": ["'Email' is not a valid email address."] },
     "traceId": "0HN..."
   }
   ```
   Client mistakes are **400**; the framework's model-binding failures remain **400**; missing/invalid credentials are
   **401**; wrong role, missing grant, expired or ended grant are **403**; unknown resources are **404**; rate limiting
   is **429** (ADR-0007). Every authorized endpoint documents these with `[ProducesResponseType]` and `<response>`
   (ADR-0008).
6. **No stack traces, ever.** `ASPNETCORE_ENVIRONMENT=Development` may show the developer exception page (framework
   behaviour), but a deployed environment must not; the handler guarantees the response body shape regardless.
7. **Privacy:** error messages describe the **field and rule**, never medical content, tokens, codes or file bytes
   (AGENTS.md §5, ADR-0004 §3).

## Consequences

- Every module gets the same validation entry point and the same error shape without copying code per controller.
- A validator must be written and registered for each new request contract; forgetting one means the endpoint silently
  accepts unvalidated input. The review checklist must ask for it.
- FluentValidation is a new dependency in `Application`; `Api` also references it for the filter and the exception
  handler.
- The filter resolves validators per action argument by runtime type; polymorphic request bodies resolve the declared
  type, which is acceptable for this project.
- Tests can cover the validator, the filter and the handler with plain unit tests, without the HTTP test project that
  ADR-0006 still leaves open.

## Alternatives considered

- **DataAnnotations only** (`[Required]`, `[EmailAddress]`) — no package, but conditional and cross-field rules become
  attribute classes, and the team explicitly chose FluentValidation for MP-1306.
- **Manual validation inside each action** — one rule per endpoint, guaranteed drift, and the opposite of NFR-REL-04's
  "handled in one place".
- **`FluentValidation.AspNetCore` auto-validation** — deprecated path, no benefit over a filter that already uses the
  shared `ProblemDetails` envelope.

## Open questions

- Should the 500 handler log the full exception (stack trace) server-side, or only the type and trace identifier? The
  current implementation logs the exception object; medical content must never appear in an exception message.
- Whether the API should also expose a machine-readable `errorCode` extension for clients (not required today).

## Conflict check

No conflict with an Accepted ADR. It implements NFR-SEC-06 and NFR-REL-04 and reuses the `ProblemDetails` envelope
that `AGENTS.md` §7 already mandates. It does not depend on ADR-0003 (identity), ADR-0005 (diagnosis), ADR-0006
(integration tests) or ADR-0009 (database baseline), all of which remain Proposed.
