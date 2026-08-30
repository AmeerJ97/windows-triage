param([Parameter(Mandatory = $true)][string]$ArtifactUrl)
$ErrorActionPreference = "Stop"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "SSM smoke process is not elevated." }
$root = Join-Path $env:TEMP ("WindowsTriageAws_" + [Guid]::NewGuid().ToString("N"))
$evidence = $null
function Invoke-TriageProcess([string]$Name, [string[]]$Arguments) {
    $stdout = Join-Path $root "$Name.stdout.txt"
    $stderr = Join-Path $root "$Name.stderr.txt"
    $process = Start-Process -FilePath $exe -ArgumentList $Arguments -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    [pscustomobject]@{ ExitCode = $process.ExitCode; StdOut = (Get-Content -Raw $stdout -ErrorAction SilentlyContinue); StdErr = (Get-Content -Raw $stderr -ErrorAction SilentlyContinue) }
}
try {
    New-Item -ItemType Directory -Path $root | Out-Null
    $exe = Join-Path $root "WindowsTriage.exe"
    Invoke-WebRequest -UseBasicParsing -Uri $ArtifactUrl -OutFile $exe
    $help = Invoke-TriageProcess "help" @("--help"); if ($help.ExitCode -ne 0) { throw "--help failed: $($help.StdErr)" }
    $version = Invoke-TriageProcess "version" @("--version"); if ($version.ExitCode -ne 0) { throw "--version failed: $($version.StdErr)" }
    $collect = Invoke-TriageProcess "collect" @("collect", "--profile", "quick", "--sample-seconds", "15", "--sample-interval-seconds", "5", "--output", $root)
    if ($collect.ExitCode -notin @(0, 1)) { throw "collection failed with exit code $($collect.ExitCode): $($collect.StdErr)" }
    $report = Get-ChildItem $root -Directory -Filter "WindowsTriage_*" | Select-Object -First 1
    if ($null -eq $report) { throw "No report folder was generated" }
    $required = @("diagnostic_report.txt", "diagnostic_data.json", "summary.md", "public_summary.md", "privacy_manifest.json")
    foreach ($name in $required) { if (-not (Test-Path (Join-Path $report.FullName $name))) { throw "Missing $name" } }
    $allText = (Get-ChildItem $report.FullName -File -Recurse | ForEach-Object { Get-Content -Raw $_.FullName -ErrorAction SilentlyContinue }) -join "`n"
    $privacyPatterns = @([Environment]::MachineName, 'C:\Users\', '"systemName"', '"serialNumber"', '"processorId"', '"pnpDeviceId"')
    if ([Environment]::UserName -notin @('SYSTEM', 'LOCAL SERVICE', 'NETWORK SERVICE', 'Administrator')) { $privacyPatterns += [Environment]::UserName }
    foreach ($pattern in $privacyPatterns) {
        if ($pattern -and $allText -match [regex]::Escape($pattern)) { throw "Privacy pattern found: $pattern" }
    }
    $evidence = [pscustomobject]@{ result = "pass"; os = [Environment]::OSVersion.VersionString; version = $version.StdOut.Trim(); reportFiles = $required; timestamp = (Get-Date).ToUniversalTime().ToString("O") }
}
finally {
    for ($attempt = 1; $attempt -le 6 -and (Test-Path $root); $attempt++) {
        try { Remove-Item $root -Recurse -Force -ErrorAction Stop }
        catch { if ($attempt -lt 6) { Start-Sleep -Seconds 5 } else { Write-Warning "Temporary smoke directory remains locked and will be removed when the instance terminates." } }
    }
}
$evidence | ConvertTo-Json -Compress
