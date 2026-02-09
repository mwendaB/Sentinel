using Microsoft.AspNetCore.SignalR;
using Sentinel.Core.Models;
using Sentinel.Web.Hubs;
using Models = Sentinel.Core.Models;

namespace Sentinel.Web.Services;

public sealed class LogStreamSimulator : BackgroundService
{
    private readonly LogStreamState _state;
    private readonly IHubContext<EventsHub> _hubContext;
    private readonly Random _random = new();

    private static readonly LogSource[] Sources =
    [
        new() { Name = "nginx", Platform = Platform.Linux, Type = SourceType.Syslog },
        new() { Name = "postgres", Platform = Platform.Linux, Type = SourceType.Journald },
        new() { Name = "redis", Platform = Platform.Linux, Type = SourceType.DockerContainer },
        new() { Name = "worker", Platform = Platform.Universal, Type = SourceType.HttpEndpoint },
        new() { Name = "api", Platform = Platform.Universal, Type = SourceType.HttpEndpoint }
    ];

    private static readonly string[] Messages =
    [
        "connection timeout (x3)",
        "slow query detected",
        "cache hit rate: 94%",
        "cpu pressure high",
        "disk queue length elevated",
        "autoscale event triggered",
        "rule evaluation backlog",
        "http 503 spike",
        "queue depth increasing"
    ];

    public LogStreamSimulator(LogStreamState state, IHubContext<EventsHub> hubContext)
    {
        _state = state;
        _hubContext = hubContext;
        _state.Seed();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var level = PickLevel();
            var source = Sources[_random.Next(Sources.Length)];
            var message = Messages[_random.Next(Messages.Length)];

            var logEvent = new LogEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Level = level,
                Message = $"{source.Name} {message}",
                Source = source
            };

            _state.AddEvent(logEvent);
            await _hubContext.Clients.All.SendAsync("ReceiveLogEvent", logEvent, stoppingToken);

            if (_random.NextDouble() > 0.86)
            {
                var action = new ActionRecord(
                    "Restarted service",
                    _random.Next(78, 98),
                    DateTimeOffset.UtcNow,
                    ActionStatus.Success);

                _state.AddAction(action);
                await _hubContext.Clients.All.SendAsync("ReceiveAction", action, stoppingToken);
            }

            var rate = _random.Next(980, 1650);
            _state.UpdateMetrics(rate);
            await _hubContext.Clients.All.SendAsync("UpdateMetrics", rate, stoppingToken);

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private Models.LogLevel PickLevel()
    {
        var roll = _random.Next(100);
        return roll switch
        {
            < 8 => Models.LogLevel.Error,
            < 20 => Models.LogLevel.Warning,
            _ => Models.LogLevel.Information
        };
    }
}
