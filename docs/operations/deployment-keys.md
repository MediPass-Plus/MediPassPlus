# Deployment keys, the Data Protection ring, report storage and the mail transport

This page is the deployment note for the three secrets MediPass+ separates, for the one infrastructure path that holds
the Data Protection key ring, for the report storage root, and for the password-reset mail credentials (ADR-0007
decisions 5 and 6, ADR-0005, ADR-0015 decision 6, FR-PRV-04, FR-PRV-05, FR-ACC-11, NFR-SEC-05, NFR-SEC-09).

Nothing on this page is ever committed. Locally the values live in `dotnet user-secrets` or environment variables;
in a deployment they come from the platform secret store and the mounted key volume.

## The three secrets and the ring path

| Setting | What it protects | Format | Where it comes from | If it is lost or rotated |
|---|---|---|---|---|
| `DataProtection:KeysPath` | Directory of the Data Protection key ring; it derives the diagnosis cipher key under the purpose `MediPass.Diagnosis.v1` | Absolute directory path, outside the repository | Configuration / mounted volume | Existing diagnosis ciphertext becomes unreadable. Treat the directory as durable data, not as a cache |
| `Jwt:SecretKey` | HMAC-SHA256 signature of access tokens | At least 32 characters | Secret store / user-secrets / environment variable | Every live access token stops validating; users log in again (access tokens live 15 minutes) |
| `AccessCode:HmacKey` | HMAC-SHA256 of the patient's six-digit fallback code | Base64 of at least 32 random bytes | Secret store / user-secrets / environment variable | Live codes stop verifying; they expire after five minutes anyway. The API refuses to start without it |

These are three different secrets with three different lifetimes. Never reuse one for another purpose, never derive one
from another, and never log any of them — identifiers only (AGENTS.md §5).

The database connection string is a fourth credential even when it carries no password: it names an instance and a
database. It comes from user-secrets or `ConnectionStrings__DefaultConnection` and is never committed to
`appsettings*.json` (MP-51).

The password-reset mail transport adds a fifth group. When `Smtp:Host` is set, `Smtp:UserName` and `Smtp:Password` are
provider credentials and belong in the secret store beside the four above. `Smtp:Host`, `Smtp:Port`, `Smtp:UseStartTls`,
`Smtp:FromAddress`, `Smtp:FromName` and `Smtp:TimeoutSeconds` are ordinary configuration and stay in
`appsettings*.json`; only the credentials move. `Smtp:FromAddress` has no default and is required whenever a host is set.

With those five groups accounted for, nothing else in `appsettings.json` is sensitive, and every other setting there is
a documented default rather than a secret.

## Data Protection key ring

The ring is file-based: `PersistKeysToFileSystem` writes XML key files into a directory. It is not a table, so the
configuration needs no migration and no schema step (ADR-0009 is not involved).

- **Production must configure it.** `DataProtection:KeysPath` is mandatory in the `Production` environment: a missing
  value is an `OptionsValidationException` and the process stops before it serves its first request. There is no
  ephemeral fallback in Production, because ephemeral keys would make every stored diagnosis unreadable after a restart.
- **Development and Testing may omit it.** With an empty path they use a directory under the operating system's
  temporary path (`medipass-dataprotection-keys`), so a fresh clone starts with no local setup. That directory is
  outside the repository by construction.
- **Never inside the repository.** A relative path, a path inside the checkout, or a path inside the application
  directory is refused in every environment. In a deployment, mount a volume (for example `/var/lib/medipass/dp-keys`
  or `D:\MediPassPlus\keys`) that is outside the application directory and the image.
- **Protect it at rest.** Restrict the directory to the account that runs the API, and keep it on an encrypted volume
  or an equivalently protected mount. The application does not add a certificate-based key protector; that is a
  deployment control (ADR-0007 decision 5).
- **Back it up, and restore it with the database.** A restored database without its ring yields ciphertext nobody can
  read. Losing the ring is the accepted consequence recorded in ADR-0007 and ADR-0005, not a reason to add a second
  mechanism.
- **Purpose strings are versioned.** Diagnosis text is protected under `MediPass.Diagnosis.v1`
  (`DiagnosisProtector.Purpose`). The purpose is part of every stored payload: bumping it is a data migration that
  re-protects existing rows, not a configuration change.

## Report storage root

Report files live under `Reports:Storage:RootPath`. It is not a secret, but it is required production configuration
and the same rules as the key ring apply (ADR-0015 decision 6, FR-PRV-05, NFR-SEC-09):

- **Production must configure it.** An empty value is an `OptionsValidationException` at startup, exactly like a
  missing `DataProtection:KeysPath`.
- **Absolute, outside the repository and outside `wwwroot`.** A relative path, a path inside the checkout or the
  application directory, and a path under `wwwroot` are refused in every environment. A file under the static root is
  a public link with no grant check.
- **Development and Testing may omit it.** They fall back to `medipass-report-storage` under the operating system's
  temporary path, so a fresh clone starts with no local setup.
- **Back it up with the database.** The `Report` row stores only the random stored name and metadata. A restored
  database without this directory has report rows whose bytes are gone; the two are one recovery unit.
- **The client's file name is never used as a path.** Files are stored as `{Guid:N}{ext}`, the stored name is never a
  URL, and no static-file mapping points at this directory.

## Password-reset mail transport

`POST api/Auth/forgot-password` sends the reset link over SMTP (FR-ACC-11). The transport is configured under `Smtp`
and validated at startup, so a half-configured sender never reaches a request:

- **A mail transport is required outside Development and Testing.** With an empty `Smtp:Host`, Development and Testing
  register the no-op sender, which logs one warning per send instead of failing; that is what keeps a fresh clone and CI
  runnable without mail credentials. Every other environment refuses to start with an `OptionsValidationException`: a
  deployment that silently accepted reset requests and sent nothing is not a supported state.
- **A configured host must be complete.** `Smtp:UserName`, `Smtp:Password` and `Smtp:FromAddress` are required,
  `Smtp:Port` must be 1–65535, `Smtp:TimeoutSeconds` must be 1–120, and the username and sender must each be one plain
  email address. A missing or malformed value is an `OptionsValidationException` at startup, not a `500` on the first
  reset request.
- **`Smtp:FromAddress` has no default.** The committed `appsettings.json` leaves it empty on purpose. A host-configured
  deployment must name an address its provider accepts; keep it equal to `Smtp:UserName` unless the provider explicitly
  allows an alias, because most providers reject a sender they do not own.
- **`Smtp:UseStartTls` means STARTTLS, not implicit TLS.** It maps to `SmtpClient.EnableSsl`. The supported submission
  mode is STARTTLS on port 587; implicit TLS on port 465 is not supported by `SmtpClient` and must not be configured.
  Set it to `false` only for a trusted local relay that offers no TLS.
- **The send is bounded.** `Smtp:TimeoutSeconds` (default 10) caps one send; the framework's 100-second default is never
  used. A dead host can no longer hold a reset request for over a minute.
- **A send failure never changes the answer.** The endpoint catches the transport exception, logs only the exception
  type and, for an SMTP refusal, the protocol status code, and still answers the same `202` as an unknown address, so a
  mail outage is not an enumeration oracle. The recipient address and the token never reach a log line, even when the
  server response repeats the address (NFR-SEC-06, FR-PRV-08).
- **The credentials are provider secrets.** For Gmail, `Smtp:Password` is a 16-character App Password, not the account
  password. It lives in user-secrets locally and in the platform secret store in a deployment, never in
  `appsettings*.json`.

## Failure behaviour

| Situation | Result |
|---|---|
| `Production` and `DataProtection:KeysPath` empty | Startup fails (`OptionsValidationException`) |
| `DataProtection:KeysPath` relative or inside the repository / application directory | Startup fails in every environment |
| `Development` or `Testing` and no key path | Starts, ring under the OS temp path |
| `Production` and `Reports:Storage:RootPath` empty | Startup fails (`OptionsValidationException`) |
| `Reports:Storage:RootPath` relative or inside the repository / application directory / `wwwroot` | Startup fails in every environment |
| `Development` or `Testing` and no report path | Starts, files under the OS temp path |
| `Jwt:SecretKey` missing or shorter than 32 characters | Startup fails |
| `AccessCode:HmacKey` missing, not Base64 or shorter than 32 bytes | Startup fails |
| Missing `ConnectionStrings:DefaultConnection` | The API fails at startup: the unconditional `SeedDefaultRolesAsync()` opens the database before the pipeline runs, so this is a startup failure, not a first-request failure |
| `Smtp:Host` empty in `Development` or `Testing` | Starts with the no-op sender; each reset mail is logged as not sent |
| `Smtp:Host` empty in `Staging` or `Production` | Startup fails (`OptionsValidationException`) |
| `Smtp:Host` set but `Smtp:UserName`, `Smtp:Password` or `Smtp:FromAddress` empty, or the username/sender is not one plain address | Startup fails (`OptionsValidationException`) |
| `Smtp:Port` outside 1–65535 or `Smtp:TimeoutSeconds` outside 1–120 | Startup fails in every environment |
| The SMTP server refuses a message or does not answer within `Smtp:TimeoutSeconds` | The request still answers `202`; the failure line carries the exception type and the SMTP status code only |

## Deployment checklist

- [ ] `DataProtection:KeysPath` points at a persistent, mounted, access-restricted directory outside the image.
- [ ] The key volume is included in the backup and restore procedure, together with the database.
- [ ] `Reports:Storage:RootPath` points at a persistent, access-restricted directory outside the image and outside
      `wwwroot`, and it is backed up with the database.
- [ ] `Jwt:SecretKey` and `AccessCode:HmacKey` are set from the secret store, not from `appsettings*.json`.
- [ ] `ConnectionStrings:DefaultConnection` is set from the secret store or user-secrets; `appsettings*.json` in the
      deployed image carries no connection string (MP-51).
- [ ] Outside `Development` and `Testing`, `Smtp:Host` is set together with `Smtp:UserName`, `Smtp:Password` and one
      plain `Smtp:FromAddress`, and the credentials come from the secret store rather than `appsettings*.json`.
- [ ] `Smtp:UseStartTls` matches the provider's submission mode (STARTTLS on 587; implicit TLS on 465 is not supported)
      and `Smtp:TimeoutSeconds` bounds a dead host (default 10).
- [ ] No secret value appears in logs, images, build output or the repository, and the `gitleaks` CI job is green.

The non-secret edge settings that sit next to these — login rate limits, allowed CORS origins, forwarded-header trust
and the HSTS policy — are documented in `platform-hardening.md`.
