# ADR-0008: OpenAPI and XML documentation as a delivery requirement

- **Status:** Accepted (2026-10-29). Swagger UI is approved as a Development tool; production exposure stays off by
  default.
- **Date:** 2026-10-29
- **Affects:** every endpoint PR, README, CONTRIBUTING, `.csproj` build settings, `Program.cs`, `appsettings.json`

## Context

NFR-MNT-03 requires every endpoint documented in Swagger/OpenAPI, including the JWT scheme, and the Definition of Done
in both CONTRIBUTING.md and the planning document repeats it. Before this decision the API exposed `/openapi/v1.json`
in Development only, had no XML documentation enabled, no JWT security scheme, and the README advertised `/swagger`
which returned 404 because no UI package was installed.

Two things had to be settled: whether the team gets a browsable UI, and how the document stays honest about
authentication.

## Decision

1. **One document generator.** `Microsoft.AspNetCore.OpenApi` stays the only generator. Swashbuckle's
   `SwaggerGen` is **not** added: two generators produce two contracts that drift apart.
2. **UI.** `Swashbuckle.AspNetCore.SwaggerUI` **10.3.1** (middleware + packaged swagger-ui assets only) serves the UI.
   It is compatible with `net10.0` (the package ships `lib/net10.0`) and with `Microsoft.AspNetCore.OpenApi` 10.0.13.
   It reads the document over HTTP, so it needs no generator and no extra configuration contract.
   - UI: `/swagger` (redirects to `/swagger/index.html`)
   - Document: `/openapi/v1.json` (document name `v1`)
3. **Exposure.** The document **and** the UI are mapped when
   `app.Environment.IsDevelopment() || OpenApi:ExposeOutsideDevelopment == true`. `appsettings.json` ships
   `"OpenApi": { "ExposeOutsideDevelopment": false }`, so a deployment has to opt in deliberately through configuration
   (or an environment variable such as `OpenApi__ExposeOutsideDevelopment`). Verified: in `Production` both routes
   return 404; with the flag set to `true` both return 200.
   Nothing in the pipeline calls `UseSwagger()`/`UseSwaggerUI` unconditionally.
4. **JWT scheme.** A document transformer registers a `Bearer` `http` security scheme (`bearerFormat: JWT`) under
   `components.securitySchemes`, so Swagger UI renders the **Authorize** button. An operation transformer adds a
   `Bearer` security requirement to operations whose endpoint metadata contains `IAuthorizeData`; anonymous operations
   (login, register) stay unmarked. No endpoint is marked by hand, so the document cannot silently disagree with
   `[Authorize]`. Verified with a temporary `[Authorize]` endpoint: marked operation → `security: [{ "Bearer": [] }]`,
   anonymous operation → no `security` entry.
5. **XML documentation is mandatory** for every public controller and action that represents an endpoint, plus public
   DTOs/contracts where the shape is not obvious. `<GenerateDocumentationFile>true</GenerateDocumentationFile>` is
   enabled on `MediPassPlus.Api` with `CS1591` suppressed (missing comments are a review rule, not a build error),
   and the .NET 10 source generator folds the comments into the OpenAPI document with no extra wiring.
6. **Every action declares its responses**: `[ProducesResponseType]` for each status code, then `<response code="...">`
   for the description. A `<response>` tag alone does **not** add a status code for controllers.
7. **Document the failure contract:** 400 (validation), 401 (missing/invalid token), 403 (wrong role or no active
   grant), 404 (not found or hidden from this caller), 429 (rate limited).
8. **Docs drift is checked in the same PR:** the README's `/swagger` claim is now accurate (the UI exists); its
   `https://localhost:<port>` placeholder stays until a deployment URL exists. CONTRIBUTING's "document in Swagger"
   wording now points at `/openapi/v1.json` + `/swagger` locally.
9. PR checklist gains one line: "endpoint documented in OpenAPI with XML docs and declared responses".

## Consequences

- Any new authorized endpoint is documented as requiring a bearer token automatically; a reviewer can see the auth
  contract without reading code.
- The UI is developer convenience only. It is not part of the product surface, is off in Production by default, and no
  requirement depends on it.
- Swagger UI ships its own static assets (CSS/JS). No CDN is used, so the UI works offline.
- A missing summary no longer fails the build, so the checklist and review are the enforcement.
- XML docs must not leak operational details (no internal hostnames, no secrets) since the document is shareable.
- The UI version must be bumped with the rest of the ASP.NET Core tooling; it is pinned in
  `src/MediPassPlus.Api/MediPassPlus.Api.csproj`.

## Alternatives considered

- **Built-in OpenAPI JSON only, no UI** — smallest footprint, but worse for onboarding and for demoing endpoints during
  review. Rejected by the team decision of 2026-10-29.
- **Swashbuckle.AspNetCore (full, incl. `SwaggerGen`)** — would add a second document generator beside
  `Microsoft.AspNetCore.OpenApi`. Two generators is a maintenance trap: annotations, XML comments and schema handling
  would have to be configured twice and can disagree. Rejected.
- **Scalar.AspNetCore** — one package and a modern UI, but a larger dependency for the same result, and it does not
  reuse the Swagger UI assets already familiar to the team. Viable fallback if Swagger UI is dropped later.
- **Public exposure in production** — adds attack surface for zero requirement benefit (NFR-MNT-03 requires endpoints
  to be *documented*, not published). Rejected; the opt-in flag exists for a controlled demo only.

## Implementation notes

- Package: `Swashbuckle.AspNetCore.SwaggerUI` 10.3.1 in `src/MediPassPlus.Api/MediPassPlus.Api.csproj`.
- Wiring: `builder.Services.AddOpenApi("v1", ...)` in `Program.cs` registers
  `OpenApi/BearerSecuritySchemeTransformer.cs` as document and operation transformer; the pipeline maps
  `MapOpenApi()` + `UseSwaggerUI(...)` inside the exposure condition.
- The UI configuration is served at `/swagger/index.js` (the page loads that file; the stock
  `swagger-initializer.js` asset is unused).

## Conflict check

No conflict with requirements; the decision enforces NFR-MNT-03 from day one. It resolves the documentation drift
between README/CONTRIBUTING and the real endpoint surface.
