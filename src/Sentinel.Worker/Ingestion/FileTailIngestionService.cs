using System.Text;
using Microsoft.Extensions.Options;
using Sentinel.Core.Models;
using Models = Sentinel.Core.Models;

namespace Sentinel.Worker.Ingestion;

public sealed class FileTailIngestionService : BackgroundService
{
    private readonly ILogger<FileTailIngestionService> _logger;
    private readonly IngestionApiClient _apiClient;
    private readonly FileTailOptions _options;

    public FileTailIngestionService(
        ILogger<FileTailIngestionService> logger,
        IngestionApiClient apiClient,
        IOptions<IngestionOptions> options)
    {
        _logger = logger;
        _apiClient = apiClient;
        _options = options.Value.FileTail;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || _options.Paths.Length == 0)
        {
            _logger.LogInformation("File tail ingestion disabled or no paths configured.");
            return;
        }

        var tasks = _options.Paths.Select(path => TailFileAsync(path, stoppingToken)).ToArray();
        await Task.WhenAll(tasks);
    }

    private async Task TailFileAsync(string path, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!File.Exists(path))
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                continue;
            }

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                stream.Seek(0, SeekOrigin.End);

                while (!stoppingToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();
                    if (line is null)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
                        continue;
                    }

                    var logEvent = new LogEvent
                    {
                        Id = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        Level = InferLevel(line),
                        Message = line,
                        Source = new LogSource
                        {
                            Name = Path.GetFileName(path),
                            Platform = GetPlatform(),
                            Type = SourceType.File
                        }
                    };

                    await _apiClient.SendLogAsync(logEvent, stoppingToken);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "File tailing failed for {Path}", path);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private static Models.LogLevel InferLevel(string message)
    {
        if (message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            return Models.LogLevel.Error;
        }

        if (message.Contains("warn", StringComparison.OrdinalIgnoreCase))
        {
            return Models.LogLevel.Warning;
        }

        return Models.LogLevel.Information;
    }

    private static Platform GetPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return Platform.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return Platform.MacOS;
        }

        return Platform.Linux;
    }
}
