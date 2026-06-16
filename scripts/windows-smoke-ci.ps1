$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$publishProject = Join-Path $repoRoot "src\WindowsTriage.App\WindowsTriage.App.csproj"
$publishDir = Join-Path $repoRoot "src\WindowsTriage.App\bin\Release\net10.0-windows10.0.22000.0\win-x64\publish"
$exe = Join-Path $publishDir "WindowsTriage.exe"
$outputRoot = Join-Path $env:TEMP ("WindowsTriageSmoke_" + [Guid]::NewGuid().ToString("N"))

dotnet restore $repoRoot
dotnet test $repoRoot --configuration Release --no-restore
dotnet publish $publishProject -c Release -r win-x64 --self-contained true --no-restore

if (-not (Test-Path $exe)) {
    throw "Missing published executable: $exe"
}

& $exe --help | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "--help failed with exit code $LASTEXITCODE"
}

& $exe --version | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "--version failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
& $exe collect --profile quick --sample-seconds 15 --sample-interval-seconds 5 --output $outputRoot
$collectExit = $LASTEXITCODE

if ($collectExit -ne 0 -and $collectExit -ne 1 -and $collectExit -ne 3) {
    throw "collect failed with unexpected exit code $collectExit"
}

if ($collectExit -eq 3) {
    Write-Host "Collection requires elevation on this runner; CLI help/version and publish smoke passed."
    exit 0
}

$reportFolder = Get-ChildItem -Path $outputRoot -Directory -Filter "WindowsTriage_*" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($null -eq $reportFolder) {
    throw "No report folder was created under $outputRoot"
}

$requiredFiles = @(
    "diagnostic_report.txt",
    "diagnostic_data.json",
    "summary.md",
    "public_summary.md"
)

foreach ($file in $requiredFiles) {
    $path = Join-Path $reportFolder.FullName $file
    if (-not (Test-Path $path)) {
        throw "Missing report file: $path"
    }
}

$jsonPath = Join-Path $reportFolder.FullName "diagnostic_data.json"
$publicSummaryPath = Join-Path $reportFolder.FullName "public_summary.md"
$machineName = [Environment]::MachineName
$json = Get-Content -Raw -Path $jsonPath
$publicSummary = Get-Content -Raw -Path $publicSummaryPath

$forbiddenPatterns = @(
    "computerName",
    "StartName",
    '"User"',
    "CommandLine",
    "C:\\Users\\",
    "IPAddress"
)

foreach ($pattern in $forbiddenPatterns) {
    if ($json -match [regex]::Escape($pattern)) {
        throw "Default diagnostic_data.json contains privacy-sensitive pattern: $pattern"
    }
}

if ($json -match [regex]::Escape($machineName)) {
    throw "Default diagnostic_data.json contains local machine name."
}

if ($publicSummary -match [regex]::Escape($machineName)) {
    throw "public_summary.md contains local machine name."
}

Write-Host "Windows Triage CI smoke passed."
Write-Host "Report folder: $($reportFolder.FullName)"
