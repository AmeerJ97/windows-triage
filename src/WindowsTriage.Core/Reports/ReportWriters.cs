using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WindowsTriage.Core.Reports;

public sealed class ReportWriter
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<(string TextPath, string JsonPath, string SummaryPath, string PublicSummaryPath, string PrivacyManifestPath)> WriteAsync(TriageData data, CollectionContext context, CancellationToken cancellationToken)
    {
        var textPath = Path.Combine(context.ReportFolder, "diagnostic_report.txt");
        var jsonPath = Path.Combine(context.ReportFolder, "diagnostic_data.json");
        var summaryPath = Path.Combine(context.ReportFolder, "summary.md");
        var publicSummaryPath = Path.Combine(context.ReportFolder, "public_summary.md");
        var privacyManifestPath = Path.Combine(context.ReportFolder, "privacy_manifest.json");
        var summary = BuildSummary(data);
        var publicSummary = BuildPublicSummary(data);
        await File.WriteAllTextAsync(publicSummaryPath, publicSummary, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(summaryPath, summary, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(textPath, BuildTextReport(data, summary), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(data, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(privacyManifestPath, JsonSerializer.Serialize(new
        {
            schemaVersion = data.SchemaVersion,
            data.ReportId,
            generatedAt = DateTimeOffset.Now,
            optIns = new { context.Options.IncludeMachineName, context.Options.IncludeNetwork, context.Options.IncludeCommandLines, context.Options.IncludePrivateArtifacts },
            excludedByDefault = new[] { "usernames", "machine identity", "hardware serials and IDs", "absolute local paths", "network addresses", "command lines", "service accounts", "raw event messages", "raw power reports" },
            privateArtifactsRetained = context.Options.IncludePrivateArtifacts,
            sharingGuidance = context.Options.IncludePrivateArtifacts ? "Keep this bundle private. Share public_summary.md publicly." : "Review files before sharing. public_summary.md is the public issue artifact."
        }, JsonOptions), cancellationToken).ConfigureAwait(false);
        return (textPath, jsonPath, summaryPath, publicSummaryPath, privacyManifestPath);
    }

    private static string BuildSummary(TriageData data)
    {
        var builder = new StringBuilder("# Windows Triage Summary\n\n");
        builder.AppendLine($"Generated: {DateTimeOffset.Now}");
        builder.AppendLine($"Report ID: {data.ReportId}");
        if (!string.IsNullOrWhiteSpace(data.ComputerName)) builder.AppendLine($"Computer: {data.ComputerName}");
        AppendFindings(builder, data);
        return builder.ToString();
    }

    private static string BuildPublicSummary(TriageData data)
    {
        var builder = new StringBuilder("# Windows Triage Public Summary\n\n");
        builder.AppendLine($"Generated: {DateTimeOffset.Now}");
        builder.AppendLine($"Report ID: {data.ReportId}");
        builder.AppendLine($"Tool Version: {data.ToolVersion}");
        builder.AppendLine();
        builder.AppendLine("> Intended for public GitHub issues. Review before posting; keep the full ZIP private.");
        AppendFindings(builder, data);
        builder.AppendLine();
        builder.AppendLine("## Collection Warnings");
        builder.AppendLine(data.Warnings.Count == 0 ? "None." : $"{data.Warnings.Count} warning(s). Review the local report before sharing details.");
        return PublicRedactor.Redact(builder.ToString(), data.ComputerName);
    }

    private static void AppendFindings(StringBuilder builder, TriageData data)
    {
        builder.AppendLine(); builder.AppendLine("## Findings");
        foreach (var finding in OrderedFindings(data))
        {
            builder.AppendLine(); builder.AppendLine($"### [{finding.Severity}] {finding.Title}"); builder.AppendLine();
            builder.AppendLine($"- Confidence: {finding.Confidence}"); builder.AppendLine($"- Category: {finding.Category}");
            builder.AppendLine($"- Evidence: {finding.Evidence}"); builder.AppendLine($"- Recommendation: {finding.Recommendation}");
        }
        if (data.Findings.Count == 0) { builder.AppendLine(); builder.AppendLine("No findings were generated."); }
    }

    private static string BuildTextReport(TriageData data, string summary)
    {
        var builder = new StringBuilder(summary);
        builder.AppendLine(); builder.AppendLine("# Collection Warnings");
        if (data.Warnings.Count == 0) builder.AppendLine("None.");
        else foreach (var warning in data.Warnings) builder.AppendLine($"- {warning.Area}: {warning.Message}");
        builder.AppendLine(); builder.AppendLine("# Data Sections");
        builder.AppendLine(JsonSerializer.Serialize(data.Sections, JsonOptions));
        return builder.ToString();
    }

    private static IEnumerable<Finding> OrderedFindings(TriageData data) => data.Findings.OrderBy(f => f.Severity switch { FindingSeverity.Critical => 0, FindingSeverity.Warning => 1, _ => 2 }).ThenBy(f => f.Id, StringComparer.OrdinalIgnoreCase);
}

internal static partial class PublicRedactor
{
    public static string Redact(string value, string? computerName)
    {
        if (!string.IsNullOrWhiteSpace(computerName)) value = value.Replace(computerName, "[machine]", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(Environment.UserName)) value = value.Replace(Environment.UserName, "[user]", StringComparison.OrdinalIgnoreCase);
        value = WindowsUserPath().Replace(value, @"C:\Users\[user]\");
        value = Sid().Replace(value, "[sid]");
        value = Mac().Replace(value, "[mac]");
        value = Ipv4().Replace(value, "[ip]");
        return value;
    }
    [GeneratedRegex(@"(?i)C:\\Users\\[^\\\s]+\\")] private static partial Regex WindowsUserPath();
    [GeneratedRegex(@"S-1-5-(?:\d+-){1,14}\d+")] private static partial Regex Sid();
    [GeneratedRegex(@"(?i)\b(?:[0-9A-F]{2}[:-]){5}[0-9A-F]{2}\b")] private static partial Regex Mac();
    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b")] private static partial Regex Ipv4();
}

public sealed class ArchiveWriter
{
    public string CreateZip(string reportFolder)
    {
        var zipPath = reportFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".zip";
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(reportFolder, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return zipPath;
    }
}
