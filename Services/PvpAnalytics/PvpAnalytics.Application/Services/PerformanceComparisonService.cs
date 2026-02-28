using Microsoft.EntityFrameworkCore;
using PvpAnalytics.Core.Statistics;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Infrastructure;

namespace PvpAnalytics.Application.Services;

public interface IPerformanceComparisonService
{
    Task<PerformanceComparisonDto> ComparePlayerAsync(
        long playerId,
        string spec,
        int? ratingMin = null,
        CancellationToken ct = default);
    Task<PercentileRankings> GetPlayerPercentilesAsync(
        long playerId,
        string spec,
        int? ratingMin = null,
        CancellationToken ct = default);
}

public class PerformanceComparisonService(PvpAnalyticsDbContext dbContext) : IPerformanceComparisonService
{
    public async Task<PerformanceComparisonDto> ComparePlayerAsync(
        long playerId,
        string spec,
        int? ratingMin = null,
        CancellationToken ct = default)
    {
        var player = await dbContext.Players.FindAsync([playerId], ct);
        if (player == null)
            return new PerformanceComparisonDto { PlayerId = playerId, Spec = spec };

        var dto = CreatePerformanceComparisonDto(playerId, player.Name, spec, ratingMin);
        
        var playerResults = await LoadPlayerResultsAsync(playerId, spec, ratingMin, ct);
        if (playerResults.Count == 0)
            return dto;

        var playerMatchIds = playerResults.Select(mr => mr.MatchId).Distinct().ToList();
        var playerCombatLogs = await LoadPlayerCombatLogsAsync(playerId, playerMatchIds, ct);
        
        dto.PlayerMetrics = CalculatePlayerMetrics(playerResults, playerCombatLogs, playerMatchIds);
        
        var allSpecResults = await LoadAllSpecResultsAsync(spec, ratingMin, ct);
        var allSpecMatchIds = allSpecResults.Select(mr => mr.MatchId).Distinct().ToList();
        var allSpecCombatLogs = await LoadAllSpecCombatLogsAsync(allSpecResults, allSpecMatchIds, ct);
        
        dto.TopPlayerMetrics = CalculateTopPlayerMetrics(allSpecResults, allSpecCombatLogs, allSpecMatchIds);
        dto.Gaps = CalculateGaps(dto.PlayerMetrics, dto.TopPlayerMetrics);
        AnalyzeStrengthsAndWeaknesses(dto.Gaps);
        dto.Percentiles = await GetPlayerPercentilesAsync(playerId, spec, ratingMin, ct);

        return dto;
    }

    private static PerformanceComparisonDto CreatePerformanceComparisonDto(
        long playerId, string playerName, string spec, int? ratingMin)
    {
        return new PerformanceComparisonDto
        {
            PlayerId = playerId,
            PlayerName = playerName,
            Spec = spec,
            RatingMin = ratingMin
        };
    }

    private async Task<List<Core.Entities.MatchResult>> LoadPlayerResultsAsync(
        long playerId, string spec, int? ratingMin, CancellationToken ct)
    {
        var query = dbContext.MatchResults
            .Include(mr => mr.Match)
            .Where(mr => mr.PlayerId == playerId && mr.Spec == spec);

        if (ratingMin.HasValue)
        {
            query = query.Where(mr => mr.RatingBefore >= ratingMin.Value);
        }

        return await query.ToListAsync(ct);
    }

    private async Task<List<Core.Entities.CombatLogEntry>> LoadPlayerCombatLogsAsync(
        long playerId, List<long> matchIds, CancellationToken ct)
    {
        return await dbContext.CombatLogEntries
            .Where(c => c.SourcePlayerId == playerId && matchIds.Contains(c.MatchId))
            .ToListAsync(ct);
    }

    private static PlayerMetrics CalculatePlayerMetrics(
        List<Core.Entities.MatchResult> playerResults,
        List<Core.Entities.CombatLogEntry> playerCombatLogs,
        List<long> playerMatchIds)
    {
        var playerWins = playerResults.Count(mr => mr.IsWinner);
        var playerTotalMatches = playerResults.Count;

        return new PlayerMetrics
        {
            WinRate = CalculateWinRate(playerWins, playerTotalMatches),
            AverageDamage = CalculateAverageDamage(playerCombatLogs),
            AverageHealing = CalculateAverageHealing(playerCombatLogs),
            AverageCC = CalculateAverageCc(playerCombatLogs, playerMatchIds.Count),
            CurrentRating = GetCurrentRating(playerResults),
            PeakRating = GetPeakRating(playerResults),
            AverageMatchDuration = CalculateAverageMatchDuration(playerResults)
        };
    }

    private static double CalculateWinRate(int wins, int totalMatches)
    {
        return WinRateSmoothing.Smooth(wins, totalMatches, GlobalCoefficients.Default);
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

    private static int GetCurrentRating(List<Core.Entities.MatchResult> results)
    {
        if (results == null || results.Count == 0)
        {
            throw new ArgumentException("results must contain at least one MatchResult", nameof(results));
        }

        return results.OrderByDescending(mr => mr.Match.CreatedOn).First().RatingAfter;
    }

    private static int GetPeakRating(List<Core.Entities.MatchResult> results)
    {
        return results.Max(mr => Math.Max(mr.RatingBefore, mr.RatingAfter));
    }

    private static double CalculateAverageMatchDuration(List<Core.Entities.MatchResult> results)
    {
        return results.Count != 0 ? Math.Round(results.Average(mr => (double)mr.Match.Duration), 2) : 0;
    }

    private async Task<List<Core.Entities.MatchResult>> LoadAllSpecResultsAsync(
        string spec, int? ratingMin, CancellationToken ct)
    {
        var query = dbContext.MatchResults
            .Include(mr => mr.Match)
            .Where(mr => mr.Spec == spec);

        if (ratingMin.HasValue)
        {
            query = query.Where(mr => mr.RatingBefore >= ratingMin.Value);
        }

        return await query.ToListAsync(ct);
    }

    private async Task<List<Core.Entities.CombatLogEntry>> LoadAllSpecCombatLogsAsync(
        List<Core.Entities.MatchResult> allSpecResults,
        List<long> allSpecMatchIds,
        CancellationToken ct)
    {
        var allSpecPlayerIds = allSpecResults.Select(mr => mr.PlayerId).Distinct().ToList();
        
        return await dbContext.CombatLogEntries
            .Where(c => allSpecPlayerIds.Contains(c.SourcePlayerId) && allSpecMatchIds.Contains(c.MatchId))
            .ToListAsync(ct);
    }

    private static TopPlayerMetrics CalculateTopPlayerMetrics(
        List<Core.Entities.MatchResult> allSpecResults,
        List<Core.Entities.CombatLogEntry> allSpecCombatLogs,
        List<long> allSpecMatchIds)
    {
        var topPlayerWins = allSpecResults.Count(mr => mr.IsWinner);
        var topPlayerTotalMatches = allSpecResults.Count;

        return new TopPlayerMetrics
        {
            AverageWinRate = CalculateWinRate(topPlayerWins, topPlayerTotalMatches),
            AverageDamage = CalculateAverageDamage(allSpecCombatLogs),
            AverageHealing = CalculateAverageHealing(allSpecCombatLogs),
            AverageCC = CalculateAverageCc(allSpecCombatLogs, allSpecMatchIds.Count),
            AverageRating = CalculateAverageRating(allSpecResults),
            AverageMatchDuration = CalculateAverageMatchDuration(allSpecResults)
        };
    }

    private static double CalculateAverageRating(List<Core.Entities.MatchResult> results)
    {
        return results.Count != 0 ? Math.Round(results.Average(mr => (double)mr.RatingAfter), 2) : 0;
    }

    private static ComparisonGaps CalculateGaps(PlayerMetrics playerMetrics, TopPlayerMetrics topPlayerMetrics)
    {
        return new ComparisonGaps
        {
            WinRateGap = playerMetrics.WinRate - topPlayerMetrics.AverageWinRate,
            DamageGap = playerMetrics.AverageDamage - topPlayerMetrics.AverageDamage,
            HealingGap = playerMetrics.AverageHealing - topPlayerMetrics.AverageHealing,
            CCGap = playerMetrics.AverageCC - topPlayerMetrics.AverageCC,
            RatingGap = playerMetrics.CurrentRating - topPlayerMetrics.AverageRating
        };
    }

    private static void AnalyzeStrengthsAndWeaknesses(ComparisonGaps gaps)
    {
        AnalyzeGap(gaps.WinRateGap, 5, -5, "Win Rate", gaps);
        AnalyzeGap(gaps.DamageGap, 10000, -10000, "Damage Output", gaps);
        AnalyzeGap(gaps.HealingGap, 5000, -5000, "Healing Output", gaps);
    }

    private static void AnalyzeGap(double gap, double strengthThreshold, double weaknessThreshold, string category, ComparisonGaps gaps)
    {
        if (gap > strengthThreshold)
            gaps.Strengths.Add(category);
        else if (gap < weaknessThreshold)
            gaps.Weaknesses.Add(category);
    }

    public async Task<PercentileRankings> GetPlayerPercentilesAsync(
        long playerId,
        string spec,
        int? ratingMin = null,
        CancellationToken ct = default)
    {
        var playerResults = await LoadPlayerResultsAsync(playerId, spec, ratingMin, ct);
        if (playerResults.Count == 0)
            return new PercentileRankings();

        var playerMatchIds = playerResults.Select(mr => mr.MatchId).Distinct().ToList();
        var playerCombatLogs = await LoadPlayerCombatLogsAsync(playerId, playerMatchIds, ct);
        var playerStats = CalculatePlayerStatsForPercentiles(playerResults, playerCombatLogs, playerMatchIds.Count);

        var allSpecResults = await LoadAllSpecResultsAsync(spec, ratingMin, ct);
        var allSpecPlayerIds = allSpecResults.Select(mr => mr.PlayerId).Distinct().ToList();
        var allPlayerStats = await CalculateAllSpecPlayerStatsAsync(allSpecResults, allSpecPlayerIds, ct);

        if (allPlayerStats.Count == 0)
            return new PercentileRankings();

        return CalculatePercentiles(playerStats, allPlayerStats);
    }

    private static (double winRate, double damage, double healing, double cc, int rating) CalculatePlayerStatsForPercentiles(
        List<Core.Entities.MatchResult> playerResults,
        List<Core.Entities.CombatLogEntry> playerCombatLogs,
        int matchCount)
    {
        if (playerResults.Count == 0)
        {
            return (0, 0, 0, 0, 0);
        }

        var winRate = playerResults.Count(mr => mr.IsWinner) / (double)playerResults.Count;
        var damage = playerCombatLogs.Count != 0 ? playerCombatLogs.Average(c => (double)c.DamageDone) : 0;
        var healing = playerCombatLogs.Count != 0 ? playerCombatLogs.Average(c => (double)c.HealingDone) : 0;
        var cc = CalculateCcForPercentiles(playerCombatLogs, matchCount);
        var latestResult = playerResults.OrderByDescending(mr => mr.Match.CreatedOn).First();
        var rating = latestResult.RatingAfter;

        return (winRate, damage, healing, cc, rating);
    }

    private static double CalculateCcForPercentiles(List<Core.Entities.CombatLogEntry> logs, int matchCount)
    {
        if (logs.Count == 0 || matchCount == 0)
            return 0;

        return logs.Count(c => !string.IsNullOrWhiteSpace(c.CrowdControl)) / (double)matchCount;
    }

    private async Task<List<(double winRate, double damage, double healing, double cc, int rating)>> CalculateAllSpecPlayerStatsAsync(
        List<Core.Entities.MatchResult> allSpecResults,
        List<long> allSpecPlayerIds,
        CancellationToken ct)
    {
        var allPlayerStats = new List<(double winRate, double damage, double healing, double cc, int rating)>();

        foreach (var specPlayerId in allSpecPlayerIds)
        {
            var stats = await CalculateSpecPlayerStatsAsync(specPlayerId, allSpecResults, ct);
            allPlayerStats.Add(stats);
        }

        return allPlayerStats;
    }

    private async Task<(double winRate, double damage, double healing, double cc, int rating)> CalculateSpecPlayerStatsAsync(
        long specPlayerId,
        List<Core.Entities.MatchResult> allSpecResults,
        CancellationToken ct)
    {
        var specPlayerResults = allSpecResults.Where(mr => mr.PlayerId == specPlayerId).ToList();
        var specPlayerMatchIds = specPlayerResults.Select(mr => mr.MatchId).Distinct().ToList();
        var specPlayerCombatLogs = await dbContext.CombatLogEntries
            .Where(c => c.SourcePlayerId == specPlayerId && specPlayerMatchIds.Contains(c.MatchId))
            .ToListAsync(ct);

        var winRate = specPlayerResults.Count(mr => mr.IsWinner) / (double)specPlayerResults.Count;
        var damage = specPlayerCombatLogs.Count != 0 ? specPlayerCombatLogs.Average(c => (double)c.DamageDone) : 0;
        var healing = specPlayerCombatLogs.Count != 0 ? specPlayerCombatLogs.Average(c => (double)c.HealingDone) : 0;
        var cc = CalculateCcForPercentiles(specPlayerCombatLogs, specPlayerMatchIds.Count);
        var rating = specPlayerResults.OrderByDescending(mr => mr.Match.CreatedOn).First().RatingAfter;

        return (winRate, damage, healing, cc, rating);
    }

    private static PercentileRankings CalculatePercentiles(
        (double winRate, double damage, double healing, double cc, int rating) playerStats,
        List<(double winRate, double damage, double healing, double cc, int rating)> allPlayerStats)
    {
        var totalPlayers = allPlayerStats.Count;

        return new PercentileRankings
        {
            WinRatePercentile = CalculatePercentile(s => s.winRate, playerStats.winRate, allPlayerStats, totalPlayers),
            DamagePercentile = CalculatePercentile(s => s.damage, playerStats.damage, allPlayerStats, totalPlayers),
            HealingPercentile = CalculatePercentile(s => s.healing, playerStats.healing, allPlayerStats, totalPlayers),
            CCPercentile = CalculatePercentile(s => s.cc, playerStats.cc, allPlayerStats, totalPlayers),
            RatingPercentile = CalculatePercentile(s => s.rating, playerStats.rating, allPlayerStats, totalPlayers)
        };
    }

    private static double CalculatePercentile(
        Func<(double winRate, double damage, double healing, double cc, int rating), double> selector,
        double playerValue,
        List<(double winRate, double damage, double healing, double cc, int rating)> allPlayerStats,
        int totalPlayers)
    {
        var percentile = allPlayerStats.Count(s => selector(s) <= playerValue) * 100.0 / totalPlayers;
        return Math.Round(percentile, 2);
    }
}

