using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using Sentinel.Core.Models;
using Models = Sentinel.Core.Models;

namespace Sentinel.Worker.Ingestion;

public sealed class SyslogUdpIngestionService : BackgroundService
{
    private readonly ILogger<SyslogUdpIngestionService> _logger;
    private readonly IngestionApiClient _apiClient;
    private readonly SyslogOptions _options;

    public SyslogUdpIngestionService(
        ILogger<SyslogUdpIngestionService> logger,
        IngestionApiClient apiClient,
        IOptions<IngestionOptions> options)
    {
        _logger = logger;
        _apiClient = apiClient;
        _options = options.Value.Syslog;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Syslog ingestion disabled.");
            return;
        }

        var address = IPAddress.Any;
        if (!string.IsNullOrWhiteSpace(_options.BindAddress) &&
            !IPAddress.TryParse(_options.BindAddress, out address))
        {
            _logger.LogWarning("Invalid syslog bind address {BindAddress}. Using 0.0.0.0.", _options.BindAddress);
            address = IPAddress.Any;
        }

        using var client = new UdpClient(new IPEndPoint(address, _options.Port));
        _logger.LogInformation("Syslog UDP listener started on {Address}:{Port}.", address, _options.Port);

        while (!stoppingToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await client.ReceiveAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Syslog UDP receive failed.");
                continue;
            }

            var message = Encoding.UTF8.GetString(result.Buffer);
            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            var parsed = SyslogMessage.Parse(message);
            var logEvent = new LogEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = parsed.Timestamp ?? DateTimeOffset.UtcNow,
                Level = parsed.Level,
                Message = parsed.Message,
                Source = new LogSource
                {
                    Name = parsed.SourceName ?? parsed.Host ?? "syslog",
                    Platform = Platform.Universal,
                    Type = SourceType.Syslog
                },
                Metadata = new Dictionary<string, object?>
                {
                    ["Host"] = parsed.Host ?? string.Empty
                }
            };

            await _apiClient.SendLogAsync(logEvent, stoppingToken);
        }
    }

    private sealed record SyslogMessage(
        Models.LogLevel Level,
        string Message,
        string? Host,
        string? SourceName,
        DateTimeOffset? Timestamp)
    {
        public static SyslogMessage Parse(string payload)
        {
            var text = payload.Trim();
            var level = Models.LogLevel.Information;
            var host = default(string);
            var source = default(string);
            DateTimeOffset? timestamp = null;

            if (text.StartsWith('<'))
            {
                var end = text.IndexOf('>');
                if (end > 1 && int.TryParse(text[1..end], out var pri))
                {
                    var severity = pri % 8;
                    level = severity switch
                    {
                        0 or 1 => Models.LogLevel.Critical,
                        2 => Models.LogLevel.Error,
                        3 => Models.LogLevel.Error,
                        4 => Models.LogLevel.Warning,
                        5 => Models.LogLevel.Information,
                        6 => Models.LogLevel.Information,
                        _ => Models.LogLevel.Debug
                    };
                    text = text[(end + 1)..].Trim();
                }
            }

            var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length >= 5)
            {
                var tsCandidate = string.Join(' ', tokens[0], tokens[1], tokens[2]);
                if (DateTimeOffset.TryParse(tsCandidate, out var parsedTs))
                {
                    timestamp = parsedTs;
                }

                host = tokens[3];
                var remaining = string.Join(' ', tokens.Skip(4));
                var colonIndex = remaining.IndexOf(':');
                if (colonIndex > 0)
                {
                    source = remaining[..colonIndex];
                    remaining = remaining[(colonIndex + 1)..].Trim();
                }

                return new SyslogMessage(level, remaining, host, source, timestamp);
            }

            return new SyslogMessage(level, text, host, source, timestamp);
        }
    }
}
