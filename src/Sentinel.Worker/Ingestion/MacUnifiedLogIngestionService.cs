using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sentinel.Core.Models;
using Models = Sentinel.Core.Models;

namespace Sentinel.Worker.Ingestion;

public sealed class MacUnifiedLogIngestionService : BackgroundService
{
    private readonly ILogger<MacUnifiedLogIngestionService> _logger;
    private readonly IngestionApiClient _apiClient;
    private readonly MacUnifiedOptions _options;

    public MacUnifiedLogIngestionService(
        ILogger<MacUnifiedLogIngestionService> logger,
        IngestionApiClient apiClient,
        IOptions<IngestionOptions> options)
    {
        _logger = logger;
        _apiClient = apiClient;
        _options = options.Value.MacUnified;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || !OperatingSystem.IsMacOS())
        {
            _logger.LogInformation("macOS unified log ingestion disabled or not on macOS.");
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "log",
            Arguments = "stream --style json --info --debug",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            _logger.LogWarning("Failed to start macOS log stream.");
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

                var message = GetString(root, "eventMessage") ?? GetString(root, "message");
                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                var sourceName = GetString(root, "subsystem") ?? GetString(root, "processImagePath") ?? "unified";
                var level = InferLevel(message, GetString(root, "messageType"));

                var timestamp = DateTimeOffset.UtcNow;
                if (root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String)
                {
                    if (DateTimeOffset.TryParse(ts.GetString(), out var parsed))
                    {
                        timestamp = parsed;
                    }
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
                        Platform = Platform.MacOS,
                        Type = SourceType.UnifiedLog
                    }
                };

                await _apiClient.SendLogAsync(logEvent, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to parse unified log entry.");
            }
        }
    }

    private static string? GetString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    private static Models.LogLevel InferLevel(string message, string? messageType)
    {
        if (!string.IsNullOrWhiteSpace(messageType))
        {
            if (messageType.Equals("Error", StringComparison.OrdinalIgnoreCase))
            {
                return Models.LogLevel.Error;
            }

            if (messageType.Equals("Fault", StringComparison.OrdinalIgnoreCase))
            {
                return Models.LogLevel.Critical;
            }

            if (messageType.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                return Models.LogLevel.Information;
            }
        }

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
}
