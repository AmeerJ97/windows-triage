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
                Console.WriteLine(JsonSerializer.Serialize(package.Data, new JsonSerializerOptions { WriteIndented = true }));
            }
            else if (!parsed.Options.Quiet)
            {
                Console.WriteLine();
                Console.WriteLine("Windows Triage complete.");
                Console.WriteLine($"Report: {package.TextReportPath}");
                Console.WriteLine($"JSON:   {package.JsonReportPath}");
                Console.WriteLine($"Zip:    {package.ZipPath ?? "not created"}");
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
          WindowsTriage.exe --help
          WindowsTriage.exe --version

        Options for collect:
          --profile quick|full|advanced     Scan profile. Default: full.
          --output DIR                      Output root directory. Default: Desktop.
          --include-network                 Include IP addressing details.
          --include-command-lines           Include process/service command lines.
          --include-machine-name            Include the local computer name in reports.
          --sample-seconds N                Override profile sample duration.
          --sample-interval-seconds N       Override sample interval.
          --no-zip                          Do not create zip archive.
          --json                            Write collected data JSON to stdout.
          --quiet                           Suppress progress output.
          --verbose                         Reserved for more detailed CLI output.
        """;

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
