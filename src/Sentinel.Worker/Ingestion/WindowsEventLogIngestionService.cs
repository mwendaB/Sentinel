using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Options;
using Sentinel.Core.Models;
using Models = Sentinel.Core.Models;

namespace Sentinel.Worker.Ingestion;

[SupportedOSPlatform("windows")]
public sealed class WindowsEventLogIngestionService : BackgroundService
{
    private readonly ILogger<WindowsEventLogIngestionService> _logger;
    private readonly IngestionApiClient _apiClient;
    private readonly WindowsEventLogOptions _options;

    public WindowsEventLogIngestionService(
        ILogger<WindowsEventLogIngestionService> logger,
        IngestionApiClient apiClient,
        IOptions<IngestionOptions> options)
    {
        _logger = logger;
        _apiClient = apiClient;
        _options = options.Value.WindowsEventLog;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || !OperatingSystem.IsWindows())
        {
            _logger.LogInformation("Windows Event Log ingestion disabled or not on Windows.");
            return;
        }

        if (_options.LogNames.Length == 0)
        {
            _logger.LogInformation("Windows Event Log ingestion enabled but no log names configured.");
            return;
        }

        var tasks = _options.LogNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => MonitorLogAsync(name, stoppingToken))
            .ToArray();

        await Task.WhenAll(tasks);
    }

    private async Task MonitorLogAsync(string logName, CancellationToken stoppingToken)
    {
        var pollDelay = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));
        var lastIndex = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var eventLog = new EventLog(logName);
                if (lastIndex == 0 && eventLog.Entries.Count > 0)
                {
                    lastIndex = eventLog.Entries[^1].Index;
                    await Task.Delay(pollDelay, stoppingToken);
                    continue;
                }

                foreach (EventLogEntry entry in eventLog.Entries)
                {
                    if (entry.Index <= lastIndex)
                    {
                        continue;
                    }

                    lastIndex = Math.Max(lastIndex, entry.Index);

                    var logEvent = new LogEvent
                    {
                        Id = Guid.NewGuid(),
                        Timestamp = entry.TimeGenerated,
                        Level = MapLevel(entry.EntryType),
                        Message = entry.Message,
                        Source = new LogSource
                        {
                            Name = string.IsNullOrWhiteSpace(entry.Source) ? logName : entry.Source,
                            Platform = Platform.Windows,
                            Type = SourceType.WindowsEventLog
                        },
                        Metadata = new Dictionary<string, object?>
                        {
                            ["EventId"] = entry.InstanceId,
                            ["Category"] = entry.Category,
                            ["MachineName"] = entry.MachineName
                        }
                    };

                    await _apiClient.SendLogAsync(logEvent, stoppingToken);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to read Windows Event Log {LogName}.", logName);
            }

            await Task.Delay(pollDelay, stoppingToken);
        }
    }

    private static Models.LogLevel MapLevel(EventLogEntryType entryType) => entryType switch
    {
        EventLogEntryType.Error => Models.LogLevel.Error,
        EventLogEntryType.Warning => Models.LogLevel.Warning,
        EventLogEntryType.FailureAudit => Models.LogLevel.Critical,
        EventLogEntryType.SuccessAudit => Models.LogLevel.Information,
        _ => Models.LogLevel.Information
    };
}
