using System.Diagnostics;

namespace Sentinel.Remediation;

internal sealed class CommandRunner
{
    public async Task<CommandResult> RunAsync(string fileName, string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        if (!process.Start())
        {
            return new CommandResult(-1, string.Empty, "Failed to start process.");
        }

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var exitTask = process.WaitForExitAsync(cancellationToken);
        var timeoutTask = Task.Delay(timeout, cancellationToken);

        var completed = await Task.WhenAny(exitTask, timeoutTask);
        if (completed == timeoutTask)
        {
            TryKill(process);
            return new CommandResult(-1, await stdOutTask, "Command timed out.");
        }

        await exitTask;
        var stdout = await stdOutTask;
        var stderr = await stdErrTask;

        return new CommandResult(process.ExitCode, stdout, stderr);
    }

    public async Task<bool> CommandExistsAsync(string command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var check = OperatingSystem.IsWindows() ? "where" : "which";
        var result = await RunAsync(check, command, timeout, cancellationToken);
        return result.ExitCode == 0;
    }

    public async Task<string?> GetUidAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        var result = await RunAsync("id", "-u", timeout, cancellationToken);
        if (result.ExitCode != 0)
        {
            return null;
        }

        var value = result.StandardOutput.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void TryKill(Process process)
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
            // Ignore kill failures.
        }
    }
}

internal sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
