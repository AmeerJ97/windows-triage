using WindowsTriage.Core;
using WindowsTriage.Core.Diagnosis;
using Xunit;

namespace WindowsTriage.Tests;

public sealed class DiagnosisRuleTests
{
    [Fact]
    public void Analyze_FindsThermalShutdownAndSuppressesNoCause()
    {
        var data = CompleteData();
        data.Sections.Events = new EventSection { RecentEvents = [new(DateTimeOffset.UtcNow, "System", "Microsoft-Windows-Kernel-Power", 86, "Critical")] };
        var findings = Analyze(data);
        Assert.Contains(findings, f => f.Id == "THERMAL_SHUTDOWN_EVENT" && f.Severity == FindingSeverity.Critical);
    }

    [Fact]
    public void Analyze_FindsBugcheckAndNtfsCorruptionOnlyForExpectedProviders()
    {
        var data = CompleteData();
        data.Sections.Events = new EventSection
        {
            RecentEvents =
        [
            new(DateTimeOffset.UtcNow, "System", "Microsoft-Windows-WER-SystemErrorReporting", 1001, "Error"),
            new(DateTimeOffset.UtcNow, "System", "Microsoft-Windows-Ntfs", 55, "Error"),
            new(DateTimeOffset.UtcNow, "System", "Unrelated", 55, "Error")
        ]
        };
        var findings = Analyze(data);
        Assert.Contains(findings, f => f.Id == "BUGCHECK_EVENT");
        Assert.Single(findings, f => f.Id == "NTFS_CORRUPTION");
    }

    [Fact]
    public void Analyze_CorrelatesCpuLimitAndSuppressesStandaloneFinding()
    {
        var data = CompleteData(averageCpu: 72);
        data.CompletedAt = DateTimeOffset.UtcNow;
        data.Sections.Events = new EventSection { RecentEvents = [new(data.CompletedAt.Value.AddMinutes(-5), "System", "Microsoft-Windows-Kernel-Processor-Power", 37, "Warning")] };
        var findings = Analyze(data);
        Assert.Contains(findings, f => f.Id == "CPU_LIMIT_UNDER_LOAD");
        Assert.DoesNotContain(findings, f => f.Id == "FIRMWARE_CPU_LIMIT");
    }

    [Theory]
    [InlineData(79.9, true)]
    [InlineData(80.0, false)]
    public void Analyze_UsesBatteryWearBoundary(double health, bool expected)
    {
        var data = CompleteData();
        data.Sections.Power = new PowerSection { BatteryDesignCapacityMWh = 1000, BatteryFullChargeCapacityMWh = (uint)(health * 10), BatteryHealthPercent = health, BatteryCapacitySource = "fixture" };
        Assert.Equal(expected, Analyze(data).Any(f => f.Id == "BATTERY_WEAR"));
    }

    [Fact]
    public void Analyze_ReportsDiskPredictionDefenderAndPowerErrors()
    {
        var data = CompleteData();
        data.Sections.Storage = new StorageSection { FailurePredicted = true };
        data.Sections.UpdatesSecurity = new UpdateSecuritySection { Defender = new DefenderInfo(false, false, 8, null) };
        data.Sections.Power = new PowerSection { EnergyErrorCount = 2, EnergyWarningCount = 1 };
        var ids = Analyze(data).Select(f => f.Id).ToHashSet();
        Assert.Contains("DISK_FAILURE_PREDICTED", ids);
        Assert.Contains("DEFENDER_INACTIVE", ids);
        Assert.Contains("POWER_EFFICIENCY_ERRORS", ids);
    }

    [Fact]
    public void Analyze_MissingCpuIsUnavailableNotHealthy()
    {
        var data = CompleteData();
        data.Sections.Performance = new PerformanceSection { Summary = new PerformanceSummary { CpuAvailable = false } };
        Assert.Contains(Analyze(data), f => f.Id == "PERFORMANCE_DATA_UNAVAILABLE");
        Assert.False(GeneralHealthRules.HasEssentialEvidence(data));
    }

    private static List<Finding> Analyze(TriageData data) => new GeneralHealthRules().Analyze(data).ToList();
    private static TriageData CompleteData(double averageCpu = 20)
    {
        var data = new TriageData { CompletedAt = DateTimeOffset.UtcNow };
        data.Sections.Run = new RunSection { Profile = ScanProfile.Full };
        data.Sections.Events = new EventSection();
        data.Sections.Storage = new StorageSection();
        data.Sections.Thermal = new ThermalSection { NativeReadingsAvailable = true, Readings = [new("fixture", "zone", 45, "fixture")] };
        data.Sections.Performance = new PerformanceSection { Summary = new PerformanceSummary { CpuAvailable = true, InterruptAvailable = true, MemoryAvailable = true, DiskAvailable = true, SampleCount = 3, ValidCpuSampleCount = 3, AverageCpuPercent = averageCpu, MaxCpuPercent = averageCpu } };
        return data;
    }
}
