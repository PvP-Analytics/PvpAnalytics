using Microsoft.EntityFrameworkCore;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Infrastructure;

namespace PvpAnalytics.Application.Services;

public interface ICommunityRankingService
{
    Task<CommunityRankingDto> GetRankingsAsync(string rankingType, string period = "weekly", string? scope = null, int limit = 50, CancellationToken ct = default);
}

public class CommunityRankingService(PvpAnalyticsDbContext dbContext) : ICommunityRankingService
{
    public async Task<CommunityRankingDto> GetRankingsAsync(string rankingType, string period = "weekly", string? scope = null, int limit = 50, CancellationToken ct = default)
    {
        var consentedPlayerIds = await dbContext.PlayerProfiles
            .Where(pp => pp.PublicConsent)
            .Select(pp => pp.PlayerId)
            .ToListAsync(ct);

        var rankings = await dbContext.CommunityRankings
            .Include(cr => cr.Player)
            .Include(cr => cr.Team)
            .Where(cr => cr.RankingType == rankingType && cr.Period == period)
            .Where(cr => cr.PlayerId == null || consentedPlayerIds.Contains(cr.PlayerId.Value))
            .OrderBy(cr => cr.Rank)
            .Take(limit)
            .ToListAsync(ct);

        var entries = rankings.Select(cr => new RankingEntryDto
        {
            Rank = cr.Rank,
            PlayerId = cr.PlayerId,
            PlayerName = cr.Player?.Name,
            TeamId = cr.TeamId,
            TeamName = cr.Team?.Name,
            Score = cr.Score
        }).ToList();

        return new CommunityRankingDto
        {
            RankingType = rankingType,
            Period = period,
            Scope = scope,
            Entries = entries,
            LastUpdated = rankings.Count != 0 ? rankings.Max(cr => cr.CalculatedAt) : DateTime.UtcNow
        };
    }
}

