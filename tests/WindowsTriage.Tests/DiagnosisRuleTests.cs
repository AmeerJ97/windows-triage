using WindowsTriage.Core;
using WindowsTriage.Core.Diagnosis;
using Xunit;

namespace WindowsTriage.Tests;

public sealed class DiagnosisRuleTests
{
    [Fact]
    public void Analyze_FindsThermalShutdownEvent()
    {
        var data = new TriageData();
        data.Sections[SectionNames.Events] = new Dictionary<string, object?>
        {
            ["recentEvents"] = new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["providerName"] = "Microsoft-Windows-Kernel-Power",
                    ["id"] = 86,
                    ["timeCreated"] = "2026-06-12T12:00:00Z"
                }
            }
        };

        var findings = new GeneralHealthRules().Analyze(data).ToList();

        Assert.Contains(findings, finding => finding.Id == "THERMAL_SHUTDOWN_EVENT" && finding.Severity == FindingSeverity.Critical);
    }

    [Fact]
    public void Analyze_FindsRunawayProcessFromCpuDelta()
    {
        var data = new TriageData();
        data.Sections[SectionNames.Performance] = new Dictionary<string, object?>
        {
            ["summary"] = new Dictionary<string, object?>
            {
                ["averageCpuPercent"] = 45,
                ["maxCpuPercent"] = 80
            },
            ["topCpuProcesses"] = new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["id"] = 123,
                    ["name"] = "ExampleApp",
                    ["cpuPercent"] = 35
                }
            }
        };

        var findings = new GeneralHealthRules().Analyze(data).ToList();

        Assert.Contains(findings, finding => finding.Id == "RUNAWAY_PROCESS" && finding.Title.Contains("ExampleApp"));
    }

    [Fact]
    public void Analyze_ReportsMissingTemperatureReadings()
    {
        var data = new TriageData();
        data.Sections[SectionNames.Thermal] = new Dictionary<string, object?>
        {
            ["readings"] = new List<Dictionary<string, object?>>()
        };

        var findings = new GeneralHealthRules().Analyze(data).ToList();

        Assert.Contains(findings, finding => finding.Id == "TEMPERATURE_UNAVAILABLE");
    }
}
