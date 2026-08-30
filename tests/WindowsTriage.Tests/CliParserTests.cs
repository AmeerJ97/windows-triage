using WindowsTriage.Core;
using Xunit;

namespace WindowsTriage.Tests;

public sealed class CliParserTests
{
    [Fact]
    public void Parse_UsesFullProfileDefaults()
    {
        var parsed = CliOptions.Parse(["collect"]);

        Assert.Equal(ScanProfile.Full, parsed.Options.Profile);
        Assert.Equal(60, parsed.Options.EffectiveSampleSeconds);
        Assert.False(parsed.Options.IncludeNetwork);
        Assert.False(parsed.Options.IncludeCommandLines);
        Assert.False(parsed.Options.IncludeMachineName);
    }

    [Fact]
    public void Parse_AcceptsAdvancedPrivacyOptInsAndSampleOverride()
    {
        var parsed = CliOptions.Parse([
            "collect",
            "--profile", "advanced",
            "--include-network",
            "--include-command-lines",
            "--include-machine-name",
            "--sample-seconds", "30",
            "--sample-interval-seconds", "3",
            "--no-zip",
            "--json",
            "--quiet"
        ]);

        Assert.Equal(ScanProfile.Advanced, parsed.Options.Profile);
        Assert.True(parsed.Options.IncludeNetwork);
        Assert.True(parsed.Options.IncludeCommandLines);
        Assert.True(parsed.Options.IncludeMachineName);
        Assert.True(parsed.Options.NoZip);
        Assert.True(parsed.Options.JsonToStdout);
        Assert.True(parsed.Options.Quiet);
        Assert.Equal(30, parsed.Options.EffectiveSampleSeconds);
        Assert.Equal(3, parsed.Options.EffectiveSampleIntervalSeconds);
    }

    [Fact]
    public void Parse_RejectsUnknownCommand()
    {
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(["wat"]));
    }

    [Fact]
    public void Parse_RejectsInvalidSampleSeconds()
    {
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(["collect", "--sample-seconds", "5"]));
    }

    [Theory]
    [InlineData("quick", ScanProfile.Quick)]
    [InlineData("full", ScanProfile.Full)]
    [InlineData("advanced", ScanProfile.Advanced)]
    public void Parse_AcceptsProfileShorthandCommands(string command, ScanProfile expected)
    {
        var parsed = CliOptions.Parse([command, "--open", "--print-summary"]);

        Assert.Equal(expected, parsed.Options.Profile);
        Assert.True(parsed.Options.OpenReportFolder);
        Assert.True(parsed.Options.PrintPublicSummary);
    }

    [Fact]
    public void Parse_RejectsConflictingStdoutFormats()
    {
        var error = Assert.Throws<ArgumentException>(() => CliOptions.Parse(["quick", "--json", "--print-summary"]));
        Assert.Contains("cannot be used together", error.Message);
    }
}
