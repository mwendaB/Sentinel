using System.Diagnostics;
using Microsoft.Extensions.Options;
using Sentinel.Core.Models;
using Models = Sentinel.Core.Models;

namespace Sentinel.Worker.Ingestion;

public sealed class KubernetesLogIngestionService : BackgroundService
{
    private readonly ILogger<KubernetesLogIngestionService> _logger;
    private readonly IngestionApiClient _apiClient;
    private readonly KubernetesOptions _options;

    public KubernetesLogIngestionService(
        ILogger<KubernetesLogIngestionService> logger,
        IngestionApiClient apiClient,
        IOptions<IngestionOptions> options)
    {
        _logger = logger;
        _apiClient = apiClient;
        _options = options.Value.Kubernetes;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Kubernetes log ingestion disabled.");
            return;
        }

        if (_options.Pods.Length == 0)
        {
            _logger.LogInformation("Kubernetes log ingestion enabled but no pods configured.");
            return;
        }

        var tasks = _options.Pods
            .Where(pod => !string.IsNullOrWhiteSpace(pod))
            .Select(pod => TailPodAsync(pod, stoppingToken))
            .ToArray();

        await Task.WhenAll(tasks);
    }

    private async Task TailPodAsync(string pod, CancellationToken stoppingToken)
    {
        var args = BuildArgs(pod);
        var startInfo = new ProcessStartInfo
        {
            FileName = "kubectl",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process is null)
            {
                _logger.LogWarning("Failed to start kubectl logs for {Pod}.", pod);
                return;
            }

            await foreach (var line in ReadLinesAsync(process.StandardOutput, stoppingToken))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parsed = ParseLine(line);
                var logEvent = new LogEvent
                {
                    Id = Guid.NewGuid(),
                    Timestamp = parsed.Timestamp ?? DateTimeOffset.UtcNow,
                    Level = parsed.Level,
                    Message = parsed.Message,
                    Source = new LogSource
                    {
                        Name = pod,
                        Platform = Platform.Universal,
                        Type = SourceType.KubernetesPod
                    }
                };

                await _apiClient.SendLogAsync(logEvent, stoppingToken);
            }
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Kubernetes log tail failed for {Pod}.", pod);
        }
        finally
        {
            try
            {
                if (process is { HasExited: false })
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // ignored
            }
        }
    }

    private string BuildArgs(string pod)
    {
        var since = Math.Max(1, _options.SinceSeconds);
        var args = $"logs -f --since {since}s";
        if (_options.IncludeTimestamps)
        {
            args += " --timestamps";
        }

        if (!string.IsNullOrWhiteSpace(_options.Container))
        {
            args += $" -c {_options.Container}";
        }

        if (!string.IsNullOrWhiteSpace(_options.Namespace))
        {
            args += $" -n {_options.Namespace}";
        }

        args += $" {pod}";
        return args;
    }

    private static ParsedLine ParseLine(string line)
    {
        if (!line.Contains(' '))
        {
            return new ParsedLine(Models.LogLevel.Information, line, null);
        }

        var split = line.Split(' ', 2);
        if (DateTimeOffset.TryParse(split[0], out var timestamp))
        {
            return new ParsedLine(Models.LogLevel.Information, split[1].Trim(), timestamp);
        }

        return new ParsedLine(InferLevel(line), line, null);
    }

    private static Models.LogLevel InferLevel(string message)
    {
        if (message.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            return Models.LogLevel.Error;
        }

        if (message.Contains("warn", StringComparison.OrdinalIgnoreCase))
        {
            return Models.LogLevel.Warning;
        }

        return Models.LogLevel.Information;
    }

    private static async IAsyncEnumerable<string?> ReadLinesAsync(
        StreamReader reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            yield return line;
        }
    }

    private sealed record ParsedLine(Models.LogLevel Level, string Message, DateTimeOffset? Timestamp);
}
