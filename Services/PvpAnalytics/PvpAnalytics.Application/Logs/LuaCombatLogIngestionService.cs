using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using PvpAnalytics.Application.Services;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Core.Entities;
using PvpAnalytics.Core.Enum;
using PvpAnalytics.Core.Logs;
using PvpAnalytics.Core.Models;
using PvpAnalytics.Core.Repositories;
using PvpAnalytics.Shared.Constants;

namespace PvpAnalytics.Application.Logs;

/// <summary>
/// Ingests combat log files in Lua table format.
/// </summary>
public class LuaCombatLogIngestionService(
    IRepository<Player> playerRepo,
    IRepository<Match> matchRepo,
    IRepository<MatchResult> resultRepo,
    IRepository<CombatLogEntry> entryRepo,
    IWowApiService wowApiService,
    ILogger<LuaCombatLogIngestionService> logger)
    : ICombatLogIngestionService
{
    private readonly PlayerCache _playerCache = new(playerRepo);
    private readonly Dictionary<string, string> _playerRegions = new(StringComparer.OrdinalIgnoreCase);

    public async Task<List<Match>> IngestAsync(Stream fileStream, CancellationToken ct = default)
    {
        logger.LogInformation("Lua combat log ingestion started.");

        var luaMatches = LuaTableParser.Parse(fileStream);

        // Process root players data if available
        List<LuaPlayerData>? rootPlayersData = null;
        if (luaMatches.Count > 0 && luaMatches[0].Players.Count > 0)
        {
            rootPlayersData = luaMatches[0].Players;
            await ProcessRootPlayersAsync(rootPlayersData, ct);
            await _playerCache.BatchPersistAsync(ct);
        }

        var allPersistedMatches = new List<Match>();

        foreach (var luaMatch in luaMatches)
        {
            try
            {
                var persistedMatch = await ProcessLuaMatchAsync(luaMatch, ct);
                if (persistedMatch is { Id: > 0 })
                {
                    allPersistedMatches.Add(persistedMatch);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process Lua match with zone {Zone}, start {StartTime}",
                    luaMatch.Zone, luaMatch.StartTime);
            }
        }

        var pendingPlayerNames = _playerCache.GetPendingCreates().Keys.ToList();
        await _playerCache.BatchPersistAsync(ct);

        // Apply root player data to newly created players
        if (rootPlayersData != null)
        {
            await ApplyRootPlayerDataToNewPlayersAsync(rootPlayersData, ct);
            await _playerCache.BatchPersistAsync(ct);
        }

        await EnrichPlayersWithWowApiAsync(pendingPlayerNames, ct);

        await _playerCache.BatchPersistAsync(ct);

        logger.LogInformation("Lua combat log ingestion completed. Persisted {MatchCount} match(es).",
            allPersistedMatches.Count);
        return allPersistedMatches;
    }

    private async Task<Match?> ProcessLuaMatchAsync(LuaMatchData luaMatch, CancellationToken ct)
    {
        if (!TryParseTimestamps(luaMatch, out var startTime, out var endTime))
        {
            return null;
        }

        var arenaZone = MapZoneNameToArenaZone(luaMatch.Zone);
        var gameMode = ParseGameMode(luaMatch.Mode);
        var matchState = new LuaMatchProcessingState();

        CollectPlayersForLookup(luaMatch.Logs, startTime);
        await _playerCache.BatchLookupAsync(ct);
        ProcessCombatEvents(luaMatch.Logs, startTime, matchState);
        await UpdatePlayersFromSpellsAsync(matchState.PlayersByKey, matchState.PlayerSpells);

        var arenaMatchId = GenerateArenaMatchId(luaMatch, startTime, endTime);
        var mapName = ArenaZoneIds.GetDisplayName(arenaZone);
        var context = new MatchIngestionContext(arenaZone,
            startTime,
            endTime,
            matchState.Participants,
            matchState.BufferedEntries,
            matchState.PlayersByKey,
            matchState.PlayerSpells,
            gameMode,
            arenaMatchId,
            mapName);

        return await FinalizeAndPersistAsync(context, ct);
    }

    private bool TryParseTimestamps(LuaMatchData luaMatch, out DateTime startTime, out DateTime endTime)
    {
        if (TryParseDateTime(luaMatch.StartTime, out startTime) &&
            TryParseDateTime(luaMatch.EndTime, out endTime))
        {
            return true;
        }

        logger.LogWarning("Failed to parse timestamps for match. Start: {Start}, End: {End}",
            luaMatch.StartTime, luaMatch.EndTime);
        endTime = DateTime.MinValue;
        return false;
    }

    private void CollectPlayersForLookup(List<string> logLines, DateTime startTime)
    {
        foreach (var parsed in logLines
                     .Select(logLine => SimplifiedLogParser.ParseLine(logLine, startTime))
                     .OfType<ParsedCombatLogEvent>())
        {
            AddPlayerToPendingIfValid(parsed.SourceName);
            AddPlayerToPendingIfValid(parsed.TargetName);
        }
    }

    private void AddPlayerToPendingIfValid(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName))
            return;

        var (playerName, realm) = PlayerInfoExtractor.ParsePlayerName(fullName);
        if (string.IsNullOrEmpty(playerName) || string.IsNullOrWhiteSpace(realm))
        {
            return;
        }

        _playerCache.GetOrAddPending(playerName, realm);
        var region = ExtractRegion(fullName);
        if (!string.IsNullOrEmpty(region))
        {
            _playerRegions[playerName] = region;
        }
    }

    private void ProcessCombatEvents(List<string> logLines, DateTime startTime, LuaMatchProcessingState state)
    {
        foreach (var logLine in logLines)
        {
            var parsed = SimplifiedLogParser.ParseLine(logLine, startTime);
            if (parsed == null) continue;

            ProcessParsedEvent(parsed, state.PlayersByKey, state.Participants, state.BufferedEntries,
                state.PlayerSpells);
        }
    }

    private sealed class LuaMatchProcessingState
    {
        public Dictionary<string, Player> PlayersByKey { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Participants { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<CombatLogEntry> BufferedEntries { get; } = new();
        public Dictionary<string, HashSet<string>> PlayerSpells { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private void ProcessParsedEvent(
        ParsedCombatLogEvent parsed,
        Dictionary<string, Player> playersByKey,
        HashSet<string> participants,
        List<CombatLogEntry> bufferedEntries,
        Dictionary<string, HashSet<string>> playerSpells)
    {
        ProcessPlayer(parsed.SourceName, parsed.SpellName, playersByKey, participants, playerSpells);
        ProcessPlayer(parsed.TargetName, null, playersByKey, participants, playerSpells);
        CreateCombatLogEntryIfValid(parsed, playersByKey, bufferedEntries);
    }

    private void ProcessPlayer(
        string? fullName,
        string? spellName,
        Dictionary<string, Player> playersByKey,
        HashSet<string> participants,
        Dictionary<string, HashSet<string>> playerSpells)
    {
        if (string.IsNullOrEmpty(fullName))
            return;

        var (playerName, realm) = PlayerInfoExtractor.ParsePlayerName(fullName);
        if (string.IsNullOrEmpty(playerName))
        {
            return;
        }

        var cached = _playerCache.GetCached(playerName);
        if (cached != null)
        {
            playersByKey.TryAdd(playerName, cached);
            participants.Add(playerName);
        }
        else if (!string.IsNullOrWhiteSpace(realm))
        {
            AddPlayerToPending(playerName, realm, fullName);
            participants.Add(playerName);
        }

        if (!string.IsNullOrEmpty(spellName))
        {
            TrackSpellForPlayer(playerName, spellName, playerSpells);
        }
    }

    private void AddPlayerToPending(string playerName, string realm, string fullName)
    {
        _playerCache.GetOrAddPending(playerName, realm);
        var region = ExtractRegion(fullName);
        if (!string.IsNullOrEmpty(region))
        {
            _playerRegions[playerName] = region;
        }
    }

    private static void TrackSpellForPlayer(
        string playerName,
        string spellName,
        Dictionary<string, HashSet<string>> playerSpells)
    {
        if (!playerSpells.TryGetValue(playerName, out var spells))
        {
            spells = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            playerSpells[playerName] = spells;
        }

        spells.Add(spellName);
    }

    private static void CreateCombatLogEntryIfValid(
        ParsedCombatLogEvent parsed,
        Dictionary<string, Player> playersByKey,
        List<CombatLogEntry> bufferedEntries)
    {
        if (string.IsNullOrEmpty(parsed.SourceName))
            return;

        var (sourceName, _) = PlayerInfoExtractor.ParsePlayerName(parsed.SourceName);
        if (string.IsNullOrEmpty(sourceName) || !playersByKey.TryGetValue(sourceName, out var source))
            return;

        if (source.Id <= 0)
            return;

        var target = GetTargetPlayer(parsed.TargetName, playersByKey);
        bufferedEntries.Add(new CombatLogEntry
        {
            Timestamp = parsed.Timestamp,
            SourcePlayerId = source.Id,
            TargetPlayerId = target?.Id,
            Ability = parsed.SpellName ?? parsed.EventType,
            DamageDone = parsed.Damage ?? 0,
            HealingDone = parsed.Healing ?? 0,
            CrowdControl = string.Empty,
            SourcePlayer = source,
        });
    }

    private static Player? GetTargetPlayer(string? targetName, Dictionary<string, Player> playersByKey)
    {
        if (string.IsNullOrEmpty(targetName))
            return null;

        var (targetPlayerName, _) = PlayerInfoExtractor.ParsePlayerName(targetName);
        return !string.IsNullOrEmpty(targetPlayerName) && playersByKey.TryGetValue(targetPlayerName, out var target)
            ? target
            : null;
    }

    private static bool TryParseDateTime(string? dateTimeStr, out DateTime dateTime)
    {
        dateTime = DateTime.MinValue;
        if (string.IsNullOrWhiteSpace(dateTimeStr))
            return false;

        // Try format: "2025-11-20 00:33:57"
        if (DateTime.TryParseExact(dateTimeStr, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal, out dateTime))
        {
            return true;
        }

        // Fallback to general parsing
        return DateTime.TryParse(dateTimeStr, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal, out dateTime);
    }

    private static ArenaZone MapZoneNameToArenaZone(string? zoneName)
    {
        if (string.IsNullOrWhiteSpace(zoneName))
            return ArenaZone.Unknown;

        // Map zone names to ArenaZone enum
        return zoneName.ToLowerInvariant() switch
        {
            "dornogal" => ArenaZone.MaldraxxusColiseum, // Dornogal is Maldraxxus Coliseum
            "blood ring" => ArenaZone.BloodRing,
            "dalaran arena" => ArenaZone.DalaranArena,
            "ring of valor" => ArenaZone.RingOfValor,
            "ruins of lordaeron" => ArenaZone.RuinsOfLordaeron,
            "nagrand arena" => ArenaZone.NagrandArena,
            "mugambala" => ArenaZone.Mugambala,
            "the tiger's peak" => ArenaZone.TheTigersPeak,
            "tol'viron arena" => ArenaZone.TolvironArena,
            "black rook hold arena" => ArenaZone.BlackRookHoldArena,
            "maldraxxus coliseum" => ArenaZone.MaldraxxusColiseum,
            _ => ArenaZone.Unknown
        };
    }

    private static GameMode ParseGameMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return GameMode.TwoVsTwo;

        return mode.ToLowerInvariant() switch
        {
            "2v2" => GameMode.TwoVsTwo,
            "3v3" => GameMode.ThreeVsThree,
            "skirmish" => GameMode.Skirmish,
            "rbg" => GameMode.Rbg,
            "shuffle" => GameMode.Shuffle,
            _ => GameMode.TwoVsTwo
        };
    }

    private static string GenerateArenaMatchId(LuaMatchData luaMatch, DateTime startTime, DateTime endTime)
    {
        // Generate a unique match ID based on zone, timestamps, and mode
        var baseStr = $"{luaMatch.Zone}_{startTime:yyyyMMddHHmmss}_{endTime:yyyyMMddHHmmss}_{luaMatch.Mode}";
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(baseStr)))[..16];
    }

    private static string ExtractRegion(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return string.Empty;

        var trimmed = fullName.Trim('"', ' ');
        var regionSuffixes = new[] { "-EU", "-US", "-KR", "-TW", "-CN" };

        var suffix = regionSuffixes.FirstOrDefault(s =>
            trimmed.EndsWith(s, StringComparison.OrdinalIgnoreCase));

        return !string.IsNullOrEmpty(suffix) ? suffix[1..].ToLowerInvariant() : "eu"; // Default to EU
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

        if (string.IsNullOrWhiteSpace(player.Faction) && !string.IsNullOrWhiteSpace(apiData.Faction))
        {
            player.Faction = apiData.Faction;
            needsUpdate = true;
        }

        return needsUpdate;
    }

    private async Task<Match> FinalizeAndPersistAsync(MatchIngestionContext context, CancellationToken ct)
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
            MapName = mapName,
            ArenaZone = arenaZone,
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

    /// <summary>
    /// Processes player data from the root players table in the new format.
    /// Creates or updates Player entities with the available information.
    /// </summary>
    private async Task ProcessRootPlayersAsync(List<LuaPlayerData> rootPlayers, CancellationToken ct)
    {
        // First, ensure we have pending players added to the cache
        foreach (var luaPlayer in rootPlayers.Where(IsValidPlayerData))
        {
            _playerCache.GetOrAddPending(luaPlayer.Name!, luaPlayer.Realm!);
        }

        // Do a batch lookup to get existing players
        await _playerCache.BatchLookupAsync(ct);

        // Now process each player
        foreach (var luaPlayer in rootPlayers)
        {
            if (!IsValidPlayerData(luaPlayer))
            {
                logger.LogDebug("Skipping player with missing name or realm. GUID: {Guid}", luaPlayer.PlayerGuid);
                continue;
            }

            try
            {
                ProcessSingleRootPlayer(luaPlayer);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to process root player data for GUID {Guid}, Name: {Name}",
                    luaPlayer.PlayerGuid, luaPlayer.Name);
            }
        }
    }

    private static bool IsValidPlayerData(LuaPlayerData luaPlayer)
    {
        return !string.IsNullOrWhiteSpace(luaPlayer.Name) && !string.IsNullOrWhiteSpace(luaPlayer.Realm);
    }

    private void ProcessSingleRootPlayer(LuaPlayerData luaPlayer)
    {
        var existingPlayer = _playerCache.GetCached(luaPlayer.Name!);

        if (existingPlayer != null)
        {
            UpdateExistingPlayerFromRootData(existingPlayer, luaPlayer);
        }
        else
        {
            logger.LogDebug(
                "Player {PlayerName}-{Realm} will be created. Root data available: Class: {Class}, Faction: {Faction}",
                luaPlayer.Name, luaPlayer.Realm, luaPlayer.Class, luaPlayer.Faction);
        }
    }

    private void UpdateExistingPlayerFromRootData(Player existingPlayer, LuaPlayerData luaPlayer)
    {
        var originalClass = existingPlayer.Class;
        var originalFaction = existingPlayer.Faction;
        var originalSpec = existingPlayer.Spec;

        UpdatePlayerFromLuaData(existingPlayer, luaPlayer);

        if (HasPlayerDataChanged(existingPlayer, originalClass, originalFaction, originalSpec))
        {
            _playerCache.MarkForUpdate(existingPlayer);
            logger.LogDebug(
                "Updated existing player {PlayerName}-{Realm} from root data. Class: {Class}, Faction: {Faction}",
                luaPlayer.Name, luaPlayer.Realm, luaPlayer.Class, luaPlayer.Faction);
        }
    }

    private static bool HasPlayerDataChanged(Player player, string originalClass, string originalFaction,
        string originalSpec)
    {
        return player.Class != originalClass ||
               player.Faction != originalFaction ||
               player.Spec != originalSpec;
    }

    /// <summary>
    /// Applies root player data to newly created players after they've been persisted.
    /// </summary>
    private async Task ApplyRootPlayerDataToNewPlayersAsync(List<LuaPlayerData> rootPlayers, CancellationToken ct)
    {
        // Do another lookup to get any newly created players
        await _playerCache.BatchLookupAsync(ct);

        foreach (var luaPlayer in rootPlayers)
        {
            if (string.IsNullOrWhiteSpace(luaPlayer.Name))
                continue;

            var player = _playerCache.GetCached(luaPlayer.Name);
            if (player is not { Id: > 0 })
                continue;

            // Only update if player still has empty fields
            if (string.IsNullOrWhiteSpace(player.Class) ||
                string.IsNullOrWhiteSpace(player.Faction) ||
                string.IsNullOrWhiteSpace(player.Spec))
            {
                var originalClass = player.Class;
                var originalFaction = player.Faction;
                var originalSpec = player.Spec;

                UpdatePlayerFromLuaData(player, luaPlayer);

                if (player.Class != originalClass ||
                    player.Faction != originalFaction ||
                    player.Spec != originalSpec)
                {
                    _playerCache.MarkForUpdate(player);
                    logger.LogDebug(
                        "Applied root player data to newly created player {PlayerName}-{Realm}. Class: {Class}, Faction: {Faction}",
                        luaPlayer.Name, luaPlayer.Realm, luaPlayer.Class, luaPlayer.Faction);
                }
            }
        }
    }

    /// <summary>
    /// Maps a WoW specialization ID to its human-readable name.
    /// </summary>
    private static string MapSpecIdToName(int specId)
    {
        // Map SpecId to WoWSpecialization enum, then to AppConstants.WoWSpec name
        return specId switch
        {
            // Death Knight
            (int)WoWSpecialization.Blood => AppConstants.WoWSpec.DeathKnight.Blood,
            (int)WoWSpecialization.FrostDK => AppConstants.WoWSpec.DeathKnight.Frost,
            (int)WoWSpecialization.Unholy => AppConstants.WoWSpec.DeathKnight.Unholy,

            // Demon Hunter
            (int)WoWSpecialization.Havoc => AppConstants.WoWSpec.DemonHunter.Havoc,
            (int)WoWSpecialization.Vengeance => AppConstants.WoWSpec.DemonHunter.Vengeance,

            // Druid
            (int)WoWSpecialization.Balance => AppConstants.WoWSpec.Druid.Balance,
            (int)WoWSpecialization.Feral => AppConstants.WoWSpec.Druid.Feral,
            (int)WoWSpecialization.Guardian => AppConstants.WoWSpec.Druid.Guardian,
            (int)WoWSpecialization.RestorationDruid => AppConstants.WoWSpec.Druid.Restoration,

            // Evoker
            (int)WoWSpecialization.Devastation => AppConstants.WoWSpec.Evoker.Devastation,
            (int)WoWSpecialization.Preservation => AppConstants.WoWSpec.Evoker.Preservation,
            (int)WoWSpecialization.Augmentation => AppConstants.WoWSpec.Evoker.Augmentation,

            // Hunter
            (int)WoWSpecialization.BeastMastery => AppConstants.WoWSpec.Hunter.BeastMastery,
            (int)WoWSpecialization.Marksmanship => AppConstants.WoWSpec.Hunter.Marksmanship,
            (int)WoWSpecialization.Survival => AppConstants.WoWSpec.Hunter.Survival,

            // Mage
            (int)WoWSpecialization.Arcane => AppConstants.WoWSpec.Mage.Arcane,
            (int)WoWSpecialization.Fire => AppConstants.WoWSpec.Mage.Fire,
            (int)WoWSpecialization.FrostMage => AppConstants.WoWSpec.Mage.Frost,

            // Monk
            (int)WoWSpecialization.Brewmaster => AppConstants.WoWSpec.Monk.Brewmaster,
            (int)WoWSpecialization.Mistweaver => AppConstants.WoWSpec.Monk.Mistweaver,
            (int)WoWSpecialization.Windwalker => AppConstants.WoWSpec.Monk.Windwalker,

            // Paladin
            (int)WoWSpecialization.HolyPaladin => AppConstants.WoWSpec.Paladin.Holy,
            (int)WoWSpecialization.ProtectionPaladin => AppConstants.WoWSpec.Paladin.Protection,
            (int)WoWSpecialization.Retribution => AppConstants.WoWSpec.Paladin.Retribution,

            // Priest
            (int)WoWSpecialization.Discipline => AppConstants.WoWSpec.Priest.Discipline,
            (int)WoWSpecialization.HolyPriest => AppConstants.WoWSpec.Priest.Holy,
            (int)WoWSpecialization.Shadow => AppConstants.WoWSpec.Priest.Shadow,

            // Rogue
            (int)WoWSpecialization.Assassination => AppConstants.WoWSpec.Rogue.Assassination,
            (int)WoWSpecialization.Outlaw => AppConstants.WoWSpec.Rogue.Outlaw,
            (int)WoWSpecialization.Subtlety => AppConstants.WoWSpec.Rogue.Subtlety,

            // Shaman
            (int)WoWSpecialization.Elemental => AppConstants.WoWSpec.Shaman.Elemental,
            (int)WoWSpecialization.Enhancement => AppConstants.WoWSpec.Shaman.Enhancement,
            (int)WoWSpecialization.RestorationShaman => AppConstants.WoWSpec.Shaman.Restoration,

            // Warlock
            (int)WoWSpecialization.Affliction => AppConstants.WoWSpec.Warlock.Affliction,
            (int)WoWSpecialization.Demonology => AppConstants.WoWSpec.Warlock.Demonology,
            (int)WoWSpecialization.Destruction => AppConstants.WoWSpec.Warlock.Destruction,

            // Warrior
            (int)WoWSpecialization.Arms => AppConstants.WoWSpec.Warrior.Arms,
            (int)WoWSpecialization.Fury => AppConstants.WoWSpec.Warrior.Fury,
            (int)WoWSpecialization.ProtectionWarrior => AppConstants.WoWSpec.Warrior.Protection,

            _ => string.Empty
        };
    }

    /// <summary>
    /// Updates a Player entity with data from LuaPlayerData, only setting fields that are not already populated.
    /// </summary>
    private static void UpdatePlayerFromLuaData(Player player, LuaPlayerData luaPlayer)
    {
        // Only update if the field is empty/null in the player entity
        if (string.IsNullOrWhiteSpace(player.Class) && !string.IsNullOrWhiteSpace(luaPlayer.Class))
        {
            player.Class = luaPlayer.Class;
        }
        else if (string.IsNullOrWhiteSpace(player.Class) && !string.IsNullOrWhiteSpace(luaPlayer.ClassId))
        {
            // Fallback to ClassId if Class is not available
            player.Class = luaPlayer.ClassId;
        }

        if (string.IsNullOrWhiteSpace(player.Faction) && !string.IsNullOrWhiteSpace(luaPlayer.Faction))
        {
            player.Faction = luaPlayer.Faction;
        }

        if (string.IsNullOrWhiteSpace(player.Spec) && luaPlayer.SpecId.HasValue)
        {
            // Convert specId to human-readable spec name
            var specName = MapSpecIdToName(luaPlayer.SpecId.Value);
            if (!string.IsNullOrWhiteSpace(specName))
            {
                player.Spec = specName;
            }
        }
    }
}