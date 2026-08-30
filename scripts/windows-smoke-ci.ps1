$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Windows smoke requires an elevated Administrator runner; collection was not exercised."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "src\WindowsTriage.App\WindowsTriage.App.csproj"
$publishDir = Join-Path $repoRoot "src\WindowsTriage.App\bin\Release\net10.0-windows10.0.22000.0\win-x64\publish"
$exe = Join-Path $publishDir "WindowsTriage.exe"
$outputRoot = Join-Path $env:TEMP ("WindowsTriageSmoke_" + [Guid]::NewGuid().ToString("N"))

function Assert-ExitCode([int[]]$Expected, [string]$Area) {
    if ($Expected -notcontains $LASTEXITCODE) { throw "$Area failed with exit code $LASTEXITCODE" }
}

function Invoke-TriageProcess([string]$Name, [string[]]$Arguments) {
    $stdout = Join-Path $outputRoot "$Name.stdout.txt"
    $stderr = Join-Path $outputRoot "$Name.stderr.txt"
    $process = Start-Process -FilePath $exe -ArgumentList $Arguments -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if ($process.ExitCode -notin @(0, 1)) { throw "$Name failed with exit code $($process.ExitCode): $(Get-Content -Raw $stderr -ErrorAction SilentlyContinue)" }
    $process.ExitCode
}

function Assert-Privacy([string]$ReportFolder) {
    $forbiddenLiterals = @(
        [Environment]::MachineName,
        [Environment]::UserName,
        'C:\Users\',
        '"systemName"',
        '"serialNumber"',
        '"processorId"',
        '"pnpDeviceId"'
    )
    $forbiddenPatterns = @(
        '"commandLine"\s*:\s*"[^\"]+',
        '"path"\s*:\s*"[A-Za-z]:\\'
    )
    foreach ($file in Get-ChildItem -Path $ReportFolder -File -Recurse) {
        if ($file.Extension -eq ".zip") { continue }
        $text = Get-Content -Raw -LiteralPath $file.FullName -ErrorAction SilentlyContinue
        foreach ($literal in $forbiddenLiterals) {
            if ($literal -and $text.IndexOf($literal, [StringComparison]::OrdinalIgnoreCase) -ge 0) { throw "Privacy-sensitive value '$literal' found in $($file.FullName)" }
        }
        foreach ($pattern in $forbiddenPatterns) {
            if ($text -match $pattern) { throw "Privacy-sensitive pattern '$pattern' found in $($file.FullName)" }
        }
    }
}

try {
    dotnet restore $repoRoot --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }
    dotnet test $repoRoot --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }
    dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
    if (-not (Test-Path $exe)) { throw "Missing published executable: $exe" }

    New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
    if ((Invoke-TriageProcess "help" @("--help")) -ne 0) { throw "--help failed" }
    if ((Invoke-TriageProcess "version" @("--version")) -ne 0) { throw "--version failed" }
    $null = Invoke-TriageProcess "default-collect" @("collect", "--profile", "quick", "--sample-seconds", "15", "--sample-interval-seconds", "5", "--output", $outputRoot)
    $defaultReport = Get-ChildItem $outputRoot -Directory -Filter "WindowsTriage_*" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $defaultReport) { throw "Default collection did not create a report folder." }
    foreach ($name in @("diagnostic_report.txt", "diagnostic_data.json", "summary.md", "public_summary.md", "privacy_manifest.json")) {
        if (-not (Test-Path (Join-Path $defaultReport.FullName $name))) { throw "Missing report file: $name" }
    }
    if (Test-Path (Join-Path $defaultReport.FullName "private")) { throw "Default report retained a private directory." }
    Assert-Privacy $defaultReport.FullName
    $defaultZip = $defaultReport.FullName + ".zip"
    if (-not (Test-Path $defaultZip)) { throw "Default ZIP was not created." }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($defaultZip)
    try { if ($archive.Entries.FullName -match '^private/') { throw "Default ZIP contains private artifacts." } } finally { $archive.Dispose() }

    $null = Invoke-TriageProcess "private-collect" @("collect", "--profile", "quick", "--sample-seconds", "15", "--output", $outputRoot, "--include-machine-name", "--include-network", "--include-command-lines", "--include-private-artifacts")
    $privateReport = Get-ChildItem $outputRoot -Directory -Filter "WindowsTriage_*" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not (Test-Path (Join-Path $privateReport.FullName "private"))) { throw "Private opt-in did not retain raw artifacts." }
    $publicSummary = Get-Content -Raw (Join-Path $privateReport.FullName "public_summary.md")
    if ($publicSummary -match [regex]::Escape([Environment]::MachineName)) { throw "Public summary leaked machine name under opt-in." }

    Write-Host "Windows Triage elevated collection and privacy smoke passed."
}
finally {
    if (Test-Path $outputRoot) { Remove-Item -LiteralPath $outputRoot -Recurse -Force }
}
