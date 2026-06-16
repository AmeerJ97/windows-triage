using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsTriage.Core.Reports;

public sealed class ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<(string TextPath, string JsonPath, string SummaryPath, string PublicSummaryPath)> WriteAsync(TriageData data, CollectionContext context, CancellationToken cancellationToken)
    {
        var textPath = Path.Combine(context.ReportFolder, "diagnostic_report.txt");
        var jsonPath = Path.Combine(context.ReportFolder, "diagnostic_data.json");
        var summaryPath = Path.Combine(context.ReportFolder, "summary.md");
        var publicSummaryPath = Path.Combine(context.ReportFolder, "public_summary.md");

        var summary = BuildSummary(data);
        var publicSummary = BuildPublicSummary(data);
        await File.WriteAllTextAsync(publicSummaryPath, publicSummary, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(summaryPath, summary, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(textPath, BuildTextReport(data, summary), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(data, JsonOptions), cancellationToken).ConfigureAwait(false);

        return (textPath, jsonPath, summaryPath, publicSummaryPath);
    }

    private static string BuildSummary(TriageData data)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Windows Triage Summary");
        builder.AppendLine();
        builder.AppendLine($"Generated: {DateTimeOffset.Now}");
        builder.AppendLine($"Report ID: {data.ReportId}");
        if (!string.IsNullOrWhiteSpace(data.ComputerName))
        {
            builder.AppendLine($"Computer: {data.ComputerName}");
        }
        builder.AppendLine();
        builder.AppendLine("## Findings");

        foreach (var finding in OrderedFindings(data))
        {
            builder.AppendLine();
            builder.AppendLine($"### [{finding.Severity}] {finding.Title}");
            builder.AppendLine();
            builder.AppendLine($"- Confidence: {finding.Confidence}");
            builder.AppendLine($"- Category: {finding.Category}");
            builder.AppendLine($"- Evidence: {finding.Evidence}");
            builder.AppendLine($"- Recommendation: {finding.Recommendation}");
        }

        if (data.Findings.Count == 0)
        {
            builder.AppendLine();
            builder.AppendLine("No findings were generated.");
        }

        return builder.ToString();
    }

    private static string BuildPublicSummary(TriageData data)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Windows Triage Public Summary");
        builder.AppendLine();
        builder.AppendLine($"Generated: {DateTimeOffset.Now}");
        builder.AppendLine($"Report ID: {data.ReportId}");
        builder.AppendLine($"Tool Version: {data.ToolVersion}");
        builder.AppendLine();
        builder.AppendLine("> This summary is intended for public GitHub issues. Review it before posting. Do not attach the full diagnostic ZIP publicly unless a maintainer asks for it through a private channel.");
        builder.AppendLine();
        builder.AppendLine("## Findings");

        foreach (var finding in OrderedFindings(data))
        {
            builder.AppendLine();
            builder.AppendLine($"### [{finding.Severity}] {finding.Title}");
            builder.AppendLine();
            builder.AppendLine($"- Confidence: {finding.Confidence}");
            builder.AppendLine($"- Category: {finding.Category}");
            builder.AppendLine($"- Evidence: {finding.Evidence}");
            builder.AppendLine($"- Recommendation: {finding.Recommendation}");
        }

        if (data.Findings.Count == 0)
        {
            builder.AppendLine();
            builder.AppendLine("No findings were generated.");
        }

        builder.AppendLine();
        builder.AppendLine("## Collection Warnings");
        builder.AppendLine(data.Warnings.Count == 0
            ? "None."
            : $"{data.Warnings.Count} warning(s). Review the full local report before sharing details.");

        return builder.ToString();
    }

    private static string BuildTextReport(TriageData data, string summary)
    {
        var builder = new StringBuilder(summary);
        builder.AppendLine();
        builder.AppendLine("# Collection Warnings");
        if (data.Warnings.Count == 0)
        {
            builder.AppendLine("None.");
        }
        else
        {
            foreach (var warning in data.Warnings)
            {
                builder.AppendLine($"- {warning.Area}: {warning.Message}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("# Data Sections");
        foreach (var section in data.Sections.OrderBy(pair => pair.Key))
        {
            builder.AppendLine();
            builder.AppendLine($"## {section.Key}");
            builder.AppendLine(JsonSerializer.Serialize(section.Value, JsonOptions));
        }

        return builder.ToString();
    }

    private static IEnumerable<Finding> OrderedFindings(TriageData data)
    {
        return data.Findings.OrderBy(finding => finding.Severity switch
        {
            FindingSeverity.Critical => 0,
            FindingSeverity.Warning => 1,
            _ => 2
        }).ThenBy(finding => finding.Id, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class ArchiveWriter
{
    public string CreateZip(string reportFolder)
    {
        var zipPath = reportFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".zip";
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        ZipFile.CreateFromDirectory(reportFolder, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return zipPath;
    }
}
