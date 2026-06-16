using WindowsTriage.Core.Collectors;
using WindowsTriage.Core.Diagnosis;
using WindowsTriage.Core.Reports;

namespace WindowsTriage.Core;

public sealed class TriageRunner
{
    private readonly IReadOnlyList<ITriageCollector> _collectors;
    private readonly IReadOnlyList<IDiagnosisRule> _rules;
    private readonly ReportWriter _reportWriter = new();
    private readonly ArchiveWriter _archiveWriter = new();

    public TriageRunner()
        : this(DefaultCollectors(), DefaultRules())
    {
    }

    public TriageRunner(IReadOnlyList<ITriageCollector> collectors, IReadOnlyList<IDiagnosisRule> rules)
    {
        _collectors = collectors;
        _rules = rules;
    }

    public async Task<ReportPackage> RunAsync(CollectionOptions options, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var reportId = ReportIdentity.Create();
        var reportFolder = CreateReportFolder(options.OutputPath, reportId);
        var context = new CollectionContext(options, reportFolder);
        var data = new TriageData
        {
            ReportId = reportId,
            ComputerName = options.IncludeMachineName ? Environment.MachineName : null,
            ReportFolder = reportFolder
        };

        foreach (var collector in _collectors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await collector.CollectAsync(data, context, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                context.AddWarning(data, collector.Name, ex.Message);
            }
        }

        progress?.Report("Analyzing findings...");
        foreach (var rule in _rules)
        {
            foreach (var finding in rule.Analyze(data))
            {
                data.Findings.Add(finding);
            }
        }

        if (data.Findings.Count == 0)
        {
            data.Findings.Add(new Finding(
                "NO_OBVIOUS_CAUSE",
                FindingSeverity.Info,
                FindingConfidence.Medium,
                "General",
                "No obvious problem was detected in this run",
                "The sampled counters and queried event logs did not cross the built-in thresholds.",
                "Run Advanced Scan while the system is actively misbehaving for more evidence."));
        }

        data.CompletedAt = DateTimeOffset.Now;

        progress?.Report("Writing reports...");
        var reportPaths = await _reportWriter.WriteAsync(data, context, cancellationToken).ConfigureAwait(false);
        string? zipPath = null;
        if (!options.NoZip)
        {
            progress?.Report("Creating zip archive...");
            zipPath = _archiveWriter.CreateZip(reportFolder);
        }

        progress?.Report("Complete.");
        return new ReportPackage(reportFolder, reportPaths.TextPath, reportPaths.JsonPath, reportPaths.SummaryPath, reportPaths.PublicSummaryPath, zipPath, data);
    }

    private static IReadOnlyList<ITriageCollector> DefaultCollectors() =>
    [
        new RunMetadataCollector(),
        new SystemCollector(),
        new HardwareCollector(),
        new PowerCollector(),
        new EventLogCollector(),
        new PerformanceCollector(),
        new ServicesStartupCollector(),
        new UpdatesSecurityCollector(),
        new NetworkCollector(),
        new DriverCollector()
    ];

    private static IReadOnlyList<IDiagnosisRule> DefaultRules() =>
    [
        new GeneralHealthRules()
    ];

    internal static string CreateReportFolder(string? outputPath)
    {
        return CreateReportFolder(outputPath, ReportIdentity.Create());
    }

    internal static string CreateReportFolder(string? outputPath, string reportId)
    {
        var root = string.IsNullOrWhiteSpace(outputPath)
            ? DefaultOutputRoot()
            : outputPath;

        Directory.CreateDirectory(root);
        var safeReportId = string.Concat(reportId.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_'));
        var folder = Path.Combine(root, safeReportId);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string DefaultOutputRoot()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return string.IsNullOrWhiteSpace(desktop)
            ? Environment.CurrentDirectory
            : desktop;
    }
}
