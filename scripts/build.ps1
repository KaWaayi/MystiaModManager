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
Copy-Item "$root\setup\bin\Release\MystiaModManager.Setup.exe" "$root\ui\bin\Release\MystiaModManager.Update.exe" -Force

# Package zip for GitHub / Gitee Release (manager runtime only)
$pkg = "$root\dist"
New-Item -ItemType Directory -Force -Path $pkg | Out-Null
$zip = "$pkg\MystiaModManager.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
$stage = Join-Path $env:TEMP "MystiaModManager-pack"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$names = @(
  "MystiaModManager.exe",
  "MystiaModManager.exe.config",
  "MystiaModManager.Update.exe",
  "mystia_core.dll",
  "Izakaya.Rules.dll",
  "Newtonsoft.Json.dll",
  "System.Buffers.dll",
  "System.Memory.dll",
  "System.Numerics.Vectors.dll",
  "System.Runtime.CompilerServices.Unsafe.dll",
  "System.ValueTuple.dll",
  "Wpf.Ui.Abstractions.dll",
  "Wpf.Ui.dll"
)
foreach ($name in $names) {
  $src = Join-Path "$root\ui\bin\Release" $name
  if (-not (Test-Path $src)) { throw "缺少打包文件: $src" }
  Copy-Item $src (Join-Path $stage $name) -Force
}
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force
Remove-Item $stage -Recurse -Force

# Single-file installer for players
$setupSrc = "$root\setup\bin\Release\MystiaModManager.Setup.exe"
$setupOut = "$pkg\MystiaModManager.Setup.exe"
Copy-Item $setupSrc $setupOut -Force

Write-Host "Done."
Write-Host "  Manager: $root\ui\bin\Release\MystiaModManager.exe"
Write-Host "  Setup:   $setupOut"
Write-Host "  Package: $zip"
