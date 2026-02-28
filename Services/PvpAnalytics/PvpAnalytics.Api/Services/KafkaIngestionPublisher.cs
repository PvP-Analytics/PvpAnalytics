using System.IO;
using Confluent.Kafka;
using Google.Protobuf;
using Microsoft.Extensions.Options;
using PvpAnalytics.Core.Configuration;
using PvpAnalytics.Shared.Protos.Ingestion;

namespace PvpAnalytics.Api.Services;

internal sealed class KafkaIngestionPublisher : IIngestionPublisher
{
    private readonly IProducer<string, byte[]>? _producer;
    private readonly KafkaOptions _kafkaOptions;
    private readonly IngestionOptions _ingestionOptions;

    public KafkaIngestionPublisher(
        IOptions<KafkaOptions> kafkaOptions,
        IOptions<IngestionOptions> ingestionOptions)
    {
        _kafkaOptions = kafkaOptions.Value;
        _ingestionOptions = ingestionOptions.Value;
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

        await _producer.ProduceAsync(_kafkaOptions.IngestionTopic, new Message<string, byte[]>
        {
            Key = key,
            Value = value,
            Headers = new Headers { new Header("correlation-id", System.Text.Encoding.UTF8.GetBytes(correlationId)) }
        }, ct).ConfigureAwait(false);
        return true;
    }
}
