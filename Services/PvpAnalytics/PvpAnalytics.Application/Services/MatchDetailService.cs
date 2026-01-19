using Microsoft.EntityFrameworkCore;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Core.Logs;
using PvpAnalytics.Infrastructure;
using PvpAnalytics.Shared.Constants;

namespace PvpAnalytics.Application.Services;

public interface IMatchDetailService
{
    Task<MatchDetailDto?> GetMatchDetailAsync(long matchId, CancellationToken ct = default);
}

public class MatchDetailService(PvpAnalyticsDbContext dbContext) : IMatchDetailService
{
    public async Task<MatchDetailDto?> GetMatchDetailAsync(long matchId, CancellationToken ct = default)
    {
        var match = await LoadMatchWithRelatedDataAsync(matchId, ct);
        if (match == null)
            return null;

        var basicInfo = CreateMatchBasicInfo(match);
        var participantStats = AggregateParticipantStats(match.CombatLogs);
        var teams = BuildTeams(match.Results, participantStats);
        var timelineEvents = BuildTimelineEvents(match.CombatLogs, match.CreatedOn);

        return new MatchDetailDto
        {
            BasicInfo = basicInfo,
            Teams = teams,
            TimelineEvents = timelineEvents,
        };
    }

    private async Task<Core.Entities.Match?> LoadMatchWithRelatedDataAsync(long matchId, CancellationToken ct)
    {
        return await dbContext.Matches
            .Include(m => m.Results)
            .ThenInclude(r => r.Player)
            .Include(m => m.CombatLogs)
            .ThenInclude(e => e.SourcePlayer)
            .Include(m => m.CombatLogs)
            .ThenInclude(e => e.TargetPlayer)
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matchId, ct);
    }

    private static MatchBasicInfo CreateMatchBasicInfo(Core.Entities.Match match)
    {
        return new MatchBasicInfo
        {
            Id = match.Id,
            UniqueHash = match.UniqueHash,
            CreatedOn = match.CreatedOn,
            MapName = match.MapName,
            ArenaZone = match.ArenaZone,
            GameMode = match.GameMode,
            Duration = match.Duration,
            IsRanked = match.IsRanked,
        };
    }

    private static Dictionary<long, (long damage, long healing, int cc)> AggregateParticipantStats(
        ICollection<Core.Entities.CombatLogEntry> combatLogs)
    {
        var participantStats = new Dictionary<long, (long damage, long healing, int cc)>();

        foreach (var entry in combatLogs)
        {
            if (entry.SourcePlayerId <= 0)
                continue;

            if (!participantStats.TryGetValue(entry.SourcePlayerId, out var stats))
            {
                stats = (0, 0, 0);
                participantStats[entry.SourcePlayerId] = stats;
            }

            stats.damage += entry.DamageDone;
            stats.healing += entry.HealingDone;
            if (!string.IsNullOrWhiteSpace(entry.CrowdControl))
            {
                stats.cc++;
            }

            participantStats[entry.SourcePlayerId] = stats;
        }

        return participantStats;
    }

    private static List<TeamInfo> BuildTeams(
        ICollection<Core.Entities.MatchResult> matchResults,
        Dictionary<long, (long damage, long healing, int cc)> participantStats)
    {
        var participantsByTeam = matchResults.GroupBy(r => r.Team).ToList();

        return (from teamGroup in participantsByTeam
            let participants = CreateParticipantsForTeam(teamGroup, participantStats)
            select CreateTeamInfo(teamGroup.Key, participants)).ToList();
    }

    private static List<ParticipantInfo> CreateParticipantsForTeam(
        IGrouping<string, Core.Entities.MatchResult> teamGroup,
        Dictionary<long, (long damage, long healing, int cc)> participantStats)
    {
        return teamGroup.Select(r =>
        {
            var stats = participantStats.GetValueOrDefault(r.PlayerId, (0, 0, 0));
            return new ParticipantInfo
            {
                PlayerId = r.PlayerId,
                PlayerName = r.Player.Name,
                Realm = r.Player.Realm,
                Class = r.Player.Class,
                Spec = r.Spec,
                Team = r.Team,
                RatingBefore = r.RatingBefore,
                RatingAfter = r.RatingAfter,
                IsWinner = r.IsWinner,
                TotalDamage = stats.damage,
                TotalHealing = stats.healing,
                TotalCC = stats.cc,
            };
        }).ToList();
    }

    private static TeamInfo CreateTeamInfo(string teamName, List<ParticipantInfo> participants)
    {
        return new TeamInfo
        {
            TeamName = teamName,
            Participants = participants,
            TotalDamage = participants.Sum(p => p.TotalDamage),
            TotalHealing = participants.Sum(p => p.TotalHealing),
            IsWinner = participants.Any(p => p.IsWinner),
        };
    }

    private static List<TimelineEvent> BuildTimelineEvents(
        ICollection<Core.Entities.CombatLogEntry> combatLogs,
        DateTime matchStartTime)
    {
        return combatLogs
            .OrderBy(e => e.Timestamp)
            .Select(e => CreateTimelineEvent(e, matchStartTime))
            .Where(e => e.IsImportant
                        || e.DamageDone > AppConstants.AnalyticsThresholds.TimelineMinDamage
                        || e.HealingDone > AppConstants.AnalyticsThresholds.TimelineMinHealing)
            .ToList();
    }

    private static TimelineEvent CreateTimelineEvent(
        Core.Entities.CombatLogEntry entry,
        DateTime matchStartTime)
    {
        var relativeTimestamp = (long)(entry.Timestamp - matchStartTime).TotalSeconds;
        var isCooldown = ImportantAbilities.IsCooldownOrDefensive(entry.Ability);
        var isCc = ImportantAbilities.IsCrowdControl(entry.Ability);
        var isImportant = isCooldown
                          || isCc
                          || entry.DamageDone > AppConstants.AnalyticsThresholds.MatchDetailHighDamage
                          || entry.HealingDone > AppConstants.AnalyticsThresholds.MatchDetailHighHealing;
        var eventType = DetermineEventType(entry, isCooldown);

        return new TimelineEvent
        {
            Timestamp = relativeTimestamp,
            EventType = eventType,
            SourcePlayerId = entry.SourcePlayerId,
            SourcePlayerName = entry.SourcePlayer.Name,
            TargetPlayerId = entry.TargetPlayerId,
            TargetPlayerName = entry.TargetPlayer?.Name ?? "Unknown",
            Ability = entry.Ability,
            DamageDone = entry.DamageDone > 0 ? entry.DamageDone : null,
            HealingDone = entry.HealingDone > 0 ? entry.HealingDone : null,
            CrowdControl = !string.IsNullOrWhiteSpace(entry.CrowdControl) ? entry.CrowdControl : null,
            IsImportant = isImportant,
            IsCooldown = isCooldown,
            IsCC = isCc,
        };
    }

    private static string DetermineEventType(Core.Entities.CombatLogEntry entry, bool isCooldown)
    {
        if (entry.HealingDone > 0)
            return "healing";

        if (!string.IsNullOrWhiteSpace(entry.CrowdControl))
            return "cc";

        if (isCooldown)
            return "cooldown";

        return "damage";
    }
}