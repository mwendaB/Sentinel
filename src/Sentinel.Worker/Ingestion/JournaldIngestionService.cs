using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sentinel.Core.Models;
using Models = Sentinel.Core.Models;

namespace Sentinel.Worker.Ingestion;

public sealed class JournaldIngestionService : BackgroundService
{
    private readonly ILogger<JournaldIngestionService> _logger;
    private readonly IngestionApiClient _apiClient;
    private readonly JournaldOptions _options;

    public JournaldIngestionService(
        ILogger<JournaldIngestionService> logger,
        IngestionApiClient apiClient,
        IOptions<IngestionOptions> options)
    {
        _logger = logger;
        _apiClient = apiClient;
        _options = options.Value.Journald;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || !OperatingSystem.IsLinux())
        {
            _logger.LogInformation("Journald ingestion disabled or not on Linux.");
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "journalctl",
            Arguments = "-f -o json",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            _logger.LogWarning("Failed to start journalctl.");
            return;
        }

        await foreach (var line in ReadLinesAsync(process.StandardOutput, stoppingToken))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var message = root.TryGetProperty("MESSAGE", out var msg) ? msg.GetString() : null;
                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                var sourceName = root.TryGetProperty("SYSLOG_IDENTIFIER", out var identifier)
                    ? identifier.GetString() ?? "journald"
                    : "journald";

                var timestamp = DateTimeOffset.UtcNow;
                if (root.TryGetProperty("__REALTIME_TIMESTAMP", out var ts))
                {
                    if (long.TryParse(ts.GetString(), out var micros))
                    {
                        timestamp = DateTimeOffset.FromUnixTimeMilliseconds(micros / 1000);
                    }
                }

                var level = Models.LogLevel.Information;
                if (root.TryGetProperty("PRIORITY", out var priority) && int.TryParse(priority.GetString(), out var prio))
                {
                    level = prio switch
                    {
                        <= 2 => Models.LogLevel.Critical,
                        3 => Models.LogLevel.Error,
                        4 => Models.LogLevel.Warning,
                        5 => Models.LogLevel.Information,
                        _ => Models.LogLevel.Debug
                    };
                }

                var logEvent = new LogEvent
                {
                    Id = Guid.NewGuid(),
                    Timestamp = timestamp,
                    Level = level,
                    Message = message,
                    Source = new LogSource
                    {
                        Name = sourceName,
                        Platform = Platform.Linux,
                        Type = SourceType.Journald
                    }
                };

                await _apiClient.SendLogAsync(logEvent, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to parse journald entry.");
            }
        }
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
}
