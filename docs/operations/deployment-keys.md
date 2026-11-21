# Deployment keys, the Data Protection ring and report storage

This page is the deployment note for the three secrets MediPass+ separates, for the one infrastructure path that holds
the Data Protection key ring, and for the report storage root (ADR-0007 decisions 5 and 6, ADR-0005, ADR-0015
decision 6, FR-PRV-04, FR-PRV-05, NFR-SEC-05, NFR-SEC-09).

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
| Missing `ConnectionStrings:DefaultConnection` | The API starts; the first request that opens the database fails |

## Deployment checklist

- [ ] `DataProtection:KeysPath` points at a persistent, mounted, access-restricted directory outside the image.
- [ ] The key volume is included in the backup and restore procedure, together with the database.
- [ ] `Reports:Storage:RootPath` points at a persistent, access-restricted directory outside the image and outside
      `wwwroot`, and it is backed up with the database.
- [ ] `Jwt:SecretKey` and `AccessCode:HmacKey` are set from the secret store, not from `appsettings*.json`.
- [ ] No secret value appears in logs, images, build output or the repository.
