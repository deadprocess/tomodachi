# Bringt die installierte tomodachi-Exe (die, auf die der Autostart zeigt) auf den neuesten Build aus dist\.
#
#   .\tools\publish.ps1          (neu bauen)
#   .\tools\update-install.ps1   (fragt einmal per UAC, ersetzt die Exe, startet tomodachi neu)
#
# Die vorherige Exe bleibt als tomodachi-alt.exe daneben liegen. Der Pfad bleibt gleich, der Autostart also auch.
param(
    [string]$InstallDir
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# Ziel: der Ordner der Exe, die die Autostart-Aufgabe startet
if (-not $InstallDir) {
    $xml = [xml](schtasks /Query /TN tomodachi /XML | Out-String)
    if ($LASTEXITCODE -ne 0) { throw 'Keine Autostart-Aufgabe "tomodachi" gefunden. Zuerst in den Einstellungen „Mit Windows starten“ einschalten.' }
    $exec = $xml.Task.Actions.Exec
    $exe = $exec.Command.Trim('"')
    # Seit 1.5.0 startet die Aufgabe cmd.exe, und die Exe steht in Anführungszeichen in den Argumenten
    if ($exe -notmatch 'tomodachi\.exe$') {
        if ($exec.Arguments -notmatch '"([^"]*tomodachi\.exe)"') { throw "Aufgabe zeigt nicht auf tomodachi.exe: $($exec.Command) $($exec.Arguments)" }
        $exe = $Matches[1]
    }
    $InstallDir = Split-Path $exe
}
if ($InstallDir.TrimEnd('\') -like "$($env:SystemRoot)*") { throw "Installationsordner $InstallDir liegt unter Windows, hier wird nichts kopiert." }

# In Program Files darf nur ein Admin schreiben: einmal per UAC neu starten
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-InstallDir', "`"$InstallDir`"")
    return
}

# Protokoll für die Fehlersuche (das Admin-Fenster ist sonst sofort wieder zu)
Start-Transcript -Path (Join-Path $env:TEMP 'tomodachi-update.log') -Force | Out-Null
trap {
    Write-Host "Fehler: $_" -ForegroundColor Red
    Stop-Transcript | Out-Null
    Read-Host 'Enter zum Schließen'
    exit 1
}

$newest = Get-ChildItem (Join-Path $root 'dist') -Directory -Filter 'tomodachi-*' |
    Sort-Object { [version]($_.Name -replace '^tomodachi-', '') } | Select-Object -Last 1
if (-not $newest) { throw 'Kein Build in dist\ gefunden. Zuerst .\tools\publish.ps1 ausführen.' }

$target = Join-Path $InstallDir 'tomodachi.exe'
Write-Host "Aktualisiere $target auf $($newest.Name) …"

# Laufende Instanz sauber beenden (stellt die Taskleiste wieder her) und warten, bis die Exe frei ist
if (Test-Path $target) { & $target --quit }
$deadline = (Get-Date).AddSeconds(15)
while ((Get-Process tomodachi -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }

if (Test-Path $target) { Move-Item $target (Join-Path $InstallDir 'tomodachi-alt.exe') -Force }
Copy-Item (Join-Path $newest.FullName '*') $InstallDir -Force

# So starten wie bei der Anmeldung: über die Aufgabe, also mit Adminrechten
schtasks /Run /TN tomodachi | Out-Null
Write-Host "Fertig: tomodachi $((Get-Item $target).VersionInfo.ProductVersion) läuft. Die alte Version liegt als tomodachi-alt.exe daneben."
Stop-Transcript | Out-Null
Start-Sleep 3
