using Microsoft.EntityFrameworkCore;
using PvpAnalytics.Core.Statistics;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Core.Entities;
using PvpAnalytics.Core.Repositories;
using PvpAnalytics.Infrastructure;

namespace PvpAnalytics.Application.Services;

public interface IOpponentScoutingService
{
    Task<OpponentScoutDto?> GetScoutingDataAsync(long playerId, CancellationToken ct = default);
    Task<List<OpponentScoutDto>> SearchPlayersAsync(string name, string? realm = null, CancellationToken ct = default);
    Task<List<CompositionWinRate>> GetPlayerCompositionsAsync(long playerId, GlobalCoefficients? coefficients = null, CancellationToken ct = default);
    Task<List<ClassMatchup>> GetPlayerMatchupsAsync(long playerId, GlobalCoefficients? coefficients = null, CancellationToken ct = default);
}

public class OpponentScoutingService(
    IRepository<Player> playerRepo,
    IGlobalCoefficientsProvider coefficientsProvider,
    PvpAnalyticsDbContext dbContext) : IOpponentScoutingService
{
    public async Task<OpponentScoutDto?> GetScoutingDataAsync(long playerId, CancellationToken ct = default)
    {
        var player = await playerRepo.GetByIdAsync(playerId, ct);
        if (player == null) return null;

        var scout = new OpponentScoutDto
        {
            PlayerId = player.Id,
            PlayerName = player.Name,
            Realm = player.Realm,
            Class = player.Class,
        };

        var matchResults = await dbContext.MatchResults
            .Include(mr => mr.Match)
            .Where(mr => mr.PlayerId == playerId)
            .ToListAsync(ct);

        if (matchResults.Count == 0)
            return scout;

        var coefficients = await coefficientsProvider.GetCoefficientsAsync(ct);
        var totalMatches = matchResults.Count;
        var wins = matchResults.Count(mr => mr.IsWinner);
        scout.TotalMatches = totalMatches;
        scout.WinRate = WinRateSmoothing.Smooth(wins, totalMatches, coefficients);

        var latestResult = matchResults.OrderByDescending(mr => mr.Match.CreatedOn).First();
        scout.CurrentRating = latestResult.RatingAfter;
        scout.PeakRating = matchResults.Max(mr => Math.Max(mr.RatingBefore, mr.RatingAfter));
        scout.CurrentSpec = latestResult.Spec;

        scout.CommonCompositions = await GetPlayerCompositionsAsync(playerId, coefficients, ct);

        var mapStats = matchResults
            .GroupBy(mr => mr.Match.MapName)
            .Select(g => new MapPreference
            {
                MapName = g.Key,
                Matches = g.Count(),
                Wins = g.Count(m => m.IsWinner),
                WinRate = WinRateSmoothing.Smooth(g.Count(m => m.IsWinner), g.Count(), coefficients)
            })
            .OrderByDescending(m => m.Matches)
            .Take(10)
            .ToList();

        scout.PreferredMaps = mapStats;

        var combatLogs = await dbContext.CombatLogEntries
            .Where(c => c.SourcePlayerId == playerId)
            .ToListAsync(ct);

        var matchIds = matchResults.Select(mr => mr.MatchId).Distinct().ToList();
        var matches = await dbContext.Matches
            .Where(m => matchIds.Contains(m.Id))
            .ToListAsync(ct);

        var avgDamage = combatLogs.Count != 0 ? combatLogs.Average(c => (double)c.DamageDone) : 0;
        var avgHealing = combatLogs.Count != 0 ? combatLogs.Average(c => (double)c.HealingDone) : 0;
        var avgCc = combatLogs.Count != 0
            ? combatLogs.Count(c => !string.IsNullOrWhiteSpace(c.CrowdControl)) / (double)matchIds.Count
            : 0;
        var avgDuration = matches.Count != 0 ? matches.Average(m => (double)m.Duration) : 0;

        var avgEffDamage = combatLogs.Count != 0 ? combatLogs.Average(c => (double)c.EffectiveDamage) : 0;
        var avgEffHealing = combatLogs.Count != 0 ? combatLogs.Average(c => (double)c.EffectiveHealing) : 0;

        scout.Playstyle = new PlaystylePattern
        {
            AverageDamagePerMatch = Math.Round(avgDamage, 2),
            AverageHealingPerMatch = Math.Round(avgHealing, 2),
            AverageCCPerMatch = Math.Round(avgCc, 2),
            AverageMatchDuration = Math.Round(avgDuration, 2),
            Style = DeterminePlaystyle(avgDamage, avgHealing),
            AverageEffectiveDamage = Math.Round(avgEffDamage, 2),
            AverageEffectiveHealing = Math.Round(avgEffHealing, 2)
        };

        scout.ClassMatchups = await GetPlayerMatchupsAsync(playerId, coefficients, ct);

        return scout;
    }

    public async Task<List<OpponentScoutDto>> SearchPlayersAsync(string name, string? realm = null,
        CancellationToken ct = default)
    {
        var query = dbContext.Players.AsQueryable();

        if (!string.IsNullOrWhiteSpace(name))
        {
            query = query.Where(p => EF.Functions.Like(p.Name, $"%{name}%"));
        }

        if (!string.IsNullOrWhiteSpace(realm))
        {
            query = query.Where(p => EF.Functions.Like(p.Realm, $"%{realm}%"));
        }

        var players = await query.Take(20).ToListAsync(ct);
        var results = new List<OpponentScoutDto>();

        foreach (var player in players)
        {
            var scout = await GetScoutingDataAsync(player.Id, ct);
            if (scout != null)
                results.Add(scout);
        }

        return results;
    }

    public async Task<List<CompositionWinRate>> GetPlayerCompositionsAsync(long playerId,
        GlobalCoefficients? coefficients = null, CancellationToken ct = default)
    {
        var coeffs = coefficients ?? await coefficientsProvider.GetCoefficientsAsync(ct);

        var playerMatches = await dbContext.MatchResults
            .Where(mr => mr.PlayerId == playerId)
            .Select(mr => mr.MatchId)
            .Distinct()
            .ToListAsync(ct);

        if (playerMatches.Count == 0)
            return [];

        var teamCompositions = await dbContext.MatchResults
            .Include(mr => mr.Player)
            .Include(mr => mr.Match)
            .Where(mr => playerMatches.Contains(mr.MatchId))
            .GroupBy(mr => new { mr.MatchId, mr.Team })
            .Select(g => new
            {
                g.Key.MatchId,
                g.Key.Team,
                Players = g.Select(mr => new { mr.Player.Class, mr.Spec }).ToList(),
                IsWinner = g.Any(mr => mr.IsWinner),
                Rating = g.Average(mr => (double)mr.RatingBefore)
            })
            .ToListAsync(ct);

        var compositionGroups = teamCompositions
            .Where(tc => tc.Players.Any(_ => true))
            .GroupBy(tc =>
            {
                var classes = tc.Players
                    .Where(p => !string.IsNullOrWhiteSpace(p.Class))
                    .Select(p => p.Class)
                    .OrderBy(c => c)
                    .ToList();
                return string.Join("-", classes);
            })
            .Select(g => new CompositionWinRate
            {
                Composition = g.Key,
                Matches = g.Count(),
                Wins = g.Count(tc => tc.IsWinner),
                WinRate = WinRateSmoothing.Smooth(g.Count(tc => tc.IsWinner), g.Count(), coeffs),
                AverageRating = Math.Round(g.Average(tc => tc.Rating), 0)
            })
            .OrderByDescending(c => c.Matches)
            .Take(10)
            .ToList();

        return compositionGroups;
    }

    public async Task<List<ClassMatchup>> GetPlayerMatchupsAsync(long playerId, GlobalCoefficients? coefficients = null, CancellationToken ct = default)
    {
        var coeffs = coefficients ?? await coefficientsProvider.GetCoefficientsAsync(ct);

        var playerMatchIds = await dbContext.MatchResults
            .Where(mr => mr.PlayerId == playerId)
            .Select(mr => mr.MatchId)
            .ToListAsync(ct);

        if (playerMatchIds.Count == 0)
            return [];

        var playerTeam = await dbContext.MatchResults
            .Where(mr => mr.PlayerId == playerId)
            .Select(mr => new { mr.MatchId, mr.Team })
            .ToListAsync(ct);

        // Load all relevant match results into memory so we can safely apply
        // team-based filtering without relying on provider-specific translation.
        var matchResults = await dbContext.MatchResults
            .Include(mr => mr.Player)
            .Include(mr => mr.Match)
            .Where(mr => playerMatchIds.Contains(mr.MatchId))
            .ToListAsync(ct);

        var opponentResults = matchResults
            .Where(mr => !playerTeam.Any(pt => pt.MatchId == mr.MatchId && pt.Team == mr.Team))
            .ToList();

        var matchups = opponentResults
            .GroupBy(mr => new { mr.Player.Class, mr.Spec })
            .Select(g => new ClassMatchup
            {
                OpponentClass = g.Key.Class ?? "Unknown",
                OpponentSpec = g.Key.Spec,
                Matches = g.Count(),
                Wins = g.Count(mr => !mr.IsWinner), // Opponent lost = player won
                WinRate = WinRateSmoothing.Smooth(g.Count(mr => !mr.IsWinner), g.Count(), coeffs)
            })
            .OrderByDescending(m => m.Matches)
            .Take(20)
            .ToList();

        return matchups;
    }

    private static string DeterminePlaystyle(double avgDamage, double avgHealing)
    {
        if (avgDamage > avgHealing * 2)
            return "Aggressive";
        return avgHealing > avgDamage * 2 ? "Defensive" : "Balanced";
    }
}