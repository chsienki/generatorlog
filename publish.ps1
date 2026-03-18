<#
.SYNOPSIS
    Builds, packs, and publishes the GeneratorLog dotnet tools to NuGet.

.DESCRIPTION
    This script:
    1. Cleans and builds the solution in Release configuration
    2. Packs the tool projects into NuGet packages
    3. Prompts for a NuGet API key
    4. Pushes both packages to nuget.org

.PARAMETER NuGetKey
    Optional. The NuGet API key. If not provided, the script will prompt for it.

.PARAMETER Source
    Optional. The NuGet source URL. Defaults to https://api.nuget.org/v3/index.json.

.PARAMETER SkipTests
    Optional. Skip running tests before packing.

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -NuGetKey "my-api-key"
    .\publish.ps1 -Source "https://my-private-feed/v3/index.json"
#>
param(
    [string]$NuGetKey,
    [string]$Source = "https://api.nuget.org/v3/index.json",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$repoRoot = $PSScriptRoot
$configuration = "Release"
$outputDir = Join-Path $repoRoot "artifacts" "packages"

Write-Host ""
Write-Host "===============================" -ForegroundColor Cyan
Write-Host " GeneratorLog Publish Script" -ForegroundColor Cyan
Write-Host "===============================" -ForegroundColor Cyan
Write-Host ""

# Clean
Write-Host "[1/5] Cleaning..." -ForegroundColor Yellow
dotnet clean "$repoRoot" -c $configuration --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "Clean failed." }

# Build
Write-Host "[2/5] Building ($configuration)..." -ForegroundColor Yellow
dotnet build "$repoRoot" -c $configuration --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

# Test
if (-not $SkipTests) {
    Write-Host "[3/5] Running tests..." -ForegroundColor Yellow
    dotnet test "$repoRoot" -c $configuration --no-build --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "Tests failed. Fix test failures before publishing." }
} else {
    Write-Host "[3/5] Skipping tests (-SkipTests)" -ForegroundColor DarkGray
}

# Pack
Write-Host "[4/5] Packing..." -ForegroundColor Yellow
if (Test-Path $outputDir) { Remove-Item $outputDir -Recurse -Force }

dotnet pack (Join-Path $repoRoot "src" "GeneratorLog" "GeneratorLog.csproj") `
    -c $configuration --no-build -o $outputDir --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "Pack failed for GeneratorLog." }

dotnet pack (Join-Path $repoRoot "src" "GeneratorLog.Analyze" "GeneratorLog.Analyze.csproj") `
    -c $configuration --no-build -o $outputDir --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "Pack failed for GeneratorLog.Analyze." }

$packages = Get-ChildItem "$outputDir" -Filter "*.nupkg"
Write-Host ""
Write-Host "Packages created:" -ForegroundColor Green
foreach ($pkg in $packages) {
    Write-Host "  $($pkg.Name)" -ForegroundColor Green
}

# Prompt for NuGet key if not provided
if (-not $NuGetKey) {
    Write-Host ""
    $NuGetKey = Read-Host "Enter your NuGet API key"
    if (-not $NuGetKey) { throw "NuGet API key is required." }
}

# Push
Write-Host ""
Write-Host "[5/5] Pushing to $Source..." -ForegroundColor Yellow
foreach ($pkg in $packages) {
    Write-Host "  Pushing $($pkg.Name)..." -ForegroundColor Cyan
    dotnet nuget push $pkg.FullName --api-key $NuGetKey --source $Source --skip-duplicate
    if ($LASTEXITCODE -ne 0) { throw "Push failed for $($pkg.Name)." }
}

Write-Host ""
Write-Host "===============================" -ForegroundColor Green
Write-Host " Published successfully!" -ForegroundColor Green
Write-Host "===============================" -ForegroundColor Green
Write-Host ""
Write-Host "Install with:" -ForegroundColor Cyan
Write-Host "  dotnet tool install -g GeneratorLog"
Write-Host "  dotnet tool install -g GeneratorLog.Analyze"
Write-Host ""
Write-Host "Or run one-shot with:" -ForegroundColor Cyan
Write-Host "  dnx generatorlog"
Write-Host "  dnx generatorlog-analyze <file.etl>"
Write-Host ""
