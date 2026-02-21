using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Sentinel.Core.Models;
using Models = Sentinel.Core.Models;

namespace Sentinel.Worker.Ingestion;

public sealed class KafkaIngestionService : BackgroundService
{
    private readonly ILogger<KafkaIngestionService> _logger;
    private readonly IngestionApiClient _apiClient;
    private readonly KafkaOptions _options;

    public KafkaIngestionService(
        ILogger<KafkaIngestionService> logger,
        IngestionApiClient apiClient,
        IOptions<IngestionOptions> options)
    {
        _logger = logger;
        _apiClient = apiClient;
        _options = options.Value.Kafka;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Kafka ingestion disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.BootstrapServers) || _options.Topics.Length == 0)
        {
            _logger.LogWarning("Kafka ingestion enabled but bootstrap servers or topics not configured.");
            return;
        }

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.GroupId,
            AutoOffsetReset = ParseOffsetReset(_options.AutoOffsetReset),
            EnableAutoCommit = true,
            EnablePartitionEof = false
        };

        using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
        consumer.Subscribe(_options.Topics);
        _logger.LogInformation("Kafka ingestion subscribed to {Topics}.", string.Join(", ", _options.Topics));

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<Ignore, string>? result = null;
            try
            {
                result = consumer.Consume(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                _logger.LogWarning(ex, "Kafka consume failed.");
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kafka ingestion failure.");
                continue;
            }

            if (result?.Message?.Value is null)
            {
                continue;
            }

            var logEvent = BuildLogEvent(result);
            await _apiClient.SendLogAsync(logEvent, stoppingToken);
        }
    }

    private LogEvent BuildLogEvent(ConsumeResult<Ignore, string> result)
    {
        var payload = TryParsePayload(result.Message.Value);
        var timestamp = payload.Timestamp ?? ConvertTimestamp(result.Message.Timestamp);

        return new LogEvent
        {
            Id = Guid.NewGuid(),
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Level = payload.Level ?? Models.LogLevel.Information,
            Message = payload.Message ?? result.Message.Value,
            Source = payload.Source ?? new LogSource
            {
                Name = $"kafka:{result.Topic}",
                Platform = Platform.Universal,
                Type = SourceType.KafkaTopic
            }
        };
    }

    private static DateTimeOffset? ConvertTimestamp(Timestamp timestamp)
    {
        return timestamp.Type == TimestampType.NotAvailable
            ? null
            : new DateTimeOffset(timestamp.UtcDateTime);
    }

    private static KafkaPayload TryParsePayload(string raw)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<KafkaPayloadDto>(raw, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (payload is null)
            {
                return KafkaPayload.Empty;
            }

            var level = payload.Level;
            Models.LogLevel? parsedLevel = null;
            if (!string.IsNullOrWhiteSpace(level) &&
                Enum.TryParse(level, true, out Models.LogLevel levelEnum))
            {
                parsedLevel = levelEnum;
            }

            LogSource? source = null;
            if (!string.IsNullOrWhiteSpace(payload.SourceName))
            {
                source = new LogSource
                {
                    Name = payload.SourceName!,
                    Platform = payload.Platform ?? Platform.Universal,
                    Type = payload.SourceType ?? SourceType.KafkaTopic
                };
            }

            DateTimeOffset? timestamp = null;
            if (!string.IsNullOrWhiteSpace(payload.Timestamp) &&
                DateTimeOffset.TryParse(payload.Timestamp, out var parsedTimestamp))
            {
                timestamp = parsedTimestamp;
            }

            return new KafkaPayload(payload.Message, parsedLevel, source, timestamp);
        }
        catch (JsonException)
        {
            return KafkaPayload.Empty;
        }
    }

    private static AutoOffsetReset ParseOffsetReset(string value)
    {
        return Enum.TryParse<AutoOffsetReset>(value, true, out var parsed)
            ? parsed
            : AutoOffsetReset.Latest;
    }

    private sealed record KafkaPayload(
        string? Message,
        Models.LogLevel? Level,
        LogSource? Source,
        DateTimeOffset? Timestamp)
    {
        public static KafkaPayload Empty { get; } = new(null, null, null, null);
    }

    private sealed record KafkaPayloadDto
    {
        public string? Message { get; init; }
        public string? Level { get; init; }
        public string? SourceName { get; init; }
        public SourceType? SourceType { get; init; }
        public Platform? Platform { get; init; }
        public string? Timestamp { get; init; }
    }
}
