using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using WindowsTriage.Core;

namespace WindowsTriage.App;

internal static class Program
{
    private const int AttachParentProcess = -1;

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var wantsConsole = args.Length > 0 && !IsCommand(args[0], "gui");
        if (wantsConsole)
        {
            AttachConsole(AttachParentProcess);
        }

        if (args.Any(IsHelp))
        {
            Console.WriteLine(HelpText());
            return 0;
        }

        if (args.Any(arg => arg.Equals("--version", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine(typeof(TriageData).Assembly.GetName().Version?.ToString() ?? "0.2.0");
            return 0;
        }

        if (args.Length == 1 && IsCommand(args[0], "profiles"))
        {
            Console.WriteLine(ProfileText());
            return 0;
        }

        if (args.Length == 1 && IsCommand(args[0], "privacy"))
        {
            Console.WriteLine(PrivacyText());
            return 0;
        }

        if (args.Length == 0 || IsCommand(args[0], "gui"))
        {
            if (!IsAdministrator())
            {
                return RelaunchElevated(args);
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
            return 0;
        }

        try
        {
            var parsed = CliOptions.Parse(args);
            if (!IsAdministrator())
            {
                throw new UnauthorizedAccessException("Windows Triage collection requires an elevated Administrator shell. Re-run this command from an Administrator terminal.");
            }

            var runner = new TriageRunner();
            var progress = parsed.Options.Quiet ? null : new Progress<string>(message => Console.Error.WriteLine(message));
            var package = await runner.RunAsync(parsed.Options, progress).ConfigureAwait(false);

            if (parsed.Options.JsonToStdout)
            {
                Console.WriteLine(JsonSerializer.Serialize(package.Data, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            else if (parsed.Options.PrintPublicSummary)
            {
                Console.WriteLine(await File.ReadAllTextAsync(package.PublicSummaryPath).ConfigureAwait(false));
            }
            else if (!parsed.Options.Quiet)
            {
                Console.WriteLine();
                Console.WriteLine("Windows Triage complete.");
                Console.WriteLine($"Report: {package.TextReportPath}");
                Console.WriteLine($"JSON:   {package.JsonReportPath}");
                Console.WriteLine($"Public: {package.PublicSummaryPath}");
                Console.WriteLine($"Privacy:{package.PrivacyManifestPath}");
                Console.WriteLine($"Zip:    {package.ZipPath ?? "not created"}");
                if (package.Data.Warnings.Count > 0)
                {
                    Console.WriteLine($"Warnings: {package.Data.Warnings.Count} (the scan completed; review the report for details)");
                }
            }

            if (parsed.Options.OpenReportFolder)
            {
                OpenFolder(package.ReportFolder);
            }

            return package.Data.Warnings.Count == 0 ? 0 : 1;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(HelpText());
            return 2;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 4;
        }
    }

    private static bool IsCommand(string value, string command)
    {
        return value.Equals(command, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHelp(string value)
    {
        return value is "-h" or "/?" || value.Equals("--help", StringComparison.OrdinalIgnoreCase);
    }

    private static string HelpText() =>
        """
        WindowsTriage - Windows 11 health diagnostics

        Usage:
          WindowsTriage.exe gui
          WindowsTriage.exe collect [options]
          WindowsTriage.exe quick|full|advanced [options]
          WindowsTriage.exe profiles
          WindowsTriage.exe privacy
          WindowsTriage.exe --help
          WindowsTriage.exe --version

        Options for collect:
          --profile quick|full|advanced     Scan profile. Default: full.
          --output DIR                      Output root directory. Default: Desktop.
          --include-network                 Include IP addressing details.
          --include-command-lines           Include process/service command lines.
          --include-machine-name            Include the local computer name in reports.
          --include-private-artifacts       Retain raw reports that may contain machine identifiers.
          --sample-seconds N                Override profile sample duration.
          --sample-interval-seconds N       Override sample interval.
          --no-zip                          Do not create zip archive.
          --json                            Write collected data JSON to stdout.
          --print-summary                   Write the public summary to stdout.
          --open                            Open the report folder after collection.
          --quiet                           Suppress progress output.
          --verbose                         Show collector timing and command status details.
        """;

    private static string ProfileText() =>
        """
        Scan profiles:
          quick      20-second sample for a problem happening now.
          full       60-second balanced scan (recommended default).
          advanced  120-second sample plus deeper power and driver evidence.

        Examples:
          WindowsTriage.exe quick --output C:\Temp
          WindowsTriage.exe full --print-summary
          WindowsTriage.exe advanced --include-private-artifacts
        """;

    private static string PrivacyText() =>
        """
        Privacy defaults:
          Machine name, usernames, hardware IDs, local paths, network addresses,
          command lines, raw event messages, and raw power reports are omitted.

        Explicit opt-ins:
          --include-machine-name
          --include-network
          --include-command-lines
          --include-private-artifacts

        Share public_summary.md publicly. Keep ZIP bundles private, especially
        when any opt-in is enabled.
        """;

    private static void OpenFolder(string folder)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Collection succeeded, but the report folder could not be opened: {ex.Message}");
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int RelaunchElevated(string[] args)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? Application.ExecutablePath,
                UseShellExecute = true,
                Verb = "runas"
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            System.Diagnostics.Process.Start(startInfo);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Windows Triage needs Administrator access to collect a complete report.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Windows Triage",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return 3;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);
}
