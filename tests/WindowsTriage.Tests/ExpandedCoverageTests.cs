using WindowsTriage.Core;
using WindowsTriage.Core.Collectors;
using WindowsTriage.Core.Diagnosis;
using Xunit;

namespace WindowsTriage.Tests;

public sealed class ExpandedCoverageTests
{
    [Fact]
    public void Analyze_CoversRemainingEventFamilies()
    {
        var data = CompleteData();
        data.Sections.Events = new EventSection
        {
            RecentEvents =
            [
                new(DateTimeOffset.UtcNow.AddHours(-2), "System", "Microsoft-Windows-Kernel-Processor-Power", 37, "Warning"),
                new(DateTimeOffset.UtcNow, "System", "Microsoft-Windows-Kernel-Power", 41, "Critical"),
                new(DateTimeOffset.UtcNow, "System", "Microsoft-Windows-WHEA-Logger", 18, "Error")
            ]
        };
        var ids = Analyze(data);
        Assert.Contains("FIRMWARE_CPU_LIMIT", ids);
        Assert.Contains("UNCLEAN_SHUTDOWN", ids);
        Assert.Contains("WHEA_HARDWARE_ERROR", ids);
    }

    [Fact]
    public void Analyze_CoversPerformanceAndResourceFamilies()
    {
        var data = CompleteData();
        data.Sections.Performance = new PerformanceSection
        {
            Summary = new PerformanceSummary
            {
                CpuAvailable = true,
                InterruptAvailable = true,
                MemoryAvailable = true,
                DiskAvailable = true,
                ValidCpuSampleCount = 4,
                AverageCpuPercent = 90,
                MaxCpuPercent = 98,
                AverageInterruptPercent = 12,
                MinAvailableMemoryMB = 500
            },
            TopCpuProcesses = [new ProcessSnapshotInfo { Id = 42, Name = "Busy", CpuPercent = 35 }]
        };
        data.Sections.Storage = new StorageSection
        {
            LogicalDisks = [new LogicalDiskInfo("C:", "NTFS", 1000, 50)]
        };
        var ids = Analyze(data);
        Assert.Contains("SUSTAINED_HIGH_CPU", ids);
        Assert.Contains("HIGH_INTERRUPT_TIME", ids);
        Assert.Contains("RUNAWAY_PROCESS", ids);
        Assert.Contains("MEMORY_PRESSURE", ids);
        Assert.Contains("LOW_DISK_SPACE", ids);
    }

    [Fact]
    public void Analyze_CoversDegradedDiskStaleDefenderAndThermalReading()
    {
        var data = CompleteData();
        data.Sections.Storage = new StorageSection { PhysicalDisks = [new PhysicalDiskInfo { Model = "Disk", HealthStatus = 1 }] };
        data.Sections.UpdatesSecurity = new UpdateSecuritySection { Defender = new DefenderInfo(true, true, 4, null) };
        data.Sections.Thermal = new ThermalSection { NativeReadingsAvailable = true, Readings = [new("fixture", "zone", 86, "fixture")] };
        var ids = Analyze(data);
        Assert.Contains("DISK_HEALTH_DEGRADED", ids);
        Assert.Contains("DEFENDER_SIGNATURE_STALE", ids);
        Assert.Contains("NATIVE_THERMAL_READING_HIGH", ids);
    }

    [Fact]
    public void Analyze_CorrelatesActiveThermalLoadAndSuppressesStandaloneThermalFinding()
    {
        var data = CompleteData(70);
        data.Sections.Thermal = new ThermalSection { NativeReadingsAvailable = true, Readings = [new("fixture", "zone", 88, "fixture")] };
        var ids = Analyze(data);
        Assert.Contains("ACTIVE_THERMAL_LOAD", ids);
        Assert.DoesNotContain("NATIVE_THERMAL_READING_HIGH", ids);
    }

    [Fact]
    public void CollectionContext_RetainsRawArtifactsOnlyWithOptIn()
    {
        var root = TempRoot();
        try
        {
            var context = new CollectionContext(new CollectionOptions { IncludePrivateArtifacts = true }, root);
            File.WriteAllText(Path.Combine(context.TemporaryFolder, "raw.xml"), "private");
            context.RetainPrivateArtifacts();
            Assert.True(File.Exists(Path.Combine(context.PrivateFolder, "raw.xml")));
            context.CleanupTemporaryArtifacts();
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task CommandRunner_ExplainsNonzeroExitWithEmptyStderr()
    {
        var root = TempRoot();
        try
        {
            var output = Path.Combine(root, "command.txt");
            var capture = OperatingSystem.IsWindows()
                ? await CommandRunner.CaptureAsync("failure", "cmd.exe", ["/c", "exit", "7"], output, CancellationToken.None)
                : await CommandRunner.CaptureAsync("failure", "/bin/sh", ["-c", "exit 7"], output, CancellationToken.None);
            Assert.False(capture.Succeeded);
            Assert.Contains("code 7", capture.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Runner_CancellationRemovesIncompleteReport()
    {
        var root = TempRoot();
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var runner = new TriageRunner([new CancelCollector()], []);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(new CollectionOptions { OutputPath = root }, cancellationToken: cancellation.Token));
            Assert.Empty(Directory.EnumerateDirectories(root));
        }
        finally { Delete(root); }
    }

    private static HashSet<string> Analyze(TriageData data) => new GeneralHealthRules().Analyze(data).Select(f => f.Id).ToHashSet();
    private static TriageData CompleteData(double cpu = 20)
    {
        var data = new TriageData { CompletedAt = DateTimeOffset.UtcNow };
        data.Sections.Run = new RunSection { Profile = ScanProfile.Full };
        data.Sections.Events = new EventSection();
        data.Sections.Storage = new StorageSection();
        data.Sections.Thermal = new ThermalSection { NativeReadingsAvailable = true, Readings = [new("fixture", "zone", 45, "fixture")] };
        data.Sections.Performance = new PerformanceSection { Summary = new PerformanceSummary { CpuAvailable = true, InterruptAvailable = true, MemoryAvailable = true, DiskAvailable = true, ValidCpuSampleCount = 3, AverageCpuPercent = cpu, MaxCpuPercent = cpu } };
        return data;
    }
    private static string TempRoot() { var path = Path.Combine(Path.GetTempPath(), "WindowsTriageExpanded", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static void Delete(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); }
    private sealed class CancelCollector : ITriageCollector
    {
        public string Name => "Cancel";
        public Task CollectAsync(TriageData data, CollectionContext context, IProgress<string>? progress, CancellationToken cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
