using WindowsTriage.Core.Collectors;
using Xunit;

namespace WindowsTriage.Tests;

public sealed class CollectorContractTests
{
    [Fact]
    public void SnapshotProcesses_RedactsExecutablePathAndCommandLineByDefault()
    {
        var snapshots = PerformanceCollector.SnapshotProcesses(includeCommandLines: false);

        Assert.NotEmpty(snapshots);
        Assert.All(snapshots, snapshot =>
        {
            Assert.Null(snapshot.Path);
            Assert.Null(snapshot.CommandLine);
        });
    }

    [Fact]
    public void HotfixInstalledOn_ParsesDatesForChronologicalSorting()
    {
        var newer = new Dictionary<string, object?> { ["InstalledOn"] = "6/12/2026" };
        var older = new Dictionary<string, object?> { ["InstalledOn"] = "1/5/2024" };
        var missing = new Dictionary<string, object?>();

        Assert.True(UpdatesSecurityCollector.HotfixInstalledOn(newer) > UpdatesSecurityCollector.HotfixInstalledOn(older));
        Assert.Equal(DateTime.MinValue, UpdatesSecurityCollector.HotfixInstalledOn(missing));
    }
}
