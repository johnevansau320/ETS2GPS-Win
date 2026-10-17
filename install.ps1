# Installs ETS2 GPS for the current user (no administrator rights): copies the exe to %LOCALAPPDATA%\Programs\ETS2 GPS
# and adds a Start Menu shortcut. Asks whether to start it with Windows and whether to start it now;
# use -NoPrompt to skip the questions, -Autostart / -Start to answer yes without being asked.
param([switch]$Autostart, [switch]$Start, [switch]$NoPrompt)
$ErrorActionPreference = "Stop"
$exe = Join-Path $PSScriptRoot "publish\Ets2Gps.exe"
if (-not (Test-Path $exe)) { $exe = Join-Path $PSScriptRoot "Ets2Gps.exe" }
if (-not (Test-Path $exe)) { throw "Ets2Gps.exe not found: run build.ps1 first (or put the exe next to this script)." }
$dir = Join-Path $env:LOCALAPPDATA "Programs\ETS2 GPS"
New-Item -ItemType Directory -Force $dir | Out-Null
Copy-Item $exe $dir -Force
$target = Join-Path $dir "Ets2Gps.exe"
$sh = New-Object -ComObject WScript.Shell
function Shortcut($path) { $s = $sh.CreateShortcut($path); $s.TargetPath = $target; $s.WorkingDirectory = $dir; $s.Description = "ETS2 GPS"; $s.Save() }
Shortcut (Join-Path ([Environment]::GetFolderPath("Programs")) "ETS2 GPS.lnk")
Write-Host "Installed to $dir (Start Menu: ETS2 GPS)"
if (-not $NoPrompt -and -not $Autostart) {
    if ((Read-Host "Also start it automatically when you log in to Windows? (y/n)") -eq "y") { $Autostart = $true }
}
if ($Autostart) { Shortcut (Join-Path ([Environment]::GetFolderPath("Startup")) "ETS2 GPS.lnk"); Write-Host "Autostart added." }
if (-not $NoPrompt -and -not $Start) {
    if ((Read-Host "Start it now? (y/n)") -eq "y") { $Start = $true }
}
if ($Start) { Start-Process $target }
