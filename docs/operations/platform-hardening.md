# Platform hardening: rate limits, CORS, response headers, HTTPS and secret hygiene

This page is the operations and review note for the HTTP-edge controls added by MP-51 (FR-PRV-07, NFR-SEC-01,
NFR-SEC-06, NFR-SEC-07, NFR-SEC-08) and for the numbers and switches that live in configuration. It does not repeat the
requirement text; it records what the running API actually does, so a reviewer can check a deployment against it.

The architecture is ADR-0007 (rate limiting and Data Protection), ADR-0010 (error envelope) and ADR-0013
(observability). Everything below is configuration or a small piece of middleware around an accepted decision; no new
architecture is introduced here.

## 1. Rate limits

Two independent layers exist, as ADR-0007 decision 1 requires. The per-credential attempt lockout is the authoritative
FR-ACS-05 rule; the limiter in front of it is defence-in-depth and never replaces or relaxes it. A limit therefore
never makes a lockout test pass — the lockout still fires on its own.

Every rejection is `429` with `Retry-After` and the shared `ProblemDetails` body, and carries no medical content, no
password, no code or token value and no account identifier (ADR-0007 decision 3, ADR-0010).

| Policy | Endpoints | Partition key | Default | Configuration |
|---|---|---|---|---|
| `Login` | `POST api/Auth/login` | normalized email + client address | 5 per 15 min | `RateLimiting:Login:PermitLimit`, `RateLimiting:Login:WindowMinutes` |
| `QrApprove` | approve/reject, by request id and by token | patient id | 5 per 15 min | `RateLimiting:QrApprove:*` |
| `CodeGeneration` | `POST api/appointments/{id}/access-code` | patient id | 3 per 15 min | `RateLimiting:CodeGeneration:*` |
| `CodeVerification` | `POST api/appointments/{id}/verify-code` | doctor id + appointment id | 5 per 15 min | `RateLimiting:CodeVerification:*` |
| `ChangePassword` | `POST api/me/change-password` | account id | 5 per 15 min | `RateLimiting:ChangePassword:*` |

The numbers are tunable after the security review ADR-0007 records as pending. Where a limit applies and how it is
keyed is not tunable without a new decision.

### The login partition

ADR-0007 decision 2 fixes the login partition as **email + IP**. That needs the email before the limiter runs, and the
limiter runs before MVC model binding, so `LoginRateLimitKeyMiddleware` reads the body for the login endpoint only,
bounded to 8 KiB, and rewinds the stream. Rules that follow from it:

- The email is normalized (trimmed, lower-cased) so `Patient@Example.com` and `patient@example.com` share one budget.
  Identity itself compares addresses case-insensitively; a case-sensitive limiter would let one account be guessed
  through a second spelling.
- A body that is missing, larger than the bound, not JSON, or has no `email` string falls back to the client address
  alone (`ip:{address}`). A malformed body can therefore not escape the limiter.
- The value is never logged.
- An identical-IP caller still spends one shared IP budget for unknown addresses; that is the intended trade-off and
  the reason a shared NAT does not throttle unrelated accounts (each account keeps its own email+IP bucket).

The partition is in-memory and per instance, which ADR-0007 decision 4 records as a single-instance scaling
limitation. Behind more than one instance, each replica enforces its own window.

One honest consequence of the default numbers: the login budget (5) and the lockout threshold (5) coincide, so the
sixth attempt from the same email and address is a `429` and never reaches Identity to be recorded or answered as a
`401` lockout. The lockout is still the authoritative state — the account was already locked by the fifth failure —
and a caller on another address, and therefore another partition, still receives the `401` lockout answer. Raising
`RateLimiting:Login:PermitLimit` above 5 makes the `401` visible from the original address as well; that is the reason
the shared integration host sets it to 100 so the lockout tests stay meaningful, while `LoginRateLimitTests` proves the
real `429` on a host configured with the production-equivalent budget.

## 2. CORS

Only the origins in `Cors:AllowedOrigins` may call the API from a browser (NFR-SEC-07). The default is the local
Angular server:

```json
"Cors": { "AllowedOrigins": [ "http://localhost:4200" ] }
```

- The list is validated at startup. A wildcard, a relative value, a value with a path, query or fragment, a
  non-HTTP scheme, a user-info component or an empty list fails startup instead of silently widening the policy.
- Credentials are not allowed; the API authenticates with a bearer token, not a browser cookie, so `AllowCredentials`
  is deliberately absent.
- A preflight is answered before authentication and rate limiting, so an `OPTIONS` request never spends a login budget
  and never reaches an action.
- The policy name is `Frontend`, applied once with `UseCors` in the pipeline.

## 3. Response headers

`SecurityHeadersMiddleware` registers the headers from the response's `OnStarting` callback, so every response keeps
them — including the rate limiter's `429` and the exception handler's `500` — and a value an endpoint already set is
never overwritten.

| Header | Value | Why |
|---|---|---|
| `X-Content-Type-Options` | `nosniff` | Stop the browser from guessing a content type |
| `X-Frame-Options` | `DENY` | No framing; also covered by `frame-ancestors 'none'` |
| `Referrer-Policy` | `no-referrer` | No URL, and therefore no record or appointment id, leaks to a third party |
| `Permissions-Policy` | `camera=(self)` | The QR scan helper needs the camera; no other origin does |
| `Content-Security-Policy` | `default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'` | Lock the document to the API's own origin |

`'unsafe-inline'` in `script-src` and `style-src` is deliberate and reviewable: Swagger UI and the camera helper
(`/visit-access-tool.html`) carry inline scripts and styles, and removing it would break both tools. Tightening it
means replacing the inline blocks in those development-only pages first, so it is a change to those pages, not a header
edit. `data:` and `blob:` in `img-src` are needed for the rendered QR data URI and the camera preview.

The Kestrel `Server` header is turned off, so the API does not advertise its product or version.

## 4. HTTPS and HSTS

`UseHsts()` runs in every environment **except Development**, where the local tools are served over plain HTTP. The
policy is a one-year `max-age`, `includeSubDomains`, and `preload` **off**: preload is a browser-vendor commitment that
needs a deliberate decision, not a side effect of a default. HSTS is only emitted on an HTTPS request.

Two exclusions are worth knowing before a review claims a bug:

- **Development** never sees the header, because `UseHsts()` is not called there. That keeps the local camera helper
  and Swagger UI usable over HTTP.
- **`localhost`, `127.0.0.1` and `[::1]`** are excluded by the framework itself (`HstsOptions.ExcludedHosts`), so a
  developer who serves local HTTPS does not pin HSTS in their own browser for every future local project. The API
  keeps that default. The integration test therefore asserts the header on a deployment-style host name and asserts
  its absence on `https://localhost`; both are intentional.

HTTPS redirection keeps its existing carve-out: `/health` answers its status instead of a `307`, because a probe has no
HTTPS client (ADR-0013).

## 5. Forwarded headers

`X-Forwarded-For` and `X-Forwarded-Proto` are **disabled by default**:

```json
"ForwardedHeaders": { "Enabled": false, "ForwardLimit": 1, "TrustAllProxies": false, "KnownProxies": [] }
```

Trusting them without a proxy in front lets any client claim any address and any scheme, which defeats every IP-keyed
limit — including the login partition above — and can make the API believe an HTTP request arrived over HTTPS. A
deployment behind a reverse proxy therefore has to say so:

- `Enabled: true` plus at least one `KnownProxies` entry (an IP address) is the normal setting.
- `TrustAllProxies: true` is only safe when the API is reachable exclusively through a network that strips
  client-supplied forwarded headers. It is an explicit, reviewed choice, never a convenience switch.
- `ForwardLimit` is the number of proxies that may append to the header chain; the default of 1 matches a single load
  balancer.
- Validation runs at startup: enabled with an empty trust list and `TrustAllProxies` off fails, and a value that is not
  an IP address fails.

When enabled, the middleware runs before anything reads the scheme or the client address, so HTTPS redirection, HSTS
and every IP-keyed limit see the address the proxy reports.

## 6. Secret hygiene

The rule is AGENTS.md §5: no connection string, JWT key, HMAC key, certificate or real patient data in Git. MP-51
closes the one place that disagreed with it.

- `src/MediPassPlus.Api/appsettings.Development.json` no longer carries a `ConnectionStrings:DefaultConnection`
  value. A committed connection string is a credential even when it uses Windows authentication, because it names an
  instance and a database and teaches the next reader to commit the next one. The connection string comes from
  user-secrets or an environment variable (`ConnectionStrings__DefaultConnection`).
- The three secrets and the ring path are listed in `deployment-keys.md`; that page, not this one, is the authority on
  what each one protects and where it comes from.
- `docs/development/onboarding.md` tells a developer how to confirm **which** keys are set without printing their
  values. `dotnet user-secrets list` prints secrets to the terminal and into any shared screen or recording; use the
  key-name-only form it shows.
- CI runs `gitleaks` on the working tree, on a pull request's commits, and on the full history of a push to `main`
  (ADR-0013). A finding fails the job; the fix is to rotate the secret, not to delete the line.
- Never log a secret, a code, a token, a password or a body. The logging contract is
  `docs/development/logging.md`.

## 7. Review checklist

- [ ] `RateLimiting:Login` is set to an approved value for the environment, and the environment is not relying on the
      default by accident.
- [ ] `Cors:AllowedOrigins` lists exactly the deployed frontend origin, with no wildcard and no path.
- [ ] `ForwardedHeaders:Enabled` matches the deployment: `false` with no proxy, otherwise a named proxy list.
- [ ] HSTS is reachable: the deployment serves HTTPS directly or the proxy sets `X-Forwarded-Proto` and the trust list
      is configured.
- [ ] Nothing in the deployment's `appsettings*.json` holds a secret; every secret comes from the secret store.
- [ ] A `gitleaks` run over the branch is clean, and any past finding has been rotated rather than only removed.
