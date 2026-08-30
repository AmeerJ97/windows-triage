using System.Security.Principal;
using System.Xml.Linq;

namespace WindowsTriage.Core.Collectors;

public sealed class RunMetadataCollector : ITriageCollector
{
    public string Name => "Run metadata";
    public Task CollectAsync(TriageData data, CollectionContext context, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Collecting run metadata...");
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        data.Sections.Run = new RunSection
        {
            ToolName = data.ToolName,
            ToolVersion = data.ToolVersion,
            ReportId = data.ReportId,
            StartedAt = data.StartedAt,
            MachineNameIncluded = context.Options.IncludeMachineName,
            IsAdministrator = principal.IsInRole(WindowsBuiltInRole.Administrator),
            OsVersion = Environment.OSVersion.ToString(),
            Is64BitProcess = Environment.Is64BitProcess,
            Is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
            Profile = context.Options.Profile,
            IncludeNetwork = context.Options.IncludeNetwork,
            IncludeCommandLines = context.Options.IncludeCommandLines,
            IncludeMachineName = context.Options.IncludeMachineName,
            IncludePrivateArtifacts = context.Options.IncludePrivateArtifacts,
            SampleSeconds = context.Options.EffectiveSampleSeconds,
            SampleIntervalSeconds = context.Options.EffectiveSampleIntervalSeconds
        };
        return Task.CompletedTask;
    }
}

public sealed class SystemCollector : ITriageCollector
{
    public string Name => "System";
    public Task CollectAsync(TriageData data, CollectionContext context, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Collecting system details...");
        try
        {
            var os = WmiHelper.FirstOrEmpty("Win32_OperatingSystem", ["Caption", "BuildNumber", "Version", "InstallDate", "LastBootUpTime"]);
            var computer = WmiHelper.FirstOrEmpty("Win32_ComputerSystem", ["Manufacturer", "Model", "SystemType", "TotalPhysicalMemory"]);
            var bios = WmiHelper.FirstOrEmpty("Win32_BIOS", ["Manufacturer", "Name", "SMBIOSBIOSVersion", "ReleaseDate"]);
            data.Sections.System = new SystemSection
            {
                Manufacturer = computer.Text("Manufacturer"),
                Model = computer.Text("Model"),
                SystemType = computer.Text("SystemType"),
                TotalPhysicalMemoryBytes = computer.UInt64("TotalPhysicalMemory"),
                WindowsCaption = os.Text("Caption"),
                WindowsBuild = os.Text("BuildNumber"),
                WindowsVersion = os.Text("Version"),
                InstallDate = os.Text("InstallDate"),
                LastBootUpTime = os.Text("LastBootUpTime"),
                BiosManufacturer = bios.Text("Manufacturer"),
                BiosName = bios.Text("Name"),
                BiosVersion = bios.Text("SMBIOSBIOSVersion"),
                BiosReleaseDate = bios.Text("ReleaseDate")
            };
        }
        catch (Exception ex) { context.AddWarning(data, Name, ex.Message); }
        return Task.CompletedTask;
    }
}

public sealed class HardwareCollector : ITriageCollector
{
    public string Name => "Hardware";
    public Task CollectAsync(TriageData data, CollectionContext context, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Collecting privacy-allowlisted hardware details...");
        data.Sections.Cpu = Collect(data, context, "CPU", () => WmiHelper.Query("Win32_Processor", ["Name", "Manufacturer", "CurrentClockSpeed", "MaxClockSpeed", "LoadPercentage", "NumberOfCores", "NumberOfLogicalProcessors", "Architecture"])
            .Select(r => new CpuInfo(r.Text("Name"), r.Text("Manufacturer"), r.UInt32("CurrentClockSpeed"), r.UInt32("MaxClockSpeed"), r.UInt16("LoadPercentage"), r.UInt32("NumberOfCores"), r.UInt32("NumberOfLogicalProcessors"), r.UInt16("Architecture"))).ToList());
        data.Sections.Memory = Collect(data, context, "Memory", () => WmiHelper.Query("Win32_PhysicalMemory", ["Capacity", "Speed", "ConfiguredClockSpeed", "BankLabel"])
            .Select(r => new MemoryModuleInfo(r.UInt64("Capacity"), r.UInt32("Speed"), r.UInt32("ConfiguredClockSpeed"), r.Text("BankLabel"))).ToList());
        data.Sections.Gpu = Collect(data, context, "GPU", () => WmiHelper.Query("Win32_VideoController", ["Name", "AdapterCompatibility", "DriverVersion", "DriverDate", "AdapterRAM", "CurrentHorizontalResolution", "CurrentVerticalResolution", "Status"])
            .Select(r => new GpuInfo(r.Text("Name"), r.Text("AdapterCompatibility"), r.Text("DriverVersion"), r.Text("DriverDate"), r.UInt64("AdapterRAM"), r.UInt32("CurrentHorizontalResolution"), r.UInt32("CurrentVerticalResolution"), r.Text("Status"))).ToList());

        var logical = Collect(data, context, "Logical disks", () => WmiHelper.Query("Win32_LogicalDisk", ["DeviceID", "FileSystem", "Size", "FreeSpace"], where: "DriveType = 3")
            .Select(r => new LogicalDiskInfo(r.Text("DeviceID"), r.Text("FileSystem"), r.UInt64("Size"), r.UInt64("FreeSpace"))).ToList());
        var physical = Collect(data, context, "Physical disks", () => WmiHelper.Query("Win32_DiskDrive", ["Model", "InterfaceType", "MediaType", "Size", "Status"])
            .Select(r => new PhysicalDiskInfo { Model = r.Text("Model"), InterfaceType = r.Text("InterfaceType"), MediaType = r.Text("MediaType"), SizeBytes = r.UInt64("Size"), Status = r.Text("Status") }).ToList());
        try
        {
            var storageRows = WmiHelper.Query("MSFT_PhysicalDisk", ["FriendlyName", "MediaType", "HealthStatus", "OperationalStatus", "Size"], @"root\Microsoft\Windows\Storage");
            if (storageRows.Count > 0)
                physical = storageRows.Select(r => new PhysicalDiskInfo { Model = r.Text("FriendlyName"), MediaType = r.Text("MediaType"), SizeBytes = r.UInt64("Size"), HealthStatus = r.UInt16("HealthStatus"), OperationalStatus = r.Strings("OperationalStatus") }).ToList();
        }
        catch (Exception ex) { context.AddWarning(data, "Storage health", ex.Message, "Storage health provider was unavailable; basic disk status was retained."); }

        bool? failurePredicted = null;
        try { failurePredicted = WmiHelper.Query("MSStorageDriver_FailurePredictStatus", ["PredictFailure"], @"root\wmi").Any(r => r.Bool("PredictFailure") == true); }
        catch (Exception ex) { context.AddWarning(data, "Disk failure prediction", ex.Message, "SMART prediction may be unavailable for some storage controllers."); }
        data.Sections.Storage = new StorageSection { LogicalDisks = logical, PhysicalDisks = physical, FailurePredicted = failurePredicted };

        data.Sections.Battery = new BatterySection
        {
            Batteries = Collect(data, context, "Battery", () => WmiHelper.Query("Win32_Battery", ["Name", "BatteryStatus", "EstimatedChargeRemaining", "EstimatedRunTime", "DesignCapacity", "FullChargeCapacity", "Status"])
                .Select(r => new BatteryInfo(r.Text("Name"), r.UInt16("BatteryStatus"), r.UInt16("EstimatedChargeRemaining"), r.UInt32("EstimatedRunTime"), r.UInt32("DesignCapacity"), r.UInt32("FullChargeCapacity"), r.Text("Status"))).ToList())
        };

        try
        {
            var readings = WmiHelper.Query("MSAcpi_ThermalZoneTemperature", ["InstanceName", "CurrentTemperature"], @"root\wmi")
                .Select(r => new ThermalReading("MSAcpi_ThermalZoneTemperature", r.Text("InstanceName"), ToCelsius(r.Double("CurrentTemperature")), "ACPI thermal-zone reading; not necessarily CPU package/core temperature."))
                .Concat(WmiHelper.Query("Win32_PerfFormattedData_Counters_ThermalZoneInformation", ["Name", "HighPrecisionTemperature"])
                    .Select(r => new ThermalReading("Win32_PerfFormattedData_Counters_ThermalZoneInformation", r.Text("Name"), ToCelsius(r.Double("HighPrecisionTemperature")), "Windows thermal-zone counter; not necessarily CPU package/core temperature.")))
                .ToList();
            data.Sections.Thermal = new ThermalSection { NativeReadingsAvailable = readings.Any(r => r.TemperatureC.HasValue), Readings = readings };
        }
        catch (Exception ex)
        {
            context.AddWarning(data, "Thermal", ex.Message);
            data.Sections.Thermal = new ThermalSection { NativeReadingsAvailable = false };
        }
        return Task.CompletedTask;
    }

    private static IReadOnlyList<T> Collect<T>(TriageData data, CollectionContext context, string area, Func<IReadOnlyList<T>> action)
    {
        try { return action(); }
        catch (Exception ex) { context.AddWarning(data, area, ex.Message); return Array.Empty<T>(); }
    }
    private static double? ToCelsius(double? value) => value is null or <= 0 ? null : Math.Round((value.Value - 2732) / 10, 1);
}

public sealed class PowerCollector : ITriageCollector
{
    public string Name => "Power";
    public async Task CollectAsync(TriageData data, CollectionContext context, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Collecting power configuration...");
        var captures = new List<CommandCapture>();
        async Task<string> Capture(string name, string file, params string[] args)
        {
            var path = Path.Combine(context.TemporaryFolder, file);
            var capture = await CommandRunner.CaptureAsync(name, "powercfg.exe", args, path, cancellationToken).ConfigureAwait(false);
            captures.Add(capture);
            if (!capture.Succeeded) context.AddWarning(data, name, capture.Error ?? $"powercfg exited with code {capture.ExitCode?.ToString() ?? "unknown"}.");
            return path;
        }

        await Capture("Active power scheme", "powercfg_getactivescheme.txt", "/getactivescheme").ConfigureAwait(false);
        await Capture("Available sleep states", "powercfg_a.txt", "/a").ConfigureAwait(false);
        await Capture("Current power requests", "powercfg_requests.txt", "/requests").ConfigureAwait(false);
        await Capture("Processor power settings", "powercfg_query_processor.txt", "/query", "SCHEME_CURRENT", "SUB_PROCESSOR").ConfigureAwait(false);

        string? batteryXml = null;
        string? energyXml = null;
        if (context.Options.Profile != ScanProfile.Quick)
        {
            batteryXml = Path.Combine(context.TemporaryFolder, "battery_report.xml");
            await Capture("Battery report", "battery_report_command.txt", "/batteryreport", "/xml", "/output", batteryXml).ConfigureAwait(false);
        }
        if (context.Options.Profile == ScanProfile.Advanced)
        {
            energyXml = Path.Combine(context.TemporaryFolder, "powercfg_energy.xml");
            await Capture("Energy report", "powercfg_energy_command.txt", "/energy", "/xml", "/duration", "60", "/output", energyXml).ConfigureAwait(false);
            await Capture("System power report", "system_power_report_command.txt", "/systempowerreport", "/xml", "/output", Path.Combine(context.TemporaryFolder, "system_power_report.xml")).ConfigureAwait(false);
        }

        var parsedBattery = TryParseBattery(batteryXml, data, context);
        var parsedEnergy = TryParseEnergy(energyXml, data, context);
        var fallbackBattery = data.Sections.Battery?.Batteries.FirstOrDefault(b => b.DesignCapacityMWh > 0 && b.FullChargeCapacityMWh > 0);
        var design = parsedBattery.Design ?? fallbackBattery?.DesignCapacityMWh;
        var full = parsedBattery.Full ?? fallbackBattery?.FullChargeCapacityMWh;
        data.Sections.Power = new PowerSection
        {
            Commands = captures.Select(c => new PowerCommandInfo(c.Name, SanitizeCommand(c.Command, context.TemporaryFolder), context.Options.IncludePrivateArtifacts ? Path.Combine("private", Path.GetFileName(c.OutputFile)) : null, c.ExitCode, c.Succeeded, c.Error)).ToList(),
            EnergyErrorCount = parsedEnergy.Errors,
            EnergyWarningCount = parsedEnergy.Warnings,
            BatteryDesignCapacityMWh = design,
            BatteryFullChargeCapacityMWh = full,
            BatteryHealthPercent = design > 0 && full.HasValue ? Math.Round(full.Value * 100d / design.Value, 1) : null,
            BatteryCapacitySource = parsedBattery.Design.HasValue ? "powercfg battery report" : fallbackBattery is null ? null : "Win32_Battery"
        };
    }

    private static string SanitizeCommand(string command, string temporaryFolder) => command.Replace(temporaryFolder, "<temporary>", StringComparison.OrdinalIgnoreCase);
    private static (uint? Design, uint? Full) TryParseBattery(string? path, TriageData data, CollectionContext context)
    {
        if (path is null || !File.Exists(path)) return default;
        try
        {
            return PowerReportParser.ParseBattery(File.ReadAllText(path));
        }
        catch (Exception ex) { context.AddWarning(data, "Battery report parsing", ex.Message); return default; }
    }
    private static (int? Errors, int? Warnings) TryParseEnergy(string? path, TriageData data, CollectionContext context)
    {
        if (path is null || !File.Exists(path)) return default;
        try
        {
            return PowerReportParser.ParseEnergy(File.ReadAllText(path));
        }
        catch (Exception ex) { context.AddWarning(data, "Energy report parsing", ex.Message); return default; }
    }
}

internal static class PowerReportParser
{
    public static (uint? Design, uint? Full) ParseBattery(string xml)
    {
        var document = XDocument.Parse(xml);
        return (FindUInt(document, "DesignCapacity", "DesignedCapacity"), FindUInt(document, "FullChargeCapacity", "FullChargedCapacity"));
    }

    public static (int Errors, int Warnings) ParseEnergy(string xml)
    {
        var document = XDocument.Parse(xml);
        return (document.Descendants().Count(e => e.Name.LocalName.Equals("Error", StringComparison.OrdinalIgnoreCase)), document.Descendants().Count(e => e.Name.LocalName.Equals("Warning", StringComparison.OrdinalIgnoreCase)));
    }

    private static uint? FindUInt(XDocument document, params string[] names)
    {
        foreach (var element in document.Descendants().Where(e => names.Any(n => e.Name.LocalName.Equals(n, StringComparison.OrdinalIgnoreCase))))
            if (uint.TryParse(new string(element.Value.Where(char.IsDigit).ToArray()), out var value) && value > 0) return value;
        return null;
    }
}
