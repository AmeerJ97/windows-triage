using WindowsTriage.Core;
using Xunit;

namespace WindowsTriage.Tests;

public sealed class OptionsAndRunnerTests
{
    [Fact]
    public void EffectiveSampleSeconds_UsesProfileDefaultsWhenNoOverrideIsSet()
    {
        Assert.Equal(20, new CollectionOptions { Profile = ScanProfile.Quick }.EffectiveSampleSeconds);
        Assert.Equal(60, new CollectionOptions { Profile = ScanProfile.Full }.EffectiveSampleSeconds);
        Assert.Equal(120, new CollectionOptions { Profile = ScanProfile.Advanced }.EffectiveSampleSeconds);
    }

    [Fact]
    public void EffectiveSampleSeconds_UsesExplicitOverrideForAnyProfile()
    {
        Assert.Equal(30, new CollectionOptions { Profile = ScanProfile.Quick, SampleSeconds = 30 }.EffectiveSampleSeconds);
        Assert.Equal(30, new CollectionOptions { Profile = ScanProfile.Full, SampleSeconds = 30 }.EffectiveSampleSeconds);
        Assert.Equal(30, new CollectionOptions { Profile = ScanProfile.Advanced, SampleSeconds = 30 }.EffectiveSampleSeconds);
    }

    [Fact]
    public void CreateReportFolder_ReturnsUniqueFoldersForRapidCalls()
    {
        var root = Path.Combine(Path.GetTempPath(), "WindowsTriageTests", Guid.NewGuid().ToString("N"));

        try
        {
            var first = TriageRunner.CreateReportFolder(root);
            var second = TriageRunner.CreateReportFolder(root);

            Assert.NotEqual(first, second);
            Assert.True(Directory.Exists(first));
            Assert.True(Directory.Exists(second));
            Assert.DoesNotContain(Environment.MachineName, first, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Environment.MachineName, second, StringComparison.OrdinalIgnoreCase);
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
