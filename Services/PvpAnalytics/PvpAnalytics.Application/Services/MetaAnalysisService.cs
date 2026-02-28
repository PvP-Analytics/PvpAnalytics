using Microsoft.EntityFrameworkCore;
using PvpAnalytics.Core.Statistics;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Core.Enum;
using PvpAnalytics.Infrastructure;

namespace PvpAnalytics.Application.Services;

public interface IMetaAnalysisService
{
    Task<MetaAnalysisDto> GetMetaAnalysisAsync(
        int? ratingMin = null,
        int? ratingMax = null,
        GameMode? gameMode = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken ct = default);
    Task<MetaTrends> GetCompositionTrendsAsync(string composition, int days = 30, CancellationToken ct = default);
}

public class MetaAnalysisService(PvpAnalyticsDbContext dbContext) : IMetaAnalysisService
{
    public async Task<MetaAnalysisDto> GetMetaAnalysisAsync(
        int? ratingMin = null,
        int? ratingMax = null,
        GameMode? gameMode = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken ct = default)
    {
        var dto = new MetaAnalysisDto
        {
            RatingMin = ratingMin,
            RatingMax = ratingMax,
            GameMode = gameMode,
            StartDate = startDate,
            EndDate = endDate
        };

        var query = dbContext.MatchResults
            .Include(mr => mr.Match)
            .Include(mr => mr.Player)
            .AsQueryable();

        if (gameMode.HasValue)
        {
            query = query.Where(mr => mr.Match.GameMode == gameMode.Value);
        }

        if (startDate.HasValue)
        {
            query = query.Where(mr => mr.Match.CreatedOn >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(mr => mr.Match.CreatedOn <= endDate.Value);
        }

        var allResults = await query.ToListAsync(ct);

        // Filter by rating if specified
        if (ratingMin.HasValue || ratingMax.HasValue)
        {
            allResults = allResults.Where(mr =>
                (!ratingMin.HasValue || mr.RatingBefore >= ratingMin.Value) &&
                (!ratingMax.HasValue || mr.RatingBefore <= ratingMax.Value))
                .ToList();
        }

        // Group by match and team to get compositions
        var teamCompositions = allResults
            .GroupBy(mr => new { mr.MatchId, mr.Team })
            .Select(g => new
            {
                 g.Key.MatchId,
                 g.Key.Team,
                Composition = string.Join("-", g
                    .Where(mr => !string.IsNullOrWhiteSpace(mr.Player.Class))
                    .Select(mr => mr.Player.Class)
                    .OrderBy(c => c)
                    .Distinct()),
                IsWinner = g.Any(mr => mr.IsWinner),
                Rating = g.Average(mr => (double)mr.RatingBefore)
            })
            .Where(tc => !string.IsNullOrWhiteSpace(tc.Composition))
            .ToList();

        var totalMatches = teamCompositions.Select(tc => tc.MatchId).Distinct().Count();

        // Group by composition
        var compositionStats = teamCompositions
            .GroupBy(tc => tc.Composition)
            .Select(g => new CompositionMeta
            {
                Composition = g.Key,
                TotalMatches = g.Count(),
                Wins = g.Count(tc => tc.IsWinner),
                WinRate = g.Any() ? WinRateSmoothing.Smooth(g.Count(tc => tc.IsWinner), g.Count(), GlobalCoefficients.Default) : 0,
                Popularity = totalMatches > 0 ? Math.Round(g.Count() * 100.0 / totalMatches, 2) : 0,
                AverageRating = Math.Round(g.Average(tc => tc.Rating), 0)
            })
            .OrderByDescending(c => c.Popularity)
            .ToList();

        // Assign ranks
        for (int i = 0; i < compositionStats.Count; i++)
        {
            compositionStats[i].Rank = i + 1;
        }

        dto.Compositions = compositionStats;

        return dto;
    }

    public async Task<MetaTrends> GetCompositionTrendsAsync(string composition, int days = 30, CancellationToken ct = default)
    {
        var startDate = DateTime.UtcNow.AddDays(-days);
        var endDate = DateTime.UtcNow;

        var query = dbContext.MatchResults
            .Include(mr => mr.Match)
            .Include(mr => mr.Player)
            .Where(mr => mr.Match.CreatedOn >= startDate && mr.Match.CreatedOn <= endDate)
            .AsQueryable();

        var allResults = await query.ToListAsync(ct);

        // Group by match and team to get compositions
        var teamCompositions = allResults
            .GroupBy(mr => new { mr.MatchId, mr.Team, mr.Match.CreatedOn })
            .Select(g => new
            {
                g.Key.MatchId,
                MatchDate = g.Key.CreatedOn.Date,
                g.Key.Team,
                Composition = string.Join("-", g
                    .Where(mr => !string.IsNullOrWhiteSpace(mr.Player.Class))
                    .Select(mr => mr.Player.Class)
                    .OrderBy(c => c)
                    .Distinct()),
                IsWinner = g.Any(mr => mr.IsWinner)
            })
            .Where(tc => tc.Composition == composition)
            .ToList();

        var totalMatchesByDate = allResults
            .GroupBy(mr => mr.Match.CreatedOn.Date)
            .ToDictionary(g => g.Key, g => g.Select(mr => mr.MatchId).Distinct().Count());

        // Group by date
        var trends = teamCompositions
            .GroupBy(tc => tc.MatchDate)
            .Select(g =>
            {
                var date = g.Key;
                var matches = g.Count();
                var wins = g.Count(tc => tc.IsWinner);
                var totalMatches = totalMatchesByDate.GetValueOrDefault(date, 1);

                return new TrendDataPoint
                {
                    Date = date,
                    Matches = matches,
                    Popularity = totalMatches > 0 ? Math.Round(matches * 100.0 / totalMatches, 2) : 0,
                    WinRate = WinRateSmoothing.Smooth(wins, matches, GlobalCoefficients.Default)
                };
            })
            .OrderBy(t => t.Date)
            .ToList();

        return new MetaTrends
        {
            Composition = composition,
            DataPoints = trends
        };
    }
}

