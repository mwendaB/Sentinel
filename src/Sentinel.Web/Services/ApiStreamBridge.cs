using Microsoft.AspNetCore.SignalR;
using Sentinel.Core.Models;
using Sentinel.Web.Hubs;

namespace Sentinel.Web.Services;

public sealed class ApiStreamBridge : BackgroundService
{
    private readonly ApiClient _apiClient;
    private readonly LogStreamState _state;
    private readonly IHubContext<EventsHub> _hubContext;

    private DateTimeOffset _lastLogTimestamp = DateTimeOffset.MinValue;
    private DateTimeOffset _lastActionTimestamp = DateTimeOffset.MinValue;

    public ApiStreamBridge(ApiClient apiClient, LogStreamState state, IHubContext<EventsHub> hubContext)
    {
        _apiClient = apiClient;
        _state = state;
        _hubContext = hubContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var metrics = await _apiClient.GetMetricsAsync(stoppingToken);
            var logs = await _apiClient.GetLogsAsync(200, stoppingToken);
            var actions = await _apiClient.GetActionsAsync(200, stoppingToken);

            ReplaceState(metrics, logs, actions);

            foreach (var logEvent in logs.Where(evt => evt.Timestamp > _lastLogTimestamp))
            {
                await _hubContext.Clients.All.SendAsync("ReceiveLogEvent", logEvent, stoppingToken);
            }

            foreach (var action in actions.Where(evt => evt.Timestamp > _lastActionTimestamp))
            {
                var record = new ActionRecord(action.Description, action.Confidence, action.Timestamp, action.Status);
                await _hubContext.Clients.All.SendAsync("ReceiveAction", record, stoppingToken);
            }

            if (logs.Count > 0)
            {
                _lastLogTimestamp = logs.Max(evt => evt.Timestamp);
            }

            if (actions.Count > 0)
            {
                _lastActionTimestamp = actions.Max(evt => evt.Timestamp);
            }

            await _hubContext.Clients.All.SendAsync("UpdateMetrics", metrics.EventsPerSecond, stoppingToken);

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private void ReplaceState(MetricsSnapshot metrics, IReadOnlyList<LogEvent> logs, IReadOnlyList<ActionEvent> actions)
    {
        _state.ReplaceMetrics(metrics);
        _state.ReplaceEvents(logs);
        _state.ReplaceActions(actions);
    }
}
