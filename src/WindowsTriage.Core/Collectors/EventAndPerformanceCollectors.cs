using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;

namespace WindowsTriage.Core.Collectors;

public sealed class EventLogCollector : ITriageCollector
{
    public string Name => "Event logs";

    public Task CollectAsync(TriageData data, CollectionContext context, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Collecting recent power, thermal, and hardware events...");
        var events = new List<Dictionary<string, object?>>();
        const long fourteenDaysMs = 14L * 24L * 60L * 60L * 1000L;
        var eventIds = "EventID=41 or EventID=86 or EventID=125 or EventID=37 or EventID=55 or EventID=18 or EventID=19 or EventID=1001 or EventID=6008";
        var queryText = $"*[System[({eventIds}) and TimeCreated[timediff(@SystemTime) <= {fourteenDaysMs}]]]";

        try
        {
            var query = new EventLogQuery("System", PathType.LogName, queryText)
            {
                ReverseDirection = true
            };

            using var reader = new EventLogReader(query);
            for (var i = 0; i < 160; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var record = reader.ReadEvent();
                if (record is null)
                {
                    break;
                }

                events.Add(new Dictionary<string, object?>
                {
                    ["timeCreated"] = record.TimeCreated?.ToString("O"),
                    ["logName"] = record.LogName,
                    ["providerName"] = record.ProviderName,
                    ["id"] = record.Id,
                    ["levelDisplayName"] = record.LevelDisplayName,
                    ["message"] = Trim(record.FormatDescription(), 800)
                });
            }
        }
        catch (Exception ex)
        {
            context.AddWarning(data, Name, ex.Message, "Event log collection may require Administrator privileges.");
        }

        data.Sections[SectionNames.Events] = new Dictionary<string, object?>
        {
            ["lookbackDays"] = 14,
            ["recentEvents"] = events
        };

        return Task.CompletedTask;
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value[..max] + "...";
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

        using var cpuCounter = TryCounter("Processor", "% Processor Time", "_Total");
        using var interruptCounter = TryCounter("Processor", "% Interrupt Time", "_Total");
        using var memoryCounter = TryCounter("Memory", "Available MBytes", null);
        using var diskCounter = TryCounter("PhysicalDisk", "% Disk Time", "_Total");

        PrimeRateCounters(cpuCounter, interruptCounter, diskCounter);
        if (cpuCounter is not null || interruptCounter is not null || diskCounter is not null)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        var logicalProcessors = Math.Max(1, Environment.ProcessorCount);
        var start = SnapshotProcesses(context.Options.IncludeCommandLines);
        var startedAt = DateTimeOffset.Now;
        var samples = new List<Dictionary<string, object?>>();
        var deadline = DateTimeOffset.Now.AddSeconds(seconds);

        while (DateTimeOffset.Now < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            samples.Add(new Dictionary<string, object?>
            {
                ["time"] = DateTimeOffset.Now.ToString("O"),
                ["cpuPercent"] = ReadCounter(cpuCounter),
                ["interruptPercent"] = ReadCounter(interruptCounter),
                ["availableMemoryMB"] = ReadCounter(memoryCounter),
                ["diskTimePercent"] = ReadCounter(diskCounter)
            });

            var remaining = deadline - DateTimeOffset.Now;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Min(interval, Math.Max(1, remaining.TotalSeconds))), cancellationToken).ConfigureAwait(false);
        }

        var endedAt = DateTimeOffset.Now;
        var end = SnapshotProcesses(context.Options.IncludeCommandLines);
        var elapsedSeconds = Math.Max(1, (endedAt - startedAt).TotalSeconds);
        var startByPid = start.Where(p => p.CpuSeconds.HasValue).ToDictionary(p => p.Id, p => p);
        var deltas = new List<Dictionary<string, object?>>();

        foreach (var process in end)
        {
            if (!process.CpuSeconds.HasValue || !startByPid.TryGetValue(process.Id, out var startProcess) || !startProcess.CpuSeconds.HasValue)
            {
                continue;
            }

            var deltaSeconds = Math.Max(0, process.CpuSeconds.Value - startProcess.CpuSeconds.Value);
            var cpuPercent = Math.Round((deltaSeconds / elapsedSeconds / logicalProcessors) * 100, 1);
            deltas.Add(new Dictionary<string, object?>
            {
                ["id"] = process.Id,
                ["name"] = process.Name,
                ["cpuPercent"] = cpuPercent,
                ["cpuSecondsDelta"] = Math.Round(deltaSeconds, 2),
                ["workingSetMB"] = process.WorkingSetMb,
                ["privateMemoryMB"] = process.PrivateMemoryMb,
                ["threads"] = process.Threads,
                ["handles"] = process.Handles,
                ["path"] = process.Path,
                ["commandLine"] = process.CommandLine
            });
        }

        var topCpu = deltas.OrderByDescending(p => Number(p.GetValueOrDefault("cpuPercent"))).Take(20).ToList();
        var topMemory = end.OrderByDescending(p => p.WorkingSetMb).Take(20).Select(ProcessSnapshotToDictionary).ToList();

        data.Sections[SectionNames.Performance] = new Dictionary<string, object?>
        {
            ["summary"] = BuildSummary(samples, elapsedSeconds),
            ["samples"] = samples,
            ["topCpuProcesses"] = topCpu,
            ["topMemoryProcesses"] = topMemory
        };
    }

    private static PerformanceCounter? TryCounter(string category, string counter, string? instance)
    {
        try
        {
            return instance is null
                ? new PerformanceCounter(category, counter)
                : new PerformanceCounter(category, counter, instance);
        }
        catch
        {
            return null;
        }
    }

    private static double? ReadCounter(PerformanceCounter? counter)
    {
        if (counter is null)
        {
            return null;
        }

        try
        {
            return Math.Round(counter.NextValue(), 1);
        }
        catch
        {
            return null;
        }
    }

    internal static IReadOnlyList<ProcessSnapshot> SnapshotProcesses(bool includeCommandLines)
    {
        var snapshots = new List<ProcessSnapshot>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var path = includeCommandLines ? SafeString(() => process.MainModule?.FileName) : null;
                var commandLine = includeCommandLines ? TryGetCommandLine(process.Id) : null;
                snapshots.Add(new ProcessSnapshot(
                    process.Id,
                    process.ProcessName,
                    Safe(() => process.TotalProcessorTime.TotalSeconds),
                    Math.Round(Safe(() => process.WorkingSet64) / 1024d / 1024d, 1),
                    Math.Round(Safe(() => process.PrivateMemorySize64) / 1024d / 1024d, 1),
                    Safe(() => process.Threads.Count),
                    Safe(() => process.HandleCount),
                    path,
                    commandLine));
            }
            catch
            {
                // Processes can exit or deny access while sampling. Ignore and continue.
            }
            finally
            {
                process.Dispose();
            }
        }

        return snapshots;
    }

    private static string? TryGetCommandLine(int pid)
    {
        try
        {
            return WmiHelper.FirstOrEmpty("Win32_Process", where: $"ProcessId = {pid}").GetValueOrDefault("CommandLine")?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static void PrimeRateCounters(params PerformanceCounter?[] counters)
    {
        foreach (var counter in counters)
        {
            if (counter is null)
            {
                continue;
            }

            try
            {
                _ = counter.NextValue();
            }
            catch
            {
                // Counter availability varies across systems; later reads will record null if unavailable.
            }
        }
    }

    private static Dictionary<string, object?> BuildSummary(IReadOnlyList<Dictionary<string, object?>> samples, double elapsedSeconds)
    {
        var cpu = Values(samples, "cpuPercent");
        var interrupts = Values(samples, "interruptPercent");
        var disk = Values(samples, "diskTimePercent");
        var memory = Values(samples, "availableMemoryMB");

        return new Dictionary<string, object?>
        {
            ["elapsedSeconds"] = Math.Round(elapsedSeconds, 1),
            ["sampleCount"] = samples.Count,
            ["averageCpuPercent"] = Average(cpu),
            ["maxCpuPercent"] = cpu.Count == 0 ? null : cpu.Max(),
            ["averageInterruptPercent"] = Average(interrupts),
            ["maxInterruptPercent"] = interrupts.Count == 0 ? null : interrupts.Max(),
            ["averageDiskTimePercent"] = Average(disk),
            ["minAvailableMemoryMB"] = memory.Count == 0 ? null : memory.Min()
        };
    }

    private static List<double> Values(IReadOnlyList<Dictionary<string, object?>> rows, string key)
    {
        return rows.Select(row => Number(row.GetValueOrDefault(key))).Where(value => value.HasValue).Select(value => value!.Value).ToList();
    }

    private static double? Average(IReadOnlyList<double> values)
    {
        return values.Count == 0 ? null : Math.Round(values.Average(), 1);
    }

    private static double? Number(object? value)
    {
        return double.TryParse(value?.ToString(), out var number) ? number : null;
    }

    private static double Safe(Func<double> read)
    {
        try { return read(); } catch { return 0; }
    }

    private static long Safe(Func<long> read)
    {
        try { return read(); } catch { return 0; }
    }

    private static int Safe(Func<int> read)
    {
        try { return read(); } catch { return 0; }
    }

    private static string? SafeString(Func<string?> read)
    {
        try { return read(); } catch { return null; }
    }

    private static Dictionary<string, object?> ProcessSnapshotToDictionary(ProcessSnapshot snapshot) => new()
    {
        ["id"] = snapshot.Id,
        ["name"] = snapshot.Name,
        ["cpuSeconds"] = snapshot.CpuSeconds,
        ["workingSetMB"] = snapshot.WorkingSetMb,
        ["privateMemoryMB"] = snapshot.PrivateMemoryMb,
        ["threads"] = snapshot.Threads,
        ["handles"] = snapshot.Handles,
        ["path"] = snapshot.Path,
        ["commandLine"] = snapshot.CommandLine
    };

    internal sealed record ProcessSnapshot(
        int Id,
        string Name,
        double? CpuSeconds,
        double WorkingSetMb,
        double PrivateMemoryMb,
        int Threads,
        int Handles,
        string? Path,
        string? CommandLine);
}
