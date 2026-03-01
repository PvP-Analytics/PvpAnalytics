using System.Text;
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
    private const int MaxPersistRetries = 3;
    private const int PersistRetryDelaySeconds = 2;
    private const int MaxDlqSendRetries = 3;

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

        using var dlqProducer = new ProducerBuilder<string, byte[]>(new ProducerConfig
        {
            BootstrapServers = _kafkaOptions.BootstrapServers
        }).Build();

        _logger.LogInformation("Ingestion Kafka consumer started. Topic: {Topic}", _kafkaOptions.IngestionTopic);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(TimeSpan.FromSeconds(5));
                if (result?.Message?.Value == null)
                    continue;

                await ProcessMessageAsync(consumer, result, dlqProducer, stoppingToken).ConfigureAwait(false);
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

    private async Task ProcessMessageAsync(
        IConsumer<string, byte[]> consumer,
        ConsumeResult<string, byte[]> result,
        IProducer<string, byte[]> dlqProducer,
        CancellationToken ct)
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

        Exception? lastException = null;
        for (var attempt = 1; attempt <= MaxPersistRetries; attempt++)
        {
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var matchPersist = scope.ServiceProvider.GetRequiredService<IMatchPersistService>();
                    var playerRepo = scope.ServiceProvider.GetRequiredService<IRepository<Player>>();
                    var context = await MapToContextAsync(payload, playerRepo, ct).ConfigureAwait(false);
                    await matchPersist.PersistAsync(context, payload.MatchDedupKey, ct).ConfigureAwait(false);
                }
                consumer.Commit(result);
                _logger.LogDebug("Persisted match from stream. DedupKey: {Key}", payload.MatchDedupKey);
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Persist attempt {Attempt}/{Max} failed for DedupKey {Key}",
                    attempt, MaxPersistRetries, payload.MatchDedupKey);
                if (attempt < MaxPersistRetries)
                    await Task.Delay(TimeSpan.FromSeconds(PersistRetryDelaySeconds), ct).ConfigureAwait(false);
            }
        }

        _logger.LogError(lastException, "Failed to persist match from stream after {Attempts} attempts. DedupKey: {Key}. Sending to DLQ.",
            MaxPersistRetries, payload.MatchDedupKey);
        await SendToDlqAndCommitAsync(consumer, result, payload, lastException!, dlqProducer, ct).ConfigureAwait(false);
    }

    private async Task SendToDlqAndCommitAsync(
        IConsumer<string, byte[]> consumer,
        ConsumeResult<string, byte[]> result,
        MatchPayload payload,
        Exception failure,
        IProducer<string, byte[]> dlqProducer,
        CancellationToken ct)
    {
        var headers = new Headers();
        if (result.Message.Headers != null)
        {
            foreach (var h in result.Message.Headers)
                headers.Add(h.Key, h.GetValueBytes());
        }
        headers.Add("dlq-error", Encoding.UTF8.GetBytes(failure.Message));
        headers.Add("dlq-timestamp", Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O")));
        headers.Add("dlq-dedup-key", Encoding.UTF8.GetBytes(payload.MatchDedupKey ?? string.Empty));

        var dlqMessage = new Message<string, byte[]>
        {
            Key = result.Message.Key,
            Value = result.Message.Value,
            Headers = headers
        };

        Exception? lastDlqException = null;
        for (var attempt = 1; attempt <= MaxDlqSendRetries; attempt++)
        {
            try
            {
                await dlqProducer.ProduceAsync(_kafkaOptions.IngestionDeadLetterTopic, dlqMessage, ct).ConfigureAwait(false);
                consumer.Commit(result);
                _logger.LogInformation("Sent message to DLQ and committed offset. DedupKey: {Key}", payload.MatchDedupKey);
                return;
            }
            catch (Exception ex)
            {
                lastDlqException = ex;
                _logger.LogWarning(ex, "DLQ produce attempt {Attempt}/{Max} failed for DedupKey {Key}",
                    attempt, MaxDlqSendRetries, payload.MatchDedupKey);
                if (attempt < MaxDlqSendRetries)
                    await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }
        }

        _logger.LogError(lastDlqException, "DLQ send failed after {Attempts} attempts for DedupKey {Key}. Offset not committed; message will redeliver.",
            MaxDlqSendRetries, payload.MatchDedupKey);
        throw lastDlqException!;
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
        if (allNames.Count > 0)
        {
            var existingPlayers = await playerRepo.ListAsync(p => allNames.Contains(p.Name), ct).ConfigureAwait(false);
            foreach (var player in existingPlayers)
                playersByKey[player.Name] = player;
            foreach (var name in allNames)
            {
                if (!playersByKey.ContainsKey(name))
                    playersByKey[name] = new Player { Name = name };
            }
        }

        var entries = new List<CombatLogEntry>();
        foreach (var e in payload.Entries)
        {
            Player? sourcePlayer = string.IsNullOrEmpty(e.SourceName) ? null : playersByKey.GetValueOrDefault(e.SourceName);
            if (sourcePlayer == null && !string.IsNullOrEmpty(e.SourceName))
                sourcePlayer = new Player { Name = e.SourceName! };

            Player? targetPlayer = string.IsNullOrEmpty(e.TargetName) ? null : playersByKey.GetValueOrDefault(e.TargetName);
            if (targetPlayer == null && !string.IsNullOrEmpty(e.TargetName))
                targetPlayer = new Player { Name = e.TargetName! };

            if (sourcePlayer == null || targetPlayer == null)
                continue;

            entries.Add(new CombatLogEntry
            {
                Timestamp = e.Timestamp?.ToDateTime() ?? DateTime.UtcNow,
                SourcePlayer = sourcePlayer,
                TargetPlayer = targetPlayer,
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
