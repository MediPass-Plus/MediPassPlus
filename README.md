# MediPass+

MediPass+ is a web platform that keeps a patient's medical history in one place and keeps it private.
A patient books a doctor. When the patient arrives, the doctor starts the visit and the patient approves
access on their own phone by scanning a QR code. Only then can the doctor see the patient's history, and
only for that visit. When the visit ends, the access ends too. The patient can always see their own history,
see who looked at it, and end a doctor's access at any moment.

DEPI graduation project, Full Stack .NET track.

## Main features

- Accounts and login for four kinds of user: patient, doctor, clinic staff, admin (JWT, roles, lockout)
- Clinics, doctors, time slots and appointment booking
- Visit access: QR code approval (6-digit code as fallback) that creates an access grant for one visit
- Medical record: diagnoses, prescriptions, reports and scans
- Privacy layer: audit log, "Who viewed my record", encrypted diagnosis text, protected file access
- Admin tools and a counts dashboard
- Chat between patient and doctor or clinic (stretch)

## Technology

| Part | Technology |
|---|---|
| Backend | ASP.NET Core Web API (C#), Entity Framework Core |
| Database | SQL Server |
| Frontend | Angular (responsive web app) |
| Auth | ASP.NET Core Identity with JWT |
| Quality | xUnit, FluentValidation, Serilog, Swagger/OpenAPI, GitHub Actions |

## Repository structure

```
MediPassPlus/
  src/                  Backend projects (Domain, Application, Infrastructure, Api)
  tests/                Unit and integration tests
  frontend/             Angular application
  scripts/              Helper scripts
  Documentations/       Project documents (planning, requirements, design, reports)
  Diagrams/             UML diagrams and the Gantt chart (PNG)
  .github/              CI workflow and templates
```

## Getting started

### Prerequisites

- Git
- .NET SDK (the version in `global.json`)
- Node.js LTS and Angular CLI (`npm install -g @angular/cli`)
- SQL Server Express, LocalDB or Docker
- Visual Studio or VS Code

### Run the backend

```bash
git clone https://github.com/MediPass-Plus/MediPassPlus.git
cd MediPassPlus

# store secrets locally, never in the repository
dotnet user-secrets init --project src/MediPassPlus.Api
dotnet user-secrets set "ConnectionStrings:Default" "<your connection string>" --project src/MediPassPlus.Api
dotnet user-secrets set "Jwt:Key" "<a long random key>" --project src/MediPassPlus.Api

dotnet ef database update --project src/MediPassPlus.infrastructure --startup-project src/MediPassPlus.Api
dotnet run --project src/MediPassPlus.Api
```

Swagger is available at `https://localhost:<port>/swagger` (TODO: fill in when the API exists).

### Database migrations

- One migration per pull request.
- Rebase on `main` before merging.
- Never edit a migration that is already merged.

Create a migration:

```
dotnet ef migrations add <MigrationName> --project src/MediPassPlus.Infrastructure --startup-project src/MediPassPlus.Api --output-dir Persistence/Migrations
```

Apply migrations to the database:

```
dotnet ef database update --project src/MediPassPlus.Infrastructure --startup-project src/MediPassPlus.Api
```

### Seed data

To add demo data for your module:

1. Create a class that implements `ISeeder` in `src/MediPassPlus.Infrastructure/Persistence/Seeding`.
2. Register it in `Program.cs`, for example: `builder.Services.AddScoped<ISeeder, ClinicsSeeder>();`
3. Keep it safe to run twice: check if the data already exists before adding it.

Seeders run automatically when the API starts in the Development environment.

### Run the frontend

```bash
cd frontend
npm install
ng serve
```

The phone camera needs HTTPS (or localhost) to scan the QR code.

## Documentation

- Project Planning and Management: `Documentations/`
- Requirements Gathering: `Documentations/`
- UML diagrams and the Gantt chart: `Diagrams/`

## Working on the project

Read [CONTRIBUTING.md](CONTRIBUTING.md) before you start: branch names, commit messages, pull requests and reviews.

## Team

- Waleed Ahmed Korashy Abdo (Team Leader)
- Ahmed Gamal Khairy Hassan
- Azza Ibrahim Ali Shaban
- Nagham Ahmed Elsayed Elsorady
- Arwa Mahmoud Sayed Hussien
- Hend Abdelnaser Rashad Elsadany
