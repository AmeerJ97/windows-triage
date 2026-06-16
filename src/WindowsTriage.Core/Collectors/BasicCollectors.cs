using System.Diagnostics;
using System.Security.Principal;

namespace WindowsTriage.Core.Collectors;

public sealed class RunMetadataCollector : ITriageCollector
{
    public string Name => "Run metadata";

    public Task CollectAsync(TriageData data, CollectionContext context, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Collecting run metadata...");
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);

        var run = new Dictionary<string, object?>
        {
            ["toolName"] = data.ToolName,
            ["toolVersion"] = data.ToolVersion,
            ["reportId"] = data.ReportId,
            ["startedAt"] = data.StartedAt,
            ["machineNameIncluded"] = context.Options.IncludeMachineName,
            ["isAdministrator"] = principal.IsInRole(WindowsBuiltInRole.Administrator),
            ["osVersion"] = Environment.OSVersion.ToString(),
            ["is64BitProcess"] = Environment.Is64BitProcess,
            ["is64BitOperatingSystem"] = Environment.Is64BitOperatingSystem,
            ["profile"] = context.Options.Profile.ToString(),
            ["includeNetwork"] = context.Options.IncludeNetwork,
            ["includeCommandLines"] = context.Options.IncludeCommandLines,
            ["includeMachineName"] = context.Options.IncludeMachineName,
            ["sampleSeconds"] = context.Options.EffectiveSampleSeconds,
            ["sampleIntervalSeconds"] = context.Options.EffectiveSampleIntervalSeconds
        };

        if (context.Options.IncludeMachineName)
        {
            run["computerName"] = Environment.MachineName;
        }

        data.Sections[SectionNames.Run] = run;

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
            var os = WmiHelper.FirstOrEmpty("Win32_OperatingSystem");
            var computer = WmiHelper.FirstOrEmpty("Win32_ComputerSystem");
            var bios = WmiHelper.FirstOrEmpty("Win32_BIOS");

            data.Sections[SectionNames.System] = new Dictionary<string, object?>
            {
                ["manufacturer"] = computer.GetValueOrDefault("Manufacturer"),
                ["model"] = computer.GetValueOrDefault("Model"),
                ["systemType"] = computer.GetValueOrDefault("SystemType"),
                ["totalPhysicalMemoryBytes"] = computer.GetValueOrDefault("TotalPhysicalMemory"),
                ["windowsCaption"] = os.GetValueOrDefault("Caption"),
                ["windowsBuild"] = os.GetValueOrDefault("BuildNumber"),
                ["windowsVersion"] = os.GetValueOrDefault("Version"),
                ["installDate"] = os.GetValueOrDefault("InstallDate"),
                ["lastBootUpTime"] = os.GetValueOrDefault("LastBootUpTime"),
                ["biosManufacturer"] = bios.GetValueOrDefault("Manufacturer"),
                ["biosName"] = bios.GetValueOrDefault("Name"),
                ["biosVersion"] = bios.GetValueOrDefault("SMBIOSBIOSVersion"),
                ["biosReleaseDate"] = bios.GetValueOrDefault("ReleaseDate")
            };
        }
        catch (Exception ex)
        {
            context.AddWarning(data, Name, ex.Message);
        }

        return Task.CompletedTask;
    }
}

public sealed class HardwareCollector : ITriageCollector
{
    public string Name => "Hardware";

    public Task CollectAsync(TriageData data, CollectionContext context, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Collecting hardware details...");
        TryCollect(data, context, SectionNames.Cpu, "Win32_Processor");
        TryCollect(data, context, SectionNames.Memory, "Win32_PhysicalMemory");
        TryCollect(data, context, SectionNames.Gpu, "Win32_VideoController");
        TryCollect(data, context, SectionNames.Storage, "Win32_LogicalDisk", where: "DriveType = 3");
        TryCollect(data, context, "physicalDisks", "Win32_DiskDrive");
        TryCollect(data, context, SectionNames.Battery, "Win32_Battery");

        try
        {
            var readings = new List<Dictionary<string, object?>>();
            foreach (var zone in WmiHelper.Query("MSAcpi_ThermalZoneTemperature", @"root\wmi"))
            {
                zone["Source"] = "MSAcpi_ThermalZoneTemperature";
                zone["TemperatureC"] = ToCelsius(zone.GetValueOrDefault("CurrentTemperature"));
                zone["Note"] = "ACPI thermal-zone reading; not necessarily CPU package/core temperature.";
                readings.Add(zone);
            }

            foreach (var zone in WmiHelper.Query("Win32_PerfFormattedData_Counters_ThermalZoneInformation"))
            {
                zone["Source"] = "Win32_PerfFormattedData_Counters_ThermalZoneInformation";
                zone["TemperatureC"] = ToCelsius(zone.GetValueOrDefault("HighPrecisionTemperature"));
                zone["Note"] = "Windows thermal-zone counter; not necessarily CPU package/core temperature.";
                readings.Add(zone);
            }

            data.Sections[SectionNames.Thermal] = new Dictionary<string, object?>
            {
                ["nativeReadingsAvailable"] = readings.Count > 0,
                ["readings"] = readings,
                ["note"] = "Windows built-in temperature data is often incomplete on many systems."
            };
        }
        catch (Exception ex)
        {
            context.AddWarning(data, "Thermal", ex.Message);
            data.Sections[SectionNames.Thermal] = new Dictionary<string, object?>
            {
                ["nativeReadingsAvailable"] = false,
                ["readings"] = Array.Empty<object>(),
                ["note"] = "Native temperature readings were unavailable."
            };
        }

        return Task.CompletedTask;
    }

    private static void TryCollect(TriageData data, CollectionContext context, string section, string className, string nameSpace = @"root\cimv2", string? where = null)
    {
        try
        {
            data.Sections[section] = WmiHelper.Query(className, nameSpace, where);
        }
        catch (Exception ex)
        {
            context.AddWarning(data, className, ex.Message);
            data.Sections[section] = Array.Empty<object>();
        }
    }

    private static double? ToCelsius(object? deciKelvin)
    {
        if (deciKelvin is null)
        {
            return null;
        }

        if (!double.TryParse(deciKelvin.ToString(), out var value) || value <= 0)
        {
            return null;
        }

        return Math.Round((value - 2732) / 10, 1);
    }
}

public sealed class PowerCollector : ITriageCollector
{
    public string Name => "Power";

    public async Task CollectAsync(TriageData data, CollectionContext context, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Collecting power configuration...");
        var commands = new List<CommandCapture>();

        async Task Capture(string name, string outputFile, params string[] args)
        {
            var path = Path.Combine(context.LogsFolder, outputFile);
            var capture = await CommandRunner.CaptureAsync(name, "powercfg.exe", args, path, cancellationToken).ConfigureAwait(false);
            commands.Add(capture);
            if (!capture.Succeeded && !string.IsNullOrWhiteSpace(capture.Error))
            {
                context.AddWarning(data, name, capture.Error);
            }
        }

        await Capture("Active power scheme", "powercfg_getactivescheme.txt", "/getactivescheme").ConfigureAwait(false);
        await Capture("Available sleep states", "powercfg_a.txt", "/a").ConfigureAwait(false);
        await Capture("Current power requests", "powercfg_requests.txt", "/requests").ConfigureAwait(false);
        await Capture("Processor power settings", "powercfg_query_processor.txt", "/query", "SCHEME_CURRENT", "SUB_PROCESSOR").ConfigureAwait(false);

        if (context.Options.Profile != ScanProfile.Quick)
        {
            await Capture("Battery report", "powercfg_batteryreport_command.txt", "/batteryreport", "/output", Path.Combine(context.LogsFolder, "battery_report.html")).ConfigureAwait(false);
        }

        if (context.Options.Profile == ScanProfile.Advanced)
        {
            await Capture("Energy report", "powercfg_energy_command.txt", "/energy", "/duration", "60", "/output", Path.Combine(context.LogsFolder, "powercfg_energy.html")).ConfigureAwait(false);
            await Capture("Sleep study", "powercfg_sleepstudy_command.txt", "/sleepstudy", "/output", Path.Combine(context.LogsFolder, "sleepstudy_report.html")).ConfigureAwait(false);
        }

        data.Sections[SectionNames.Power] = new Dictionary<string, object?>
        {
            ["commands"] = commands
        };
    }
}
