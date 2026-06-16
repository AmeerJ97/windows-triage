using System.Diagnostics;

namespace WindowsTriage.Core.Collectors;

internal sealed record CommandCapture(string Name, string Command, string OutputFile, int? ExitCode, bool Succeeded, string? Error);

internal static class CommandRunner
{
    public static async Task<CommandCapture> CaptureAsync(
        string name,
        string fileName,
        IReadOnlyList<string> arguments,
        string outputPath,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var commandLine = $"{fileName} {string.Join(" ", arguments)}";
        var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(3);

        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(effectiveTimeout);
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new CommandCapture(name, commandLine, outputPath, null, false, "Process did not start.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Best effort cleanup only.
                }

                var timeoutMessage = $"{name} timed out after {effectiveTimeout.TotalSeconds:0} seconds.";
                await File.WriteAllTextAsync(outputPath, timeoutMessage, CancellationToken.None).ConfigureAwait(false);
                return new CommandCapture(name, commandLine, outputPath, null, false, timeoutMessage);
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            await File.WriteAllTextAsync(outputPath, stdout + Environment.NewLine + stderr, cancellationToken).ConfigureAwait(false);

            return new CommandCapture(name, commandLine, outputPath, process.ExitCode, process.ExitCode == 0, string.IsNullOrWhiteSpace(stderr) ? null : stderr.Trim());
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(outputPath, ex.ToString(), cancellationToken).ConfigureAwait(false);
            return new CommandCapture(name, commandLine, outputPath, null, false, ex.Message);
        }
    }
}
