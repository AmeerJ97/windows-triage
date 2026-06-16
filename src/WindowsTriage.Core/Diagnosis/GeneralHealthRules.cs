namespace WindowsTriage.Core.Diagnosis;

public sealed class GeneralHealthRules : IDiagnosisRule
{
    public IEnumerable<Finding> Analyze(TriageData data)
    {
        foreach (var finding in EventFindings(data))
        {
            yield return finding;
        }

        foreach (var finding in PerformanceFindings(data))
        {
            yield return finding;
        }

        foreach (var finding in ResourceFindings(data))
        {
            yield return finding;
        }

        foreach (var finding in ThermalFindings(data))
        {
            yield return finding;
        }
    }

    private static IEnumerable<Finding> EventFindings(TriageData data)
    {
        var events = Rows(data.Section(SectionNames.Events).GetValueOrDefault("recentEvents")).ToList();

        if (HasEvent(events, "Microsoft-Windows-Kernel-Power", 86, out var thermal))
        {
            yield return new Finding(
                "THERMAL_SHUTDOWN_EVENT",
                FindingSeverity.Critical,
                FindingConfidence.High,
                "Thermal",
                "Windows recorded a critical thermal shutdown",
                $"Kernel-Power event 86 was found. Most recent: {thermal.GetValueOrDefault("timeCreated")}.",
                "Prioritize cooling hardware: fan operation, heatsink seating, thermal paste, blocked vents, and OEM BIOS or thermal-control updates.");
        }

        if (HasEvent(events, "Microsoft-Windows-Kernel-Processor-Power", 37, out var throttle))
        {
            yield return new Finding(
                "FIRMWARE_CPU_LIMIT",
                FindingSeverity.Warning,
                FindingConfidence.High,
                "Power",
                "Firmware or platform policy limited CPU speed",
                $"Kernel-Processor-Power event 37 was found. Most recent: {throttle.GetValueOrDefault("timeCreated")}.",
                "Check OEM power mode, BIOS/UEFI updates, thermal settings, and whether the system is power or thermally capped.");
        }

        if (HasEvent(events, "Microsoft-Windows-Kernel-Power", 41, out var shutdown) || HasEvent(events, null, 6008, out shutdown))
        {
            yield return new Finding(
                "UNCLEAN_SHUTDOWN",
                FindingSeverity.Warning,
                FindingConfidence.Medium,
                "Stability",
                "Unexpected shutdown or restart events were found",
                $"Most recent matching event: {shutdown.GetValueOrDefault("timeCreated")}.",
                "Correlate shutdown times with heat, charger/battery behavior, WHEA errors, and heavy workloads.");
        }

        if (HasEvent(events, "Microsoft-Windows-WHEA-Logger", 18, out var whea) || HasEvent(events, "Microsoft-Windows-WHEA-Logger", 19, out whea))
        {
            yield return new Finding(
                "WHEA_HARDWARE_ERROR",
                FindingSeverity.Critical,
                FindingConfidence.High,
                "Hardware",
                "Windows hardware error events were found",
                $"WHEA-Logger event 18/19 was found. Most recent: {whea.GetValueOrDefault("timeCreated")}.",
                "Investigate thermal instability, BIOS/chipset/GPU drivers, memory/storage health, and hardware faults.");
        }
    }

    private static IEnumerable<Finding> PerformanceFindings(TriageData data)
    {
        var performance = data.Section(SectionNames.Performance);
        var summary = Map(performance.GetValueOrDefault("summary"));
        var averageCpu = Number(summary.GetValueOrDefault("averageCpuPercent"));
        var maxCpu = Number(summary.GetValueOrDefault("maxCpuPercent"));

        if (averageCpu >= 85)
        {
            yield return new Finding(
                "SUSTAINED_HIGH_CPU",
                FindingSeverity.Critical,
                FindingConfidence.High,
                "Performance",
                "CPU usage was very high during the live sample",
                $"Average CPU {averageCpu}%, max CPU {maxCpu}%.",
                "Review top CPU processes. If one process dominates unexpectedly, update, disable, uninstall, or troubleshoot that application or service.");
        }
        else if (averageCpu >= 65)
        {
            yield return new Finding(
                "SUSTAINED_HIGH_CPU",
                FindingSeverity.Warning,
                FindingConfidence.Medium,
                "Performance",
                "CPU usage was elevated during the live sample",
                $"Average CPU {averageCpu}%, max CPU {maxCpu}%.",
                "If the system was idle, review top CPU processes and startup/background services.");
        }

        var averageInterrupt = Number(summary.GetValueOrDefault("averageInterruptPercent"));
        if (averageInterrupt >= 10)
        {
            yield return new Finding(
                "HIGH_INTERRUPT_TIME",
                FindingSeverity.Warning,
                FindingConfidence.Medium,
                "Drivers",
                "CPU interrupt time was elevated",
                $"Average interrupt time {averageInterrupt}%.",
                "High interrupt time can point to a driver or device issue. Check chipset, storage, network, Bluetooth, and GPU drivers.");
        }

        var topCpu = Rows(performance.GetValueOrDefault("topCpuProcesses"))
            .OrderByDescending(row => Number(row.GetValueOrDefault("cpuPercent")))
            .FirstOrDefault();

        if (topCpu is not null && Number(topCpu.GetValueOrDefault("cpuPercent")) >= 30)
        {
            yield return new Finding(
                "RUNAWAY_PROCESS",
                FindingSeverity.Warning,
                FindingConfidence.High,
                "Performance",
                $"A process consumed a large share of CPU: {topCpu.GetValueOrDefault("name")}",
                $"PID {topCpu.GetValueOrDefault("id")}, estimated CPU {topCpu.GetValueOrDefault("cpuPercent")}% during sample.",
                "Identify the application or service behind the process and check for updates, stuck scans, sync loops, or malware.");
        }
    }

    private static IEnumerable<Finding> ResourceFindings(TriageData data)
    {
        var memoryRows = data.ListSection(SectionNames.Memory);
        var storageRows = data.ListSection(SectionNames.Storage);

        var totalMemory = memoryRows.Sum(row => Number(row.GetValueOrDefault("Capacity")));
        if (totalMemory > 0)
        {
            var performance = data.Section(SectionNames.Performance);
            var summary = Map(performance.GetValueOrDefault("summary"));
            var minAvailableMb = Number(summary.GetValueOrDefault("minAvailableMemoryMB"));
            if (minAvailableMb > 0 && minAvailableMb < 1024)
            {
                yield return new Finding(
                    "MEMORY_PRESSURE",
                    FindingSeverity.Warning,
                    FindingConfidence.Medium,
                    "Memory",
                    "Available memory was low during the sample",
                    $"Minimum available memory was {minAvailableMb} MB.",
                    "Close memory-heavy apps, reduce startup items, or consider a RAM upgrade. Paging can increase disk and CPU activity.");
            }
        }

        foreach (var disk in storageRows)
        {
            var size = Number(disk.GetValueOrDefault("Size"));
            var free = Number(disk.GetValueOrDefault("FreeSpace"));
            if (size <= 0 || free < 0)
            {
                continue;
            }

            var usedPercent = Math.Round(((size - free) / size) * 100, 1);
            if (usedPercent >= 90)
            {
                yield return new Finding(
                    "LOW_DISK_SPACE",
                    FindingSeverity.Warning,
                    FindingConfidence.High,
                    "Storage",
                    $"Drive {disk.GetValueOrDefault("DeviceID")} is nearly full",
                    $"{usedPercent}% used.",
                    "Free disk space. Low space can worsen updates, paging, indexing, and responsiveness.");
            }
        }
    }

    private static IEnumerable<Finding> ThermalFindings(TriageData data)
    {
        var thermal = data.Section(SectionNames.Thermal);
        var readings = Rows(thermal.GetValueOrDefault("readings")).ToList();

        if (readings.Count == 0)
        {
            yield return new Finding(
                "TEMPERATURE_UNAVAILABLE",
                FindingSeverity.Info,
                FindingConfidence.High,
                "Thermal",
                "Native Windows temperature readings were unavailable",
                "No ACPI or thermal-zone counter readings were returned.",
                "Use event logs, CPU behavior, and optional hardware sensor tools for actual CPU/GPU temperatures.");
            yield break;
        }

        foreach (var reading in readings.Where(row => Number(row.GetValueOrDefault("TemperatureC")) >= 85))
        {
            yield return new Finding(
                "NATIVE_THERMAL_READING_HIGH",
                FindingSeverity.Warning,
                FindingConfidence.Low,
                "Thermal",
                "A native Windows thermal-zone reading is high",
                $"{reading.GetValueOrDefault("Name") ?? reading.GetValueOrDefault("InstanceName")} reported {reading.GetValueOrDefault("TemperatureC")} C.",
                "Treat this as a clue, not a final CPU temperature. Corroborate with thermal events or a hardware sensor tool.");
        }
    }

    private static bool HasEvent(IEnumerable<Dictionary<string, object?>> events, string? provider, int id, out Dictionary<string, object?> match)
    {
        match = events.FirstOrDefault(row =>
            Number(row.GetValueOrDefault("id")) == id
            && (provider is null || string.Equals(row.GetValueOrDefault("providerName")?.ToString(), provider, StringComparison.OrdinalIgnoreCase)))
            ?? new Dictionary<string, object?>();

        return match.Count > 0;
    }

    private static IEnumerable<Dictionary<string, object?>> Rows(object? value)
    {
        return value as IEnumerable<Dictionary<string, object?>> ?? Array.Empty<Dictionary<string, object?>>();
    }

    private static Dictionary<string, object?> Map(object? value)
    {
        return value as Dictionary<string, object?> ?? new Dictionary<string, object?>();
    }

    private static double Number(object? value)
    {
        return double.TryParse(value?.ToString(), out var number) ? number : 0;
    }
}
