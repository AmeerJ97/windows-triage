namespace WindowsTriage.Core.Collectors;

public sealed class ServicesStartupCollector : ITriageCollector
{
    public string Name => "Services and startup";
    public Task CollectAsync(TriageData data, CollectionContext context, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Collecting privacy-allowlisted services and startup entries...");
        try
        {
            var services = WmiHelper.Query("Win32_Service", ["Name", "DisplayName", "State", "StartMode", "ProcessId", "PathName"])
                .Where(r => string.Equals(r.Text("State"), "Running", StringComparison.OrdinalIgnoreCase))
                .Select(r => new ServiceInfo(r.Text("Name"), r.Text("DisplayName"), r.Text("State"), r.Text("StartMode"), r.UInt32("ProcessId"), context.Options.IncludeCommandLines ? r.Text("PathName") : null)).ToList();
            var startup = WmiHelper.Query("Win32_StartupCommand", ["Name", "Command"])
                .Select(r => new StartupInfo(r.Text("Name"), context.Options.IncludeCommandLines ? r.Text("Command") : null)).ToList();
            data.Sections.ServicesStartup = new ServicesStartupSection { RunningServices = services, StartupItems = startup, CommandLinesIncluded = context.Options.IncludeCommandLines };
        }
        catch (Exception ex) { context.AddWarning(data, Name, ex.Message); }
        return Task.CompletedTask;
    }
}

public sealed class UpdatesSecurityCollector : ITriageCollector
{
    public string Name => "Updates and security";
    public Task CollectAsync(TriageData data, CollectionContext context, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Collecting Windows Update and Defender state...");
        var hotfixIds = new List<string>();
        DefenderInfo? defender = null;
        try
        {
            hotfixIds = WmiHelper.Query("Win32_QuickFixEngineering", ["HotFixID", "InstalledOn"])
                .OrderByDescending(r => HotfixInstalledOn(r)).Take(30).Select(r => r.Text("HotFixID")).Where(v => !string.IsNullOrWhiteSpace(v)).Cast<string>().ToList();
        }
        catch (Exception ex) { context.AddWarning(data, "Hotfixes", ex.Message); }
        try
        {
            var row = WmiHelper.FirstOrEmpty("MSFT_MpComputerStatus", ["AntivirusEnabled", "RealTimeProtectionEnabled", "AntivirusSignatureAge", "AntivirusSignatureLastUpdated"], @"root\Microsoft\Windows\Defender");
            defender = new DefenderInfo(row.Bool("AntivirusEnabled"), row.Bool("RealTimeProtectionEnabled"), row.UInt32("AntivirusSignatureAge"), row.Text("AntivirusSignatureLastUpdated"));
        }
        catch (Exception ex) { context.AddWarning(data, "Defender", ex.Message, "Defender status may be unavailable when another antivirus is active."); }
        data.Sections.UpdatesSecurity = new UpdateSecuritySection { RecentHotfixIds = hotfixIds, Defender = defender };
        return Task.CompletedTask;
    }

    internal static DateTime HotfixInstalledOn(IReadOnlyDictionary<string, object?> row)
    {
        var value = row.Text("InstalledOn");
        return DateTime.TryParse(value, out var date) ? date : DateTime.MinValue;
    }
}

public sealed class NetworkCollector : ITriageCollector
{
    public string Name => "Network";
    public Task CollectAsync(TriageData data, CollectionContext context, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (!context.Options.IncludeNetwork)
        {
            data.Sections.Network = new NetworkSection { Included = false, Reason = "Network addressing details are omitted by default for privacy." };
            return Task.CompletedTask;
        }
        progress?.Report("Collecting explicitly requested network details...");
        try
        {
            var adapters = WmiHelper.Query("Win32_NetworkAdapterConfiguration", ["Description", "DHCPEnabled", "IPAddress", "DefaultIPGateway", "DNSServerSearchOrder", "MACAddress"], where: "IPEnabled = True")
                .Select(r => new NetworkAdapterInfo { Description = r.Text("Description"), DhcpEnabled = r.Bool("DHCPEnabled"), IpAddress = r.Strings("IPAddress"), DefaultIpGateway = r.Strings("DefaultIPGateway"), DnsServers = r.Strings("DNSServerSearchOrder"), MacAddress = r.Text("MACAddress") }).ToList();
            data.Sections.Network = new NetworkSection { Included = true, Adapters = adapters };
        }
        catch (Exception ex) { context.AddWarning(data, Name, ex.Message); }
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
            data.Sections.Drivers = new DriverSection { Included = false, Reason = "Focused driver inventory is collected in Advanced Scan." };
            return Task.CompletedTask;
        }
        progress?.Report("Collecting focused driver inventory...");
        try
        {
            var classes = new HashSet<string>(["DISPLAY", "System", "Processor", "HDC", "SCSIAdapter", "MEDIA", "Net", "Battery", "USB"], StringComparer.OrdinalIgnoreCase);
            var rows = WmiHelper.Query("Win32_PnPSignedDriver", ["DeviceName", "DeviceClass", "Manufacturer", "DriverProviderName", "DriverVersion", "DriverDate", "IsSigned"])
                .Where(r => classes.Contains(r.Text("DeviceClass") ?? ""))
                .Select(r => new DriverInfo(r.Text("DeviceName"), r.Text("DeviceClass"), r.Text("Manufacturer"), r.Text("DriverProviderName"), r.Text("DriverVersion"), r.Text("DriverDate"), r.Bool("IsSigned"))).ToList();
            data.Sections.Drivers = new DriverSection { Included = true, FocusedDrivers = rows };
        }
        catch (Exception ex) { context.AddWarning(data, Name, ex.Message); }
        return Task.CompletedTask;
    }
}
