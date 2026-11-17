<#
.SYNOPSIS
    Fails when Domain or Application line coverage is below the NFR-MNT-02 gate.

.DESCRIPTION
    Reads the Cobertura report each test project produces with
    `dotnet test <project> --collect:"XPlat Code Coverage"` and checks the package that project owns. Every project
    is measured separately: coverlet also reports packages it merely loaded, and summing two reports would count the
    same lines twice.

.PARAMETER ResultsDirectory
    The root the CI passes to `--results-directory`. It must contain a `domain` and an `application` folder.

.PARAMETER Minimum
    The required line-coverage percentage. The requirement is 50 (NFR-MNT-02).

.EXAMPLE
    pwsh -NoProfile -File scripts/check-coverage.ps1

.EXAMPLE
    # Prove the gate itself fails; this must exit 1.
    pwsh -NoProfile -File scripts/check-coverage.ps1 -Minimum 99
#>
[CmdletBinding()]
param(
    [string]$ResultsDirectory = "artifacts/coverage",
    [double]$Minimum = 50
)

$ErrorActionPreference = "Stop"

$targets = @(
    [pscustomobject]@{ Package = "MediPassPlus.Domain"; Folder = "domain" }
    [pscustomobject]@{ Package = "MediPassPlus.Application"; Folder = "application" }
)

$failed = $false

foreach ($target in $targets) {
    $folder = Join-Path $ResultsDirectory $target.Folder
    $reports = @(
        if (Test-Path -Path $folder) {
            Get-ChildItem -Path $folder -Recurse -Filter "coverage.cobertura.xml" -File
        }
    )

    if ($reports.Count -ne 1) {
        Write-Host "::error::$($target.Package): expected exactly one coverage.cobertura.xml under '$folder', found $($reports.Count)."
        $failed = $true
        continue
    }

    [xml]$document = Get-Content -Path $reports[0].FullName -Raw
    $package = $document.coverage.packages.package | Where-Object { $_.name -eq $target.Package }

    if ($null -eq $package) {
        Write-Host "::error::$($target.Package): not present in $($reports[0].FullName)."
        $failed = $true
        continue
    }

    $percent = [math]::Round(([double]$package.GetAttribute("line-rate")) * 100, 1)

    if ($percent -lt $Minimum) {
        Write-Host "::error::$($target.Package): $percent% line coverage is below the $Minimum% gate (NFR-MNT-02)."
        $failed = $true
    }
    else {
        Write-Host "$($target.Package): $percent% line coverage (gate $Minimum%)."
    }
}

if ($failed) {
    exit 1
}

Write-Host "Coverage gate passed."
exit 0
