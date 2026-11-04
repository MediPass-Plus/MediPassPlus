# Reusable Jira task prompt

Copy everything below the line, fill in the placeholders, and paste it as the first message of a new session.
Replace every `__` before sending; an unfilled placeholder is a stop sign for the agent.

---

## Task

- **Jira key:** MP-__
- **Module:** __ (accounts | booking | staff-admin | visit-access | record | privacy | chat | infra)
- **Title:** __
- **Description:** __
- **Acceptance criteria:** __ (list them exactly as written in Jira)
- **Dependencies / parent epic:** __
- **Design links:** __ (Figma/sequence diagram/ADR, or "none")

## Step 1 — Ground yourself (no code changes)

Read, in this order:

1. `AGENTS.md`.
2. `docs/architecture/decisions/README.md`, then every ADR that touches this module or the area I named.
3. `Documentations/MediPass_Requirements_Gathering.docx` — the requirement IDs this task implements.
4. `CONTRIBUTING.md` and `.github/pull_request_template.md` for branch, commit and PR rules.

## Step 2 — Report before implementing, then wait

Give me, in one message:

- the requirement IDs (FR/NFR/US/UC) this task implements, with file and section;
- the ADRs that apply, and any conflict between this task and an accepted ADR;
- the status of any ADR this task depends on. If it is **Proposed**, stop and ask for the decision. If it does not
  depend on it and does not contradict it, say so and continue;
- prerequisite tasks, other modules touched, API-contract impact, and whether a migration is needed;
- the implementation plan, the files you expect to create or modify, and the tests you will add;
- anything ambiguous that needs my decision.

**Do not write code until I answer with an explicit "approved".**

## Step 3 — Approval gates

Ask separately, and wait for a separate yes, before any of these:

- creating or applying an EF Core migration (one migration per PR, ADR-0009; never edit a merged migration);
- adding, removing or upgrading a NuGet/npm package;
- changing a public API contract (route, verb, request/response shape, status codes);
- renaming or deleting an entity, column or table;
- any change outside the module named above.

These are always forbidden without a new, explicit instruction: `git commit`, `git push`, `git merge`, `git rebase`,
force-push, and touching `main`.

## Step 4 — Implement

- Smallest complete solution that fits the existing layers. No drive-by refactors, no new abstractions for one caller.
- Respect the boundaries: `Domain` → nothing, `Application` → `Domain`, `Infrastructure` → `Application`,
  `Api` → `Application` + `Infrastructure`.
- Security rules that always apply: re-check the access grant in the database on every medical request (never trust a
  JWT claim), write an audit entry for every medical read/write, refuse clinic staff and admin on medical endpoints,
  never log medical content, never hard-code a secret, page lists 20 by default / 50 maximum.
- Codestyle: 4 spaces, file-scoped namespaces, `System` usings first, nullable enabled; frontend 2 spaces + Prettier
  (`singleQuote`, width 100).
- XML documentation on every public controller action and every public contract whose shape is not obvious, and a
  `[ProducesResponseType]` plus `<response code="...">` for each status code the action can return. The comments flow
  into `/openapi/v1.json` automatically.
- Preserve unrelated working-tree changes. If a file is already modified and is not yours, leave it untouched and say
  so in your report. Never `git reset`, `git checkout --`, `git clean` or `git stash`.

## Step 5 — Test

- Unit tests for business rules and validation.
- HTTP tests for endpoints. When the endpoint is medical, cover the negative matrix: no token (401), wrong role (403),
  no active grant (403), expired grant (403), ended grant (403), wrong doctor (403) — NFR-SEC-10.
- Run and report the exact commands and their real output:

```powershell
dotnet build --configuration Release
dotnet test --no-build --configuration Release
```

and, if `frontend/` changed:

```powershell
npm test -- --watch=false
npm run build
```

Never weaken, skip or delete a test to get green. If something cannot be verified locally (missing SQL Server,
missing secret, no Docker), say exactly which check could not run.

## Step 6 — Review and report

1. Show the full diff of every file you changed (`git diff`, plus new files).
2. Confirm: no secrets, no real patient data, no unrelated files, no migration unless approved, no medical content in
   logs, no new package unless approved.
3. List what is still unverified and why.
4. Produce the PR summary using `.github/pull_request_template.md`: Jira key, what changed, why, how it was tested
   (real commands and results), risks, and anything deferred.

**Stop there.** Do not commit, push, open the PR or merge until I explicitly authorize it.
