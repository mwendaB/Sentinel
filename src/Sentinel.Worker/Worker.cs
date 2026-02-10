using Microsoft.Extensions.Options;
using Sentinel.Core.Models;
using Sentinel.Worker.Ingestion;
using Models = Sentinel.Core.Models;

namespace Sentinel.Worker;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IngestionApiClient _apiClient;
    private readonly Random _random = new();
    private readonly SyntheticOptions _syntheticOptions;

    private static readonly LogSource[] Sources =
    [
        new() { Name = "worker", Platform = Platform.Universal, Type = SourceType.HttpEndpoint },
        new() { Name = "ingestion", Platform = Platform.Linux, Type = SourceType.Journald },
        new() { Name = "analysis", Platform = Platform.Linux, Type = SourceType.DockerContainer }
    ];

    private static readonly string[] Messages =
    [
        "queue depth increasing",
        "rule evaluation backlog",
        "auto-scaling triggered",
        "slow query detected",
        "connection timeout",
        "cache miss spike"
    ];

    public Worker(
        ILogger<Worker> logger,
        IngestionApiClient apiClient,
        IOptions<IngestionOptions> ingestionOptions)
    {
        _logger = logger;
        _apiClient = apiClient;
        _syntheticOptions = ingestionOptions.Value.Synthetic;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_syntheticOptions.Enabled)
        {
            _logger.LogInformation("Synthetic ingestion disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var source = PickSource();
            var logEvent = new LogEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Level = PickLevel(),
                Message = $"{source.Name} {Messages[_random.Next(Messages.Length)]}",
                Source = source
            };

            await _apiClient.SendLogAsync(logEvent, stoppingToken);

            if (_random.NextDouble() > 0.82)
            {
                var actionEvent = new ActionEvent
                {
                    Description = "Restarted service",
                    Confidence = _random.Next(75, 98),
                    Timestamp = DateTimeOffset.UtcNow,
                    Status = ActionStatus.Success
                };
                await _apiClient.SendActionAsync(actionEvent, stoppingToken);
            }

            if (_logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information))
            {
                _logger.LogInformation("Worker published log event at: {time}", DateTimeOffset.Now);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private LogSource PickSource() => Sources[_random.Next(Sources.Length)];

    private static Models.LogLevel PickLevel()
    {
        var roll = Random.Shared.Next(100);
        return roll switch
        {
            < 8 => Models.LogLevel.Error,
            < 20 => Models.LogLevel.Warning,
            _ => Models.LogLevel.Information
        };
    }
}
