using Microsoft.EntityFrameworkCore;
using PvpAnalytics.Core.Statistics;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Core.Entities;
using PvpAnalytics.Core.Repositories;
using PvpAnalytics.Infrastructure;

namespace PvpAnalytics.Application.Services;

public interface IRivalService
{
    Task<List<RivalDto>> GetRivalsAsync(Guid userId, CancellationToken ct = default);
    Task<RivalDto?> AddRivalAsync(Guid userId, CreateRivalDto dto, CancellationToken ct = default);
    Task<RivalDto?> UpdateRivalAsync(Guid userId, long rivalId, UpdateRivalDto dto, CancellationToken ct = default);
    Task<bool> RemoveRivalAsync(Guid userId, long rivalId, CancellationToken ct = default);
}

public class RivalService(
    IRepository<Rival> rivalRepo,
    IRepository<Player> playerRepo,
    PvpAnalyticsDbContext dbContext) : IRivalService
{
    public async Task<List<RivalDto>> GetRivalsAsync(Guid userId, CancellationToken ct = default)
    {
        var rivals = await LoadRivalsAsync(userId, ct);
        if (rivals.Count == 0)
            return new List<RivalDto>();

        var userPlayerIds = await LoadUserPlayerIdsAsync(userId, ct);
        var matchData = await LoadAndOrganizeMatchDataAsync(rivals, userPlayerIds, ct);

        var result = new List<RivalDto>();
        foreach (var rival in rivals)
        {
            var stats = CalculateRivalMatchStats(rival.OpponentPlayerId, userPlayerIds, matchData);
            result.Add(CreateRivalDto(rival, stats));
        }

        return result;
    }

    private async Task<List<Rival>> LoadRivalsAsync(Guid userId, CancellationToken ct)
    {
        return await dbContext.Rivals
            .Include(r => r.OpponentPlayer)
            .Where(r => r.OwnerUserId == userId)
            .OrderByDescending(r => r.IntensityScore)
            .ThenByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    private async Task<List<long>> LoadUserPlayerIdsAsync(Guid userId, CancellationToken ct)
    {
        return await dbContext.FavoritePlayers
            .Where(fp => fp.OwnerUserId == userId)
            .Select(fp => fp.TargetPlayerId)
            .ToListAsync(ct);
    }

    private async Task<MatchData> LoadAndOrganizeMatchDataAsync(
        List<Rival> rivals,
        List<long> userPlayerIds,
        CancellationToken ct)
    {
        var rivalOpponentIds = rivals.Select(r => r.OpponentPlayerId).ToList();
        var allPlayerIds = userPlayerIds.Union(rivalOpponentIds).ToList();

        var allMatchResults = await dbContext.MatchResults
            .Where(mr => allPlayerIds.Contains(mr.PlayerId))
            .Select(mr => new MatchResultData
            {
                MatchId = mr.MatchId,
                PlayerId = mr.PlayerId,
                IsWinner = mr.IsWinner
            })
            .ToListAsync(ct);

        var matchResultsByMatch = allMatchResults
            .GroupBy(mr => mr.MatchId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var matchesByPlayer = allMatchResults
            .GroupBy(mr => mr.PlayerId)
            .ToDictionary(g => g.Key, g => g.Select(mr => mr.MatchId).Distinct().ToHashSet());

        return new MatchData
        {
            MatchResultsByMatch = matchResultsByMatch,
            MatchesByPlayer = matchesByPlayer
        };
    }

    private static RivalMatchStats CalculateRivalMatchStats(
        long rivalOpponentId,
        List<long> userPlayerIds,
        MatchData matchData)
    {
        if (userPlayerIds.Count == 0)
        {
            return CalculateStatsWithoutUserPlayers(rivalOpponentId, matchData);
        }

        return CalculateStatsWithUserPlayers(rivalOpponentId, userPlayerIds, matchData);
    }

    private static RivalMatchStats CalculateStatsWithoutUserPlayers(
        long rivalOpponentId,
        MatchData matchData)
    {
        var matchesPlayed = 0;
        if (matchData.MatchesByPlayer.TryGetValue(rivalOpponentId, out var rivalMatches))
        {
            matchesPlayed = rivalMatches.Count;
        }

        return new RivalMatchStats
        {
            MatchesPlayed = matchesPlayed,
            Wins = null,
            Losses = null,
            WinRate = null
        };
    }

    private static RivalMatchStats CalculateStatsWithUserPlayers(
        long rivalOpponentId,
        List<long> userPlayerIds,
        MatchData matchData)
    {
        var rivalMatches = GetRivalMatches(rivalOpponentId, matchData);
        var userMatches = GetUserMatches(userPlayerIds, matchData);
        var matchesWithBoth = rivalMatches.Intersect(userMatches).ToList();
        var matchesPlayed = matchesWithBoth.Count;

        if (matchesPlayed == 0)
        {
            return new RivalMatchStats
            {
                MatchesPlayed = 0,
                Wins = 0,
                Losses = 0,
                WinRate = 0.0
            };
        }

        var wins = CountWins(matchesWithBoth, userPlayerIds, matchData);
        var losses = matchesPlayed - wins;
        var winRate = WinRateSmoothing.Smooth(wins, matchesPlayed, GlobalCoefficients.Default);

        return new RivalMatchStats
        {
            MatchesPlayed = matchesPlayed,
            Wins = wins,
            Losses = losses,
            WinRate = winRate
        };
    }

    private static HashSet<long> GetRivalMatches(long rivalOpponentId, MatchData matchData)
    {
        return matchData.MatchesByPlayer.TryGetValue(rivalOpponentId, out var rivalMatches)
            ? rivalMatches
            : new HashSet<long>();
    }

    private static HashSet<long> GetUserMatches(List<long> userPlayerIds, MatchData matchData)
    {
        return userPlayerIds
            .Where(pid => matchData.MatchesByPlayer.ContainsKey(pid))
            .SelectMany(pid => matchData.MatchesByPlayer[pid])
            .ToHashSet();
    }

    private static int CountWins(
        List<long> matchesWithBoth,
        List<long> userPlayerIds,
        MatchData matchData)
    {
        return matchesWithBoth.Count(matchId =>
        {
            if (!matchData.MatchResultsByMatch.TryGetValue(matchId, out var matchResults))
                return false;

            return matchResults.Any(mr => userPlayerIds.Contains(mr.PlayerId) && mr.IsWinner);
        });
    }

    private static RivalDto CreateRivalDto(Rival rival, RivalMatchStats stats)
    {
        return new RivalDto
        {
            Id = rival.Id,
            OpponentPlayerId = rival.OpponentPlayerId,
            OpponentPlayerName = rival.OpponentPlayer.Name,
            Realm = rival.OpponentPlayer.Realm,
            Class = rival.OpponentPlayer.Class,
            Spec = rival.OpponentPlayer.Spec,
            Notes = rival.Notes,
            IntensityScore = rival.IntensityScore,
            CreatedAt = rival.CreatedAt,
            MatchesPlayed = stats.MatchesPlayed,
            Wins = stats.Wins,
            Losses = stats.Losses,
            WinRate = stats.WinRate
        };
    }

    public async Task<RivalDto?> AddRivalAsync(Guid userId, CreateRivalDto dto, CancellationToken ct = default)
    {
        var player = await playerRepo.GetByIdAsync(dto.OpponentPlayerId, ct);
        if (player == null)
            return null;

        var existing = await dbContext.Rivals
            .FirstOrDefaultAsync(r => r.OwnerUserId == userId && r.OpponentPlayerId == dto.OpponentPlayerId, ct);

        if (existing != null)
            return null; // Already a rival

        var rival = new Rival
        {
            OwnerUserId = userId,
            OpponentPlayerId = dto.OpponentPlayerId,
            Notes = dto.Notes,
            IntensityScore = Math.Clamp(dto.IntensityScore, 1, 10),
            CreatedAt = DateTime.UtcNow
        };

        await rivalRepo.AddAsync(rival, true, ct);

        return new RivalDto
        {
            Id = rival.Id,
            OpponentPlayerId = dto.OpponentPlayerId,
            OpponentPlayerName = player.Name,
            Realm = player.Realm,
            Class = player.Class,
            Spec = player.Spec,
            Notes = rival.Notes,
            IntensityScore = rival.IntensityScore,
            CreatedAt = rival.CreatedAt,
            MatchesPlayed = 0,
            Wins = null,
            Losses = null,
            WinRate = null
        };
    }

    public async Task<RivalDto?> UpdateRivalAsync(Guid userId, long rivalId, UpdateRivalDto dto, CancellationToken ct = default)
    {
        var rival = await dbContext.Rivals
            .Include(r => r.OpponentPlayer)
            .FirstOrDefaultAsync(r => r.Id == rivalId && r.OwnerUserId == userId, ct);

        if (rival == null)
            return null;

        if (dto.Notes != null)
            rival.Notes = dto.Notes;

        if (dto.IntensityScore.HasValue)
            rival.IntensityScore = Math.Clamp(dto.IntensityScore.Value, 1, 10);

        await rivalRepo.UpdateAsync(rival, true, ct);

        return new RivalDto
        {
            Id = rival.Id,
            OpponentPlayerId = rival.OpponentPlayerId,
            OpponentPlayerName = rival.OpponentPlayer.Name,
            Realm = rival.OpponentPlayer.Realm,
            Class = rival.OpponentPlayer.Class,
            Spec = rival.OpponentPlayer.Spec,
            Notes = rival.Notes,
            IntensityScore = rival.IntensityScore,
            CreatedAt = rival.CreatedAt,
            MatchesPlayed = 0,
            Wins = null,
            Losses = null,
            WinRate = null
        };
    }

    public async Task<bool> RemoveRivalAsync(Guid userId, long rivalId, CancellationToken ct = default)
    {
        var rival = await dbContext.Rivals
            .FirstOrDefaultAsync(r => r.Id == rivalId && r.OwnerUserId == userId, ct);

        if (rival == null)
            return false;

        await rivalRepo.DeleteAsync(rival, true, ct);
        return true;
    }
}

internal class MatchData
{
    public required Dictionary<long, List<MatchResultData>> MatchResultsByMatch { get; init; }
    public required Dictionary<long, HashSet<long>> MatchesByPlayer { get; init; }
}

internal class MatchResultData
{
    public long MatchId { get; init; }
    public long PlayerId { get; init; }
    public bool IsWinner { get; init; }
}

internal class RivalMatchStats
{
    public int MatchesPlayed { get; init; }
    public int? Wins { get; init; }
    public int? Losses { get; init; }
    public double? WinRate { get; init; }
}

