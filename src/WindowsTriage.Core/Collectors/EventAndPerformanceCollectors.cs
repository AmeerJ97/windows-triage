using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Text.Json;

namespace WindowsTriage.Core.Collectors;

public sealed class EventLogCollector : ITriageCollector
{
    public string Name => "Event logs";
    public Task CollectAsync(TriageData data, CollectionContext context, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Collecting recent power, thermal, storage, and crash events...");
        var safeEvents = new List<EventRecordInfo>();
        var privateEvents = new List<object>();
        const long fourteenDaysMs = 14L * 24L * 60L * 60L * 1000L;
        var queryText = $"*[System[((EventID=41 or EventID=86 or EventID=125 or EventID=37 or EventID=55 or EventID=18 or EventID=19 or EventID=1001 or EventID=6008) and TimeCreated[timediff(@SystemTime) <= {fourteenDaysMs})]]]";
        try
        {
            using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, queryText) { ReverseDirection = true });
            for (var i = 0; i < 200; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var record = reader.ReadEvent();
                if (record is null) break;
                safeEvents.Add(new EventRecordInfo(record.TimeCreated, record.LogName, record.ProviderName, record.Id, record.LevelDisplayName));
                if (context.Options.IncludePrivateArtifacts)
                {
                    string? message;
                    try { message = record.FormatDescription(); } catch { message = null; }
                    privateEvents.Add(new { record.TimeCreated, record.LogName, record.ProviderName, record.Id, record.LevelDisplayName, Message = message });
                }
            }
            if (context.Options.IncludePrivateArtifacts)
                File.WriteAllText(Path.Combine(context.TemporaryFolder, "event_messages.json"), JsonSerializer.Serialize(privateEvents, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { context.AddWarning(data, Name, ex.Message, "Event log collection may require Administrator privileges."); }
        data.Sections.Events = new EventSection { RecentEvents = safeEvents };
        return Task.CompletedTask;
    }
}

public sealed class PerformanceCollector : ITriageCollector
{
    public string Name => "Performance";
    public async Task CollectAsync(TriageData data, CollectionContext context, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var seconds = context.Options.EffectiveSampleSeconds;
        var interval = context.Options.EffectiveSampleIntervalSeconds;
        progress?.Report($"Sampling performance for {seconds} seconds...");
        var logicalProcessors = Math.Max(1, Environment.ProcessorCount);
        var start = SnapshotProcesses(includeCommandLines: false);
        var startedAt = DateTimeOffset.Now;
        var samples = new List<PerformanceSample>();
        var deadline = DateTimeOffset.Now.AddSeconds(seconds);
        while (DateTimeOffset.Now < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            samples.Add(ReadPerformanceSample());
            var remaining = deadline - DateTimeOffset.Now;
            if (remaining <= TimeSpan.Zero) break;
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(interval, Math.Max(1, remaining.TotalSeconds))), cancellationToken).ConfigureAwait(false);
        }

        var endedAt = DateTimeOffset.Now;
        var end = SnapshotProcesses(context.Options.IncludeCommandLines);
        var elapsedSeconds = Math.Max(1, (endedAt - startedAt).TotalSeconds);
        var startByPid = start.Where(p => p.CpuSeconds.HasValue).ToDictionary(p => p.Id);
        var deltas = new List<ProcessSnapshotInfo>();
        foreach (var process in end)
        {
            if (!process.CpuSeconds.HasValue || !startByPid.TryGetValue(process.Id, out var before) || !before.CpuSeconds.HasValue) continue;
            var delta = Math.Max(0, process.CpuSeconds.Value - before.CpuSeconds.Value);
            deltas.Add(process with { CpuPercent = Math.Round(delta / elapsedSeconds / logicalProcessors * 100, 1), CpuSeconds = Math.Round(delta, 2) });
        }

        var summary = BuildSummary(samples, elapsedSeconds);
        if (!summary.CpuAvailable) context.AddWarning(data, "CPU performance", "No language-neutral CPU performance samples were available.");
        if (!summary.InterruptAvailable) context.AddWarning(data, "Interrupt performance", "CPU interrupt performance data was unavailable.");
        if (!summary.MemoryAvailable) context.AddWarning(data, "Memory performance", "Available-memory performance data was unavailable.");
        if (!summary.DiskAvailable) context.AddWarning(data, "Disk performance", "Physical-disk performance data was unavailable.");
        data.Sections.Performance = new PerformanceSection
        {
            Summary = summary,
            Samples = samples,
            TopCpuProcesses = deltas.OrderByDescending(p => p.CpuPercent).Take(20).ToList(),
            TopMemoryProcesses = end.OrderByDescending(p => p.WorkingSetMB).Take(20).ToList()
        };
    }

    private static PerformanceSample ReadPerformanceSample()
    {
        double? cpu = null, interrupt = null, memory = null, disk = null;
        try
        {
            var row = WmiHelper.FirstOrEmpty("Win32_PerfFormattedData_PerfOS_Processor", ["Name", "PercentProcessorTime", "PercentInterruptTime"], where: "Name = '_Total'");
            cpu = row.Double("PercentProcessorTime"); interrupt = row.Double("PercentInterruptTime");
        }
        catch { }
        try { memory = WmiHelper.FirstOrEmpty("Win32_PerfFormattedData_PerfOS_Memory", ["AvailableMBytes"]).Double("AvailableMBytes"); } catch { }
        try { disk = WmiHelper.FirstOrEmpty("Win32_PerfFormattedData_PerfDisk_PhysicalDisk", ["Name", "PercentDiskTime"], where: "Name = '_Total'").Double("PercentDiskTime"); } catch { }
        return new PerformanceSample(DateTimeOffset.Now, Round(cpu), Round(interrupt), Round(memory), Round(disk));
    }

    internal static IReadOnlyList<ProcessSnapshotInfo> SnapshotProcesses(bool includeCommandLines)
    {
        var snapshots = new List<ProcessSnapshotInfo>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                snapshots.Add(new ProcessSnapshotInfo
                {
                    Id = process.Id,
                    Name = process.ProcessName,
                    CpuSeconds = Safe(() => process.TotalProcessorTime.TotalSeconds),
                    WorkingSetMB = Math.Round(Safe(() => process.WorkingSet64) / 1024d / 1024d, 1),
                    PrivateMemoryMB = Math.Round(Safe(() => process.PrivateMemorySize64) / 1024d / 1024d, 1),
                    Threads = Safe(() => process.Threads.Count),
                    Handles = Safe(() => process.HandleCount),
                    Path = includeCommandLines ? SafeString(() => process.MainModule?.FileName) : null,
                    CommandLine = includeCommandLines ? TryGetCommandLine(process.Id) : null
                });
            }
            catch { }
            finally { process.Dispose(); }
        }
        return snapshots;
    }

    private static string? TryGetCommandLine(int pid)
    {
        try { return WmiHelper.FirstOrEmpty("Win32_Process", ["CommandLine"], where: $"ProcessId = {pid}").Text("CommandLine"); }
        catch { return null; }
    }

    private static PerformanceSummary BuildSummary(IReadOnlyList<PerformanceSample> samples, double elapsed)
    {
        var cpu = samples.Where(s => s.CpuPercent.HasValue).Select(s => s.CpuPercent!.Value).ToList();
        var interrupts = samples.Where(s => s.InterruptPercent.HasValue).Select(s => s.InterruptPercent!.Value).ToList();
        var memory = samples.Where(s => s.AvailableMemoryMB.HasValue).Select(s => s.AvailableMemoryMB!.Value).ToList();
        var disk = samples.Where(s => s.DiskTimePercent.HasValue).Select(s => s.DiskTimePercent!.Value).ToList();
        return new PerformanceSummary
        {
            ElapsedSeconds = Math.Round(elapsed, 1),
            SampleCount = samples.Count,
            ValidCpuSampleCount = cpu.Count,
            AverageCpuPercent = Average(cpu),
            MaxCpuPercent = cpu.Count == 0 ? null : cpu.Max(),
            AverageInterruptPercent = Average(interrupts),
            MaxInterruptPercent = interrupts.Count == 0 ? null : interrupts.Max(),
            AverageDiskTimePercent = Average(disk),
            MinAvailableMemoryMB = memory.Count == 0 ? null : memory.Min(),
            CpuAvailable = cpu.Count > 0,
            InterruptAvailable = interrupts.Count > 0,
            MemoryAvailable = memory.Count > 0,
            DiskAvailable = disk.Count > 0
        };
    }
    private static double? Average(IReadOnlyList<double> values) => values.Count == 0 ? null : Math.Round(values.Average(), 1);
    private static double? Round(double? value) => value.HasValue ? Math.Round(value.Value, 1) : null;
    private static double Safe(Func<double> read) { try { return read(); } catch { return 0; } }
    private static long Safe(Func<long> read) { try { return read(); } catch { return 0; } }
    private static int Safe(Func<int> read) { try { return read(); } catch { return 0; } }
    private static string? SafeString(Func<string?> read) { try { return read(); } catch { return null; } }
}
