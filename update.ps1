# devlog'u derleyip kurulu surumu gunceller.
#
#   .\update.ps1        -> derle, paketle, kur
#   .\update.ps1 -Open  -> kurulumdan sonra arayuzu da acar
#
# Calisan devlog surecleri dosyalari kilitledigi icin once onlari kapatir.
# Surum numarasini otomatik bir artirir; 'dotnet tool update' ayni surumu yeniden kurmaz.

param([switch]$Open)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\DevLog.Cli\DevLog.Cli.csproj"
$nupkg = Join-Path $root "nupkg"

Write-Host "1/4  Calisan devlog surecleri kapatiliyor..." -ForegroundColor Cyan
$processes = Get-Process devlog -ErrorAction SilentlyContinue
if ($processes) {
    $processes | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Write-Host "     $($processes.Count) surec kapatildi."
} else {
    Write-Host "     Acik surec yok."
}

Write-Host "2/4  Surum numarasi artiriliyor..." -ForegroundColor Cyan
$content = Get-Content $project -Raw
if ($content -notmatch '<Version>(\d+)\.(\d+)\.(\d+)</Version>') {
    throw "csproj icinde <Version> etiketi bulunamadi."
}
$newVersion = "{0}.{1}.{2}" -f $Matches[1], $Matches[2], ([int]$Matches[3] + 1)
$content = $content -replace '<Version>[\d.]+</Version>', "<Version>$newVersion</Version>"
Set-Content -Path $project -Value $content -Encoding utf8
Write-Host "     $newVersion"

Write-Host "3/4  Paketleniyor..." -ForegroundColor Cyan
dotnet pack $project -c Release -o $nupkg --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet pack basarisiz oldu." }

Write-Host "4/4  Kuruluyor..." -ForegroundColor Cyan
dotnet tool update --global --add-source $nupkg DevLog.Cli --version $newVersion
if ($LASTEXITCODE -ne 0) { throw "dotnet tool update basarisiz oldu." }

Write-Host ""
Write-Host "devlog $newVersion kuruldu." -ForegroundColor Green

if ($Open) {
    Write-Host "Arayuz aciliyor..." -ForegroundColor Cyan
    Start-Process devlog -ArgumentList "ui"
}
