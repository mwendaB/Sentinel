using System.Diagnostics;
using Microsoft.Extensions.Options;
using Sentinel.Core.Models;
using Models = Sentinel.Core.Models;

namespace Sentinel.Worker.Ingestion;

public sealed class DockerLogIngestionService : BackgroundService
{
    private readonly ILogger<DockerLogIngestionService> _logger;
    private readonly IngestionApiClient _apiClient;
    private readonly DockerOptions _options;

    public DockerLogIngestionService(
        ILogger<DockerLogIngestionService> logger,
        IngestionApiClient apiClient,
        IOptions<IngestionOptions> options)
    {
        _logger = logger;
        _apiClient = apiClient;
        _options = options.Value.Docker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Docker log ingestion disabled.");
            return;
        }

        if (_options.Containers.Length == 0)
        {
            _logger.LogInformation("Docker log ingestion enabled but no containers configured.");
            return;
        }

        var tasks = _options.Containers
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => TailContainerAsync(name, stoppingToken))
            .ToArray();

        await Task.WhenAll(tasks);
    }

    private async Task TailContainerAsync(string container, CancellationToken stoppingToken)
    {
        var args = BuildArgs(container);
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
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
                _logger.LogWarning("Failed to start docker logs for {Container}.", container);
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
                        Name = container,
                        Platform = Platform.Universal,
                        Type = SourceType.DockerContainer
                    }
                };

                await _apiClient.SendLogAsync(logEvent, stoppingToken);
            }
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Docker log tail failed for {Container}.", container);
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

    private string BuildArgs(string container)
    {
        var since = Math.Max(1, _options.SinceSeconds);
        var args = $"logs -f --since {since}s";
        if (_options.IncludeTimestamps)
        {
            args += " --timestamps";
        }

        args += $" {container}";
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
