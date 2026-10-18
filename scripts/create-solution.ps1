# Creates the solution skeleton (Clean Architecture). Run once, from the repository root, in PowerShell.
# Needs the .NET SDK and Node.js with the Angular CLI installed.

$ErrorActionPreference = "Stop"

$sdk = (dotnet --version)
dotnet new globaljson --sdk-version $sdk --roll-forward latestFeature --force
dotnet new sln -n MediPassPlus

New-Item -ItemType Directory -Force src, tests | Out-Null

dotnet new classlib -n MediPassPlus.Domain         -o src/MediPassPlus.Domain
dotnet new classlib -n MediPassPlus.Application    -o src/MediPassPlus.Application
dotnet new classlib -n MediPassPlus.Infrastructure -o src/MediPassPlus.Infrastructure
dotnet new webapi   -n MediPassPlus.Api            -o src/MediPassPlus.Api --use-controllers
dotnet new xunit    -n MediPassPlus.Domain.Tests      -o tests/MediPassPlus.Domain.Tests
dotnet new xunit    -n MediPassPlus.Application.Tests -o tests/MediPassPlus.Application.Tests

$projects = (Get-ChildItem src, tests -Recurse -Filter *.csproj).FullName
dotnet sln add $projects

# Layer rules: Domain depends on nothing
dotnet add src/MediPassPlus.Application    reference src/MediPassPlus.Domain
dotnet add src/MediPassPlus.Infrastructure reference src/MediPassPlus.Application
dotnet add src/MediPassPlus.Api            reference src/MediPassPlus.Application src/MediPassPlus.Infrastructure
dotnet add tests/MediPassPlus.Domain.Tests      reference src/MediPassPlus.Domain
dotnet add tests/MediPassPlus.Application.Tests reference src/MediPassPlus.Application

dotnet build

# Frontend (uncomment when Node and the Angular CLI are installed)
# ng new frontend --routing --style=scss --skip-git --defaults
Write-Host "Done. Commit this on a branch named chore/solution-skeleton and open a pull request."
