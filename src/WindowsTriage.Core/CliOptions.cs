namespace WindowsTriage.Core;

public sealed record ParsedCli(CollectionOptions Options);

public static class CliOptions
{
    public static ParsedCli Parse(string[] args)
    {
        var index = 0;
        if (args.Length > 0 && args[0].Equals("collect", StringComparison.OrdinalIgnoreCase))
        {
            index = 1;
        }
        else if (args.Length > 0 && !args[0].StartsWith("-", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unknown command: {args[0]}");
        }

        var profile = ScanProfile.Full;
        string? output = null;
        var includeNetwork = false;
        var includeCommandLines = false;
        var includeMachineName = false;
        var noZip = false;
        var json = false;
        var quiet = false;
        var verbose = false;
        int? sampleSeconds = null;
        var sampleIntervalSeconds = 5;

        while (index < args.Length)
        {
            var arg = args[index++];
            switch (arg.ToLowerInvariant())
            {
                case "--profile":
                    profile = ParseProfile(RequireValue(args, ref index, arg));
                    break;
                case "--output":
                    output = RequireValue(args, ref index, arg);
                    break;
                case "--include-network":
                    includeNetwork = true;
                    break;
                case "--include-command-lines":
                    includeCommandLines = true;
                    break;
                case "--include-machine-name":
                    includeMachineName = true;
                    break;
                case "--sample-seconds":
                    sampleSeconds = ParseInt(RequireValue(args, ref index, arg), arg, 15, 900);
                    break;
                case "--sample-interval-seconds":
                    sampleIntervalSeconds = ParseInt(RequireValue(args, ref index, arg), arg, 1, 60);
                    break;
                case "--no-zip":
                    noZip = true;
                    break;
                case "--json":
                    json = true;
                    break;
                case "--quiet":
                    quiet = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {arg}");
            }
        }

        return new ParsedCli(new CollectionOptions
        {
            Profile = profile,
            OutputPath = output,
            IncludeNetwork = includeNetwork,
            IncludeCommandLines = includeCommandLines,
            IncludeMachineName = includeMachineName,
            NoZip = noZip,
            JsonToStdout = json,
            Quiet = quiet,
            Verbose = verbose,
            SampleSeconds = sampleSeconds,
            SampleIntervalSeconds = sampleIntervalSeconds
        });
    }

    private static ScanProfile ParseProfile(string value) => value.ToLowerInvariant() switch
    {
        "quick" => ScanProfile.Quick,
        "full" => ScanProfile.Full,
        "advanced" => ScanProfile.Advanced,
        _ => throw new ArgumentException($"Invalid profile: {value}")
    };

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index >= args.Length || args[index].StartsWith("-", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return args[index++];
    }

    private static int ParseInt(string value, string option, int min, int max)
    {
        if (!int.TryParse(value, out var parsed) || parsed < min || parsed > max)
        {
            throw new ArgumentException($"{option} must be a number from {min} to {max}.");
        }

        return parsed;
    }
}
