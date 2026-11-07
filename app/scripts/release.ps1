<#
.SYNOPSIS
    Builds the DirkDraft 2.0 installer (Tauri + Velopack) and, unless -NoUpload, publishes it as a
    GitHub release - the same feed 1.x reads, so installed 1.x copies see 2.0 as a normal update.

.DESCRIPTION
    Stamps the version (Cargo.toml, package.json), runs the tests (Vitest, cargo), builds the
    release exe (tauri build), packs it with vpk under the package id DirkDraft (same id, same
    DirkDraft.exe as 1.x - docs/redesign/migration.md) and uploads it. Before the upload it commits
    the version bump as "Release X.Y.Z", tags vX.Y.Z and pushes, like the 1.x script.

    -PackId DirkDraft-Beta packs a separate app (own folder, own shortcut "DirkDraft Beta") for
    trying a build without touching any installed DirkDraft - and never uploads. It also packs on
    the channel "beta": Velopack's update check does not look at the package id, only at the
    channel's feed, so a beta on "win" would take a later real release as its own update. Nothing
    publishes "beta"; a probe install finds updates only where DIRKDRAFT_UPDATE_SOURCE points.

    Kept ASCII-only: Windows PowerShell 5.1 reads a .ps1 without byte-order mark as ANSI.

.EXAMPLE
    .\app\scripts\release.ps1 -Version 2.0.0 -PackId DirkDraft-Beta
    .\app\scripts\release.ps1 -Version 2.0.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [ValidateSet('DirkDraft', 'DirkDraft-Beta')]
    [string]$PackId = 'DirkDraft',

    [string]$Repo = 'https://github.com/Sazzlez/DirkDraft',

    [string]$Notes,

    [switch]$NoUpload
)

$ErrorActionPreference = 'Stop'

# Native programs report success by exit code only; their stderr lines must not become terminating
# errors when this script's output is redirected (see the 1.x release.ps1 for the story).
function Use-NativeErrors {
    param([Parameter(Mandatory)][scriptblock]$Body)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Body } finally { $ErrorActionPreference = $previous }
}

$beta = $PackId -ne 'DirkDraft'
if ($beta) { $NoUpload = $true }
# The real channel is Velopack's default on Windows, the one 1.x was packed and reads on.
$channel = if ($beta) { 'beta' } else { 'win' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$app = Join-Path $root 'app'
$tauri = Join-Path $app 'src-tauri'
$cargoToml = Join-Path $tauri 'Cargo.toml'
$packageJson = Join-Path $app 'package.json'
$publishDir = Join-Path $root 'build\publish-2'
# Deltas are built against the previous version found here, so the real channel shares the 1.x
# Releases folder; the beta gets its own.
$releaseDir = if ($beta) { Join-Path $root 'build\Releases-beta' } else { Join-Path $root 'build\Releases' }

# --- 0. Clean working tree -------------------------------------------------------------------
if (-not $NoUpload) {
    $dirty = @(Use-NativeErrors { & git -C $root status --porcelain } | Where-Object { $_ -and ($_ -notmatch '(Cargo\.toml|Cargo\.lock|package\.json)$') })
    if ($dirty.Count -gt 0) {
        throw "Arbeitskopie hat uneingecheckte Aenderungen - erst committen, dann Release:`n" + ($dirty -join "`n")
    }
}

# --- 1. Version stamp ------------------------------------------------------------------------
$cargo = [IO.File]::ReadAllText($cargoToml)
$stamped = [regex]::Replace($cargo, '(?m)^version = "[^"]*"', "version = `"$Version`"", 1)
[IO.File]::WriteAllText($cargoToml, $stamped, (New-Object Text.UTF8Encoding($false)))
$package = [IO.File]::ReadAllText($packageJson)
$package = [regex]::Replace($package, '"version": "[^"]*"', "`"version`": `"$Version`"", 1)
[IO.File]::WriteAllText($packageJson, $package, (New-Object Text.UTF8Encoding($false)))
Write-Host "Version $Version eingetragen."

# --- 2. Test gate ----------------------------------------------------------------------------
Write-Host 'Teste (Vitest, cargo)'
Push-Location $app
try {
    Use-NativeErrors { & npx vitest run --reporter=dot }
    if ($LASTEXITCODE -ne 0) { throw "Vitest ist fehlgeschlagen (Exitcode $LASTEXITCODE) - kein Release." }
} finally { Pop-Location }
Push-Location $tauri
try {
    Use-NativeErrors { & cargo test --quiet }
    if ($LASTEXITCODE -ne 0) { throw "cargo test ist fehlgeschlagen (Exitcode $LASTEXITCODE) - kein Release." }
} finally { Pop-Location }

# --- 3. Release build ------------------------------------------------------------------------
Write-Host 'Baue (tauri build)'
Push-Location $app
try {
    Use-NativeErrors { & npx tauri build }
    if ($LASTEXITCODE -ne 0) { throw "tauri build ist fehlgeschlagen (Exitcode $LASTEXITCODE)." }
} finally { Pop-Location }

# Only the exe: Tauri embeds the page, WebView2 comes with Windows (or with the installer below).
if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force $publishDir | Out-Null
Copy-Item (Join-Path $tauri 'target\release\DirkDraft.exe') $publishDir

# --- 4. Installer + delta packages -----------------------------------------------------------
$vpkArgs = @(
    'pack',
    '--packId', $PackId,
    '--packVersion', $Version,
    '--packDir', $publishDir,
    '--mainExe', 'DirkDraft.exe',
    '--packTitle', $(if ($beta) { 'DirkDraft Beta' } else { 'DirkDraft' }),
    '--channel', $channel,
    '--packAuthors', 'mirko',
    '--icon', (Join-Path $tauri 'icons\icon.ico'),
    # Windows 10 without Edge's WebView2: the installer fetches it first.
    '--framework', 'webview2',
    '--outputDir', $releaseDir
)
if ($Notes) { $vpkArgs += @('--releaseNotes', $Notes) }

Write-Host 'Packe Installer'
Use-NativeErrors { & vpk @vpkArgs }
if ($LASTEXITCODE -ne 0) { throw "vpk pack ist fehlgeschlagen (Exitcode $LASTEXITCODE)." }

$setup = Join-Path $releaseDir "$PackId-$channel-Setup.exe"
if (-not (Test-Path -LiteralPath $setup)) { throw "Erwartete Datei fehlt: $setup" }
$setupMb = [Math]::Round((Get-Item -LiteralPath $setup).Length / 1MB, 1)
Write-Host "Installer: $setup ($setupMb MB)"

if ($NoUpload) {
    Write-Host 'Kein Upload.'
    exit 0
}

# --- 5. GitHub CLI and token -----------------------------------------------------------------
$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    $candidate = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages\GitHub.cli_Microsoft.Winget.Source_8wekyb3d8bbwe\bin\gh.exe'
    if (Test-Path -LiteralPath $candidate) { $gh = Get-Command $candidate }
}
if (-not $gh) { throw 'GitHub CLI (gh) nicht gefunden.' }
$token = Use-NativeErrors { & $gh.Source auth token 2>$null }
if ($LASTEXITCODE -ne 0 -or -not $token) { throw 'Nicht bei GitHub angemeldet ("gh auth login").' }

# --- 6. Release commit, tag, push ------------------------------------------------------------
$env:Path = (Split-Path -Parent $gh.Source) + ';' + $env:Path
$env:GIT_TERMINAL_PROMPT = '0'
$gitAuth = @('-c', 'credential.helper=', '-c', 'credential.helper=!gh auth git-credential')
$tag = "v$Version"
if (Use-NativeErrors { & git -C $root tag -l $tag }) { throw "Tag $tag existiert schon." }

Use-NativeErrors { & git -C $root add -- $cargoToml (Join-Path $tauri 'Cargo.lock') $packageJson }
Use-NativeErrors { & git -C $root diff --cached --quiet }
if ($LASTEXITCODE -ne 0) {
    Use-NativeErrors { & git -C $root commit -q -m "Release $Version" }
    if ($LASTEXITCODE -ne 0) { throw 'git commit ist fehlgeschlagen.' }
}
Use-NativeErrors { & git -C $root tag -a $tag -m "DirkDraft $Version" }
if ($LASTEXITCODE -ne 0) { throw "git tag $tag ist fehlgeschlagen." }

Write-Host "Schiebe Release-Commit und Tag $tag"
Use-NativeErrors { & git -C $root @gitAuth push origin HEAD $tag }
if ($LASTEXITCODE -ne 0) { throw "git push ist fehlgeschlagen. Tag lokal entfernen: git tag -d $tag" }

# --- 7. Upload to GitHub Releases ------------------------------------------------------------
Write-Host "Lade nach $Repo hoch"
Use-NativeErrors {
    & vpk upload github --outputDir $releaseDir --repoUrl $Repo --token $token `
        --publish true --tag $tag --releaseName "DirkDraft $Version"
}
if ($LASTEXITCODE -ne 0) { throw "vpk upload ist fehlgeschlagen (Exitcode $LASTEXITCODE)." }

Write-Host ''
Write-Host "Version $Version ist veroeffentlicht. Installierte Kopien (auch 1.x) melden sie beim naechsten Start."
