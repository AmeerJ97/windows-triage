namespace WindowsTriage.Core.Diagnosis;

public sealed class GeneralHealthRules : IDiagnosisRule
{
    public IEnumerable<Finding> Analyze(TriageData data)
    {
        foreach (var finding in EventFindings(data)) yield return finding;
        foreach (var finding in PerformanceFindings(data)) yield return finding;
        foreach (var finding in ResourceFindings(data)) yield return finding;
        foreach (var finding in SecurityFindings(data)) yield return finding;
        foreach (var finding in PowerFindings(data)) yield return finding;
        foreach (var finding in ThermalFindings(data)) yield return finding;
    }

    public static bool HasEssentialEvidence(TriageData data)
        => data.Sections.Events is not null
           && data.Sections.Performance?.Summary.CpuAvailable == true
           && data.Sections.Storage is not null
           && data.Sections.Thermal is not null;

    private static IEnumerable<Finding> EventFindings(TriageData data)
    {
        var events = data.Sections.Events?.RecentEvents ?? Array.Empty<EventRecordInfo>();
        if (Latest(events, 86, "Kernel-Power") is { } thermal)
            yield return Finding("THERMAL_SHUTDOWN_EVENT", FindingSeverity.Critical, FindingConfidence.High, "Thermal", "Windows recorded a critical thermal shutdown", $"Kernel-Power event 86 was found. Most recent: {thermal.TimeCreated:O}.", "Prioritize cooling hardware, blocked vents, fan operation, heatsink seating, thermal paste, and OEM firmware updates.");

        var throttle = Latest(events, 37, "Kernel-Processor-Power");
        var averageCpu = data.Sections.Performance?.Summary.AverageCpuPercent;
        var completed = data.CompletedAt ?? DateTimeOffset.Now;
        if (throttle?.TimeCreated is { } throttleTime && averageCpu >= 65 && throttleTime >= completed.AddMinutes(-30))
            yield return Finding("CPU_LIMIT_UNDER_LOAD", FindingSeverity.Warning, FindingConfidence.Medium, "Power", "CPU limiting and elevated load occurred close together", $"CPU averaged {averageCpu:0.#}% and processor-power event 37 occurred at {throttleTime:O}.", "Check OEM power mode, cooling, BIOS/UEFI updates, and whether the workload is expected. These signals are correlated but do not prove a thermal cause.");
        else if (throttle is not null)
            yield return Finding("FIRMWARE_CPU_LIMIT", FindingSeverity.Warning, FindingConfidence.High, "Power", "Firmware or platform policy limited CPU speed", $"Kernel-Processor-Power event 37 was found. Most recent: {throttle.TimeCreated:O}.", "Check OEM power mode, BIOS/UEFI updates, thermal settings, and whether the system is power or thermally capped.");

        var shutdown = Latest(events, 41, "Kernel-Power") ?? Latest(events, 6008);
        if (shutdown is not null)
            yield return Finding("UNCLEAN_SHUTDOWN", FindingSeverity.Warning, FindingConfidence.Medium, "Stability", "Unexpected shutdown or restart events were found", $"Most recent matching event: {shutdown.TimeCreated:O}.", "Correlate shutdown times with heat, charger or battery behavior, WHEA errors, and heavy workloads.");

        var whea = Latest(events, 18, "WHEA-Logger") ?? Latest(events, 19, "WHEA-Logger");
        if (whea is not null)
            yield return Finding("WHEA_HARDWARE_ERROR", FindingSeverity.Critical, FindingConfidence.High, "Hardware", "Windows hardware error events were found", $"WHEA event 18/19 was found. Most recent: {whea.TimeCreated:O}.", "Investigate thermal instability, BIOS/chipset/GPU drivers, memory/storage health, and hardware faults.");

        var bugchecks = events.Where(e => e.Id == 1001 && (Contains(e.ProviderName, "BugCheck") || Contains(e.ProviderName, "WER-SystemErrorReporting"))).ToList();
        if (bugchecks.Count > 0)
            yield return Finding("BUGCHECK_EVENT", FindingSeverity.Warning, FindingConfidence.High, "Stability", "Windows recorded a bugcheck", $"{bugchecks.Count} bugcheck event(s) were found in 14 days. Most recent: {bugchecks.Max(e => e.TimeCreated):O}.", "Review dump files and correlate the crash time with drivers, WHEA events, heat, and recent changes.");

        var ntfs = events.Where(e => e.Id == 55 && Contains(e.ProviderName, "Ntfs")).ToList();
        if (ntfs.Count > 0)
            yield return Finding("NTFS_CORRUPTION", FindingSeverity.Critical, FindingConfidence.High, "Storage", "Windows reported file-system corruption", $"{ntfs.Count} NTFS event 55 occurrence(s) were found. Most recent: {ntfs.Max(e => e.TimeCreated):O}.", "Back up important data promptly, inspect disk health, and run the appropriate Windows file-system diagnostics.");
    }

    private static IEnumerable<Finding> PerformanceFindings(TriageData data)
    {
        var performance = data.Sections.Performance;
        if (performance is null || !performance.Summary.CpuAvailable)
        {
            yield return Finding("PERFORMANCE_DATA_UNAVAILABLE", FindingSeverity.Info, FindingConfidence.High, "Performance", "CPU performance data was unavailable", "The scan produced no valid CPU samples.", "Repair Windows performance instrumentation or rerun as Administrator before treating this scan as healthy.");
            yield break;
        }

        var summary = performance.Summary;
        var profile = data.Sections.Run?.Profile ?? ScanProfile.Full;
        if (summary.AverageCpuPercent >= 85)
            yield return Finding("SUSTAINED_HIGH_CPU", FindingSeverity.Warning, profile == ScanProfile.Quick ? FindingConfidence.Medium : FindingConfidence.High, "Performance", "CPU usage was very high during the live sample", $"Average CPU {summary.AverageCpuPercent:0.#}%, max {summary.MaxCpuPercent:0.#}% across {summary.ValidCpuSampleCount} valid samples.", "Review top CPU processes and confirm whether the workload was expected.");
        else if (summary.AverageCpuPercent >= 65)
            yield return Finding("SUSTAINED_HIGH_CPU", FindingSeverity.Warning, profile == ScanProfile.Quick ? FindingConfidence.Low : FindingConfidence.Medium, "Performance", "CPU usage was elevated during the live sample", $"Average CPU {summary.AverageCpuPercent:0.#}%, max {summary.MaxCpuPercent:0.#}% across {summary.ValidCpuSampleCount} valid samples.", "If the system was expected to be idle, review top CPU processes and background services.");

        if (summary.AverageInterruptPercent >= 10)
            yield return Finding("HIGH_INTERRUPT_TIME", FindingSeverity.Warning, FindingConfidence.Medium, "Drivers", "CPU interrupt time was elevated", $"Average interrupt time {summary.AverageInterruptPercent:0.#}%.", "Check chipset, storage, network, Bluetooth, and GPU drivers.");

        var top = performance.TopCpuProcesses.OrderByDescending(p => p.CpuPercent).FirstOrDefault();
        if (top?.CpuPercent >= 30)
            yield return Finding("RUNAWAY_PROCESS", FindingSeverity.Warning, profile == ScanProfile.Quick ? FindingConfidence.Medium : FindingConfidence.High, "Performance", $"A process consumed a large share of CPU: {top.Name}", $"PID {top.Id}, estimated CPU {top.CpuPercent:0.#}% during the sample.", "Identify the application or service and check for updates, stuck scans, sync loops, or malware.");

        if (IsActiveThermalLoad(data))
        {
            var hottest = data.Sections.Thermal!.Readings.Where(r => r.TemperatureC.HasValue).Max(r => r.TemperatureC);
            yield return Finding("ACTIVE_THERMAL_LOAD", FindingSeverity.Warning, FindingConfidence.Medium, "Thermal", "Elevated CPU load and a high thermal-zone reading occurred together", $"CPU averaged {summary.AverageCpuPercent:0.#}% and the highest native thermal-zone reading was {hottest:0.#} C.", "Reduce the workload and check cooling. The signals occurred together but do not prove the CPU package temperature or root cause.");
        }
    }

    private static IEnumerable<Finding> ResourceFindings(TriageData data)
    {
        var summary = data.Sections.Performance?.Summary;
        if (summary?.MinAvailableMemoryMB is > 0 and < 1024)
            yield return Finding("MEMORY_PRESSURE", FindingSeverity.Warning, FindingConfidence.Medium, "Memory", "Available memory was low during the sample", $"Minimum available memory was {summary.MinAvailableMemoryMB:0} MB.", "Close memory-heavy apps, reduce startup items, or consider a RAM upgrade.");

        var storage = data.Sections.Storage;
        if (storage is not null)
        {
            foreach (var disk in storage.LogicalDisks)
            {
                if (disk.SizeBytes is not > 0 || disk.FreeSpaceBytes is null) continue;
                var used = Math.Round((disk.SizeBytes.Value - disk.FreeSpaceBytes.Value) * 100d / disk.SizeBytes.Value, 1);
                if (used >= 90) yield return Finding("LOW_DISK_SPACE", FindingSeverity.Warning, FindingConfidence.High, "Storage", $"Drive {disk.DeviceId} is nearly full", $"{used}% used.", "Free disk space to protect updates, paging, indexing, and responsiveness.");
            }
            if (storage.FailurePredicted == true)
                yield return Finding("DISK_FAILURE_PREDICTED", FindingSeverity.Critical, FindingConfidence.High, "Storage", "A storage device reports predicted failure", "Windows storage failure prediction returned true.", "Back up important data immediately and replace or professionally assess the affected drive.");
            else if (storage.PhysicalDisks.Any(d => d.HealthStatus is 1 or 2 || (!string.IsNullOrWhiteSpace(d.Status) && !d.Status.Equals("OK", StringComparison.OrdinalIgnoreCase))))
                yield return Finding("DISK_HEALTH_DEGRADED", FindingSeverity.Warning, FindingConfidence.High, "Storage", "A storage device reports degraded health", "At least one physical disk did not report a healthy status.", "Back up important data and inspect the device with the OEM or Windows storage diagnostic tools.");
        }

        var power = data.Sections.Power;
        if (power?.BatteryHealthPercent is > 0 and < 80)
            yield return Finding("BATTERY_WEAR", FindingSeverity.Warning, FindingConfidence.High, "Battery", "Battery full-charge capacity is below 80% of design capacity", $"Battery health is {power.BatteryHealthPercent:0.#}% using {power.BatteryCapacitySource ?? "available capacity data"}.", "Plan for battery replacement if runtime or stability is impaired.");
    }

    private static IEnumerable<Finding> SecurityFindings(TriageData data)
    {
        var defender = data.Sections.UpdatesSecurity?.Defender;
        if (defender is null) yield break;
        if (defender.AntivirusEnabled == false || defender.RealTimeProtectionEnabled == false)
            yield return Finding("DEFENDER_INACTIVE", FindingSeverity.Warning, FindingConfidence.Medium, "Security", "Microsoft Defender antivirus protection is inactive", "Defender reported antivirus or real-time protection disabled.", "Confirm that another trusted antivirus is active; otherwise re-enable and update Microsoft Defender.");
        if (defender.AntivirusEnabled == true && defender.AntivirusSignatureAge > 3)
            yield return Finding("DEFENDER_SIGNATURE_STALE", FindingSeverity.Warning, FindingConfidence.High, "Security", "Microsoft Defender signatures are stale", $"Antivirus signature age is {defender.AntivirusSignatureAge} days.", "Connect to Windows Update or Defender update services and refresh security intelligence.");
    }

    private static IEnumerable<Finding> PowerFindings(TriageData data)
    {
        if (data.Sections.Power?.EnergyErrorCount > 0)
            yield return Finding("POWER_EFFICIENCY_ERRORS", FindingSeverity.Warning, FindingConfidence.Medium, "Power", "Windows energy analysis reported efficiency errors", $"powercfg energy analysis reported {data.Sections.Power.EnergyErrorCount} error(s) and {data.Sections.Power.EnergyWarningCount ?? 0} warning(s).", "Review the retained private energy report if enabled, then check drivers, sleep blockers, and OEM power software.");
    }

    private static IEnumerable<Finding> ThermalFindings(TriageData data)
    {
        var thermal = data.Sections.Thermal;
        if (thermal is null || thermal.Readings.Count == 0)
        {
            yield return Finding("TEMPERATURE_UNAVAILABLE", FindingSeverity.Info, FindingConfidence.High, "Thermal", "Native Windows temperature readings were unavailable", "No ACPI or thermal-zone readings were returned.", "Use event logs, CPU behavior, and an optional hardware sensor tool for actual CPU/GPU temperatures.");
            yield break;
        }
        if (IsActiveThermalLoad(data)) yield break;
        foreach (var reading in thermal.Readings.Where(r => r.TemperatureC >= 85))
            yield return Finding("NATIVE_THERMAL_READING_HIGH", FindingSeverity.Warning, FindingConfidence.Low, "Thermal", "A native Windows thermal-zone reading is high", $"{reading.Name ?? "Thermal zone"} reported {reading.TemperatureC:0.#} C.", "Treat this as a clue, not a final CPU temperature. Corroborate it with events or a hardware sensor tool.");
    }

    private static bool IsActiveThermalLoad(TriageData data) => data.Sections.Performance?.Summary.AverageCpuPercent >= 65 && data.Sections.Thermal?.Readings.Any(r => r.TemperatureC >= 85) == true;
    private static EventRecordInfo? Latest(IEnumerable<EventRecordInfo> events, int id, string? providerContains = null) => events.Where(e => e.Id == id && (providerContains is null || Contains(e.ProviderName, providerContains))).OrderByDescending(e => e.TimeCreated).FirstOrDefault();
    private static bool Contains(string? value, string part) => value?.Contains(part, StringComparison.OrdinalIgnoreCase) == true;
    private static Finding Finding(string id, FindingSeverity severity, FindingConfidence confidence, string category, string title, string evidence, string recommendation) => new(id, severity, confidence, category, title, evidence, recommendation);
}
