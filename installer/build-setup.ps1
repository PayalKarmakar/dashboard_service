# One-shot: publish apps + compile Setup.exe
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "=== 1/2 Publish ===" -ForegroundColor Cyan
& "$PSScriptRoot\build-publish.ps1"
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

$iscc = @(
  "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
  "C:\Program Files\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "Inno Setup ISCC.exe not found. Install from https://jrsoftware.org/isinfo.php"
}

Write-Host "`n=== 2/2 Compile Setup ===" -ForegroundColor Cyan
Write-Host "Using: $iscc"
& $iscc "$PSScriptRoot\srp-setup.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno compile failed" }

Write-Host "`nDone: $PSScriptRoot\output\SRP-Setup.exe" -ForegroundColor Green
Get-Item "$PSScriptRoot\output\SRP-Setup.exe" | Format-List FullName, Length, LastWriteTime
