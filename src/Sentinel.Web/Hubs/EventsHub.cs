using Microsoft.AspNetCore.SignalR;
using Sentinel.Core.Models;

namespace Sentinel.Web.Hubs;

public sealed class EventsHub : Hub
{
    public async Task SendLogEvent(LogEvent evt)
    {
        await Clients.All.SendAsync("ReceiveLogEvent", evt);
    }

    public async Task SendAction(ActionRecord action)
    {
        await Clients.All.SendAsync("ReceiveAction", action);
    }

    public async Task UpdateMetrics(int eventsPerSecond)
    {
        await Clients.All.SendAsync("UpdateMetrics", eventsPerSecond);
    }
}

public sealed record ActionRecord(
    string Description,
    int Confidence,
    DateTimeOffset Timestamp,
    ActionStatus Status);
