using System.IO;
using Confluent.Kafka;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PvpAnalytics.Core.Configuration;
using PvpAnalytics.Shared.Protos.Ingestion;

namespace PvpAnalytics.Api.Services;

internal sealed class KafkaIngestionPublisher : IIngestionPublisher, IDisposable
{
    private readonly IProducer<string, byte[]>? _producer;
    private readonly KafkaOptions _kafkaOptions;
    private readonly IngestionOptions _ingestionOptions;
    private readonly ILogger<KafkaIngestionPublisher> _logger;

    public KafkaIngestionPublisher(
        IOptions<KafkaOptions> kafkaOptions,
        IOptions<IngestionOptions> ingestionOptions,
        ILogger<KafkaIngestionPublisher> logger)
    {
        _kafkaOptions = kafkaOptions.Value;
        _ingestionOptions = ingestionOptions.Value;
        _logger = logger;
        if (_ingestionOptions.StreamingEnabled && !string.IsNullOrWhiteSpace(_kafkaOptions.BootstrapServers))
        {
            _producer = new ProducerBuilder<string, byte[]>(new ProducerConfig
            {
                BootstrapServers = _kafkaOptions.BootstrapServers
            }).Build();
        }
        else
        {
            _producer = null;
        }
    }

    public void Dispose()
    {
        _producer?.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task<bool> PublishAsync(MatchPayload payload, string correlationId, CancellationToken ct = default)
    {
        if (_producer == null)
            return false;

        byte[] value;
        using (var ms = new MemoryStream())
        {
            using (var cos = new CodedOutputStream(ms))
            {
                payload.WriteTo(cos);
            }
            value = ms.ToArray();
        }
        var key = payload.MatchDedupKey;
        if (string.IsNullOrEmpty(key))
            key = correlationId;

        try
        {
            await _producer.ProduceAsync(_kafkaOptions.IngestionTopic, new Message<string, byte[]>
            {
                Key = key,
                Value = value,
                Headers = new Headers { new Header("correlation-id", System.Text.Encoding.UTF8.GetBytes(correlationId)) }
            }, ct).ConfigureAwait(false);
            return true;
        }
        catch (ProduceException<string, byte[]> ex)
        {
            _logger.LogError(ex, "Kafka produce failed. Topic: {Topic}, Key: {Key}, CorrelationId: {CorrelationId}",
                _kafkaOptions.IngestionTopic, key, correlationId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kafka produce failed unexpectedly. Topic: {Topic}, Key: {Key}, CorrelationId: {CorrelationId}",
                _kafkaOptions.IngestionTopic, key, correlationId);
            return false;
        }
    }
}
