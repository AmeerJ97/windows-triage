using System.IO.Compression;
using WindowsTriage.Core;
using WindowsTriage.Core.Reports;
using Xunit;

namespace WindowsTriage.Tests;

public sealed class ReportWriterTests
{
    [Fact]
    public async Task WriteAsync_WritesTypedReportsAndPrivacyManifest()
    {
        var root = TempRoot();
        try
        {
            var context = new CollectionContext(new CollectionOptions { OutputPath = root, NoZip = true }, root);
            var data = new TriageData { ReportFolder = root };
            data.Sections.Performance = new PerformanceSection { Summary = new PerformanceSummary { CpuAvailable = true } };
            data.Findings.Add(new Finding("TEST", FindingSeverity.Warning, FindingConfidence.High, "Test", "Synthetic", "Evidence", "Recommendation"));
            var paths = await new ReportWriter().WriteAsync(data, context, CancellationToken.None);
            Assert.All([paths.TextPath, paths.JsonPath, paths.SummaryPath, paths.PublicSummaryPath, paths.PrivacyManifestPath], path => Assert.True(File.Exists(path)));
            var json = await File.ReadAllTextAsync(paths.JsonPath);
            Assert.Contains("\"SchemaVersion\": 1", json);
            Assert.Contains("\"performance\"", json);
            Assert.DoesNotContain(root, json, StringComparison.OrdinalIgnoreCase);
            context.CleanupTemporaryArtifacts();
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task WriteAsync_PublicSummaryRedactsIdentityWithAllOptIns()
    {
        var root = TempRoot();
        try
        {
            var options = new CollectionOptions { IncludeMachineName = true, IncludeNetwork = true, IncludeCommandLines = true, IncludePrivateArtifacts = true, NoZip = true };
            var context = new CollectionContext(options, root);
            var data = new TriageData { ReportFolder = root, ComputerName = "PRIVATE-PC" };
            data.Findings.Add(new Finding("TEST", FindingSeverity.Info, FindingConfidence.High, "Test", "PRIVATE-PC C:\\Users\\Alice\\secret", "10.0.0.5 S-1-5-21-123-456", "00:11:22:33:44:55"));
            var paths = await new ReportWriter().WriteAsync(data, context, CancellationToken.None);
            var publicSummary = await File.ReadAllTextAsync(paths.PublicSummaryPath);
            Assert.DoesNotContain("PRIVATE-PC", publicSummary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Alice", publicSummary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("10.0.0.5", publicSummary);
            Assert.DoesNotContain("S-1-5", publicSummary);
            Assert.DoesNotContain("00:11:22", publicSummary);
            context.CleanupTemporaryArtifacts();
        }
        finally { Delete(root); }
    }

    [Fact]
    public void Archive_DefaultHasNoPrivateDirectory()
    {
        var root = TempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "public_summary.md"), "safe");
            var zip = new ArchiveWriter().CreateZip(root);
            using (var archive = ZipFile.OpenRead(zip))
            {
                Assert.DoesNotContain(archive.Entries, e => e.FullName.StartsWith("private/", StringComparison.OrdinalIgnoreCase));
            }
            File.Delete(zip);
        }
        finally { Delete(root); }
    }

    private static string TempRoot() { var path = Path.Combine(Path.GetTempPath(), "WindowsTriageTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static void Delete(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); }
}
