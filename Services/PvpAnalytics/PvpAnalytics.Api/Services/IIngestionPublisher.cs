using PvpAnalytics.Shared.Protos.Ingestion;

namespace PvpAnalytics.Api.Services;

/// <summary>
/// Publishes match payloads to Kafka when streaming ingestion is enabled. No-op when disabled.
/// </summary>
internal interface IIngestionPublisher
{
    Task<bool> PublishAsync(MatchPayload payload, string correlationId, CancellationToken ct = default);
}
