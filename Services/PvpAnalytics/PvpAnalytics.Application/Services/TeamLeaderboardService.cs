using Microsoft.EntityFrameworkCore;
using PvpAnalytics.Core.Statistics;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Infrastructure;

namespace PvpAnalytics.Application.Services;

public interface ITeamLeaderboardService
{
    Task<TeamLeaderboardDto> GetLeaderboardAsync(string bracket, string? region = null, int? limit = 100, CancellationToken ct = default);
}

public class TeamLeaderboardService(
    PvpAnalyticsDbContext dbContext,
    IGlobalCoefficientsProvider coefficientsProvider) : ITeamLeaderboardService
{
    public async Task<TeamLeaderboardDto> GetLeaderboardAsync(string bracket, string? region = null, int? limit = 100, CancellationToken ct = default)
    {
        var query = dbContext.Teams
            .Include(t => t.Members)
                .ThenInclude(m => m.Player)
            .Where(t => t.Bracket == bracket);

        if (!string.IsNullOrEmpty(region))
            query = query.Where(t => t.Region == region);

        var teams = await query.ToListAsync(ct);

        if (teams.Count == 0)
        {
            return new TeamLeaderboardDto
            {
                Bracket = bracket,
                Region = region,
                Entries = new List<TeamLeaderboardEntryDto>(),
                TotalTeams = 0,
                LastUpdated = DateTime.UtcNow
            };
        }

        var teamIds = teams.Select(t => t.Id).ToList();
        var teamStatsList = await dbContext.TeamMatches
            .Where(tm => teamIds.Contains(tm.TeamId))
            .GroupBy(tm => tm.TeamId)
            .Select(g => new
            {
                TeamId = g.Key,
                TotalMatches = g.Count(),
                Wins = g.Count(tm => tm.IsWin),
                LastMatchDate = g.Max(tm => tm.Match.CreatedOn)
            })
            .ToListAsync(ct);
        
        var teamStats = teamStatsList.ToDictionary(x => x.TeamId);

        var coefficients = await coefficientsProvider.GetCoefficientsAsync(ct);
        var entries = new List<TeamLeaderboardEntryDto>();

        foreach (var team in teams)
        {
            var stats = teamStats.GetValueOrDefault(team.Id);
            var totalMatches = stats?.TotalMatches ?? 0;
            var wins = stats?.Wins ?? 0;
            var losses = totalMatches - wins;
            var winRate = WinRateSmoothing.Smooth(wins, totalMatches, coefficients);
            var lastMatchDate = stats?.LastMatchDate ?? team.CreatedAt;

            entries.Add(new TeamLeaderboardEntryDto
            {
                TeamId = team.Id,
                TeamName = team.Name,
                Rating = team.Rating,
                TotalMatches = totalMatches,
                Wins = wins,
                Losses = losses,
                WinRate = winRate,
                LastMatchDate = lastMatchDate,
                MemberNames = team.Members.Select(m => m.Player.Name).ToList()
            });
        }

        entries = entries
            .OrderByDescending(e => e.Rating ?? 0)
            .ThenByDescending(e => e.WinRate)
            .ThenByDescending(e => e.TotalMatches)
            .Take(limit ?? 100)
            .ToList();

        for (int i = 0; i < entries.Count; i++)
        {
            entries[i].Rank = i + 1;
        }

        return new TeamLeaderboardDto
        {
            Bracket = bracket,
            Region = region,
            Entries = entries,
            TotalTeams = teams.Count,
            LastUpdated = DateTime.UtcNow
        };
    }
}

