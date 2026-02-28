using Microsoft.EntityFrameworkCore;
using PvpAnalytics.Core.Statistics;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Core.Entities;
using PvpAnalytics.Core.Repositories;
using PvpAnalytics.Infrastructure;

namespace PvpAnalytics.Application.Services;

public interface ITeamCompositionService
{
    Task<List<TeamCompositionDto>> GetPlayerTeamsAsync(long playerId, CancellationToken ct = default);
    Task<TeamCompositionDto?> GetTeamStatsAsync(string composition, CancellationToken ct = default);
    Task<PlayerSynergyDto?> GetPlayerSynergyAsync(long player1Id, long player2Id, CancellationToken ct = default);
}

public class TeamCompositionService(
    IRepository<Player> playerRepo,
    PvpAnalyticsDbContext dbContext) : ITeamCompositionService
{
    public async Task<List<TeamCompositionDto>> GetPlayerTeamsAsync(long playerId, CancellationToken ct = default)
    {
        var playerMatchIds = await dbContext.MatchResults
            .Where(mr => mr.PlayerId == playerId)
            .Select(mr => mr.MatchId)
            .Distinct()
            .ToListAsync(ct);

        if (playerMatchIds.Count == 0)
            return [];

        var teamData = await dbContext.MatchResults
            .Include(mr => mr.Player)
            .Include(mr => mr.Match)
            .Where(mr => playerMatchIds.Contains(mr.MatchId))
            .GroupBy(mr => new { mr.MatchId, mr.Team })
            .Select(g => new
            {
                g.Key.MatchId,
                g.Key.Team,
                Players = g.Select(mr => new
                {
                    mr.PlayerId,
                    mr.Player.Name,
                    mr.Player.Realm,
                    mr.Player.Class,
                    mr.Spec
                }).ToList(),
                IsWinner = g.Any(mr => mr.IsWinner),
                Rating = g.Average(mr => (double)mr.RatingBefore),
                MatchDate = g.First().Match.CreatedOn
            })
            .ToListAsync(ct);

        var playerTeams = teamData
            .Where(td => td.Players.Any(p => p.PlayerId == playerId))
            .ToList();

        var compositionGroups = playerTeams
            .GroupBy(td =>
            {
                var classes = td.Players
                    .Where(p => !string.IsNullOrWhiteSpace(p.Class))
                    .Select(p => p.Class)
                    .OrderBy(c => c)
                    .ToList();
                return string.Join("-", classes);
            })
            .Select(g =>
            {
                var firstMatch = g.OrderBy(td => td.MatchDate).First();
                var lastMatch = g.OrderByDescending(td => td.MatchDate).First();
                var members = firstMatch.Players.Select(p => new Core.DTOs.TeamMember
                {
                    PlayerId = p.PlayerId,
                    PlayerName = p.Name,
                    Realm = p.Realm,
                    Class = p.Class ?? string.Empty,
                    Spec = p.Spec
                }).ToList();

                return new TeamCompositionDto
                {
                    TeamId = g.GetHashCode(), // Simple ID generation
                    Composition = g.Key,
                    Members = members,
                    TotalMatches = g.Count(),
                    Wins = g.Count(td => td.IsWinner),
                    Losses = g.Count(td => !td.IsWinner),
                    WinRate = g.Any() ? WinRateSmoothing.Smooth(g.Count(td => td.IsWinner), g.Count(), GlobalCoefficients.Default) : 0,
                    AverageRating = Math.Round(g.Average(td => td.Rating), 0),
                    PeakRating = (int)Math.Round(g.Max(td => td.Rating), 0),
                    SynergyScore = CalculateSynergyScore(g.Count(td => td.IsWinner), g.Count()),
                    FirstMatchDate = firstMatch.MatchDate,
                    LastMatchDate = lastMatch.MatchDate
                };
            })
            .OrderByDescending(tc => tc.TotalMatches)
            .ToList();

        return compositionGroups;
    }

    public Task<TeamCompositionDto?> GetTeamStatsAsync(string composition, CancellationToken ct = default)
    {
        return Task.FromResult<TeamCompositionDto?>(null);
    }

    public async Task<PlayerSynergyDto?> GetPlayerSynergyAsync(long player1Id, long player2Id, CancellationToken ct = default)
    {
        var player1 = await playerRepo.GetByIdAsync(player1Id, ct);
        var player2 = await playerRepo.GetByIdAsync(player2Id, ct);

        if (player1 == null || player2 == null)
            return null;

        var player1Matches = await dbContext.MatchResults
            .Where(mr => mr.PlayerId == player1Id)
            .Select(mr => new { mr.MatchId, mr.Team })
            .ToListAsync(ct);

        var player2Matches = await dbContext.MatchResults
            .Where(mr => mr.PlayerId == player2Id)
            .Select(mr => new { mr.MatchId, mr.Team })
            .ToListAsync(ct);

        var togetherMatches = player1Matches
            .Join(player2Matches,
                p1 => new { p1.MatchId, p1.Team },
                p2 => new { p2.MatchId, p2.Team },
                (p1, _) => p1.MatchId)
            .Distinct()
            .ToList();

        if (togetherMatches.Count == 0)
        {
            return new PlayerSynergyDto
            {
                Player1Id = player1Id,
                Player1Name = player1.Name,
                Player2Id = player2Id,
                Player2Name = player2.Name,
                MatchesTogether = 0
            };
        }

        var togetherResults = await dbContext.MatchResults
            .Include(mr => mr.Match)
            .Where(mr => togetherMatches.Contains(mr.MatchId) &&
                        (mr.PlayerId == player1Id || mr.PlayerId == player2Id))
            .ToListAsync(ct);

        var wins = togetherResults
            .GroupBy(mr => mr.MatchId)
            .Count(g => g.Any(mr => mr.IsWinner));

        var avgRating = togetherResults
            .GroupBy(mr => mr.MatchId)
            .Select(g => g.Average(mr => (double)mr.RatingBefore))
            .DefaultIfEmpty(0)
            .Average();

        var dto = new PlayerSynergyDto
        {
            Player1Id = player1Id,
            Player1Name = player1.Name,
            Player2Id = player2Id,
            Player2Name = player2.Name,
            MatchesTogether = togetherMatches.Count,
            WinsTogether = wins,
            WinRateTogether = WinRateSmoothing.Smooth(wins, togetherMatches.Count, GlobalCoefficients.Default),
            AverageRatingTogether = Math.Round(avgRating, 0),
            SynergyScore = CalculateSynergyScore(wins, togetherMatches.Count)
        };

        var teamCompositions = await GetPlayerTeamsAsync(player1Id, ct);
        dto.CommonCompositions = teamCompositions
            .Where(tc => tc.Members.Any(m => m.PlayerId == player2Id))
            .ToList();

        return dto;
    }

    private static double CalculateSynergyScore(int wins, int totalMatches)
    {
        if (totalMatches == 0) return 0;
        var winRate = wins / (double)totalMatches;
        var baseScore = winRate * 100;
        var matchBonus = Math.Min(totalMatches / 10.0, 10); // Cap bonus at 10
        return Math.Round(baseScore + matchBonus, 2);
    }
}

