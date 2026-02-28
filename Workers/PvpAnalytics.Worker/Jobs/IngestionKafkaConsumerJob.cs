using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PvpAnalytics.Application.Logs;
using PvpAnalytics.Core.Configuration;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Core.Entities;
using PvpAnalytics.Core.Enum;
using PvpAnalytics.Core.Repositories;
using Google.Protobuf.WellKnownTypes;
using PvpAnalytics.Shared.Protos.Ingestion;

namespace PvpAnalytics.Worker.Jobs;

public sealed class IngestionKafkaConsumerJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _kafkaOptions;
    private readonly IngestionOptions _ingestionOptions;
    private readonly ILogger<IngestionKafkaConsumerJob> _logger;

    public IngestionKafkaConsumerJob(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> kafkaOptions,
        IOptions<IngestionOptions> ingestionOptions,
        ILogger<IngestionKafkaConsumerJob> logger)
    {
        _scopeFactory = scopeFactory;
        _kafkaOptions = kafkaOptions.Value;
        _ingestionOptions = ingestionOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_ingestionOptions.StreamingConsumerEnabled || string.IsNullOrWhiteSpace(_kafkaOptions.BootstrapServers))
        {
            _logger.LogInformation("Streaming ingestion consumer is disabled or Kafka not configured. Exiting.");
            return;
        }

        var config = new ConsumerConfig
        {
            BootstrapServers = _kafkaOptions.BootstrapServers,
            GroupId = _kafkaOptions.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(_kafkaOptions.IngestionTopic);

        _logger.LogInformation("Ingestion Kafka consumer started. Topic: {Topic}", _kafkaOptions.IngestionTopic);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(TimeSpan.FromSeconds(5));
                if (result?.Message?.Value == null)
                    continue;

                await ProcessMessageAsync(consumer, result, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                _logger.LogWarning(ex, "Kafka consume error");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in ingestion consumer");
            }
        }

        consumer.Close();
    }

    private async Task ProcessMessageAsync(IConsumer<string, byte[]> consumer, ConsumeResult<string, byte[]> result, CancellationToken ct)
    {
        MatchPayload payload;
        try
        {
            payload = MatchPayload.Parser.ParseFrom(result.Message.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize match payload. Key: {Key}", result.Message.Key);
            consumer.Commit(result);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var matchPersist = scope.ServiceProvider.GetRequiredService<IMatchPersistService>();
        var playerRepo = scope.ServiceProvider.GetRequiredService<IRepository<Player>>();

        try
        {
            var context = await MapToContextAsync(payload, playerRepo, ct).ConfigureAwait(false);
            await matchPersist.PersistAsync(context, payload.MatchDedupKey, ct).ConfigureAwait(false);
            consumer.Commit(result);
            _logger.LogDebug("Persisted match from stream. DedupKey: {Key}", payload.MatchDedupKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist match from stream. DedupKey: {Key}", payload.MatchDedupKey);
            // Retry: do not commit; message will be redelivered. Optionally send to DLQ after N failures (future).
        }
    }

    private static async Task<MatchIngestionContext> MapToContextAsync(
        MatchPayload payload,
        IRepository<Player> playerRepo,
        CancellationToken ct)
    {
        var participants = payload.ParticipantKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allNames = new HashSet<string>(participants, StringComparer.OrdinalIgnoreCase);
        foreach (var e in payload.Entries)
        {
            if (!string.IsNullOrEmpty(e.SourceName)) allNames.Add(e.SourceName);
            if (!string.IsNullOrEmpty(e.TargetName)) allNames.Add(e.TargetName);
        }

        var playersByKey = new Dictionary<string, Player>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in allNames)
        {
            var existing = await playerRepo.ListAsync(p => p.Name == name, ct).ConfigureAwait(false);
            var player = existing.Count > 0 ? existing[0] : new Player { Name = name };
            playersByKey[name] = player;
        }

        var entries = new List<CombatLogEntry>();
        foreach (var e in payload.Entries)
        {
            var sourcePlayer = string.IsNullOrEmpty(e.SourceName) ? null : playersByKey.GetValueOrDefault(e.SourceName);
            var targetPlayer = string.IsNullOrEmpty(e.TargetName) ? null : playersByKey.GetValueOrDefault(e.TargetName);
            entries.Add(new CombatLogEntry
            {
                Timestamp = e.Timestamp?.ToDateTime() ?? DateTime.UtcNow,
                SourcePlayer = sourcePlayer ?? new Player { Name = e.SourceName ?? string.Empty },
                TargetPlayer = targetPlayer ?? new Player { Name = e.TargetName ?? string.Empty },
                Ability = e.Ability ?? string.Empty,
                DamageDone = e.DamageDone,
                HealingDone = e.HealingDone,
                EffectiveDamage = e.EffectiveDamage,
                EffectiveHealing = e.EffectiveHealing,
                CrowdControl = e.CrowdControl ?? string.Empty
            });
        }

        var startUtc = payload.StartUtc?.ToDateTime();
        var endUtc = payload.EndUtc?.ToDateTime();
        var arenaZone = payload.ArenaZone >= 0 ? (ArenaZone)payload.ArenaZone : ArenaZone.Unknown;
        var gameMode = payload.GameMode >= 0 && payload.GameMode <= 4 ? (GameMode)payload.GameMode : GameMode.TwoVsTwo;

        return new MatchIngestionContext(
            arenaZone,
            startUtc,
            endUtc,
            participants,
            entries,
            playersByKey,
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase),
            gameMode,
            payload.ArenaMatchId ?? string.Empty,
            payload.MapName ?? string.Empty);
    }
}
