<#
.SYNOPSIS
  Збирає інсталятор PoE Noob Man (Velopack) і, за бажанням, публікує реліз на GitHub.

.EXAMPLE
  ./build/pack.ps1 -Version 0.1.0
  # releases/PoeNoobMan-win-Setup.exe — інсталятор; решта файлів у releases/ — пакети для оновлень.

.EXAMPLE
  $env:GITHUB_TOKEN = "<token з правом contents:write>"
  ./build/pack.ps1 -Version 0.2.0 -RepoUrl https://github.com/<you>/POENoobMAN -Publish
  # Завантажує попередній реліз (для дельта-оновлень), пакує і створює GitHub Release.
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$RepoUrl = "",
    [switch]$Publish,
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "publish"
$releasesDir = Join-Path $root "releases"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host "Встановлюю Velopack CLI (vpk)..."
    dotnet tool install -g vpk
}

Write-Host "Тести..."
dotnet test (Join-Path $root "tests/PoeNoobMan.Core.Tests") -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Тести не пройшли" }

Write-Host "Публікація $Version..."
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish (Join-Path $root "src/PoeNoobMan.App") -c Release -r $Runtime --self-contained false `
    -p:Version=$Version -o $publishDir --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершився з помилкою" }

if ($RepoUrl -and $env:GITHUB_TOKEN) {
    Write-Host "Завантажую попередній реліз для дельта-оновлень..."
    vpk download github --repoUrl $RepoUrl --token $env:GITHUB_TOKEN -o $releasesDir
}

Write-Host "Пакування..."
# --framework: Setup сам доставить .NET Desktop Runtime, якщо його немає
vpk pack --packId PoeNoobMan --packVersion $Version --packDir $publishDir --mainExe PoeNoobMan.exe `
    --packTitle "PoE Noob Man" --packAuthors "PoE Noob Man" --framework net10.0-x64-desktop `
    --icon (Join-Path $root "src/PoeNoobMan.App/Assets/icon.ico") `
    --runtime $Runtime --outputDir $releasesDir
if ($LASTEXITCODE -ne 0) { throw "vpk pack завершився з помилкою" }

if ($Publish) {
    if (-not $RepoUrl) { throw "Для -Publish потрібен -RepoUrl" }
    if (-not $env:GITHUB_TOKEN) { throw "Для -Publish задайте змінну GITHUB_TOKEN" }
    vpk upload github --repoUrl $RepoUrl --token $env:GITHUB_TOKEN -o $releasesDir `
        --publish --releaseName "PoE Noob Man $Version" --tag "v$Version"
    if ($LASTEXITCODE -ne 0) { throw "vpk upload завершився з помилкою" }
}

Write-Host ""
Write-Host "Готово: $(Join-Path $releasesDir 'PoeNoobMan-win-Setup.exe')"
