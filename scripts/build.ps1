# MystiaModManager build script
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ((Split-Path -Leaf $root) -ne "MystiaModManager") {
  $root = "e:\项目\ai\MystiaModManager"
}

$env:Path = "$env:USERPROFILE\.cargo\bin;" + $env:Path

Write-Host "== Rust ==" -ForegroundColor Cyan
Push-Location "$root\rust"
cargo build --release
Pop-Location

Write-Host "== UI ==" -ForegroundColor Cyan
Push-Location "$root\ui"
dotnet build -c Release
Pop-Location

Copy-Item "$root\rust\target\release\mystia_core.dll" "$root\ui\bin\Release\mystia_core.dll" -Force

Write-Host "== Setup ==" -ForegroundColor Cyan
Push-Location "$root\setup"
dotnet build -c Release
Pop-Location

# Package zip for GitHub Release (manager runtime)
$pkg = "$root\dist"
New-Item -ItemType Directory -Force -Path $pkg | Out-Null
$zip = "$pkg\MystiaModManager.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$root\ui\bin\Release\*" -DestinationPath $zip -Force

# Single-file installer for players
$setupSrc = "$root\setup\bin\Release\MystiaModManager.Setup.exe"
$setupOut = "$pkg\MystiaModManager.Setup.exe"
Copy-Item $setupSrc $setupOut -Force

Write-Host "Done."
Write-Host "  Manager: $root\ui\bin\Release\MystiaModManager.exe"
Write-Host "  Setup:   $setupOut"
Write-Host "  Package: $zip"
