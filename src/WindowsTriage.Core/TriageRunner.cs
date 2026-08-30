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

        try
        {
            foreach (var collector in _collectors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var started = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    await collector.CollectAsync(data, context, progress, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { context.AddWarning(data, collector.Name, ex.Message); }
                if (options.Verbose) progress?.Report($"{collector.Name} completed in {started.Elapsed.TotalSeconds:0.0}s.");
            }

            data.CompletedAt = DateTimeOffset.Now;
            progress?.Report("Analyzing findings...");
            foreach (var rule in _rules)
            {
                foreach (var finding in rule.Analyze(data)) data.Findings.Add(finding);
            }

            if (!GeneralHealthRules.HasEssentialEvidence(data))
                data.Findings.Add(new Finding("INCOMPLETE_DIAGNOSIS", FindingSeverity.Info, FindingConfidence.High, "General", "The scan did not collect all essential evidence", "One or more event, performance, storage, or thermal evidence sources were unavailable.", "Review collection warnings and rerun as Administrator before treating the system as healthy."));
            else if (data.Findings.Count == 0)
                data.Findings.Add(new Finding("NO_OBVIOUS_CAUSE", FindingSeverity.Info, FindingConfidence.Medium, "General", "No obvious problem was detected in this run", "The available counters and event logs did not cross the built-in thresholds.", "Run Advanced Scan while the system is actively misbehaving for more evidence."));

            context.RetainPrivateArtifacts();
            progress?.Report("Writing reports...");
            var reportPaths = await _reportWriter.WriteAsync(data, context, cancellationToken).ConfigureAwait(false);
            string? zipPath = null;
            if (!options.NoZip) { progress?.Report("Creating zip archive..."); zipPath = _archiveWriter.CreateZip(reportFolder); }
            progress?.Report("Complete.");
            return new ReportPackage(reportFolder, reportPaths.TextPath, reportPaths.JsonPath, reportPaths.SummaryPath, reportPaths.PublicSummaryPath, reportPaths.PrivacyManifestPath, zipPath, data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (Directory.Exists(reportFolder)) Directory.Delete(reportFolder, recursive: true);
            var zip = reportFolder + ".zip";
            if (File.Exists(zip)) File.Delete(zip);
            throw;
        }
        finally { context.CleanupTemporaryArtifacts(); }
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
