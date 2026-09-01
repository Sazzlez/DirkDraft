<#
.SYNOPSIS
    Builds DirkDraft and puts a shortcut on the desktop.

.DESCRIPTION
    Framework-dependent build: the .NET 9 desktop runtime is already installed, so the output stays
    around 5 MB instead of the ~120 MB a self-contained build would produce.

    Safe to run repeatedly. An existing shortcut is updated rather than duplicated, and the tool is
    stopped first if it happens to be running, because its files would otherwise be locked.

    Kept deliberately ASCII-only: Windows PowerShell 5.1 reads a .ps1 without a byte-order mark as
    ANSI, and any non-ASCII character in here turns into a parse error on a German system.

.PARAMETER NoShortcut
    Build only, leave the desktop alone.

.PARAMETER OutputDirectory
    Where to place the build. Defaults to build\DraftPilot next to this script.

.PARAMETER SkipTests
    Publish without running the test suite first. Emergency exit only.
#>
[CmdletBinding()]
param(
    [switch]$NoShortcut,
    [string]$OutputDirectory,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'build\DirkDraft' }

$project = Join-Path $root 'src\DraftPilot.App\DraftPilot.App.csproj'
if (-not (Test-Path -LiteralPath $project)) { throw "Projekt nicht gefunden: $project" }

# The gate runs BEFORE the running instance is killed: a red test should cost nothing, least of all
# the instance the user has open. Against the solution, not the test project, so the app compiles
# too. $ErrorActionPreference does not react to a native exit code, hence the explicit check.
if (-not $SkipTests) {
    Write-Host 'Teste vor dem Bauen'
    & dotnet test (Join-Path $root 'DraftPilot.sln') -m:1 --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet test ist fehlgeschlagen (Exitcode $LASTEXITCODE) - es wurde nichts gebaut." }
}

# A running instance holds its DLLs open, which makes the copy step fail halfway.
# DraftPilot is the pre-rename process name; kill it too so an old instance cannot linger.
$running = @(Get-Process DirkDraft, DraftPilot -ErrorAction SilentlyContinue)
if ($running) {
    Write-Host 'DirkDraft laeuft und wird beendet, damit die Dateien nicht gesperrt sind.'
    $running | Stop-Process -Force
    # Wait for the actual exit instead of a guessed 800 ms: a slow teardown left DLLs locked
    # and dotnet publish failed halfway through the copy.
    try { $running | Wait-Process -Timeout 10 -ErrorAction Stop } catch {
        Write-Warning 'Prozess laeuft nach 10 s noch - der Kopierschritt kann an gesperrten DLLs scheitern.'
    }
}

Write-Host "Baue nach $OutputDirectory"
& dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --nologo `
    --verbosity quiet `
    --output $OutputDirectory

if ($LASTEXITCODE -ne 0) { throw "dotnet publish ist fehlgeschlagen (Exitcode $LASTEXITCODE)." }

$exe = Join-Path $OutputDirectory 'DirkDraft.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Erwartete Datei fehlt: $exe" }

$sizeMb = [Math]::Round(((Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "Fertig: $exe ($sizeMb MB gesamt)"

# The curated data files have to travel with the executable, or the composition rules go quiet.
$needed = @('data\champion_traits.json', 'data\pick_order_priors.json')
foreach ($file in $needed) {
    if (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory $file))) {
        Write-Warning "Fehlt im Build: $file - Comp-Regeln laufen dann eingeschraenkt."
    }
}

if ($NoShortcut) { return }

$desktop = [Environment]::GetFolderPath('Desktop')
$linkPath = Join-Path $desktop 'DirkDraft.lnk'

# Recreated rather than updated. Windows caches a shortcut's icon under the target path plus index,
# so overwriting an existing .lnk that points at the same executable keeps showing the old image
# even after the icon inside that executable changed. A fresh file gets read fresh.
if (Test-Path -LiteralPath $linkPath) { [System.IO.File]::Delete($linkPath) }

# Leftovers from before the rename; two shortcuts to the same tool invite starting the stale one.
$oldLink = Join-Path $desktop 'DraftPilot.lnk'
if (Test-Path -LiteralPath $oldLink) { [System.IO.File]::Delete($oldLink) }
$oldBuild = Join-Path $root 'build\DraftPilot'
# Never the freshly built output: with -OutputDirectory build\DraftPilot this WAS the build.
if ((Test-Path -LiteralPath $oldBuild) -and
    ([System.IO.Path]::GetFullPath($oldBuild) -ne [System.IO.Path]::GetFullPath($OutputDirectory))) {
    Remove-Item -LiteralPath $oldBuild -Recurse -Force -ErrorAction SilentlyContinue
}

$shell = New-Object -ComObject WScript.Shell
try {
    $link = $shell.CreateShortcut($linkPath)
    $link.TargetPath = $exe
    $link.WorkingDirectory = $OutputDirectory
    # Icon comes from the executable itself, so it can never drift from the app icon.
    $link.IconLocation = "$exe,0"
    $link.Description = 'Pick- und Ban-Assistent fuer League of Legends'
    $link.Save()
}
finally {
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
}

# Deleting the file is not enough on its own: the shell keeps a per-user icon cache database. Ask it
# to rebuild, then tell Explorer that associations changed so open windows repaint.
$ie4uinit = Join-Path $env:SystemRoot 'System32\ie4uinit.exe'
if (Test-Path -LiteralPath $ie4uinit) {
    foreach ($flag in '-show', '-ClearIconCache') {
        try { & $ie4uinit $flag 2>$null } catch { }
    }
}

if (-not ('Build.DraftPilotShell' -as [type])) {
    Add-Type -Name DraftPilotShell -Namespace Build -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shell32.dll")]
public static extern void SHChangeNotify(int eventId, uint flags, System.IntPtr item1, System.IntPtr item2);
'@
}

# SHCNE_ASSOCCHANGED with SHCNF_IDLIST: tells the shell that icon associations moved.
[Build.DraftPilotShell]::SHChangeNotify(0x08000000, 0x0000, [System.IntPtr]::Zero, [System.IntPtr]::Zero)

# SHCNE_UPDATEITEM with SHCNF_PATHW: repaints this one file, so an open desktop updates without F5.
$pathPtr = [System.Runtime.InteropServices.Marshal]::StringToHGlobalUni($linkPath)
try {
    [Build.DraftPilotShell]::SHChangeNotify(0x00002000, 0x0005, $pathPtr, [System.IntPtr]::Zero)
}
finally {
    [System.Runtime.InteropServices.Marshal]::FreeHGlobal($pathPtr)
}

Write-Host "Verknuepfung: $linkPath (Icon-Cache erneuert)"
Write-Host ''
Write-Host 'Doppelklick startet das Tool. Es legt sich in den Infobereich und meldet sich,'
Write-Host 'sobald ein Champ Select beginnt. Rechtsklick auf das Symbol: Oeffnen, Daten'
Write-Host 'aktualisieren, Beenden.'
