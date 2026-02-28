using Microsoft.EntityFrameworkCore;
using PvpAnalytics.Core.Statistics;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Core.Enum;
using PvpAnalytics.Infrastructure;

namespace PvpAnalytics.Application.Services;

public interface IMatchupAnalyticsService
{
    Task<MatchupAnalyticsDto?> GetMatchupAsync(
        MatchupQueryDto parameters,
        CancellationToken ct = default);

    Task<PlayerMatchupSummaryDto> GetPlayerMatchupSummaryAsync(long playerId, CancellationToken ct = default);
}

public class MatchupAnalyticsService(PvpAnalyticsDbContext dbContext) : IMatchupAnalyticsService
{
    public async Task<MatchupAnalyticsDto?> GetMatchupAsync(
        MatchupQueryDto parameters,
        CancellationToken ct = default)
    {
        var matchup1 = new ClassSpec(parameters.Class1, parameters.Spec1);
        var matchup2 = new ClassSpec(parameters.Class2, parameters.Spec2);
        var filters = new MatchupFilters(parameters.GameMode, parameters.RatingMin, parameters.RatingMax, parameters.StartDate, parameters.EndDate);
        var dto = CreateMatchupDto(matchup1, matchup2, filters);

        var allResults = await LoadMatchResultsAsync(parameters.StartDate, parameters.EndDate, parameters.GameMode, ct);
        var matchupMatches = FindMatchupMatches(allResults, matchup1, matchup2);

        if (matchupMatches.Count == 0)
            return dto;

        var matchupResults = FilterMatchupResults(allResults, matchupMatches, filters.RatingMin, filters.RatingMax);
        CalculateWinRates(dto, matchupResults, matchup1, matchup2);

        dto.AverageMatchDuration = await CalculateAverageMatchDurationAsync(matchupMatches, ct);
        dto.Stats = await CalculateMatchupStatsAsync(matchupMatches, matchupResults, matchup1, matchup2, ct);

        return dto;
    }

    private static MatchupAnalyticsDto CreateMatchupDto(
        ClassSpec matchup1, ClassSpec matchup2, MatchupFilters filters)
    {
        return new MatchupAnalyticsDto
        {
            Class1 = matchup1.Class,
            Spec1 = matchup1.Spec,
            Class2 = matchup2.Class,
            Spec2 = matchup2.Spec,
            GameMode = filters.GameMode,
            RatingMin = filters.RatingMin,
            RatingMax = filters.RatingMax,
            StartDate = filters.StartDate,
            EndDate = filters.EndDate
        };
    }

    private sealed record ClassSpec(string Class, string? Spec);

    private sealed record MatchupFilters(
        GameMode? GameMode,
        int? RatingMin,
        int? RatingMax,
        DateTime? StartDate,
        DateTime? EndDate);

    private async Task<List<Core.Entities.MatchResult>> LoadMatchResultsAsync(
        DateTime? startDate, DateTime? endDate, GameMode? gameMode, CancellationToken ct)
    {
        var matchesQuery = dbContext.MatchResults
            .Include(mr => mr.Match)
            .Include(mr => mr.Player)
            .AsQueryable();

        matchesQuery = ApplyDateFilters(matchesQuery, startDate, endDate);
        matchesQuery = ApplyGameModeFilter(matchesQuery, gameMode);

        return await matchesQuery.ToListAsync(ct);
    }

    private static IQueryable<Core.Entities.MatchResult> ApplyDateFilters(
        IQueryable<Core.Entities.MatchResult> query,
        DateTime? startDate,
        DateTime? endDate)
    {
        if (startDate.HasValue)
        {
            query = query.Where(mr => mr.Match.CreatedOn >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(mr => mr.Match.CreatedOn <= endDate.Value);
        }

        return query;
    }

    private static IQueryable<Core.Entities.MatchResult> ApplyGameModeFilter(
        IQueryable<Core.Entities.MatchResult> query,
        GameMode? gameMode)
    {
        if (gameMode.HasValue)
        {
            query = query.Where(mr => mr.Match.GameMode == gameMode.Value);
        }

        return query;
    }

    private static List<long> FindMatchupMatches(
        List<Core.Entities.MatchResult> allResults,
        ClassSpec matchup1, ClassSpec matchup2)
    {
        return allResults
            .GroupBy(mr => mr.MatchId)
            .Where(g => HasMatchupInMatch(g, matchup1, matchup2))
            .Select(g => g.Key)
            .ToList();
    }

    private static bool HasMatchupInMatch(
        IGrouping<long, Core.Entities.MatchResult> matchGroup,
        ClassSpec matchup1, ClassSpec matchup2)
    {
        var teams = matchGroup.GroupBy(mr => mr.Team).ToList();
        if (teams.Count < 2)
            return false;

        return TeamsHaveMatchup(teams, matchup1, matchup2);
    }

    private static bool TeamsHaveMatchup(
        List<IGrouping<string, Core.Entities.MatchResult>> teams,
        ClassSpec matchup1, ClassSpec matchup2)
    {
        return teams.Any(team1 =>
            teams.Where(t => t.Key != team1.Key)
                .Any(team2 => TeamHasClass(team1, matchup1.Class, matchup1.Spec) &&
                              TeamHasClass(team2, matchup2.Class, matchup2.Spec)));
    }

    private static bool TeamHasClass(
        IGrouping<string, Core.Entities.MatchResult> team,
        string className,
        string? spec)
    {
        return team.Any(mr =>
            mr.Player.Class.Equals(className, StringComparison.OrdinalIgnoreCase) &&
            (spec == null || (mr.Spec != null && string.Equals(mr.Spec, spec, StringComparison.OrdinalIgnoreCase))));
    }

    private static List<Core.Entities.MatchResult> FilterMatchupResults(
        List<Core.Entities.MatchResult> allResults,
        List<long> matchupMatches,
        int? ratingMin,
        int? ratingMax)
    {
        var matchupResults = allResults
            .Where(mr => matchupMatches.Contains(mr.MatchId))
            .ToList();

        if (ratingMin.HasValue || ratingMax.HasValue)
        {
            matchupResults = matchupResults.Where(mr =>
                    (!ratingMin.HasValue || mr.RatingBefore >= ratingMin.Value) &&
                    (!ratingMax.HasValue || mr.RatingBefore <= ratingMax.Value))
                .ToList();
        }

        return matchupResults;
    }

    private static void CalculateWinRates(
        MatchupAnalyticsDto dto,
        List<Core.Entities.MatchResult> matchupResults,
        ClassSpec matchup1, ClassSpec matchup2)
    {
        var matchGroups = matchupResults.GroupBy(mr => mr.MatchId).ToList();
        dto.TotalMatches = matchGroups.Count;

        var (class1Wins, class2Wins) = CountWins(matchGroups, matchup1, matchup2);

        dto.WinsForClass1 = class1Wins;
        dto.WinsForClass2 = class2Wins;
        dto.WinRateForClass1 = CalculateWinRate(class1Wins, dto.TotalMatches);
        dto.WinRateForClass2 = CalculateWinRate(class2Wins, dto.TotalMatches);
    }

    private static (int class1Wins, int class2Wins) CountWins(
        List<IGrouping<long, Core.Entities.MatchResult>> matchGroups,
        ClassSpec matchup1, ClassSpec matchup2)
    {
        var class1Wins = 0;
        var class2Wins = 0;

        foreach (var matchGroup in matchGroups)
        {
            var teams = matchGroup.GroupBy(mr => mr.Team).ToList();
            var class1Team = FindTeamWithClass(teams, matchup1.Class, matchup1.Spec);
            var class2Team = FindTeamWithClass(teams, matchup2.Class, matchup2.Spec);

            if (class1Team == null || class2Team == null)
                continue;

            if (class1Team.Any(mr => mr.IsWinner))
                class1Wins++;
            else if (class2Team.Any(mr => mr.IsWinner))
                class2Wins++;
        }

        return (class1Wins, class2Wins);
    }

    private static IGrouping<string, Core.Entities.MatchResult>? FindTeamWithClass(
        List<IGrouping<string, Core.Entities.MatchResult>> teams,
        string className,
        string? spec)
    {
        return teams.FirstOrDefault(t => TeamHasClass(t, className, spec));
    }

    /// <summary>Returns smoothed win rate (0–100), or <see cref="double.NaN"/> when there is no data (totalMatches == 0).</summary>
    private static double CalculateWinRate(int wins, int totalMatches)
    {
        if (totalMatches == 0)
            return double.NaN;
        return WinRateSmoothing.Smooth(wins, totalMatches, GlobalCoefficients.Default);
    }

    private async Task<double> CalculateAverageMatchDurationAsync(List<long> matchIds, CancellationToken ct)
    {
        var matches = await dbContext.Matches
            .Where(m => matchIds.Contains(m.Id))
            .ToListAsync(ct);

        return matches.Count != 0 ? Math.Round(matches.Average(m => (double)m.Duration), 2) : 0;
    }

    private async Task<MatchupDamageHealingStats> CalculateMatchupStatsAsync(
        List<long> matchIds,
        List<Core.Entities.MatchResult> matchupResults,
        ClassSpec matchup1, ClassSpec matchup2,
        CancellationToken ct)
    {
        var class1PlayerIds = GetPlayerIdsForClass(matchupResults, matchup1.Class, matchup1.Spec);
        var class2PlayerIds = GetPlayerIdsForClass(matchupResults, matchup2.Class, matchup2.Spec);

        var combatLogs = await dbContext.CombatLogEntries
            .Where(c => matchIds.Contains(c.MatchId))
            .ToListAsync(ct);

        var class1Logs = combatLogs.Where(c => class1PlayerIds.Contains(c.SourcePlayerId)).ToList();
        var class2Logs = combatLogs.Where(c => class2PlayerIds.Contains(c.SourcePlayerId)).ToList();

        return CreateMatchupStats(class1Logs, class2Logs, matchIds.Count);
    }

    private static List<long> GetPlayerIdsForClass(
        List<Core.Entities.MatchResult> matchupResults,
        string className,
        string? spec)
    {
        return matchupResults
            .Where(mr => mr.Player.Class.Equals(className, StringComparison.OrdinalIgnoreCase) &&
                         (spec == null || mr.Spec == spec))
            .Select(mr => mr.PlayerId)
            .Distinct()
            .ToList();
    }

    private static MatchupDamageHealingStats CreateMatchupStats(
        List<Core.Entities.CombatLogEntry> class1Logs,
        List<Core.Entities.CombatLogEntry> class2Logs,
        int matchCount)
    {
        return new MatchupDamageHealingStats
        {
            AverageDamageClass1 = CalculateAverageDamage(class1Logs),
            AverageDamageClass2 = CalculateAverageDamage(class2Logs),
            AverageHealingClass1 = CalculateAverageHealing(class1Logs),
            AverageHealingClass2 = CalculateAverageHealing(class2Logs),
            AverageCCClass1 = CalculateAverageCc(class1Logs, matchCount),
            AverageCCClass2 = CalculateAverageCc(class2Logs, matchCount)
        };
    }

    private static double CalculateAverageDamage(List<Core.Entities.CombatLogEntry> logs)
    {
        return logs.Count != 0 ? Math.Round(logs.Average(c => (double)c.DamageDone), 2) : 0;
    }

    private static double CalculateAverageHealing(List<Core.Entities.CombatLogEntry> logs)
    {
        return logs.Count != 0 ? Math.Round(logs.Average(c => (double)c.HealingDone), 2) : 0;
    }

    private static double CalculateAverageCc(List<Core.Entities.CombatLogEntry> logs, int matchCount)
    {
        if (logs.Count == 0 || matchCount == 0)
            return 0;

        var ccCount = logs.Count(c => !string.IsNullOrWhiteSpace(c.CrowdControl));
        return Math.Round(ccCount / (double)matchCount, 2);
    }

    public async Task<PlayerMatchupSummaryDto> GetPlayerMatchupSummaryAsync(long playerId,
        CancellationToken ct = default)
    {
        var player = await dbContext.Players.FindAsync([playerId], ct);
        if (player == null)
            return new PlayerMatchupSummaryDto { PlayerId = playerId };

        var dto = new PlayerMatchupSummaryDto
        {
            PlayerId = playerId,
            PlayerName = player.Name
        };

        // Get all match results for this player
        var playerResults = await dbContext.MatchResults
            .Include(mr => mr.Match)
            .Where(mr => mr.PlayerId == playerId)
            .ToListAsync(ct);

        if (playerResults.Count == 0)
            return dto;

        var matchIds = playerResults.Select(mr => mr.MatchId).Distinct().ToList();
        var playerTeams = playerResults.ToDictionary(mr => mr.MatchId, mr => mr.Team);

        // Get opponent results
        var opponentResults = await dbContext.MatchResults
            .Include(mr => mr.Player)
            .Where(mr => matchIds.Contains(mr.MatchId))
            .ToListAsync(ct);

        // Group by opponent class/spec
        var matchups = opponentResults
            .GroupBy(mr => new { mr.Player.Class, mr.Spec })
            .Select(g =>
            {
                var playerWon = g.Count(mr =>
                {
                    var playerTeam = playerTeams.GetValueOrDefault(mr.MatchId);
                    return playerTeam != null && playerTeam != mr.Team && !mr.IsWinner;
                });
                return new MatchupWinRate
                {
                    OpponentClass = g.Key.Class,
                    OpponentSpec = g.Key.Spec,
                    Matches = g.Count(),
                    Wins = playerWon,
                    WinRate = WinRateSmoothing.Smooth(playerWon, g.Count(), GlobalCoefficients.Default)
                };
            })
            .Where(m => m.Matches >= 3) // Minimum matches for meaningful data
            .ToList();

        dto.Weaknesses = matchups
            .OrderBy(m => m.WinRate)
            .ThenByDescending(m => m.Matches)
            .Take(10)
            .ToList();

        dto.Strengths = matchups
            .OrderByDescending(m => m.WinRate)
            .ThenByDescending(m => m.Matches)
            .Take(10)
            .ToList();

        return dto;
    }
}