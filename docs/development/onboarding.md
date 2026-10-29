# Onboarding — your first day on MediPass+

This guide takes you from a fresh clone to a working local checkout and a correct first agent session. It does not
assume you have read anyone's chat history: everything you need is in the repository.

## 1. What you need

| Tool | Required version | Check it yourself |
|---|---|---|
| .NET SDK | `global.json` pins **10.0.400** with `rollForward: latestFeature` (a later 10.0.x feature band also works) | `dotnet --version` |
| Node.js | LTS (CI uses `lts/*`) | `node -v` |
| npm | 11.19.0 (`packageManager` in `frontend/package.json`) | `npm -v` |
| Git | any recent version | `git --version` |
| `dotnet-ef` | global tool matching the SDK — **only for migrations** | `dotnet ef --version`; install with `dotnet tool install --global dotnet-ef` |
| SQL Server | Express, LocalDB or a Docker container — needed as soon as code opens the database | connect with your client; run `dotnet ef database update` once a migration exists |
| Docker | Desktop — needed for Testcontainers integration tests after ADR-0006 | `docker info` |

Optional: Visual Studio 2022+ or VS Code with the C# Dev Kit for the backend, and any editor for the Angular frontend.

> These are team requirements, not a description of any particular machine. Run each check and report your own result:
> a command counts as verified only in the environment where it was actually run. If a check fails, fix it locally or
> ask — do not assume it works because someone else ran it.

## 2. One-time local setup

```powershell
git clone <repo-url> MediPassPlus
cd MediPassPlus
dotnet tool install --global dotnet-ef        # skip if `dotnet ef --version` already works
```

Secrets stay on your machine and never enter Git:

```powershell
# The Api project already has a UserSecretsId, so no `user-secrets init` is needed.
dotnet user-secrets set "ConnectionStrings:Default" "<your SQL Server connection string>" --project src/MediPassPlus.Api
dotnet user-secrets set "Jwt:Key" "<at least 32 random characters>" --project src/MediPassPlus.Api

dotnet user-secrets list --project src/MediPassPlus.Api     # confirm what is set, never paste the values anywhere
```

Secrets are local to each developer: user-secrets are not shared through Git, and one developer's store says nothing
about another's. Check your own store for the values your task needs — `dotnet user-secrets list --project
src/MediPassPlus.Api` — and add whatever is missing; do not assume it is empty, and do not assume it is complete. The
API still starts without them because no code opens the database yet, so set them when your task first touches the
database or authentication. The JWT signing key, the Data Protection keys and the access-code HMAC key are three
**different** secrets (ADR-0007).

## 3. Open the repository in OpenCode

1. `git checkout main && git pull` — start from the latest approved `main`, not from an old local branch.
2. Open the **repository root** (the folder containing `MediPassPlus.slnx`, not `src/` or `frontend/`) as the
   OpenCode workspace. The agent must be able to see `AGENTS.md`, `docs/`, `src/`, `tests/` and `frontend/`.
3. Do not open a subfolder: `AGENTS.md` is loaded from the workspace root, and a narrower root hides the ADRs.

## 4. `/init` — do not run it

`AGENTS.md` is curated by the team and is the shared contract. `/init` generates a fresh instruction file from the
code and would overwrite or shadow it.

- If a tool offers to generate instructions anyway, decline, or write the result to a scratch file.
- If the generated text contains a genuinely new fact, add that fact to `AGENTS.md` through a pull request — never
  replace the file wholesale, and never delete the security or approval rules.

## 5. Make the agent read the shared rules and the ADRs

`AGENTS.md` at the repository root is loaded automatically. Start every session with the onboarding prompt below so
the agent proves it read the rules, then keep the ADRs in context:

> Read `AGENTS.md`, then `docs/architecture/decisions/README.md`, then the ADRs for the module I name. List the rules
> that apply to me and the ADRs whose status is Proposed, then wait. Do not change any file.

Rules the agent is bound by (also in `AGENTS.md`):

- it reads the governing ADR **before** changing architecture, entities, authorization, audit, rate limiting or the
  database;
- it stops when the task depends on a **Proposed** ADR, and proceeds when the task is independent and does not
  contradict one;
- it plans and waits for approval before implementing a Jira task;
- it does not commit, push, merge, migrate or install packages without approval for that specific action.

## 6. Baseline check (no database required)

From the repository root:

```powershell
dotnet build --configuration Release
dotnet test --no-build --configuration Release
```

From `frontend/`:

```powershell
npm ci
npm run build
npm test -- --watch=false
```

Verified on 2026-10-29 (scaffold state, nothing implemented yet):

| Command | Actual result |
|---|---|
| `dotnet build --configuration Release` | Build succeeded, **0 warnings, 0 errors** |
| `dotnet test --no-build --configuration Release` | **2 tests, 2 passed**: 1 in `MediPassPlus.Domain.Tests`, 1 in `MediPassPlus.Application.Tests` |
| `npm run build` | "Application bundle generation complete", output in `frontend/dist/frontend` |
| `npm test -- --watch=false` | Vitest 5.0.5: **1 test file, 2 tests passed** |

These are placeholder tests. The numbers will change as modules land — treat them as a smoke check, not a target.

## 7. Run the API and the Swagger UI

```powershell
dotnet run --project src/MediPassPlus.Api --launch-profile http
```

| URL | What |
|---|---|
| <http://localhost:5114/swagger> | Swagger UI (Development only) |
| <http://localhost:5114/openapi/v1.json> | The generated OpenAPI document |
| <http://localhost:5114/WeatherForecast> | The sample endpoint |

The `https` profile listens on <https://localhost:7041> and <http://localhost:5114>.

Both the UI and the document are mapped only in Development or when `OpenApi:ExposeOutsideDevelopment` is `true`
(default `false` in `appsettings.json`). In any other environment they return 404 without the flag — that is correct,
not a bug.

**Authorizing a request in the UI.** Once login exists, call the login endpoint, copy the `accessToken` value from the
response, click **Authorize** at the top of the page, paste the raw token (no `Bearer ` prefix) and click Authorize
again. Operations that carry `[Authorize]` are marked with a padlock, because an OpenAPI transformer reads the
endpoint metadata — you never edit the document by hand. Today no endpoint is authorized, so the padlock is absent and
the button sends nothing; that changes as soon as the first `[Authorize]` endpoint lands.

## 8. Two prompts, two jobs

**A. Verify the checkout (once, or after a big pull).** Use this when you just want to know that your machine is
healthy. It is read-only:

> Perform a read-only check of this checkout. Confirm the current branch and `git status`, run
> `dotnet build --configuration Release`, `dotnet test --no-build --configuration Release` and, in `frontend/`,
> `npm run build` and `npm test -- --watch=false`. Report the exact commands and their real results, then list any
> ADRs that are still Proposed and any secret or database that is missing. Do not modify, stage or commit anything.

**B. Implement a Jira task.** Use `docs/development/task-prompt.md` for every ticket: copy it, fill in the
placeholders, and paste it as the first message of a new session.

Do not ask an agent for a full repository audit again. The audit lives in `AGENTS.md`, the ADRs and this guide.

## 9. When something is missing or broken

| Symptom | What to do |
|---|---|
| No secrets / no SQL Server | The API still starts and the build and tests pass. Anything that opens the database will fail until `ConnectionStrings:Default` is set. Say so in your report; do not hard-code a connection string |
| Build fails after pulling | Check `dotnet --version` against `global.json`, run `dotnet restore` once, and read the first error. If the failure is inside another module, report it — never "fix" it by reverting or rewriting someone else's code |
| `npm ci` fails | Check the Node version (`node -v`) against `frontend/package.json`; delete `node_modules` and retry once |
| Frontend test dies with `Failed to start forks worker` | Record the initial failure first. Then check for a stale Node/Vitest worker holding the workspace and that `node_modules` is complete (`npm ci` if unsure), and rerun the tests as a diagnostic step. Report the initial failure and the rerun result separately — a passing rerun does not erase the initial failure |
| `/swagger` returns 404 | Confirm `ASPNETCORE_ENVIRONMENT` is `Development` (`launchSettings.json` sets it for `dotnet run`). In another environment the routes are off by design |
| An ADR is still `Proposed` | Stop and ask the owner or the team leader for the decision. Do not implement the unresolved area and do not "temporarily" pick an option |
| `git status` shows changes you did not make | Leave them alone. Mention them in your report and continue with your own task |
| Unsure whether a package, migration or contract change is allowed | Ask first. Approvals are per action, not per session |

## 10. Ground rules you should not learn the hard way

- Never commit secrets, connection strings, keys or real patient data. Never paste a token or key into a chat.
- Never create or apply a migration, add a package, change a public endpoint contract, or commit/push/merge without
  explicit approval — one migration per PR, and never edit a merged migration.
- Never log medical content: identifiers only.
- Do not run `scripts/create-solution.ps1`; it is the one-time bootstrap and would recreate a stale `.sln`.
- `.opencode/` and `session-state/` are local workspace folders and are gitignored. Do not commit them.
