namespace PvpAnalytics.Core.Configuration;

/// <summary>
/// Configuration for Kafka (streaming ingestion). Used by PvpAnalytics.Api (producer) and PvpAnalytics.Worker (consumer).
/// </summary>
public class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = string.Empty;
    public string IngestionTopic { get; set; } = "pvpanalytics.ingestion.matches";
    public string IngestionDeadLetterTopic { get; set; } = "pvpanalytics.ingestion.matches.dlq";
    /// <summary>Consumer group id (Worker only).</summary>
    public string GroupId { get; set; } = "pvpanalytics-ingestion-consumer";
}
