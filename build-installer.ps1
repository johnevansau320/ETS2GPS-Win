# Builds the app (single-file exe) and wraps it, with the lane overlay if its build is next to the project, in one setup program:
# installer\Output\Ets2GpsSetup-<version>.exe   Needs the .NET 8 SDK and Inno Setup 6 (https://jrsoftware.org/isinfo.php).
param([switch]$SkipBuild)
$ErrorActionPreference = "Stop"
if (-not $SkipBuild) { & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "build.ps1"); if ($LASTEXITCODE -ne 0) { throw "build failed" } }
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) not found" }
& $iscc (Join-Path $PSScriptRoot "installer\Ets2Gps.iss")
if ($LASTEXITCODE -ne 0) { throw "installer build failed" }
Get-ChildItem (Join-Path $PSScriptRoot "installer\Output") -Filter *.exe | Select-Object Name, @{n="MB";e={[math]::Round($_.Length/1MB,1)}}, LastWriteTime
