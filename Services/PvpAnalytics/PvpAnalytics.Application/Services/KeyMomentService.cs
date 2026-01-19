using Microsoft.EntityFrameworkCore;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Core.Entities;
using PvpAnalytics.Core.Logs;
using PvpAnalytics.Infrastructure;
using PvpAnalytics.Shared.Constants;

namespace PvpAnalytics.Application.Services;

public interface IKeyMomentService
{
    Task<KeyMomentDto> GetKeyMomentsForMatchAsync(long matchId, CancellationToken ct = default);
    Task<PlayerKeyMomentsDto> GetRecentKeyMomentsAsync(long playerId, int limit = 10, CancellationToken ct = default);
}

public class KeyMomentService(PvpAnalyticsDbContext dbContext) : IKeyMomentService
{
    public async Task<KeyMomentDto> GetKeyMomentsForMatchAsync(long matchId, CancellationToken ct = default)
    {
        var match = await LoadMatchWithCombatLogsAsync(matchId, ct);
        if (match == null)
            return new KeyMomentDto { MatchId = matchId };

        var dto = new KeyMomentDto
        {
            MatchId = matchId,
            MatchDate = match.CreatedOn
        };

        var combatLogs = match.CombatLogs.OrderBy(c => c.Timestamp).ToList();
        var moments = new List<KeyMoment>();
        var matchStart = match.CreatedOn;

        moments.AddRange(DetectDeaths(combatLogs, matchStart));
        moments.AddRange(DetectCooldowns(combatLogs, matchStart));
        moments.AddRange(DetectCcChains(combatLogs, matchStart));
        moments.AddRange(DetectDamageSpikes(combatLogs, matchStart));

        var ratingMoments = await DetectRatingChangesAsync(matchId, match.Duration, ct);
        moments.AddRange(ratingMoments);

        dto.Moments = moments.OrderBy(m => m.Timestamp).ToList();
        return dto;
    }

    private async Task<Match?> LoadMatchWithCombatLogsAsync(long matchId, CancellationToken ct)
    {
        return await dbContext.Matches
            .Include(m => m.CombatLogs)
            .ThenInclude(c => c.SourcePlayer)
            .Include(m => m.CombatLogs)
            .ThenInclude(c => c.TargetPlayer)
            .FirstOrDefaultAsync(m => m.Id == matchId, ct);
    }

    private static List<KeyMoment> DetectDeaths(List<CombatLogEntry> combatLogs, DateTime matchStart)
    {
        var moments = new List<KeyMoment>();
        var playerLastActivity = new Dictionary<long, DateTime>();

        foreach (var log in combatLogs)
        {
            if (log.SourcePlayerId > 0)
                playerLastActivity[log.SourcePlayerId] = log.Timestamp;

            if (!IsPotentialDeath(log))
                continue;

            var targetId = log.TargetPlayerId!.Value;
            // Note: Deaths may be missed for players who never act as a source (e.g., killed very early,
            // AFK/disconnected players). This check requires the target player to have previously acted
            // as a source (tracked in playerLastActivity). In typical PvP scenarios this is acceptable
            // as such players aren't really participating, but it's worth noting this limitation.
            if (IsPlayerInactiveAfterDamage(combatLogs, log, targetId) && playerLastActivity.ContainsKey(targetId))
            {
                moments.Add(CreateDeathMoment(log, matchStart));
            }
        }

        return moments;
    }

    private static bool IsPotentialDeath(CombatLogEntry log)
    {
        return log is { TargetPlayerId: not null, DamageDone: > AppConstants.AnalyticsThresholds.PotentialDeathDamage };
    }

    private static bool IsPlayerInactiveAfterDamage(
        List<CombatLogEntry> combatLogs,
        CombatLogEntry log,
        long targetId)
    {
        return combatLogs.Where(c => c.Timestamp > log.Timestamp && c.Timestamp <= log.Timestamp.AddSeconds(5))
            .All(c => c.SourcePlayerId != targetId);
    }

    private static KeyMoment CreateDeathMoment(CombatLogEntry log, DateTime matchStart)
    {
        var relativeTime = (long)(log.Timestamp - matchStart).TotalSeconds;
        return new KeyMoment
        {
            Timestamp = relativeTime,
            EventType = "death",
            Description = $"{log.TargetPlayer.Name} died",
            SourcePlayerId = log.SourcePlayerId,
            SourcePlayerName = log.SourcePlayer.Name,
            TargetPlayerId = log.TargetPlayerId!.Value,
            TargetPlayerName = log.TargetPlayer.Name,
            Ability = log.Ability,
            DamageDone = log.DamageDone,
            ImpactScore = 0.9,
            IsCritical = true
        };
    }

    private static List<KeyMoment> DetectCooldowns(List<CombatLogEntry> combatLogs, DateTime matchStart)
    {
        return (from log in combatLogs
            where ImportantAbilities.IsCooldownOrDefensive(log.Ability)
            let relativeTime = (long)(log.Timestamp - matchStart).TotalSeconds
            select new KeyMoment
            {
                Timestamp = relativeTime,
                EventType = "cooldown",
                Description = $"{log.SourcePlayer.Name} used {log.Ability}",
                SourcePlayerId = log.SourcePlayerId,
                SourcePlayerName = log.SourcePlayer.Name,
                TargetPlayerId = log.TargetPlayerId,
                TargetPlayerName = log.TargetPlayer.Name,
                Ability = log.Ability,
                ImpactScore = 0.7,
                IsCritical = false
            }).ToList();
    }

    private static List<KeyMoment> DetectCcChains(List<CombatLogEntry> combatLogs, DateTime matchStart)
    {
        var moments = new List<KeyMoment>();
        var ccLogs = combatLogs
            .Where(c => !string.IsNullOrWhiteSpace(c.CrowdControl))
            .OrderBy(c => c.Timestamp)
            .ToList();

        for (int i = 0; i < ccLogs.Count - 1; i++)
        {
            var current = ccLogs[i];
            var next = ccLogs[i + 1];
            var timeDiff = (next.Timestamp - current.Timestamp).TotalSeconds;

            if (IsCcChain(current, next, timeDiff))
            {
                moments.Add(CreateCcChainMoment(current, matchStart));
            }
        }

        return moments;
    }

    private static bool IsCcChain(CombatLogEntry current, CombatLogEntry next,
        double timeDiff)
    {
        return timeDiff <= 3 && current.TargetPlayerId == next.TargetPlayerId;
    }

    private static KeyMoment CreateCcChainMoment(CombatLogEntry current, DateTime matchStart)
    {
        var relativeTime = (long)(current.Timestamp - matchStart).TotalSeconds;
        return new KeyMoment
        {
            Timestamp = relativeTime,
            EventType = "cc_chain",
            Description = $"CC chain on {current.TargetPlayer.Name}",
            SourcePlayerId = current.SourcePlayerId,
            SourcePlayerName = current.SourcePlayer.Name,
            TargetPlayerId = current.TargetPlayerId,
            TargetPlayerName = current.TargetPlayer.Name,
            CrowdControl = current.CrowdControl,
            ImpactScore = 0.8,
            IsCritical = true
        };
    }

    private static List<KeyMoment> DetectDamageSpikes(List<CombatLogEntry> combatLogs,
        DateTime matchStart)
    {
        var damageSpikes = combatLogs
            .Where(c => c.DamageDone > AppConstants.AnalyticsThresholds.DamageSpike)
            .ToList();

        return (from spike in damageSpikes
            let relativeTime = (long)(spike.Timestamp - matchStart).TotalSeconds
            select new KeyMoment
            {
                Timestamp = relativeTime,
                EventType = "damage_spike",
                Description = $"{spike.SourcePlayer.Name} dealt {spike.DamageDone:N0} damage",
                SourcePlayerId = spike.SourcePlayerId,
                SourcePlayerName = spike.SourcePlayer.Name,
                TargetPlayerId = spike.TargetPlayerId,
                TargetPlayerName = spike.TargetPlayer.Name,
                Ability = spike.Ability,
                DamageDone = spike.DamageDone,
                ImpactScore = Math.Min(spike.DamageDone / 200000.0, 1.0),
                IsCritical = spike.DamageDone > AppConstants.AnalyticsThresholds.CriticalDamageSpike
            }).ToList();
    }

    private async Task<List<KeyMoment>> DetectRatingChangesAsync(
        long matchId,
        long matchDurationSeconds,
        CancellationToken ct)
    {
        var matchResults = await dbContext.MatchResults
            .Where(mr => mr.MatchId == matchId)
            .Include(mr => mr.Player)
            .ToListAsync(ct);

        return (from result in matchResults
            let ratingChange = result.RatingAfter - result.RatingBefore
            where Math.Abs(ratingChange) >= 10
            select CreateRatingChangeMoment(result, ratingChange, matchDurationSeconds)).ToList();
    }

    private static KeyMoment CreateRatingChangeMoment(
        MatchResult result,
        int ratingChange,
        long matchDurationSeconds)
    {
        return new KeyMoment
        {
            Timestamp = matchDurationSeconds,
            EventType = "rating_change",
            Description =
                 $"{result.Player.Name} {(ratingChange > 0 ? "gained" : "lost")} {Math.Abs(ratingChange)} rating",
            TargetPlayerId = result.PlayerId,
            ImpactScore = Math.Min(Math.Abs(ratingChange) / 50.0, 1.0),
            IsCritical = Math.Abs(ratingChange) >= 20
        };
    }

    public async Task<PlayerKeyMomentsDto> GetRecentKeyMomentsAsync(long playerId, int limit = 10,
        CancellationToken ct = default)
    {
        var player = await dbContext.Players.FindAsync([playerId], ct);
        var dto = new PlayerKeyMomentsDto
        {
            PlayerId = playerId,
            PlayerName = player?.Name ?? "Unknown"
        };

        // Get recent matches for this player
        var recentMatchIds = await dbContext.MatchResults
            .Where(mr => mr.PlayerId == playerId)
            .OrderByDescending(mr => mr.Match.CreatedOn)
            .Take(limit)
            .Select(mr => mr.MatchId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var matchId in recentMatchIds)
        {
            var keyMoments = await GetKeyMomentsForMatchAsync(matchId, ct);
            dto.RecentMatches.Add(keyMoments);
        }

        return dto;
    }
}