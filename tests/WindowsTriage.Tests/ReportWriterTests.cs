using WindowsTriage.Core;
using WindowsTriage.Core.Reports;
using Xunit;

namespace WindowsTriage.Tests;

public sealed class ReportWriterTests
{
    [Fact]
    public async Task WriteAsync_WritesSummaryTextAndJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "WindowsTriageReportTests", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            var context = new CollectionContext(new CollectionOptions { OutputPath = root, NoZip = true }, root);
            var data = new TriageData
            {
                ReportFolder = root
            };
            data.Findings.Add(new Finding(
                "TEST_FINDING",
                FindingSeverity.Warning,
                FindingConfidence.High,
                "Test",
                "Synthetic finding",
                "Synthetic evidence",
                "Synthetic recommendation"));
            data.Sections[SectionNames.Performance] = new Dictionary<string, object?>
            {
                ["topCpuProcesses"] = new List<Dictionary<string, object?>>
                {
                    new()
                    {
                        ["name"] = "Example",
                        ["path"] = null,
                        ["commandLine"] = null
                    }
                }
            };

            var writer = new ReportWriter();
            var paths = await writer.WriteAsync(data, context, CancellationToken.None);

            Assert.True(File.Exists(paths.TextPath));
            Assert.True(File.Exists(paths.JsonPath));
            Assert.True(File.Exists(paths.SummaryPath));
            Assert.True(File.Exists(paths.PublicSummaryPath));
            Assert.Contains("Synthetic finding", await File.ReadAllTextAsync(paths.SummaryPath));
            Assert.DoesNotContain("C:\\Users\\", await File.ReadAllTextAsync(paths.JsonPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WriteAsync_DefaultReportsOmitMachineNameAndWritePublicSummary()
    {
        var root = Path.Combine(Path.GetTempPath(), "WindowsTriagePrivacyTests", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            var context = new CollectionContext(new CollectionOptions { OutputPath = root, NoZip = true }, root);
            var data = new TriageData
            {
                ReportFolder = root,
                ComputerName = null
            };
            data.Findings.Add(new Finding(
                "TEST_FINDING",
                FindingSeverity.Info,
                FindingConfidence.High,
                "Test",
                "Synthetic finding",
                "Synthetic evidence",
                "Synthetic recommendation"));

            var paths = await new ReportWriter().WriteAsync(data, context, CancellationToken.None);
            var combined = string.Join(
                Environment.NewLine,
                await File.ReadAllTextAsync(paths.TextPath),
                await File.ReadAllTextAsync(paths.JsonPath),
                await File.ReadAllTextAsync(paths.SummaryPath),
                await File.ReadAllTextAsync(paths.PublicSummaryPath));

            Assert.DoesNotContain(Environment.MachineName, combined, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("computerName", await File.ReadAllTextAsync(paths.JsonPath), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Windows Triage Public Summary", await File.ReadAllTextAsync(paths.PublicSummaryPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
