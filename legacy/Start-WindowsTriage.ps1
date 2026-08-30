#requires -Version 5.1
# LEGACY / UNSUPPORTED: this prototype does not implement the privacy guarantees
# or diagnostic behavior of the maintained .NET application. Do not use it for
# public issue reports. Use the signed WindowsTriage.exe release instead.
<#
.SYNOPSIS
Collects Windows 11 overheating and high-CPU triage evidence.

.DESCRIPTION
Windows Triage is a read-only diagnostic collector for remote overheating
and high CPU investigations. It writes a human-readable text report, a JSON
data file, optional Windows power reports, and an optional zip archive.

The script does not change power settings, stop processes, disable services,
install software, or upload data.
#>

[CmdletBinding()]
param(
    [string]$OutputPath = "",

    [ValidateRange(15, 900)]
    [int]$SampleSeconds = 60,

    [ValidateRange(1, 60)]
    [int]$SampleIntervalSeconds = 5,

    [switch]$Deep,
    [switch]$IncludeNetwork,
    [switch]$IncludeCommandLines,
    [switch]$SkipPowerCfg,
    [switch]$NoZip,
    [switch]$NoPause
)

$ErrorActionPreference = "Continue"
$WarningPreference = "Continue"

$script:ToolVersion = "0.1.0"
$script:StartedAt = Get-Date
$script:Findings = New-Object System.Collections.Generic.List[object]
$script:CollectionErrors = New-Object System.Collections.Generic.List[object]
$script:Data = [ordered]@{}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $desktopPath = if ($env:USERPROFILE) { Join-Path $env:USERPROFILE "Desktop" } else { $null }
    if ($desktopPath -and (Test-Path -LiteralPath $desktopPath)) {
        $OutputPath = $desktopPath
    }
    else {
        $OutputPath = (Get-Location).Path
    }
}
elseif (-not (Test-Path -LiteralPath $OutputPath)) {
    $OutputPath = (Get-Location).Path
}

$safeComputerName = if ($env:COMPUTERNAME) { $env:COMPUTERNAME } else { "UNKNOWN-PC" }
$safeComputerName = $safeComputerName -replace '[^\w.-]', '_'
$timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$script:ReportFolder = Join-Path $OutputPath "WindowsTriage_${safeComputerName}_${timestamp}"
$script:LogsFolder = Join-Path $script:ReportFolder "logs"
$script:ReportPath = Join-Path $script:ReportFolder "diagnostic_report.txt"
$script:JsonPath = Join-Path $script:ReportFolder "diagnostic_data.json"

New-Item -ItemType Directory -Path $script:LogsFolder -Force | Out-Null

function Write-Status {
    param(
        [string]$Message,
        [ValidateSet("Info", "Success", "Warning", "Error")]
        [string]$Level = "Info"
    )

    $color = switch ($Level) {
        "Success" { "Green" }
        "Warning" { "Yellow" }
        "Error" { "Red" }
        default { "Cyan" }
    }

    $line = "[{0}] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Message
    Add-Content -Path $script:ReportPath -Value $line -Encoding UTF8
    Write-Host $line -ForegroundColor $color
}

function Add-CollectionError {
    param(
        [string]$Area,
        [string]$Message,
        [string]$Recommendation = "The tool continued. Re-run as Administrator if this data is important."
    )

    $script:CollectionErrors.Add([pscustomobject]@{
        Area = $Area
        Message = $Message
        Recommendation = $Recommendation
    }) | Out-Null
}

function Invoke-Safe {
    param(
        [string]$Area,
        [scriptblock]$ScriptBlock,
        $Default = $null
    )

    try {
        & $ScriptBlock
    }
    catch {
        Add-CollectionError -Area $Area -Message $_.Exception.Message
        $Default
    }
}

function Get-CimSafe {
    param(
        [string]$Area,
        [string]$ClassName,
        [string]$Namespace = "root/cimv2",
        [string]$Filter
    )

    Invoke-Safe -Area $Area -Default @() -ScriptBlock {
        $params = @{
            ClassName = $ClassName
            Namespace = $Namespace
            ErrorAction = "Stop"
        }
        if ($Filter) {
            $params.Filter = $Filter
        }
        @(Get-CimInstance @params)
    }
}

function Convert-BytesToGB {
    param($Bytes)
    if ($null -eq $Bytes -or $Bytes -eq 0) { return $null }
    [math]::Round(([double]$Bytes / 1GB), 2)
}

function Convert-KBToGB {
    param($Kilobytes)
    if ($null -eq $Kilobytes -or $Kilobytes -eq 0) { return $null }
    [math]::Round(([double]$Kilobytes / 1MB), 2)
}

function Convert-DeciKelvinToCelsius {
    param($DeciKelvin)
    if ($null -eq $DeciKelvin -or $DeciKelvin -le 0) { return $null }
    [math]::Round((([double]$DeciKelvin - 2732) / 10), 1)
}

function Test-IsAdministrator {
    Invoke-Safe -Area "Elevation" -Default $false -ScriptBlock {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    }
}

function Add-Finding {
    param(
        [string]$Id,
        [ValidateSet("Critical", "Warning", "Info")]
        [string]$Severity,
        [ValidateSet("High", "Medium", "Low")]
        [string]$Confidence,
        [string]$Title,
        [string]$Evidence,
        [string]$Recommendation
    )

    $script:Findings.Add([pscustomobject]@{
        Id = $Id
        Severity = $Severity
        Confidence = $Confidence
        Title = $Title
        Evidence = $Evidence
        Recommendation = $Recommendation
    }) | Out-Null
}

function Get-CounterValue {
    param(
        [string]$CounterPath,
        [string]$Area
    )

    Invoke-Safe -Area $Area -Default $null -ScriptBlock {
        $sample = Get-Counter -Counter $CounterPath -ErrorAction Stop
        if ($sample.CounterSamples.Count -gt 0) {
            [double]$sample.CounterSamples[0].CookedValue
        }
        else {
            $null
        }
    }
}

function Invoke-CaptureCommand {
    param(
        [string]$Name,
        [string]$Command,
        [string[]]$Arguments,
        [string]$OutputFileName
    )

    $outputFile = Join-Path $script:LogsFolder $OutputFileName
    $result = [ordered]@{
        Name = $Name
        Command = "$Command $($Arguments -join ' ')"
        OutputFile = $outputFile
        ExitCode = $null
        Succeeded = $false
        Message = $null
    }

    try {
        $output = & $Command @Arguments 2>&1
        $result.ExitCode = $LASTEXITCODE
        $result.Succeeded = ($LASTEXITCODE -eq 0 -or $null -eq $LASTEXITCODE)
        $output | Out-File -FilePath $outputFile -Encoding UTF8
    }
    catch {
        $result.Message = $_.Exception.Message
        Add-CollectionError -Area $Name -Message $_.Exception.Message
    }

    [pscustomobject]$result
}

function Select-EventRecord {
    param($Event)

    $message = $Event.Message
    if ($message -and $message.Length -gt 700) {
        $message = $message.Substring(0, 700) + "..."
    }

    [pscustomobject]@{
        TimeCreated = $Event.TimeCreated
        LogName = $Event.LogName
        ProviderName = $Event.ProviderName
        Id = $Event.Id
        LevelDisplayName = $Event.LevelDisplayName
        Message = $message
    }
}

function Collect-RunMetadata {
    Write-Status "Collecting run metadata..."
    $isAdmin = Test-IsAdministrator

    $script:Data.Run = [pscustomobject]@{
        ToolName = "Windows Triage"
        ToolVersion = $script:ToolVersion
        StartedAt = $script:StartedAt
        ComputerName = $env:COMPUTERNAME
        UserName = $env:USERNAME
        PowerShellVersion = $PSVersionTable.PSVersion.ToString()
        Is64BitProcess = [Environment]::Is64BitProcess
        Is64BitOperatingSystem = [Environment]::Is64BitOperatingSystem
        IsAdministrator = $isAdmin
        DeepMode = [bool]$Deep
        IncludeNetwork = [bool]$IncludeNetwork
        IncludeCommandLines = [bool]$IncludeCommandLines
        SampleSeconds = $SampleSeconds
        SampleIntervalSeconds = $SampleIntervalSeconds
        ReportFolder = $script:ReportFolder
    }

    if (-not $isAdmin) {
        Add-Finding -Id "NOT_ELEVATED" -Severity "Info" -Confidence "High" `
            -Title "The tool is not running as Administrator" `
            -Evidence "Some event logs, power reports, process details, and thermal sources can be unavailable without elevation." `
            -Recommendation "If the first report is missing important data, re-run PowerShell as Administrator and run the script again."
    }
}

function Collect-SystemInfo {
    Write-Status "Collecting system, OS, and BIOS details..."
    $os = Get-CimSafe -Area "Operating system" -ClassName "Win32_OperatingSystem" | Select-Object -First 1
    $cs = Get-CimSafe -Area "Computer system" -ClassName "Win32_ComputerSystem" | Select-Object -First 1
    $bios = Get-CimSafe -Area "BIOS" -ClassName "Win32_BIOS" | Select-Object -First 1
    $version = Invoke-Safe -Area "Windows version registry" -Default $null -ScriptBlock {
        Get-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion" -ErrorAction Stop
    }

    $lastBoot = $null
    $uptimeHours = $null
    if ($os -and $os.LastBootUpTime) {
        $lastBoot = $os.LastBootUpTime
        $uptimeHours = [math]::Round(((Get-Date) - $lastBoot).TotalHours, 1)
    }

    $script:Data.System = [pscustomobject]@{
        Manufacturer = $cs.Manufacturer
        Model = $cs.Model
        SystemType = $cs.SystemType
        TotalPhysicalMemoryGB = Convert-BytesToGB $cs.TotalPhysicalMemory
        WindowsCaption = $os.Caption
        WindowsVersion = $version.DisplayVersion
        WindowsBuild = $os.BuildNumber
        EditionId = $version.EditionID
        InstallDate = $os.InstallDate
        LastBootUpTime = $lastBoot
        UptimeHours = $uptimeHours
        BiosManufacturer = $bios.Manufacturer
        BiosName = $bios.Name
        BiosVersion = ($bios.SMBIOSBIOSVersion -join ", ")
        BiosReleaseDate = $bios.ReleaseDate
    }
}

function Collect-CpuInfo {
    Write-Status "Collecting CPU details..."
    $processors = Get-CimSafe -Area "CPU" -ClassName "Win32_Processor"
    $items = @()

    foreach ($cpu in $processors) {
        $clockRatio = $null
        if ($cpu.MaxClockSpeed -gt 0) {
            $clockRatio = [math]::Round(($cpu.CurrentClockSpeed / $cpu.MaxClockSpeed) * 100, 1)
        }

        $items += [pscustomobject]@{
            Name = $cpu.Name
            Manufacturer = $cpu.Manufacturer
            DeviceId = $cpu.DeviceID
            CurrentClockMHz = $cpu.CurrentClockSpeed
            MaxClockMHz = $cpu.MaxClockSpeed
            CurrentClockPercentOfMax = $clockRatio
            LoadPercentage = $cpu.LoadPercentage
            NumberOfCores = $cpu.NumberOfCores
            NumberOfLogicalProcessors = $cpu.NumberOfLogicalProcessors
            Architecture = $cpu.Architecture
            SocketDesignation = $cpu.SocketDesignation
        }
    }

    $script:Data.Cpu = [pscustomobject]@{
        Processors = $items
        TotalLogicalProcessors = (($items | Measure-Object -Property NumberOfLogicalProcessors -Sum).Sum)
        TotalCores = (($items | Measure-Object -Property NumberOfCores -Sum).Sum)
    }
}

function Collect-MemoryInfo {
    Write-Status "Collecting memory details..."
    $modules = Get-CimSafe -Area "Physical memory" -ClassName "Win32_PhysicalMemory"
    $os = Get-CimSafe -Area "Operating system memory" -ClassName "Win32_OperatingSystem" | Select-Object -First 1

    $moduleItems = @()
    foreach ($module in $modules) {
        $moduleItems += [pscustomobject]@{
            CapacityGB = Convert-BytesToGB $module.Capacity
            SpeedMHz = $module.Speed
            ConfiguredClockSpeedMHz = $module.ConfiguredClockSpeed
            Manufacturer = $module.Manufacturer
            PartNumber = if ($module.PartNumber) { ($module.PartNumber -as [string]).Trim() } else { $null }
            BankLabel = $module.BankLabel
        }
    }

    $totalGB = Convert-KBToGB $os.TotalVisibleMemorySize
    $freeGB = Convert-KBToGB $os.FreePhysicalMemory
    $usedGB = $null
    $pressurePercent = $null
    if ($null -ne $totalGB -and $null -ne $freeGB -and $totalGB -gt 0) {
        $usedGB = [math]::Round(($totalGB - $freeGB), 2)
        $pressurePercent = [math]::Round(($usedGB / $totalGB) * 100, 1)
    }

    $script:Data.Memory = [pscustomobject]@{
        TotalGB = $totalGB
        FreeGB = $freeGB
        UsedGB = $usedGB
        PressurePercent = $pressurePercent
        ModuleCount = $moduleItems.Count
        Modules = $moduleItems
    }
}

function Collect-GpuInfo {
    Write-Status "Collecting GPU details..."
    $adapters = Get-CimSafe -Area "GPU" -ClassName "Win32_VideoController"
    $items = @()

    foreach ($adapter in $adapters) {
        if ($adapter.Name -match "Remote|Mirror") { continue }
        $items += [pscustomobject]@{
            Name = $adapter.Name
            AdapterCompatibility = $adapter.AdapterCompatibility
            DriverVersion = $adapter.DriverVersion
            DriverDate = $adapter.DriverDate
            AdapterRamGB = Convert-BytesToGB $adapter.AdapterRAM
            CurrentResolution = if ($adapter.CurrentHorizontalResolution -and $adapter.CurrentVerticalResolution) {
                "{0}x{1}" -f $adapter.CurrentHorizontalResolution, $adapter.CurrentVerticalResolution
            } else {
                $null
            }
            Status = $adapter.Status
        }
    }

    $script:Data.Gpu = $items
}

function Collect-StorageInfo {
    Write-Status "Collecting storage details..."
    $logicalDisks = Get-CimSafe -Area "Logical disks" -ClassName "Win32_LogicalDisk" -Filter "DriveType=3"
    $physicalDisks = Get-CimSafe -Area "Physical disks" -ClassName "Win32_DiskDrive"
    $storageReliability = @()

    if (Get-Command Get-PhysicalDisk -ErrorAction SilentlyContinue) {
        $storageReliability = Invoke-Safe -Area "Get-PhysicalDisk" -Default @() -ScriptBlock {
            @(Get-PhysicalDisk | Select-Object FriendlyName, MediaType, HealthStatus, OperationalStatus, Size)
        }
    }

    $logicalItems = @()
    foreach ($disk in $logicalDisks) {
        $usedPercent = $null
        if ($disk.Size -gt 0) {
            $usedPercent = [math]::Round((($disk.Size - $disk.FreeSpace) / $disk.Size) * 100, 1)
        }

        $logicalItems += [pscustomobject]@{
            DeviceId = $disk.DeviceID
            VolumeName = $disk.VolumeName
            FileSystem = $disk.FileSystem
            SizeGB = Convert-BytesToGB $disk.Size
            FreeGB = Convert-BytesToGB $disk.FreeSpace
            UsedPercent = $usedPercent
        }
    }

    $physicalItems = @()
    foreach ($disk in $physicalDisks) {
        $physicalItems += [pscustomobject]@{
            Model = $disk.Model
            InterfaceType = $disk.InterfaceType
            MediaType = $disk.MediaType
            SizeGB = Convert-BytesToGB $disk.Size
            Status = $disk.Status
        }
    }

    $script:Data.Storage = [pscustomobject]@{
        LogicalDisks = $logicalItems
        PhysicalDisks = $physicalItems
        PhysicalDiskHealth = $storageReliability
    }
}

function Collect-BatteryInfo {
    Write-Status "Collecting battery details..."
    $batteries = Get-CimSafe -Area "Battery" -ClassName "Win32_Battery"
    $items = @()

    foreach ($battery in $batteries) {
        $items += [pscustomobject]@{
            Name = $battery.Name
            DeviceId = $battery.DeviceID
            BatteryStatus = $battery.BatteryStatus
            EstimatedChargeRemaining = $battery.EstimatedChargeRemaining
            EstimatedRunTimeMinutes = $battery.EstimatedRunTime
            DesignCapacityMWh = $battery.DesignCapacity
            FullChargeCapacityMWh = $battery.FullChargeCapacity
            Status = $battery.Status
        }
    }

    $script:Data.Battery = [pscustomobject]@{
        Present = ($items.Count -gt 0)
        Batteries = $items
    }
}

function Collect-ThermalInfo {
    Write-Status "Collecting native thermal-zone details..."
    $readings = @()

    $acpiZones = Get-CimSafe -Area "ACPI thermal zones" -Namespace "root/wmi" -ClassName "MSAcpi_ThermalZoneTemperature"
    foreach ($zone in $acpiZones) {
        $readings += [pscustomobject]@{
            Source = "MSAcpi_ThermalZoneTemperature"
            Name = $zone.InstanceName
            TemperatureC = Convert-DeciKelvinToCelsius $zone.CurrentTemperature
            RawValue = $zone.CurrentTemperature
            Confidence = "Low"
            Note = "ACPI thermal zone reading; not necessarily CPU package/core temperature."
        }
    }

    $perfZones = Get-CimSafe -Area "Thermal zone performance counters" -ClassName "Win32_PerfFormattedData_Counters_ThermalZoneInformation"
    foreach ($zone in $perfZones) {
        $readings += [pscustomobject]@{
            Source = "Win32_PerfFormattedData_Counters_ThermalZoneInformation"
            Name = $zone.Name
            TemperatureC = Convert-DeciKelvinToCelsius $zone.HighPrecisionTemperature
            RawValue = $zone.HighPrecisionTemperature
            Confidence = "Low"
            Note = "Windows thermal-zone counter; not necessarily CPU package/core temperature."
        }
    }

    $script:Data.Thermal = [pscustomobject]@{
        NativeReadingsAvailable = ($readings.Count -gt 0)
        Readings = $readings
        Note = "Windows built-in temperature data is often incomplete on laptops. Use event logs and optional hardware sensors to corroborate heat."
    }
}

function Collect-PowerInfo {
    if ($SkipPowerCfg) {
        Write-Status "Skipping powercfg collection because -SkipPowerCfg was set." -Level Warning
        $script:Data.Power = [pscustomobject]@{
            Skipped = $true
            Reason = "SkipPowerCfg"
        }
        return
    }

    Write-Status "Collecting power plan and powercfg details..."
    $powercfg = Get-Command powercfg.exe -ErrorAction SilentlyContinue
    if (-not $powercfg) {
        Add-CollectionError -Area "powercfg" -Message "powercfg.exe was not found."
        $script:Data.Power = [pscustomobject]@{
            Available = $false
        }
        return
    }

    $commands = @()
    $commands += Invoke-CaptureCommand -Name "Active power scheme" -Command "powercfg.exe" -Arguments @("/getactivescheme") -OutputFileName "powercfg_getactivescheme.txt"
    $commands += Invoke-CaptureCommand -Name "Available sleep states" -Command "powercfg.exe" -Arguments @("/a") -OutputFileName "powercfg_a.txt"
    $commands += Invoke-CaptureCommand -Name "Current power requests" -Command "powercfg.exe" -Arguments @("/requests") -OutputFileName "powercfg_requests.txt"
    $commands += Invoke-CaptureCommand -Name "Processor power settings" -Command "powercfg.exe" -Arguments @("/query", "SCHEME_CURRENT", "SUB_PROCESSOR") -OutputFileName "powercfg_query_processor.txt"

    if ($script:Data.Battery -and $script:Data.Battery.Present) {
        $batteryReport = Join-Path $script:LogsFolder "battery_report.html"
        $commands += Invoke-CaptureCommand -Name "Battery report" -Command "powercfg.exe" -Arguments @("/batteryreport", "/output", $batteryReport) -OutputFileName "powercfg_batteryreport_command.txt"
    }

    if ($Deep) {
        $energyReport = Join-Path $script:LogsFolder "powercfg_energy.html"
        $commands += Invoke-CaptureCommand -Name "Energy report" -Command "powercfg.exe" -Arguments @("/energy", "/duration", "60", "/output", $energyReport) -OutputFileName "powercfg_energy_command.txt"

        $sleepStudy = Join-Path $script:LogsFolder "sleepstudy_report.html"
        $commands += Invoke-CaptureCommand -Name "Sleep study" -Command "powercfg.exe" -Arguments @("/sleepstudy", "/output", $sleepStudy) -OutputFileName "powercfg_sleepstudy_command.txt"
    }

    $activePowerPlan = Get-CimSafe -Area "Active power plan" -Namespace "root\cimv2\power" -ClassName "Win32_PowerPlan" |
        Where-Object { $_.IsActive } |
        Select-Object -First 1

    $script:Data.Power = [pscustomobject]@{
        Available = $true
        ActivePowerPlan = if ($activePowerPlan) { $activePowerPlan.ElementName } else { $null }
        Commands = $commands
    }
}

function Collect-EventLogs {
    Write-Status "Collecting recent thermal, power, hardware, and crash events..."
    $startTime = (Get-Date).AddDays(-14)
    $events = @()

    $queries = @(
        @{ Name = "Kernel-Power thermal/power"; LogName = "System"; ProviderName = "Microsoft-Windows-Kernel-Power"; Id = @(41, 86, 125) },
        @{ Name = "Kernel-Processor-Power"; LogName = "System"; ProviderName = "Microsoft-Windows-Kernel-Processor-Power"; Id = @(37, 55) },
        @{ Name = "WHEA hardware errors"; LogName = "System"; ProviderName = "Microsoft-Windows-WHEA-Logger"; Id = @(1, 17, 18, 19, 20, 47) },
        @{ Name = "BugCheck"; LogName = "System"; Id = @(1001) },
        @{ Name = "Unexpected shutdown"; LogName = "System"; Id = @(6008) }
    )

    foreach ($query in $queries) {
        $queryName = $query.Name
        $filter = @{
            LogName = $query.LogName
            Id = $query.Id
            StartTime = $startTime
        }
        if ($query.ContainsKey("ProviderName")) {
            $filter.ProviderName = $query.ProviderName
        }

        $queryEvents = Invoke-Safe -Area $queryName -Default @() -ScriptBlock {
            @(Get-WinEvent -FilterHashtable $filter -MaxEvents 80 -ErrorAction Stop)
        }

        foreach ($event in $queryEvents) {
            $events += Select-EventRecord -Event $event
        }
    }

    $script:Data.Events = [pscustomobject]@{
        LookbackDays = 14
        RecentEvents = @($events | Sort-Object TimeCreated -Descending)
    }
}

function Get-ProcessSnapshot {
    param([switch]$IncludeCommandLine)

    $processes = Invoke-Safe -Area "Process snapshot" -Default @() -ScriptBlock {
        @(Get-Process -ErrorAction Stop)
    }

    $items = @()
    foreach ($process in $processes) {
        $cpuSeconds = $null
        try {
            if ($process.TotalProcessorTime) {
                $cpuSeconds = [double]$process.TotalProcessorTime.TotalSeconds
            }
        }
        catch { }

        $items += [pscustomobject]@{
            Id = $process.Id
            Name = $process.ProcessName
            CpuSeconds = $cpuSeconds
            WorkingSetMB = [math]::Round(($process.WorkingSet64 / 1MB), 1)
            PrivateMemoryMB = [math]::Round(($process.PrivateMemorySize64 / 1MB), 1)
            Handles = $process.HandleCount
            Threads = if ($process.Threads) { $process.Threads.Count } else { $null }
            Path = $null
            CommandLine = $null
        }
    }

    if ($IncludeCommandLine -and $items.Count -gt 0) {
        foreach ($item in $items) {
            $details = Get-CimSafe -Area "Process command line" -ClassName "Win32_Process" -Filter ("ProcessId = {0}" -f $item.Id) | Select-Object -First 1
            if ($details) {
                $item.Path = $details.ExecutablePath
                $item.CommandLine = $details.CommandLine
            }
        }
    }

    $items
}

function Collect-PerformanceSample {
    Write-Status "Sampling live performance for $SampleSeconds seconds..."

    if ($SampleIntervalSeconds -gt $SampleSeconds) {
        $SampleIntervalSeconds = $SampleSeconds
    }

    $logicalProcessors = 1
    if ($script:Data.Cpu -and $script:Data.Cpu.TotalLogicalProcessors -gt 0) {
        $logicalProcessors = [int]$script:Data.Cpu.TotalLogicalProcessors
    }

    $startSnapshot = Get-ProcessSnapshot
    $sampleStart = Get-Date
    $samples = @()
    $deadline = $sampleStart.AddSeconds($SampleSeconds)

    while ((Get-Date) -lt $deadline) {
        $cpu = Get-CounterValue -Area "CPU counter" -CounterPath "\Processor(_Total)\% Processor Time"
        $interrupt = Get-CounterValue -Area "Interrupt counter" -CounterPath "\Processor(_Total)\% Interrupt Time"
        $memoryAvailable = Get-CounterValue -Area "Memory counter" -CounterPath "\Memory\Available MBytes"
        $diskTime = Get-CounterValue -Area "Disk counter" -CounterPath "\PhysicalDisk(_Total)\% Disk Time"

        $samples += [pscustomobject]@{
            Time = Get-Date
            CpuPercent = if ($null -ne $cpu) { [math]::Round($cpu, 1) } else { $null }
            InterruptPercent = if ($null -ne $interrupt) { [math]::Round($interrupt, 1) } else { $null }
            AvailableMemoryMB = if ($null -ne $memoryAvailable) { [math]::Round($memoryAvailable, 0) } else { $null }
            DiskTimePercent = if ($null -ne $diskTime) { [math]::Round($diskTime, 1) } else { $null }
        }

        $remaining = [math]::Max(0, [math]::Round(($deadline - (Get-Date)).TotalSeconds, 0))
        if ($remaining -le 0) { break }
        Start-Sleep -Seconds ([math]::Min($SampleIntervalSeconds, $remaining))
    }

    $endSnapshot = Get-ProcessSnapshot -IncludeCommandLine:$IncludeCommandLines
    $sampleEnd = Get-Date
    $elapsedSeconds = [math]::Max(1, ($sampleEnd - $sampleStart).TotalSeconds)

    $startByPid = @{}
    foreach ($process in $startSnapshot) {
        if ($null -ne $process.CpuSeconds) {
            $startByPid[$process.Id] = $process
        }
    }

    $processDeltas = @()
    foreach ($process in $endSnapshot) {
        if ($null -eq $process.CpuSeconds -or -not $startByPid.ContainsKey($process.Id)) { continue }
        $startCpu = $startByPid[$process.Id].CpuSeconds
        if ($null -eq $startCpu) { continue }
        $deltaCpuSeconds = [math]::Max(0, ($process.CpuSeconds - $startCpu))
        $cpuPercent = [math]::Round((($deltaCpuSeconds / $elapsedSeconds) / $logicalProcessors) * 100, 1)

        $processDeltas += [pscustomobject]@{
            Id = $process.Id
            Name = $process.Name
            CpuPercent = $cpuPercent
            CpuSecondsDelta = [math]::Round($deltaCpuSeconds, 2)
            WorkingSetMB = $process.WorkingSetMB
            PrivateMemoryMB = $process.PrivateMemoryMB
            Handles = $process.Handles
            Threads = $process.Threads
            Path = $process.Path
            CommandLine = $process.CommandLine
        }
    }

    $cpuValues = @($samples | Where-Object { $null -ne $_.CpuPercent } | Select-Object -ExpandProperty CpuPercent)
    $interruptValues = @($samples | Where-Object { $null -ne $_.InterruptPercent } | Select-Object -ExpandProperty InterruptPercent)
    $diskValues = @($samples | Where-Object { $null -ne $_.DiskTimePercent } | Select-Object -ExpandProperty DiskTimePercent)
    $memoryValues = @($samples | Where-Object { $null -ne $_.AvailableMemoryMB } | Select-Object -ExpandProperty AvailableMemoryMB)

    $summary = [pscustomobject]@{
        ElapsedSeconds = [math]::Round($elapsedSeconds, 1)
        SampleCount = $samples.Count
        AverageCpuPercent = if ($cpuValues.Count -gt 0) { [math]::Round(($cpuValues | Measure-Object -Average).Average, 1) } else { $null }
        MaxCpuPercent = if ($cpuValues.Count -gt 0) { [math]::Round(($cpuValues | Measure-Object -Maximum).Maximum, 1) } else { $null }
        AverageInterruptPercent = if ($interruptValues.Count -gt 0) { [math]::Round(($interruptValues | Measure-Object -Average).Average, 1) } else { $null }
        MaxInterruptPercent = if ($interruptValues.Count -gt 0) { [math]::Round(($interruptValues | Measure-Object -Maximum).Maximum, 1) } else { $null }
        AverageDiskTimePercent = if ($diskValues.Count -gt 0) { [math]::Round(($diskValues | Measure-Object -Average).Average, 1) } else { $null }
        MinAvailableMemoryMB = if ($memoryValues.Count -gt 0) { [math]::Round(($memoryValues | Measure-Object -Minimum).Minimum, 0) } else { $null }
    }

    $script:Data.Performance = [pscustomobject]@{
        Summary = $summary
        Samples = $samples
        TopCpuProcesses = @($processDeltas | Sort-Object CpuPercent -Descending | Select-Object -First 20)
        TopMemoryProcesses = @($endSnapshot | Sort-Object WorkingSetMB -Descending | Select-Object -First 20)
    }
}

function Collect-ServicesAndStartup {
    Write-Status "Collecting services and startup entries..."
    $services = Get-CimSafe -Area "Services" -ClassName "Win32_Service"
    $startupItems = Get-CimSafe -Area "Startup commands" -ClassName "Win32_StartupCommand"

    $serviceItems = @()
    foreach ($service in ($services | Where-Object { $_.State -eq "Running" } | Sort-Object DisplayName)) {
        $serviceItems += [pscustomobject]@{
            Name = $service.Name
            DisplayName = $service.DisplayName
            State = $service.State
            StartMode = $service.StartMode
            ProcessId = $service.ProcessId
            PathName = if ($IncludeCommandLines) { $service.PathName } else { $null }
        }
    }

    $startupOutput = @()
    foreach ($item in $startupItems) {
        $startupOutput += [pscustomobject]@{
            Name = $item.Name
            Location = $item.Location
            User = $item.User
            Command = if ($IncludeCommandLines) { $item.Command } else { $null }
        }
    }

    $script:Data.ServicesAndStartup = [pscustomobject]@{
        RunningServices = $serviceItems
        StartupItems = $startupOutput
        CommandLinesIncluded = [bool]$IncludeCommandLines
    }
}

function Collect-UpdateAndDefenderInfo {
    Write-Status "Collecting Windows Update and Defender summary..."
    $hotfixes = Invoke-Safe -Area "Hotfix list" -Default @() -ScriptBlock {
        @(Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 20 -Property HotFixID, Description, InstalledOn, InstalledBy)
    }

    $pendingUpdates = Invoke-Safe -Area "Pending Windows Updates" -Default $null -ScriptBlock {
        $session = New-Object -ComObject Microsoft.Update.Session
        $searcher = $session.CreateUpdateSearcher()
        $result = $searcher.Search("IsInstalled=0 and Type='Software'")
        [pscustomobject]@{
            Count = $result.Updates.Count
            Titles = @($result.Updates | ForEach-Object { $_.Title } | Select-Object -First 20)
        }
    }

    $defender = $null
    if (Get-Command Get-MpComputerStatus -ErrorAction SilentlyContinue) {
        $defender = Invoke-Safe -Area "Defender status" -Default $null -ScriptBlock {
            Get-MpComputerStatus | Select-Object `
                AMServiceEnabled,
                AntivirusEnabled,
                RealTimeProtectionEnabled,
                FullScanAge,
                QuickScanAge,
                AntivirusSignatureAge,
                AntivirusSignatureLastUpdated,
                BehaviorMonitorEnabled,
                IoavProtectionEnabled
        }
    }

    $script:Data.UpdateAndSecurity = [pscustomobject]@{
        RecentHotfixes = $hotfixes
        PendingUpdates = $pendingUpdates
        Defender = $defender
    }
}

function Collect-NetworkInfo {
    if (-not $IncludeNetwork) {
        $script:Data.Network = [pscustomobject]@{
            Included = $false
            Reason = "Network details are omitted by default for privacy. Re-run with -IncludeNetwork if needed."
        }
        return
    }

    Write-Status "Collecting network adapter details because -IncludeNetwork was set..."
    $adapters = Get-CimSafe -Area "Network adapters" -ClassName "Win32_NetworkAdapterConfiguration" -Filter "IPEnabled = True"
    $items = @()
    foreach ($adapter in $adapters) {
        $items += [pscustomobject]@{
            Description = $adapter.Description
            DHCPEnabled = $adapter.DHCPEnabled
            IPAddress = $adapter.IPAddress
            DefaultIPGateway = $adapter.DefaultIPGateway
            DNSServerSearchOrder = $adapter.DNSServerSearchOrder
        }
    }

    $script:Data.Network = [pscustomobject]@{
        Included = $true
        Adapters = $items
    }
}

function Collect-DriverInfo {
    if (-not $Deep) {
        $script:Data.Drivers = [pscustomobject]@{
            Included = $false
            Reason = "Driver inventory is collected only in -Deep mode."
        }
        return
    }

    Write-Status "Collecting focused driver inventory for deep mode..."
    $drivers = Get-CimSafe -Area "Driver inventory" -ClassName "Win32_PnPSignedDriver"
    $classes = @("DISPLAY", "System", "Processor", "HDC", "SCSIAdapter", "MEDIA", "Net", "Battery", "USB")
    $items = @()

    foreach ($driver in ($drivers | Where-Object { $classes -contains $_.DeviceClass })) {
        $items += [pscustomobject]@{
            DeviceName = $driver.DeviceName
            DeviceClass = $driver.DeviceClass
            Manufacturer = $driver.Manufacturer
            DriverProviderName = $driver.DriverProviderName
            DriverVersion = $driver.DriverVersion
            DriverDate = $driver.DriverDate
            InfName = $driver.InfName
            IsSigned = $driver.IsSigned
        }
    }

    $script:Data.Drivers = [pscustomobject]@{
        Included = $true
        FocusedDrivers = @($items | Sort-Object DeviceClass, DeviceName)
    }
}

function Invoke-Diagnosis {
    Write-Status "Analyzing collected evidence..."

    $events = @()
    if ($script:Data.Events -and $script:Data.Events.RecentEvents) {
        $events = @($script:Data.Events.RecentEvents)
    }

    $thermalShutdowns = @($events | Where-Object { $_.ProviderName -eq "Microsoft-Windows-Kernel-Power" -and $_.Id -eq 86 })
    if ($thermalShutdowns.Count -gt 0) {
        Add-Finding -Id "THERMAL_SHUTDOWN_EVENT" -Severity "Critical" -Confidence "High" `
            -Title "Windows recorded a critical thermal shutdown" `
            -Evidence ("Kernel-Power event 86 found {0} time(s) in the last {1} days. Most recent: {2}" -f $thermalShutdowns.Count, $script:Data.Events.LookbackDays, $thermalShutdowns[0].TimeCreated) `
            -Recommendation "Prioritize hardware cooling: fan operation, heatsink seating, thermal paste, blocked vents, and OEM thermal/BIOS updates."
    }

    $firmwareLimits = @($events | Where-Object { $_.ProviderName -eq "Microsoft-Windows-Kernel-Processor-Power" -and $_.Id -eq 37 })
    if ($firmwareLimits.Count -gt 0) {
        Add-Finding -Id "FIRMWARE_CPU_LIMIT" -Severity "Warning" -Confidence "High" `
            -Title "Firmware or platform policy limited CPU speed" `
            -Evidence ("Kernel-Processor-Power event 37 found {0} time(s). Most recent: {1}" -f $firmwareLimits.Count, $firmwareLimits[0].TimeCreated) `
            -Recommendation "Check OEM BIOS/UEFI updates, OEM thermal control software, power mode, and whether the system is power or thermally capped."
    }

    $uncleanShutdowns = @($events | Where-Object { ($_.ProviderName -eq "Microsoft-Windows-Kernel-Power" -and $_.Id -eq 41) -or $_.Id -eq 6008 })
    if ($uncleanShutdowns.Count -gt 0) {
        Add-Finding -Id "UNCLEAN_SHUTDOWN" -Severity "Warning" -Confidence "Medium" `
            -Title "Unexpected shutdown or restart events were found" `
            -Evidence ("Found {0} event(s) such as Kernel-Power 41 or EventLog 6008. Most recent: {1}" -f $uncleanShutdowns.Count, $uncleanShutdowns[0].TimeCreated) `
            -Recommendation "Correlate shutdown times with heat, heavy workloads, WHEA errors, and battery/charger behavior."
    }

    $wheaEvents = @($events | Where-Object { $_.ProviderName -eq "Microsoft-Windows-WHEA-Logger" -and ($_.Id -eq 18 -or $_.Id -eq 19) })
    if ($wheaEvents.Count -gt 0) {
        Add-Finding -Id "WHEA_HARDWARE_ERROR" -Severity "Critical" -Confidence "High" `
            -Title "Windows hardware error events were found" `
            -Evidence ("WHEA-Logger event 18/19 found {0} time(s). Most recent: {1}" -f $wheaEvents.Count, $wheaEvents[0].TimeCreated) `
            -Recommendation "Investigate thermal instability, BIOS/chipset/GPU drivers, memory/storage health, and hardware faults."
    }

    if ($script:Data.Performance -and $script:Data.Performance.Summary) {
        $perf = $script:Data.Performance.Summary
        if ($null -ne $perf.AverageCpuPercent -and $perf.AverageCpuPercent -ge 85) {
            Add-Finding -Id "SUSTAINED_HIGH_CPU" -Severity "Critical" -Confidence "High" `
                -Title "CPU usage was very high during the live sample" `
                -Evidence ("Average CPU {0}%, max CPU {1}% across {2} sample(s)." -f $perf.AverageCpuPercent, $perf.MaxCpuPercent, $perf.SampleCount) `
                -Recommendation "Review the top CPU processes in this report. If one process dominates, update, disable, uninstall, or troubleshoot that application/service."
        }
        elseif ($null -ne $perf.AverageCpuPercent -and $perf.AverageCpuPercent -ge 65) {
            Add-Finding -Id "SUSTAINED_HIGH_CPU" -Severity "Warning" -Confidence "Medium" `
                -Title "CPU usage was elevated during the live sample" `
                -Evidence ("Average CPU {0}%, max CPU {1}% across {2} sample(s)." -f $perf.AverageCpuPercent, $perf.MaxCpuPercent, $perf.SampleCount) `
                -Recommendation "Check whether this is expected workload. If the laptop is idle, review top CPU processes and startup/background services."
        }

        if ($null -ne $perf.AverageInterruptPercent -and $perf.AverageInterruptPercent -ge 10) {
            Add-Finding -Id "HIGH_INTERRUPT_TIME" -Severity "Warning" -Confidence "Medium" `
                -Title "CPU interrupt time was elevated" `
                -Evidence ("Average interrupt time {0}%, max {1}%." -f $perf.AverageInterruptPercent, $perf.MaxInterruptPercent) `
                -Recommendation "High interrupt time can point to a driver or device issue. Check chipset, storage, network, Bluetooth, and GPU drivers."
        }
    }

    if ($script:Data.Performance -and $script:Data.Performance.TopCpuProcesses) {
        $topProcess = @($script:Data.Performance.TopCpuProcesses | Sort-Object CpuPercent -Descending | Select-Object -First 1)
        if ($topProcess.Count -gt 0 -and $topProcess[0].CpuPercent -ge 30) {
            Add-Finding -Id "RUNAWAY_PROCESS" -Severity "Warning" -Confidence "High" `
                -Title ("A process consumed a large share of CPU: {0}" -f $topProcess[0].Name) `
                -Evidence ("PID {0}, estimated CPU {1}% during sample, working set {2} MB." -f $topProcess[0].Id, $topProcess[0].CpuPercent, $topProcess[0].WorkingSetMB) `
                -Recommendation "If this was unexpected, identify the application/service behind the process and check for updates, stuck scans, sync loops, or malware."
        }
    }

    if ($script:Data.Memory -and $null -ne $script:Data.Memory.PressurePercent -and $script:Data.Memory.PressurePercent -ge 85) {
        Add-Finding -Id "MEMORY_PRESSURE" -Severity "Warning" -Confidence "High" `
            -Title "Memory pressure is high" `
            -Evidence ("Memory usage is {0}% ({1} GB used of {2} GB)." -f $script:Data.Memory.PressurePercent, $script:Data.Memory.UsedGB, $script:Data.Memory.TotalGB) `
            -Recommendation "Close memory-heavy apps, reduce startup items, or consider a RAM upgrade. Paging can increase disk and CPU activity."
    }

    if ($script:Data.Storage -and $script:Data.Storage.LogicalDisks) {
        $fullDisks = @($script:Data.Storage.LogicalDisks | Where-Object { $null -ne $_.UsedPercent -and $_.UsedPercent -ge 90 })
        foreach ($disk in $fullDisks) {
            Add-Finding -Id "LOW_DISK_SPACE" -Severity "Warning" -Confidence "High" `
                -Title ("Drive {0} is nearly full" -f $disk.DeviceId) `
                -Evidence ("{0}% used, {1} GB free of {2} GB." -f $disk.UsedPercent, $disk.FreeGB, $disk.SizeGB) `
                -Recommendation "Free disk space. Low space can worsen updates, paging, indexing, and general responsiveness."
        }
    }

    if ($script:Data.Thermal -and $script:Data.Thermal.Readings) {
        $hotReadings = @($script:Data.Thermal.Readings | Where-Object { $null -ne $_.TemperatureC -and $_.TemperatureC -ge 85 })
        foreach ($reading in $hotReadings) {
            Add-Finding -Id "NATIVE_THERMAL_READING_HIGH" -Severity "Warning" -Confidence "Low" `
                -Title "A native Windows thermal-zone reading is high" `
                -Evidence ("{0} reported {1} C from {2}." -f $reading.Name, $reading.TemperatureC, $reading.Source) `
                -Recommendation "Treat this as a clue, not a final CPU temperature. Corroborate with thermal events or a hardware sensor tool."
        }
    }

    if (-not $script:Data.Thermal -or -not $script:Data.Thermal.NativeReadingsAvailable) {
        Add-Finding -Id "TEMPERATURE_UNAVAILABLE" -Severity "Info" -Confidence "High" `
            -Title "Native Windows temperature readings were unavailable" `
            -Evidence "No ACPI or thermal-zone counter readings were returned." `
            -Recommendation "Use event logs, CPU behavior, and optionally LibreHardwareMonitor or an OEM utility for actual CPU/GPU temperatures."
    }

    if ($script:Findings.Count -eq 0) {
        Add-Finding -Id "NO_OBVIOUS_CAUSE" -Severity "Info" -Confidence "Medium" `
            -Title "No obvious overheating cause was detected in this run" `
            -Evidence "The sampled counters and queried event logs did not cross the built-in thresholds." `
            -Recommendation "Re-run while the laptop is actively overheating or use -Deep for additional power and driver evidence."
    }
}

function Add-ObjectSection {
    param(
        [System.Collections.Generic.List[string]]$Lines,
        [string]$Title,
        $Object
    )

    $Lines.Add("") | Out-Null
    $Lines.Add("## $Title") | Out-Null
    if ($null -eq $Object) {
        $Lines.Add("(no data)") | Out-Null
        return
    }

    $text = ($Object | Format-List * -Force | Out-String -Width 220).TrimEnd()
    if ([string]::IsNullOrWhiteSpace($text)) {
        $Lines.Add("(no data)") | Out-Null
    }
    else {
        foreach ($line in ($text -split "`r?`n")) {
            $Lines.Add($line) | Out-Null
        }
    }
}

function Write-FinalReport {
    Write-Status "Writing final text and JSON reports..."
    $script:Data.Findings = @($script:Findings)
    $script:Data.CollectionErrors = @($script:CollectionErrors)
    $script:Data.CompletedAt = Get-Date
    $script:Data.ElapsedSeconds = [math]::Round(((Get-Date) - $script:StartedAt).TotalSeconds, 1)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("Windows Triage Diagnostic Report") | Out-Null
    $lines.Add(("Generated: {0}" -f (Get-Date))) | Out-Null
    $lines.Add(("Computer: {0}" -f $env:COMPUTERNAME)) | Out-Null
    $lines.Add(("Tool version: {0}" -f $script:ToolVersion)) | Out-Null
    $lines.Add(("Report folder: {0}" -f $script:ReportFolder)) | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("Privacy note: this tool is read-only and does not upload data. Network IPs and command lines are omitted unless explicitly requested.") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("# Findings") | Out-Null

    foreach ($finding in ($script:Findings | Sort-Object @{ Expression = {
        switch ($_.Severity) {
            "Critical" { 0 }
            "Warning" { 1 }
            default { 2 }
        }
    } }, Id)) {
        $lines.Add("") | Out-Null
        $lines.Add(("[{0}] {1} ({2} confidence)" -f $finding.Severity, $finding.Title, $finding.Confidence)) | Out-Null
        $lines.Add(("Id: {0}" -f $finding.Id)) | Out-Null
        $lines.Add(("Evidence: {0}" -f $finding.Evidence)) | Out-Null
        $lines.Add(("Recommendation: {0}" -f $finding.Recommendation)) | Out-Null
    }

    $lines.Add("") | Out-Null
    $lines.Add("# Collection Summary") | Out-Null
    $lines.Add(("Started: {0}" -f $script:Data.Run.StartedAt)) | Out-Null
    $lines.Add(("Completed: {0}" -f $script:Data.CompletedAt)) | Out-Null
    $lines.Add(("Elapsed seconds: {0}" -f $script:Data.ElapsedSeconds)) | Out-Null
    $lines.Add(("Administrator: {0}" -f $script:Data.Run.IsAdministrator)) | Out-Null
    $lines.Add(("Deep mode: {0}" -f $script:Data.Run.DeepMode)) | Out-Null
    $lines.Add(("Sample duration: {0}s at {1}s interval" -f $SampleSeconds, $SampleIntervalSeconds)) | Out-Null

    if ($script:CollectionErrors.Count -gt 0) {
        $lines.Add("") | Out-Null
        $lines.Add("# Collection Warnings") | Out-Null
        foreach ($errorItem in $script:CollectionErrors) {
            $lines.Add(("- {0}: {1}" -f $errorItem.Area, $errorItem.Message)) | Out-Null
        }
    }

    Add-ObjectSection -Lines $lines -Title "System" -Object $script:Data.System
    Add-ObjectSection -Lines $lines -Title "CPU" -Object $script:Data.Cpu
    Add-ObjectSection -Lines $lines -Title "Memory" -Object $script:Data.Memory
    Add-ObjectSection -Lines $lines -Title "GPU" -Object $script:Data.Gpu
    Add-ObjectSection -Lines $lines -Title "Storage" -Object $script:Data.Storage
    Add-ObjectSection -Lines $lines -Title "Battery" -Object $script:Data.Battery
    Add-ObjectSection -Lines $lines -Title "Thermal" -Object $script:Data.Thermal
    Add-ObjectSection -Lines $lines -Title "Power" -Object $script:Data.Power
    Add-ObjectSection -Lines $lines -Title "Performance Summary" -Object $script:Data.Performance.Summary
    Add-ObjectSection -Lines $lines -Title "Top CPU Processes During Sample" -Object $script:Data.Performance.TopCpuProcesses
    Add-ObjectSection -Lines $lines -Title "Top Memory Processes" -Object $script:Data.Performance.TopMemoryProcesses
    Add-ObjectSection -Lines $lines -Title "Recent Relevant Events" -Object $script:Data.Events.RecentEvents
    Add-ObjectSection -Lines $lines -Title "Services And Startup" -Object $script:Data.ServicesAndStartup
    Add-ObjectSection -Lines $lines -Title "Updates And Defender" -Object $script:Data.UpdateAndSecurity
    Add-ObjectSection -Lines $lines -Title "Network" -Object $script:Data.Network
    Add-ObjectSection -Lines $lines -Title "Drivers" -Object $script:Data.Drivers

    $lines | Set-Content -Path $script:ReportPath -Encoding UTF8
    $script:Data | ConvertTo-Json -Depth 12 | Set-Content -Path $script:JsonPath -Encoding UTF8
}

function New-ReportArchive {
    if ($NoZip) {
        return $null
    }

    Write-Status "Creating zip archive..."
    $zipPath = "$script:ReportFolder.zip"
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop
        [System.IO.Compression.ZipFile]::CreateFromDirectory($script:ReportFolder, $zipPath)
        $zipPath
    }
    catch {
        Add-CollectionError -Area "Zip archive" -Message $_.Exception.Message -Recommendation "Send the report folder manually if zip creation failed."
        $null
    }
}

Write-Status "Starting Windows Triage $script:ToolVersion..."
Write-Status "Output folder: $script:ReportFolder"
Write-Status "This tool is read-only. It will collect diagnostics but will not change Windows settings."

Collect-RunMetadata
Collect-SystemInfo
Collect-CpuInfo
Collect-MemoryInfo
Collect-GpuInfo
Collect-StorageInfo
Collect-BatteryInfo
Collect-ThermalInfo
Collect-PowerInfo
Collect-EventLogs
Collect-PerformanceSample
Collect-ServicesAndStartup
Collect-UpdateAndDefenderInfo
Collect-NetworkInfo
Collect-DriverInfo
Invoke-Diagnosis
Write-FinalReport
$archivePath = New-ReportArchive

Write-Host ""
Write-Host "Windows Triage collection complete." -ForegroundColor Green
Write-Host "Report: $script:ReportPath" -ForegroundColor Green
Write-Host "JSON:   $script:JsonPath" -ForegroundColor Green
if ($archivePath) {
    Write-Host "Zip:    $archivePath" -ForegroundColor Green
}
else {
    Write-Host "Zip:    not created" -ForegroundColor Yellow
}

if (-not $NoPause) {
    Write-Host ""
    Write-Host "Press any key to exit..." -ForegroundColor Yellow
    try {
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    }
    catch {
        Read-Host "Press Enter to exit" | Out-Null
    }
}
