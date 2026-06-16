using System.Globalization;

namespace WindowsTriage.Core.Collectors;

public sealed class ServicesStartupCollector : ITriageCollector
{
    public string Name => "Services and startup";

    public Task CollectAsync(TriageData data, CollectionContext context, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Collecting services and startup entries...");
        try
        {
            var services = WmiHelper.Query("Win32_Service")
                .Where(row => string.Equals(row.GetValueOrDefault("State")?.ToString(), "Running", StringComparison.OrdinalIgnoreCase))
                .Select(row =>
                {
                    if (!context.Options.IncludeCommandLines)
                    {
                        row.Remove("PathName");
                    }
                    row.Remove("StartName");
                    return row;
                })
                .ToList();

            var startup = WmiHelper.Query("Win32_StartupCommand")
                .Select(row =>
                {
                    if (!context.Options.IncludeCommandLines)
                    {
                        row.Remove("Command");
                    }
                    row.Remove("User");
                    return row;
                })
                .ToList();

            data.Sections[SectionNames.ServicesStartup] = new Dictionary<string, object?>
            {
                ["runningServices"] = services,
                ["startupItems"] = startup,
                ["commandLinesIncluded"] = context.Options.IncludeCommandLines
            };
        }
        catch (Exception ex)
        {
            context.AddWarning(data, Name, ex.Message);
        }

        return Task.CompletedTask;
    }
}

public sealed class UpdatesSecurityCollector : ITriageCollector
{
    public string Name => "Updates and security";

    public Task CollectAsync(TriageData data, CollectionContext context, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Collecting Windows Update and Defender state...");
        var section = new Dictionary<string, object?>();

        try
        {
            section["recentHotfixes"] = WmiHelper.Query("Win32_QuickFixEngineering")
                .OrderByDescending(HotfixInstalledOn)
                .Take(30)
                .ToList();
        }
        catch (Exception ex)
        {
            context.AddWarning(data, "Hotfixes", ex.Message);
        }

        try
        {
            section["defender"] = WmiHelper.FirstOrEmpty("MSFT_MpComputerStatus", @"root\Microsoft\Windows\Defender");
        }
        catch (Exception ex)
        {
            context.AddWarning(data, "Defender", ex.Message, "Defender status may be unavailable on some Windows editions or when another antivirus is active.");
        }

        data.Sections[SectionNames.UpdatesSecurity] = section;
        return Task.CompletedTask;
    }

    internal static DateTime HotfixInstalledOn(Dictionary<string, object?> row)
    {
        var value = row.GetValueOrDefault("InstalledOn")?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTime.MinValue;
        }

        if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var localDate))
        {
            return localDate;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var invariantDate))
        {
            return invariantDate;
        }

        return DateTime.MinValue;
    }
}

public sealed class NetworkCollector : ITriageCollector
{
    public string Name => "Network";

    public Task CollectAsync(TriageData data, CollectionContext context, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (!context.Options.IncludeNetwork)
        {
            data.Sections[SectionNames.Network] = new Dictionary<string, object?>
            {
                ["included"] = false,
                ["reason"] = "Network addressing details are omitted by default for privacy."
            };
            return Task.CompletedTask;
        }

        progress?.Report("Collecting network details...");
        try
        {
            data.Sections[SectionNames.Network] = new Dictionary<string, object?>
            {
                ["included"] = true,
                ["adapters"] = WmiHelper.Query("Win32_NetworkAdapterConfiguration", where: "IPEnabled = True")
            };
        }
        catch (Exception ex)
        {
            context.AddWarning(data, Name, ex.Message);
        }

        return Task.CompletedTask;
    }
}

public sealed class DriverCollector : ITriageCollector
{
    public string Name => "Drivers";

    public Task CollectAsync(TriageData data, CollectionContext context, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (context.Options.Profile != ScanProfile.Advanced)
        {
            data.Sections[SectionNames.Drivers] = new Dictionary<string, object?>
            {
                ["included"] = false,
                ["reason"] = "Focused driver inventory is collected in Advanced Scan."
            };
            return Task.CompletedTask;
        }

        progress?.Report("Collecting focused driver inventory...");
        try
        {
            var classes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "DISPLAY", "System", "Processor", "HDC", "SCSIAdapter", "MEDIA", "Net", "Battery", "USB"
            };

            var rows = WmiHelper.Query("Win32_PnPSignedDriver")
                .Where(row => classes.Contains(row.GetValueOrDefault("DeviceClass")?.ToString() ?? ""))
                .ToList();

            data.Sections[SectionNames.Drivers] = new Dictionary<string, object?>
            {
                ["included"] = true,
                ["focusedDrivers"] = rows
            };
        }
        catch (Exception ex)
        {
            context.AddWarning(data, Name, ex.Message);
        }

        return Task.CompletedTask;
    }
}
