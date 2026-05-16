$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "HWT905Dashboard\HWT905Dashboard.csproj"

Write-Host ""
Write-Host "===============================================" -ForegroundColor Cyan
Write-Host " HWT905 Dashboard REV13" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan
Write-Host ""

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Host "ERROR: .NET SDK was not found." -ForegroundColor Red
    Write-Host "Install the .NET 8 SDK, then run RUN_ME.cmd again." -ForegroundColor Yellow
    exit 1
}

function Invoke-DotnetChecked {
    param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

Write-Host "Restoring packages..." -ForegroundColor Green
Invoke-DotnetChecked restore $proj

Write-Host "Building Release..." -ForegroundColor Green
Invoke-DotnetChecked build $proj -c Release --no-restore

Write-Host "Starting dashboard..." -ForegroundColor Green
Invoke-DotnetChecked run --project $proj -c Release --no-build
