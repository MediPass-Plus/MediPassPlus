# Contributing to MediPass+

These rules keep `main` always working and make our history easy to read.

## 1. Branches

- `main` is protected. Nobody pushes to it directly.
- Work on a short-lived branch created from an up-to-date `main`:

```
feature/{module}-{JIRA-KEY}-{short-description}
fix/{module}-{JIRA-KEY}-{short-description}
docs/{JIRA-KEY}-{short-description}
chore/{short-description}
```

Examples: `feature/visit-access-MP-21-qr-token`, `fix/booking-MP-48-double-booking`.

Module names: `accounts`, `booking`, `staff-admin`, `visit-access`, `record`, `privacy`, `chat`, `infra`.

## 2. Daily routine

```bash
git checkout main
git pull
git checkout -b feature/booking-MP-14-book-appointment
# ... work, small commits ...
git add .
git commit -m "MP-14 add appointment booking endpoint"
git push -u origin feature/booking-MP-14-book-appointment
```

Then open a pull request on GitHub.

## 3. Commit messages

Start with the Jira key, then a short sentence in the present tense:

```
MP-14 add appointment booking endpoint
MP-21 hash the QR token before saving
```

Commit often, keep each commit small, and never commit secrets.

## 4. Pull requests

- One pull request per Jira ticket. Keep it small.
- Fill in the pull request template.
- The build must pass and at least one teammate must approve before merging.
- The team leader approves every pull request. The reviewer assigned to the module (planning document, section 3.2) also reads it and leaves comments.
- Merge with **Squash and merge**. The branch is deleted after the merge.
- If `main` moved on, update your branch before merging:

```bash
git checkout main && git pull
git checkout your-branch
git merge main
# fix conflicts, run the tests, push
```

## 5. Code rules

- Follow the layers: Domain, Application, Infrastructure, Api. Domain depends on nothing.
- Every write endpoint validates its input.
- Every medical endpoint goes through the access grant check. No exceptions.
- Do not write medical content into logs. Log identifiers only.
- Add unit tests for the logic you write. Document every endpoint in Swagger.
- Use clear names. Comment the why, not the what.

## 6. Secrets and data

- Never commit passwords, keys, connection strings, certificates or real patient data.
- Use `dotnet user-secrets` or environment variables.
- Use fake data for demos (seed script).

## 7. Definition of done

A task is done when the reviewed pull request is merged, the build passes, the endpoint is in Swagger,
the logic has unit tests, and the Jira ticket is moved to Done.

## 8. Releases

At the end of every sprint the team leader tags `main`: `sprint-1`, `sprint-2`, and so on.
