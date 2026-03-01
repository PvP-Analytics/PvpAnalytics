namespace PvpAnalytics.Core.Configuration;

/// <summary>
/// Feature flags for streaming ingestion (Phase 5).
/// </summary>
public class IngestionOptions
{
    public const string SectionName = "Ingestion";

    /// <summary>When true, gRPC Ingress accepts match payloads and publishes to Kafka.</summary>
    public bool StreamingEnabled { get; set; }

    /// <summary>When true, Worker consumes from Kafka and persists to PostgreSQL.</summary>
    public bool StreamingConsumerEnabled { get; set; }
}
