using System.Text.Json.Serialization;

namespace WindowsTriage.Core;

public enum FindingSeverity { Critical, Warning, Info }
public enum FindingConfidence { High, Medium, Low }
public enum ScanProfile { Quick, Full, Advanced }

public sealed record CollectionOptions
{
    public ScanProfile Profile { get; init; } = ScanProfile.Full;
    public string? OutputPath { get; init; }
    public bool IncludeNetwork { get; init; }
    public bool IncludeCommandLines { get; init; }
    public bool IncludeMachineName { get; init; }
    public bool IncludePrivateArtifacts { get; init; }
    public bool PrintPublicSummary { get; init; }
    public bool OpenReportFolder { get; init; }
    public bool NoZip { get; init; }
    public bool JsonToStdout { get; init; }
    public bool Quiet { get; init; }
    public bool Verbose { get; init; }
    public int? SampleSeconds { get; init; }
    public int SampleIntervalSeconds { get; init; } = 5;
    public int EffectiveSampleSeconds => SampleSeconds ?? Profile switch { ScanProfile.Quick => 20, ScanProfile.Advanced => 120, _ => 60 };
    public int EffectiveSampleIntervalSeconds => Math.Clamp(SampleIntervalSeconds, 1, 60);
}

public sealed record CollectionWarning(string Area, string Message, string Recommendation);
public sealed record Finding(string Id, FindingSeverity Severity, FindingConfidence Confidence, string Category, string Title, string Evidence, string Recommendation);
public sealed record ReportPackage(string ReportFolder, string TextReportPath, string JsonReportPath, string SummaryPath, string PublicSummaryPath, string PrivacyManifestPath, string? ZipPath, TriageData Data);

public sealed record RunSection
{
    public string ToolName { get; init; } = "Windows Triage";
    public string ToolVersion { get; init; } = "";
    public string ReportId { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; }
    public bool MachineNameIncluded { get; init; }
    public bool IsAdministrator { get; init; }
    public string OsVersion { get; init; } = "";
    public bool Is64BitProcess { get; init; }
    public bool Is64BitOperatingSystem { get; init; }
    public ScanProfile Profile { get; init; }
    public bool IncludeNetwork { get; init; }
    public bool IncludeCommandLines { get; init; }
    public bool IncludeMachineName { get; init; }
    public bool IncludePrivateArtifacts { get; init; }
    public int SampleSeconds { get; init; }
    public int SampleIntervalSeconds { get; init; }
}

public sealed record SystemSection
{
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? SystemType { get; init; }
    public ulong? TotalPhysicalMemoryBytes { get; init; }
    public string? WindowsCaption { get; init; }
    public string? WindowsBuild { get; init; }
    public string? WindowsVersion { get; init; }
    public string? InstallDate { get; init; }
    public string? LastBootUpTime { get; init; }
    public string? BiosManufacturer { get; init; }
    public string? BiosName { get; init; }
    public string? BiosVersion { get; init; }
    public string? BiosReleaseDate { get; init; }
}

public sealed record CpuInfo(string? Name, string? Manufacturer, uint? CurrentClockMHz, uint? MaxClockMHz, ushort? LoadPercentage, uint? NumberOfCores, uint? NumberOfLogicalProcessors, ushort? Architecture);
public sealed record MemoryModuleInfo(ulong? CapacityBytes, uint? SpeedMHz, uint? ConfiguredClockSpeedMHz, string? BankLabel);
public sealed record GpuInfo(string? Name, string? AdapterCompatibility, string? DriverVersion, string? DriverDate, ulong? AdapterRamBytes, uint? CurrentHorizontalResolution, uint? CurrentVerticalResolution, string? Status);
public sealed record LogicalDiskInfo(string? DeviceId, string? FileSystem, ulong? SizeBytes, ulong? FreeSpaceBytes);

public sealed record PhysicalDiskInfo
{
    public string? Model { get; init; }
    public string? InterfaceType { get; init; }
    public string? MediaType { get; init; }
    public ulong? SizeBytes { get; init; }
    public string? Status { get; init; }
    public ushort? HealthStatus { get; init; }
    public IReadOnlyList<string> OperationalStatus { get; init; } = Array.Empty<string>();
}

public sealed record StorageSection
{
    public IReadOnlyList<LogicalDiskInfo> LogicalDisks { get; init; } = Array.Empty<LogicalDiskInfo>();
    public IReadOnlyList<PhysicalDiskInfo> PhysicalDisks { get; init; } = Array.Empty<PhysicalDiskInfo>();
    public bool? FailurePredicted { get; init; }
}

public sealed record BatteryInfo(string? Name, ushort? BatteryStatus, ushort? EstimatedChargeRemaining, uint? EstimatedRunTimeMinutes, uint? DesignCapacityMWh, uint? FullChargeCapacityMWh, string? Status);
public sealed record BatterySection { public IReadOnlyList<BatteryInfo> Batteries { get; init; } = Array.Empty<BatteryInfo>(); }
public sealed record ThermalReading(string? Source, string? Name, double? TemperatureC, string Note);

public sealed record ThermalSection
{
    public bool NativeReadingsAvailable { get; init; }
    public IReadOnlyList<ThermalReading> Readings { get; init; } = Array.Empty<ThermalReading>();
    public string Note { get; init; } = "Windows built-in temperature data is often incomplete on many systems.";
}

public sealed record PowerCommandInfo(string Name, string Command, string? OutputFile, int? ExitCode, bool Succeeded, string? Error);

public sealed record PowerSection
{
    public IReadOnlyList<PowerCommandInfo> Commands { get; init; } = Array.Empty<PowerCommandInfo>();
    public int? EnergyErrorCount { get; init; }
    public int? EnergyWarningCount { get; init; }
    public uint? BatteryDesignCapacityMWh { get; init; }
    public uint? BatteryFullChargeCapacityMWh { get; init; }
    public double? BatteryHealthPercent { get; init; }
    public string? BatteryCapacitySource { get; init; }
}

public sealed record EventRecordInfo(DateTimeOffset? TimeCreated, string? LogName, string? ProviderName, int Id, string? LevelDisplayName);
public sealed record EventSection { public int LookbackDays { get; init; } = 14; public IReadOnlyList<EventRecordInfo> RecentEvents { get; init; } = Array.Empty<EventRecordInfo>(); }
public sealed record PerformanceSample(DateTimeOffset Time, double? CpuPercent, double? InterruptPercent, double? AvailableMemoryMB, double? DiskTimePercent);

public sealed record ProcessSnapshotInfo
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public double? CpuPercent { get; init; }
    public double? CpuSeconds { get; init; }
    public double WorkingSetMB { get; init; }
    public double PrivateMemoryMB { get; init; }
    public int Threads { get; init; }
    public int Handles { get; init; }
    public string? Path { get; init; }
    public string? CommandLine { get; init; }
}

public sealed record PerformanceSummary
{
    public double ElapsedSeconds { get; init; }
    public int SampleCount { get; init; }
    public int ValidCpuSampleCount { get; init; }
    public double? AverageCpuPercent { get; init; }
    public double? MaxCpuPercent { get; init; }
    public double? AverageInterruptPercent { get; init; }
    public double? MaxInterruptPercent { get; init; }
    public double? AverageDiskTimePercent { get; init; }
    public double? MinAvailableMemoryMB { get; init; }
    public bool CpuAvailable { get; init; }
    public bool InterruptAvailable { get; init; }
    public bool MemoryAvailable { get; init; }
    public bool DiskAvailable { get; init; }
}

public sealed record PerformanceSection
{
    public PerformanceSummary Summary { get; init; } = new();
    public IReadOnlyList<PerformanceSample> Samples { get; init; } = Array.Empty<PerformanceSample>();
    public IReadOnlyList<ProcessSnapshotInfo> TopCpuProcesses { get; init; } = Array.Empty<ProcessSnapshotInfo>();
    public IReadOnlyList<ProcessSnapshotInfo> TopMemoryProcesses { get; init; } = Array.Empty<ProcessSnapshotInfo>();
}

public sealed record ServiceInfo(string? Name, string? DisplayName, string? State, string? StartMode, uint? ProcessId, string? PathName);
public sealed record StartupInfo(string? Name, string? Command);
public sealed record ServicesStartupSection { public IReadOnlyList<ServiceInfo> RunningServices { get; init; } = Array.Empty<ServiceInfo>(); public IReadOnlyList<StartupInfo> StartupItems { get; init; } = Array.Empty<StartupInfo>(); public bool CommandLinesIncluded { get; init; } }
public sealed record DefenderInfo(bool? AntivirusEnabled, bool? RealTimeProtectionEnabled, uint? AntivirusSignatureAge, string? AntivirusSignatureLastUpdated);
public sealed record UpdateSecuritySection { public IReadOnlyList<string> RecentHotfixIds { get; init; } = Array.Empty<string>(); public DefenderInfo? Defender { get; init; } }

public sealed record NetworkAdapterInfo
{
    public string? Description { get; init; }
    public bool? DhcpEnabled { get; init; }
    public IReadOnlyList<string> IpAddress { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DefaultIpGateway { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DnsServers { get; init; } = Array.Empty<string>();
    public string? MacAddress { get; init; }
}

public sealed record NetworkSection { public bool Included { get; init; } public string? Reason { get; init; } public IReadOnlyList<NetworkAdapterInfo> Adapters { get; init; } = Array.Empty<NetworkAdapterInfo>(); }
public sealed record DriverInfo(string? DeviceName, string? DeviceClass, string? Manufacturer, string? DriverProviderName, string? DriverVersion, string? DriverDate, bool? IsSigned);
public sealed record DriverSection { public bool Included { get; init; } public string? Reason { get; init; } public IReadOnlyList<DriverInfo> FocusedDrivers { get; init; } = Array.Empty<DriverInfo>(); }

public sealed record TriageSections
{
    [JsonPropertyName("run")] public RunSection? Run { get; set; }
    [JsonPropertyName("system")] public SystemSection? System { get; set; }
    [JsonPropertyName("cpu")] public IReadOnlyList<CpuInfo> Cpu { get; set; } = Array.Empty<CpuInfo>();
    [JsonPropertyName("memory")] public IReadOnlyList<MemoryModuleInfo> Memory { get; set; } = Array.Empty<MemoryModuleInfo>();
    [JsonPropertyName("gpu")] public IReadOnlyList<GpuInfo> Gpu { get; set; } = Array.Empty<GpuInfo>();
    [JsonPropertyName("storage")] public StorageSection? Storage { get; set; }
    [JsonPropertyName("battery")] public BatterySection? Battery { get; set; }
    [JsonPropertyName("thermal")] public ThermalSection? Thermal { get; set; }
    [JsonPropertyName("power")] public PowerSection? Power { get; set; }
    [JsonPropertyName("events")] public EventSection? Events { get; set; }
    [JsonPropertyName("performance")] public PerformanceSection? Performance { get; set; }
    [JsonPropertyName("servicesStartup")] public ServicesStartupSection? ServicesStartup { get; set; }
    [JsonPropertyName("updatesSecurity")] public UpdateSecuritySection? UpdatesSecurity { get; set; }
    [JsonPropertyName("network")] public NetworkSection? Network { get; set; }
    [JsonPropertyName("drivers")] public DriverSection? Drivers { get; set; }
}

public sealed class TriageData
{
    [JsonPropertyName("ToolName")] public string ToolName { get; init; } = "Windows Triage";
    [JsonPropertyName("ToolVersion")] public string ToolVersion { get; init; } = typeof(TriageData).Assembly.GetName().Version?.ToString() ?? "0.3.0";
    [JsonPropertyName("SchemaVersion")] public int SchemaVersion { get; init; } = 1;
    [JsonPropertyName("ReportId")] public string ReportId { get; init; } = ReportIdentity.Create();
    [JsonPropertyName("StartedAt")] public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
    [JsonPropertyName("CompletedAt")] public DateTimeOffset? CompletedAt { get; set; }
    [JsonPropertyName("ComputerName"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ComputerName { get; set; }
    [JsonIgnore] public string ReportFolder { get; set; } = "";
    [JsonPropertyName("Sections")] public TriageSections Sections { get; } = new();
    [JsonPropertyName("Findings")] public List<Finding> Findings { get; } = [];
    [JsonPropertyName("Warnings")] public List<CollectionWarning> Warnings { get; } = [];
}

public sealed class CollectionContext
{
    public CollectionContext(CollectionOptions options, string reportFolder)
    {
        Options = options;
        ReportFolder = reportFolder;
        LogsFolder = Path.Combine(reportFolder, "logs");
        PrivateFolder = Path.Combine(reportFolder, "private");
        TemporaryFolder = Path.Combine(Path.GetTempPath(), "WindowsTriage", Path.GetFileName(reportFolder));
        Directory.CreateDirectory(LogsFolder);
        Directory.CreateDirectory(TemporaryFolder);
    }
    public CollectionOptions Options { get; }
    public string ReportFolder { get; }
    public string LogsFolder { get; }
    public string PrivateFolder { get; }
    public string TemporaryFolder { get; }
    public void AddWarning(TriageData data, string area, string message, string recommendation = "The scan continued. Re-run in Advanced mode if this data is important.") => data.Warnings.Add(new CollectionWarning(area, message, recommendation));
    public void RetainPrivateArtifacts()
    {
        if (!Options.IncludePrivateArtifacts || !Directory.Exists(TemporaryFolder)) return;
        Directory.CreateDirectory(PrivateFolder);
        foreach (var source in Directory.EnumerateFiles(TemporaryFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(TemporaryFolder, source);
            var destination = Path.Combine(PrivateFolder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
    }
    public void CleanupTemporaryArtifacts()
    {
        if (Directory.Exists(TemporaryFolder)) Directory.Delete(TemporaryFolder, recursive: true);
        var parent = Path.GetDirectoryName(TemporaryFolder);
        if (parent is not null && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
    }
}

public interface ITriageCollector { string Name { get; } Task CollectAsync(TriageData data, CollectionContext context, IProgress<string>? progress, CancellationToken cancellationToken); }
public interface IDiagnosisRule { IEnumerable<Finding> Analyze(TriageData data); }
public static class ReportIdentity { public static string Create() => $"WindowsTriage_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..39]; }
