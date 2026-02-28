using System.Text;
using Microsoft.Extensions.Logging;
using PvpAnalytics.Application.Services;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Core.Entities;
using PvpAnalytics.Core.Enum;
using PvpAnalytics.Core.Models;
using PvpAnalytics.Core.Repositories;
using PvpAnalytics.Core.Logs;

namespace PvpAnalytics.Application.Logs;

public class CombatLogIngestionService(
    IRepository<Player> playerRepo,
    IRepository<Match> matchRepo,
    IRepository<MatchResult> resultRepo,
    IRepository<CombatLogEntry> entryRepo,
    IWowApiService wowApiService,
    ILogger<CombatLogIngestionService> logger)
    : ICombatLogIngestionService
{
    private readonly PlayerCache _playerCache = new(playerRepo);

    private readonly Dictionary<string, string>
        _playerRegions = new(StringComparer.OrdinalIgnoreCase); // Track region per player for WoW API

    /// <summary>
    /// Ingests combat-log text from the provided stream in traditional format and persists arena matches, combat entries, players, and match results.
    /// Processes multiple matches: starts recording on ARENA_MATCH_START, stops on ZONE_CHANGE, saves all matches found.
    /// </summary>
    /// <param name="fileStream">A readable stream containing the combat log text in traditional format (UTF-8 or BOM-detected).</param>
    /// <param name="ct">Token to observe for cancellation.</param>
    /// <remarks>
    /// Matches are detected by ARENA_MATCH_START events and finalized on ZONE_CHANGE events. All matches found in the file are persisted.
    /// </remarks>
    /// <returns>List of all persisted <see cref="Match"/> entities created from the stream.</returns>
    public async Task<List<Match>> IngestAsync(Stream fileStream, CancellationToken ct = default)
    {
        logger.LogInformation("Traditional combat log ingestion started.");
        
        using var reader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);

        var allPersistedMatches = new List<Match>();
        var matchState = new MatchProcessingState();

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;

            var parsed = CombatLogParser.ParseLine(line);
            if (parsed == null) continue;

            var handled = await ProcessParsedLineAsync(parsed, matchState, allPersistedMatches, ct);
            if (handled) continue;

            if (matchState.MatchInProgress)
            {
                await ProcessCombatEventAsync(parsed, matchState);
            }
        }

        await FinalizePendingMatchAsync(matchState, allPersistedMatches, ct);
        await FinalizeIngestionAsync(ct);

        logger.LogInformation("Traditional combat log ingestion completed. Persisted {MatchCount} match(es).",
            allPersistedMatches.Count);
        return allPersistedMatches;
    }

    private async Task<bool> ProcessParsedLineAsync(
            ParsedCombatLogEvent parsed,
            MatchProcessingState state,
            List<Match> allPersistedMatches,
            CancellationToken cancellationToken)
        {
            return parsed.EventType switch
            {
                CombatLogEventTypes.ArenaMatchStart => HandleArenaMatchStart(parsed, state),
                CombatLogEventTypes.ZoneChange => await HandleZoneChangeAsync(parsed, state, allPersistedMatches,
                    cancellationToken),
                _ => false
            };
        }

    private bool HandleArenaMatchStart(ParsedCombatLogEvent parsed, MatchProcessingState state)
        {
            state.MatchStart = parsed.Timestamp;
            state.CurrentArenaMatchId = parsed.ArenaMatchId;
            state.MatchInProgress = true;
            state.ResetMatchBuffers();

            if (parsed.ZoneId.HasValue)
            {
                state.CurrentZoneId = parsed.ZoneId;
            }

            logger.LogInformation("Arena match started: {ArenaMatchId} at {Timestamp}", state.CurrentArenaMatchId,
                state.MatchStart);
            return true;
        }

    private async Task<bool> HandleZoneChangeAsync(
            ParsedCombatLogEvent parsed,
            MatchProcessingState state,
            List<Match> allPersistedMatches,
            CancellationToken cancellationToken)
        {
            if (state is { MatchInProgress: true, CurrentArenaMatchId: not null })
            {
                state.MatchEnd = parsed.Timestamp;
                var persistedMatch = await FinalizeCurrentMatchAsync(state, cancellationToken);
                if (persistedMatch.Id > 0)
                {
                    allPersistedMatches.Add(persistedMatch);
                    logger.LogInformation("Persisted match {MatchId} with arena match ID {ArenaMatchId}.",
                        persistedMatch.Id, state.CurrentArenaMatchId);
                }

                state.ResetMatchState();
            }

            if (parsed.ZoneId.HasValue)
            {
                state.CurrentZoneId = parsed.ZoneId;
            }

            return true;
        }

    private Task ProcessCombatEventAsync(ParsedCombatLogEvent parsed, MatchProcessingState state)
        {
            TrackSpellIfPresent(parsed, state);
            ProcessPlayer(parsed.SourceName, state);
            ProcessPlayer(parsed.TargetName, state);
            CreateCombatLogEntryIfValid(parsed, state);
            state.MatchEnd = parsed.Timestamp;
            return Task.CompletedTask;
        }

    private static void TrackSpellIfPresent(ParsedCombatLogEvent parsed, MatchProcessingState state)
        {
            if (string.IsNullOrEmpty(parsed.SourceName) || string.IsNullOrEmpty(parsed.SpellName))
                return;

            var (playerName, _) = PlayerInfoExtractor.ParsePlayerName(parsed.SourceName);
            if (string.IsNullOrEmpty(playerName))
                return;

            if (!state.PlayerSpells.TryGetValue(playerName, out var spells))
            {
                spells = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                state.PlayerSpells[playerName] = spells;
            }

            spells.Add(parsed.SpellName);
        }

    private void ProcessPlayer(string? fullName, MatchProcessingState state)
        {
            if (string.IsNullOrEmpty(fullName))
                return;

            var (playerName, realm) = PlayerInfoExtractor.ParsePlayerName(fullName);
            if (string.IsNullOrEmpty(playerName))
                return;

            var cached = _playerCache.GetCached(playerName);
            if (cached != null)
            {
                state.PlayersByKey.TryAdd(playerName, cached);
                state.Participants.Add(playerName);
                return;
            }

            if (string.IsNullOrWhiteSpace(realm))
                return;

            _playerCache.GetOrAddPending(playerName, realm);
            var region = ExtractRegion(fullName);
            if (!string.IsNullOrEmpty(region))
            {
                _playerRegions[playerName] = region;
            }

            state.Participants.Add(playerName);
        }

    private static void CreateCombatLogEntryIfValid(ParsedCombatLogEvent parsed, MatchProcessingState state)
        {
            var (sourceName, _) = !string.IsNullOrEmpty(parsed.SourceName)
                ? PlayerInfoExtractor.ParsePlayerName(parsed.SourceName)
                : (string.Empty, string.Empty);

            if (string.IsNullOrEmpty(sourceName) || !state.PlayersByKey.TryGetValue(sourceName, out var source))
                return;

            if (source.Id <= 0)
                return;

            var (targetName, _) = !string.IsNullOrEmpty(parsed.TargetName)
                ? PlayerInfoExtractor.ParsePlayerName(parsed.TargetName)
                : (string.Empty, string.Empty);

            var target = !string.IsNullOrEmpty(targetName) && state.PlayersByKey.TryGetValue(targetName, out var tgt)
                ? tgt
                : null;

            var damage = parsed.Damage ?? 0;
            var healing = parsed.Healing ?? 0;
            var isCooldown = ImportantAbilities.IsCooldownOrDefensive(parsed.SpellName ?? string.Empty);
            var effectiveDamage = isCooldown ? 0 : damage;
            var effectiveHealing = healing;

            state.BufferedEntries.Add(new CombatLogEntry
            {
                Timestamp = parsed.Timestamp,
                SourcePlayerId = source.Id,
                TargetPlayerId = target?.Id,
                Ability = parsed.SpellName ?? parsed.EventType,
                DamageDone = damage,
                HealingDone = healing,
                CrowdControl = string.Empty,
                SourcePlayer = source,
                EffectiveDamage = effectiveDamage,
                EffectiveHealing = effectiveHealing
            });
        }

    private async Task<Match> FinalizeCurrentMatchAsync(MatchProcessingState state, CancellationToken cancellationToken)
        {
            await _playerCache.BatchLookupAsync(cancellationToken);
            LoadCachedPlayersIntoState(state);
            await UpdatePlayersFromSpellsAsync(state.PlayersByKey, state.PlayerSpells);

            var gameMode =
                GameModeHelper.GetGameModeFromParticipantCount(state.Participants.Count, state.CurrentArenaMatchId);
            var arenaZone = state.CurrentZoneId.HasValue
                ? ArenaZoneIds.GetArenaZone(state.CurrentZoneId.Value)
                : ArenaZone.Unknown;
            var mapName = ArenaZoneIds.GetDisplayName(arenaZone);
            var context = new MatchIngestionContext(
                arenaZone, state.MatchStart, state.MatchEnd,
                state.Participants, state.BufferedEntries, state.PlayersByKey, state.PlayerSpells, gameMode,
                state.CurrentArenaMatchId!, mapName);

            return await FinalizeAndPersistAsync(context, cancellationToken);
        }

    private void LoadCachedPlayersIntoState(MatchProcessingState state)
        {
            foreach (var name in state.Participants)
            {
                var cached = _playerCache.GetCached(name);
                if (cached != null)
                {
                    state.PlayersByKey.TryAdd(name, cached);
                }
            }
        }

    private async Task FinalizePendingMatchAsync(
        MatchProcessingState state,
        List<Match> allPersistedMatches,
        CancellationToken cancellationToken)
    {
        if (!state.MatchInProgress || state.CurrentArenaMatchId == null)
            return;

        var persistedMatch = await FinalizeCurrentMatchAsync(state, cancellationToken);
        if (persistedMatch.Id > 0)
        {
            allPersistedMatches.Add(persistedMatch);
            logger.LogInformation("Persisted final match {MatchId} with arena match ID {ArenaMatchId}.",
                persistedMatch.Id, state.CurrentArenaMatchId);
        }
    }

    private async Task FinalizeIngestionAsync(CancellationToken cancellationToken)
    {
        var pendingPlayerNames = _playerCache.GetPendingCreates().Keys.ToList();
        await _playerCache.BatchPersistAsync(cancellationToken);
        await EnrichPlayersWithWowApiAsync(pendingPlayerNames, cancellationToken);
        await _playerCache.BatchPersistAsync(cancellationToken);
    }

    private sealed class MatchProcessingState
    {
        public Dictionary<string, Player> PlayersByKey { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Participants { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<CombatLogEntry> BufferedEntries { get; } = new();
        public Dictionary<string, HashSet<string>> PlayerSpells { get; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTime? MatchStart { get; set; }
        public DateTime? MatchEnd { get; set; }
        public int? CurrentZoneId { get; set; }
        public string? CurrentArenaMatchId { get; set; }
        public bool MatchInProgress { get; set; }

        public void ResetMatchBuffers()
        {
            Participants.Clear();
            BufferedEntries.Clear();
            PlayerSpells.Clear();
            PlayersByKey.Clear();
        }

        public void ResetMatchState()
        {
            ResetMatchBuffers();
            MatchStart = null;
            MatchEnd = null;
            CurrentArenaMatchId = null;
            MatchInProgress = false;
        }
    }

    private static string ExtractRegion(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return string.Empty;

        var trimmed = fullName.Trim('"', ' ');
        var regionSuffixes = new[] { "-EU", "-US", "-KR", "-TW", "-CN" };
        var suffix = regionSuffixes.FirstOrDefault(s => trimmed.EndsWith(s, StringComparison.OrdinalIgnoreCase));
        return suffix?[1..]?.ToLowerInvariant() ?? "eu"; // Default to EU
    }

    private Task UpdatePlayersFromSpellsAsync(
        Dictionary<string, Player> playersByKey,
        Dictionary<string, HashSet<string>> playerSpells)
    {
        foreach (var (playerName, spells) in playerSpells)
        {
            if (!playersByKey.TryGetValue(playerName, out var player) || spells.Count == 0)
                continue;

            var originalClass = player.Class;
            var originalFaction = player.Faction;
            var originalSpec = player.Spec;

            PlayerInfoExtractor.UpdatePlayerFromSpells(player, spells);

            if (player.Class == originalClass && player.Faction == originalFaction &&
                player.Spec == originalSpec) continue;
            _playerCache.MarkForUpdate(player);
            logger.LogDebug("Marked player {PlayerName} for update: Class={Class}, Faction={Faction}, Spec={Spec}",
                player.Name, player.Class, player.Faction, player.Spec);
        }

        return Task.CompletedTask;
    }

    private async Task EnrichPlayersWithWowApiAsync(List<string> playerNamesToEnrich, CancellationToken ct)
    {
        // Get players from cache that were just created and need enrichment
        // Note: Spec is not available from WoW API, so we only enrich for missing Class/Faction
        var playersToEnrich = GetPlayersNeedingEnrichment(playerNamesToEnrich);

        foreach (var player in playersToEnrich)
        {
            await EnrichSinglePlayerAsync(player, ct);
        }
    }

    private List<Player> GetPlayersNeedingEnrichment(List<string> playerNamesToEnrich)
    {
        return playerNamesToEnrich
            .Select(name => _playerCache.GetCached(name))
            .OfType<Player>()
            .Where(cached => string.IsNullOrWhiteSpace(cached.Class) || string.IsNullOrWhiteSpace(cached.Faction))
            .ToList();
    }

    private async Task EnrichSinglePlayerAsync(Player player, CancellationToken ct)
    {
        try
        {
            var region = _playerRegions.GetValueOrDefault(player.Name, "eu");
            var apiData = await wowApiService.GetPlayerDataAsync(player.Realm, player.Name, region, ct);

            if (apiData == null)
                return;

            var updated = UpdatePlayerFromApiData(player, apiData);
            if (updated)
            {
                _playerCache.MarkForUpdate(player);
                logger.LogDebug("Enriched player {PlayerName} from WoW API: Class={Class}, Faction={Faction}",
                    player.Name, player.Class, player.Faction);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enrich player {PlayerName} from WoW API", player.Name);
        }
    }

    private static bool UpdatePlayerFromApiData(Player player, WowPlayerData apiData)
    {
        var needsUpdate = false;

        if (string.IsNullOrWhiteSpace(player.Class) && !string.IsNullOrWhiteSpace(apiData.Class))
        {
            player.Class = apiData.Class;
            needsUpdate = true;
        }

        if (!string.IsNullOrWhiteSpace(player.Faction) || string.IsNullOrWhiteSpace(apiData.Faction))
            return needsUpdate;
        player.Faction = apiData.Faction;
        needsUpdate = true;

        return needsUpdate;
    }

    private async Task<Match> FinalizeAndPersistAsync(
        MatchIngestionContext context,
        CancellationToken ct)
    {
        var uniqueHash = ComputeMatchHash(context.Participants, context.Start, context.End, context.ArenaMatchId);
        var match = CreateMatchEntity(context.ArenaZone, context.Start, context.End, context.ArenaMatchId,
            context.GameMode, context.MapName, uniqueHash);

        match = await PersistMatchWithDuplicateHandlingAsync(match, uniqueHash, context.Participants.Count, ct);
        await PersistCombatLogEntriesAsync(context.Entries, match.Id, ct);
        await PersistMatchResultsAsync(context.Participants, context.PlayersByKey, context.PlayerSpells, match.Id, ct);

        return match;
    }

    private static Match CreateMatchEntity(
        ArenaZone arenaZone,
        DateTime? start,
        DateTime? end,
        string arenaMatchId,
        GameMode gameMode,
        string mapName,
        string uniqueHash)
    {
        return new Match
        {
            CreatedOn = start ?? DateTime.UtcNow,
            ArenaZone = arenaZone,
            MapName = mapName,
            ArenaMatchId = arenaMatchId,
            GameMode = gameMode,
            Duration = start.HasValue && end.HasValue
                ? (long)(end.Value - start.Value).TotalSeconds
                : 0,
            IsRanked = true,
            UniqueHash = uniqueHash,
        };
    }

    private async Task<Match> PersistMatchWithDuplicateHandlingAsync(
        Match match,
        string uniqueHash,
        int participantCount,
        CancellationToken ct)
    {
        try
        {
            match = await matchRepo.AddAsync(match, true, ct);
            logger.LogInformation(
                "Persisted new match {MatchId} with UniqueHash {UniqueHash} and {ParticipantCount} participants.",
                match.Id, uniqueHash, participantCount);
            return match;
        }
        catch (Exception ex)
        {
            if (IsUniqueConstraintViolation(ex))
            {
                return await HandleDuplicateMatchAsync(uniqueHash, ct);
            }

            throw;
        }
    }

    private static bool IsUniqueConstraintViolation(Exception ex)
    {
        return ex.GetType().FullName?.Contains("DbUpdateException") == true &&
               ex.InnerException?.GetType().FullName?.Contains("PostgresException") == true &&
               (ex.InnerException.GetType().GetProperty("SqlState")?.GetValue(ex.InnerException)?.ToString() ==
                "23505" ||
                ex.Message.Contains("23505") ||
                ex.Message.Contains("duplicate key value") ||
                ex.Message.Contains("IX_Matches_UniqueHash"));
    }

    private async Task<Match> HandleDuplicateMatchAsync(string uniqueHash, CancellationToken ct)
    {
        logger.LogInformation(
            "Match with UniqueHash {UniqueHash} already exists in database, returning existing match.", uniqueHash);
        var existingMatches = await matchRepo.ListAsync(m => m.UniqueHash == uniqueHash, ct);
        return existingMatches.Count > 0
            ? existingMatches[0]
            : throw new InvalidOperationException(
                $"Match with UniqueHash {uniqueHash} was reported as duplicate but not found in database.");
    }

    private async Task PersistCombatLogEntriesAsync(List<CombatLogEntry> entries, long matchId, CancellationToken ct)
    {
        foreach (var e in entries)
        {
            e.MatchId = matchId;
            await entryRepo.AddAsync(e, true, ct);
        }
    }

    private async Task PersistMatchResultsAsync(
        HashSet<string> participants,
        Dictionary<string, Player> playersByKey,
        Dictionary<string, HashSet<string>> playerSpells,
        long matchId,
        CancellationToken ct)
    {
        foreach (var name in participants)
        {
            if (!playersByKey.TryGetValue(name, out var player))
                continue;

            var matchSpec = GetMatchSpecForPlayer(name, playerSpells);
            await resultRepo.AddAsync(new MatchResult
            {
                MatchId = matchId,
                PlayerId = player.Id,
                Team = "Unknown",
                RatingBefore = 0,
                RatingAfter = 0,
                IsWinner = false,
                Spec = matchSpec,
                Player = player
            }, true, ct);
        }
    }

    private static string GetMatchSpecForPlayer(string playerName, Dictionary<string, HashSet<string>> playerSpells)
    {
        return playerSpells.TryGetValue(playerName, out var spells)
            ? PlayerInfoExtractor.DetermineSpecForMatch(spells)
            : string.Empty;
    }


    private static string ComputeMatchHash(IEnumerable<string> playerKeys, DateTime? start, DateTime? end,
        string? arenaMatchId = null)
    {
        var baseStr = string.Join('|', playerKeys.OrderBy(x => x)) + $"|{start:O}|{end:O}|{arenaMatchId}";
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(baseStr)));
    }
}