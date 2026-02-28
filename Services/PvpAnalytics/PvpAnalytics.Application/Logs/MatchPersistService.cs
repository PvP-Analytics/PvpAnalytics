using System.Text;
using Microsoft.Extensions.Logging;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Core.Entities;
using PvpAnalytics.Core.Enum;
using PvpAnalytics.Core.Repositories;

namespace PvpAnalytics.Application.Logs;

public sealed class MatchPersistService(
    IRepository<Match> matchRepo,
    IRepository<MatchResult> resultRepo,
    IRepository<CombatLogEntry> entryRepo,
    ILogger<MatchPersistService> logger) : IMatchPersistService
{
    public async Task<Match> PersistAsync(MatchIngestionContext context, string? dedupKeyOverride, CancellationToken ct = default)
    {
        var uniqueHash = dedupKeyOverride ?? ComputeMatchHash(context.Participants, context.Start, context.End, context.ArenaMatchId);
        var match = CreateMatchEntity(context.ArenaZone, context.Start, context.End, context.ArenaMatchId,
            context.GameMode, context.MapName, uniqueHash);

        match = await PersistMatchWithDuplicateHandlingAsync(match, uniqueHash, context.Participants.Count, ct).ConfigureAwait(false);
        await PersistCombatLogEntriesAsync(context.Entries, match.Id, ct).ConfigureAwait(false);
        await PersistMatchResultsAsync(context.Participants, context.PlayersByKey, context.PlayerSpells, match.Id, ct).ConfigureAwait(false);

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

    private async Task<Match> PersistMatchWithDuplicateHandlingAsync(Match match, string uniqueHash, int participantCount, CancellationToken ct)
    {
        try
        {
            match = await matchRepo.AddAsync(match, true, ct).ConfigureAwait(false);
            logger.LogInformation(
                "Persisted new match {MatchId} with UniqueHash {UniqueHash} and {ParticipantCount} participants.",
                match.Id, uniqueHash, participantCount);
            return match;
        }
        catch (Exception ex)
        {
            if (IsUniqueConstraintViolation(ex))
                return await HandleDuplicateMatchAsync(uniqueHash, ct).ConfigureAwait(false);
            throw;
        }
    }

    private static bool IsUniqueConstraintViolation(Exception ex)
    {
        return ex.GetType().FullName?.Contains("DbUpdateException") == true &&
               ex.InnerException?.GetType().FullName?.Contains("PostgresException") == true &&
               (ex.InnerException.GetType().GetProperty("SqlState")?.GetValue(ex.InnerException)?.ToString() == "23505" ||
                ex.Message.Contains("23505") ||
                ex.Message.Contains("duplicate key value") ||
                ex.Message.Contains("IX_Matches_UniqueHash"));
    }

    private async Task<Match> HandleDuplicateMatchAsync(string uniqueHash, CancellationToken ct)
    {
        logger.LogInformation(
            "Match with UniqueHash {UniqueHash} already exists in database, returning existing match.", uniqueHash);
        var existingMatches = await matchRepo.ListAsync(m => m.UniqueHash == uniqueHash, ct).ConfigureAwait(false);
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
            await entryRepo.AddAsync(e, true, ct).ConfigureAwait(false);
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
            }, true, ct).ConfigureAwait(false);
        }
    }

    private static string GetMatchSpecForPlayer(string playerName, Dictionary<string, HashSet<string>> playerSpells)
    {
        return playerSpells.TryGetValue(playerName, out var spells)
            ? PlayerInfoExtractor.DetermineSpecForMatch(spells)
            : string.Empty;
    }

    private static string ComputeMatchHash(IEnumerable<string> playerKeys, DateTime? start, DateTime? end, string? arenaMatchId = null)
    {
        var baseStr = string.Join('|', playerKeys.OrderBy(x => x)) + $"|{start:O}|{end:O}|{arenaMatchId}";
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(baseStr)));
    }
}
