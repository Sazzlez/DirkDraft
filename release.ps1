<#
.SYNOPSIS
    Builds an installer for a new DirkDraft version and publishes it as a GitHub release.

.DESCRIPTION
    One command per release. It stamps the version into the project, runs the tests, publishes a
    self-contained build (the .NET runtime travels with it, so nobody has to install anything
    first), wraps that into a Velopack installer with delta packages, and uploads the result to
    GitHub Releases - the place every installed copy asks at start-up.

    The GitHub token comes from the GitHub CLI's own login (gh auth token) and is never written
    anywhere. Run "gh auth login" once before the first release.

    Kept deliberately ASCII-only: Windows PowerShell 5.1 reads a .ps1 without a byte-order mark as
    ANSI, and any non-ASCII character in here turns into a parse error on a German system.

.PARAMETER Version
    The new version, three numbers (e.g. 1.1.0). Must be higher than the last published one, or
    installed copies will not pick it up.

.PARAMETER Repo
    The GitHub repository releases go to. Must match AppUpdater.Feed in the app, or installed
    copies look in one place while releases land in another.

.PARAMETER Notes
    Optional markdown file with release notes; shown on the GitHub release page.

.PARAMETER NoUpload
    Build the installer only. build\Releases then holds Setup.exe for a manual hand-over.

.EXAMPLE
    .\release.ps1 -Version 1.1.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$Repo = 'https://github.com/OWNER/DirkDraft',

    [string]$Notes,

    [switch]$NoUpload
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\DraftPilot.App\DraftPilot.App.csproj'
$publishDir = Join-Path $root 'build\publish'
$releaseDir = Join-Path $root 'build\Releases'

# --- 1. Version stamp ------------------------------------------------------------------------
# Installed copies compare this against the newest release; a build without a bump is invisible.
$csproj = [IO.File]::ReadAllText($project)
$stamped = [regex]::Replace($csproj, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
if ($stamped -eq $csproj -and $csproj -notmatch "<Version>$([regex]::Escape($Version))</Version>") {
    throw "Kein <Version>-Eintrag in $project gefunden."
}
[IO.File]::WriteAllText($project, $stamped, (New-Object Text.UTF8Encoding($false)))
Write-Host "Version $Version eingetragen."

# --- 2. Test gate ----------------------------------------------------------------------------
# $ErrorActionPreference does not react to a native exit code, hence the explicit checks below.
Write-Host 'Teste'
& dotnet test (Join-Path $root 'DraftPilot.sln') -m:1 --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet test ist fehlgeschlagen (Exitcode $LASTEXITCODE) - kein Release." }

# --- 3. Self-contained publish ---------------------------------------------------------------
# Friends do not have the .NET 9 desktop runtime; bundling it costs ~70 MB once and saves every
# "it does not start" conversation. A fresh folder, or vpk would package stale files.
if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
Write-Host 'Baue (mit eingebauter Runtime)'
& dotnet publish $project --configuration Release --runtime win-x64 --self-contained true `
    --nologo --verbosity quiet --output $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish ist fehlgeschlagen (Exitcode $LASTEXITCODE)." }

# --- 4. Installer + delta packages -----------------------------------------------------------
# The Releases folder is kept between runs on purpose: vpk builds delta packages against the
# previous version it finds there, so an update downloads only what changed.
$vpkArgs = @(
    'pack',
    '--packId', 'DirkDraft',
    '--packVersion', $Version,
    '--packDir', $publishDir,
    '--mainExe', 'DirkDraft.exe',
    '--packTitle', 'DirkDraft',
    '--packAuthors', 'mirko',
    '--icon', (Join-Path $root 'src\DraftPilot.App\Assets\app.ico'),
    '--outputDir', $releaseDir
)
if ($Notes) { $vpkArgs += @('--releaseNotes', $Notes) }

Write-Host 'Packe Installer'
& vpk @vpkArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack ist fehlgeschlagen (Exitcode $LASTEXITCODE)." }

$setup = Join-Path $releaseDir 'DirkDraft-win-Setup.exe'
if (-not (Test-Path -LiteralPath $setup)) { throw "Erwartete Datei fehlt: $setup" }
$setupMb = [Math]::Round((Get-Item -LiteralPath $setup).Length / 1MB, 1)
Write-Host "Installer: $setup ($setupMb MB)"

if ($NoUpload) {
    Write-Host 'Kein Upload (-NoUpload). Setup.exe kann direkt weitergegeben werden.'
    exit 0
}

# --- 5. Upload to GitHub Releases ------------------------------------------------------------
# The token is read from the GitHub CLI session and passed on the command line only.
$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    # winget --scope user drops a portable copy here and only puts it on the PATH of NEW shells.
    $candidates = @(
        (Join-Path $env:ProgramFiles 'GitHub CLI\gh.exe'),
        (Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages\GitHub.cli_Microsoft.Winget.Source_8wekyb3d8bbwe\bin\gh.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { $gh = Get-Command $candidate; break }
    }
}
if (-not $gh) { throw 'GitHub CLI (gh) nicht gefunden. winget install GitHub.cli, dann gh auth login.' }

$token = & $gh.Source auth token 2>$null
if ($LASTEXITCODE -ne 0 -or -not $token) { throw 'Nicht bei GitHub angemeldet. Einmal "gh auth login" ausfuehren.' }

Write-Host "Lade nach $Repo hoch"
& vpk upload github --outputDir $releaseDir --repoUrl $Repo --token $token `
    --publish true --tag "v$Version" --releaseName "DirkDraft $Version"
if ($LASTEXITCODE -ne 0) { throw "vpk upload ist fehlgeschlagen (Exitcode $LASTEXITCODE)." }

Write-Host ''
Write-Host "Version $Version ist veroeffentlicht. Installierte Kopien melden sie beim naechsten Start."
Write-Host "Zum Einchecken: git add -A; git commit -m ""Release $Version""; git tag v$Version"
