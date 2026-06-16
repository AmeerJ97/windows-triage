using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace WindowsTriage.Core;

public enum FindingSeverity
{
    Critical,
    Warning,
    Info
}

public enum FindingConfidence
{
    High,
    Medium,
    Low
}

public enum ScanProfile
{
    Quick,
    Full,
    Advanced
}

public sealed record CollectionOptions
{
    public ScanProfile Profile { get; init; } = ScanProfile.Full;
    public string? OutputPath { get; init; }
    public bool IncludeNetwork { get; init; }
    public bool IncludeCommandLines { get; init; }
    public bool IncludeMachineName { get; init; }
    public bool NoZip { get; init; }
    public bool JsonToStdout { get; init; }
    public bool Quiet { get; init; }
    public bool Verbose { get; init; }
    public int? SampleSeconds { get; init; }
    public int SampleIntervalSeconds { get; init; } = 5;

    public int EffectiveSampleSeconds => SampleSeconds ?? Profile switch
    {
        ScanProfile.Quick => 20,
        ScanProfile.Advanced => 120,
        _ => 60
    };

    public int EffectiveSampleIntervalSeconds => Math.Clamp(SampleIntervalSeconds, 1, 60);
}

public sealed record CollectionWarning(string Area, string Message, string Recommendation);

public sealed record Finding(
    string Id,
    FindingSeverity Severity,
    FindingConfidence Confidence,
    string Category,
    string Title,
    string Evidence,
    string Recommendation);

public sealed record ReportPackage(
    string ReportFolder,
    string TextReportPath,
    string JsonReportPath,
    string SummaryPath,
    string PublicSummaryPath,
    string? ZipPath,
    TriageData Data);

public sealed class TriageData
{
    public string ToolName { get; init; } = "Windows Triage";
    public string ToolVersion { get; init; } = typeof(TriageData).Assembly.GetName().Version?.ToString() ?? "0.2.0";
    public string ReportId { get; init; } = ReportIdentity.Create();
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ComputerName { get; set; }
    public string ReportFolder { get; set; } = "";
    public Dictionary<string, object?> Sections { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Finding> Findings { get; } = [];
    public List<CollectionWarning> Warnings { get; } = [];
}

public sealed class CollectionContext
{
    public CollectionContext(CollectionOptions options, string reportFolder)
    {
        Options = options;
        ReportFolder = reportFolder;
        LogsFolder = Path.Combine(reportFolder, "logs");
        Directory.CreateDirectory(LogsFolder);
    }

    public CollectionOptions Options { get; }
    public string ReportFolder { get; }
    public string LogsFolder { get; }

    public void AddWarning(TriageData data, string area, string message, string recommendation = "The scan continued. Re-run in Advanced mode if this data is important.")
    {
        data.Warnings.Add(new CollectionWarning(area, message, recommendation));
    }
}

public interface ITriageCollector
{
    string Name { get; }
    Task CollectAsync(TriageData data, CollectionContext context, IProgress<string>? progress, CancellationToken cancellationToken);
}

public interface IDiagnosisRule
{
    IEnumerable<Finding> Analyze(TriageData data);
}

public static class SectionNames
{
    public const string Run = "run";
    public const string System = "system";
    public const string Cpu = "cpu";
    public const string Memory = "memory";
    public const string Gpu = "gpu";
    public const string Storage = "storage";
    public const string Battery = "battery";
    public const string Thermal = "thermal";
    public const string Power = "power";
    public const string Events = "events";
    public const string Performance = "performance";
    public const string ServicesStartup = "servicesStartup";
    public const string UpdatesSecurity = "updatesSecurity";
    public const string Network = "network";
    public const string Drivers = "drivers";
}

public static class ReportIdentity
{
    public static string Create()
    {
        return $"WindowsTriage_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..39];
    }
}

public static class DataExtensions
{
    public static IReadOnlyDictionary<string, object?> Section(this TriageData data, string name)
    {
        if (!data.Sections.TryGetValue(name, out var value))
        {
            return new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
        }

        return value as IReadOnlyDictionary<string, object?>
            ?? new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
    }

    public static IReadOnlyList<Dictionary<string, object?>> ListSection(this TriageData data, string name)
    {
        if (!data.Sections.TryGetValue(name, out var value))
        {
            return Array.Empty<Dictionary<string, object?>>();
        }

        return value as IReadOnlyList<Dictionary<string, object?>>
            ?? Array.Empty<Dictionary<string, object?>>();
    }
}
