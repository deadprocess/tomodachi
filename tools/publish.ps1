# Baut tomodachi zum Weitergeben: eine eigenständige tomodachi.exe (läuft ohne installiertes .NET) mit den nativen
# WPF-DLLs daneben, dazu LIES-MICH.txt und DRITTANBIETER-LIZENZEN.txt, gepackt als dist\tomodachi-<Version>.zip.
#
#   .\tools\publish.ps1
#
# Eigene Profilbilder (z. B. Reze-Fan-Art) kommen nie in die Exe: Sie liegen in %APPDATA%\tomodachi\avatar.png.
param(
    [string]$Out = (Join-Path $PSScriptRoot '..\dist')
)
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\Tomodachi\Tomodachi.csproj'

# Sicherung gegen versehentliches Weitergeben: Fan-Art darf nicht als Ressource eingebaut sein
if (Select-String -Path $project -Pattern 'reze' -Quiet) {
    throw "Tomodachi.csproj bindet ein privates Bild ein (reze). Zum Weitergeben nur assets\avatar\default.png verwenden."
}

$version = (Select-Xml -Path $project -XPath '//Version').Node.InnerText
if (-not $version) { throw 'Keine <Version> in Tomodachi.csproj gefunden' }

New-Item -ItemType Directory -Force $Out | Out-Null
$staging = Join-Path (Resolve-Path $Out).Path "tomodachi-$version"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }

Write-Host "Baue tomodachi $version …"
# Native DLLs bewusst NICHT in die Exe (kein IncludeNativeLibrariesForSelfExtract): .NET würde sie bei jedem Start nach
# %TEMP%\.net\tomodachi entpacken, ungeprüft wiederverwenden und in den elevated Prozess laden. Dort kann jedes Programm
# des Users sie austauschen und so ohne UAC-Abfrage an Adminrechte kommen. Neben der Exe schützt sie der Installationsordner.
dotnet publish $project -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o $staging -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen ($LASTEXITCODE)" }

Copy-Item (Join-Path $root 'packaging\*.txt') $staging

$zip = "$staging.zip"
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -Force

$exe = Get-Item (Join-Path $staging 'tomodachi.exe')
Write-Host ("Fertig: {0} ({1:N0} MB Exe, {2:N0} MB ZIP)" -f $zip, ($exe.Length / 1MB), ((Get-Item $zip).Length / 1MB))
