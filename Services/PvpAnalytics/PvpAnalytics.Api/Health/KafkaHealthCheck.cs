using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PvpAnalytics.Core.Configuration;

namespace PvpAnalytics.Api.Health;

internal sealed class KafkaHealthCheck : IHealthCheck
{
    private readonly KafkaOptions _kafkaOptions;
    private readonly IngestionOptions _ingestionOptions;

    public KafkaHealthCheck(IOptions<KafkaOptions> kafkaOptions, IOptions<IngestionOptions> ingestionOptions)
    {
        _kafkaOptions = kafkaOptions.Value;
        _ingestionOptions = ingestionOptions.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_ingestionOptions.StreamingEnabled || string.IsNullOrWhiteSpace(_kafkaOptions.BootstrapServers))
        {
            return Task.FromResult(HealthCheckResult.Healthy("Streaming ingestion disabled or Kafka not configured."));
        }

        try
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig
            {
                BootstrapServers = _kafkaOptions.BootstrapServers
            }).Build();
            var meta = admin.GetMetadata(TimeSpan.FromSeconds(5));
            return Task.FromResult(HealthCheckResult.Healthy(
                $"Kafka broker available. Brokers: {meta.Brokers.Count}."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Kafka broker unreachable.", ex));
        }
    }
}
