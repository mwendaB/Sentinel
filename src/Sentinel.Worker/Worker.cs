using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Sentinel.Core.Models;
using Models = Sentinel.Core.Models;

namespace Sentinel.Worker;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly HttpClient _httpClient;
    private readonly ApiOptions _apiOptions;
    private readonly Random _random = new();

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

    public Worker(ILogger<Worker> logger, HttpClient httpClient, IOptions<ApiOptions> apiOptions)
    {
        _logger = logger;
        _httpClient = httpClient;
        _apiOptions = apiOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _httpClient.BaseAddress = new Uri(_apiOptions.BaseUrl);
        _httpClient.DefaultRequestHeaders.Remove("X-Api-Key");
        _httpClient.DefaultRequestHeaders.Add("X-Api-Key", _apiOptions.ApiKey);

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

            await PostAsync("/api/logs", logEvent, stoppingToken);

            if (_random.NextDouble() > 0.82)
            {
                var actionEvent = new ActionEvent
                {
                    Description = "Restarted service",
                    Confidence = _random.Next(75, 98),
                    Timestamp = DateTimeOffset.UtcNow,
                    Status = ActionStatus.Success
                };

                await PostAsync("/api/actions", actionEvent, stoppingToken);
            }

            if (_logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information))
            {
                _logger.LogInformation("Worker published log event at: {time}", DateTimeOffset.Now);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task PostAsync<T>(string path, T payload, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync(path, payload, cancellationToken);
        response.EnsureSuccessStatusCode();
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
