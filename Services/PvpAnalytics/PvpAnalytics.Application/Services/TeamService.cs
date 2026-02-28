using Microsoft.EntityFrameworkCore;
using PvpAnalytics.Core.Statistics;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Core.Entities;
using PvpAnalytics.Core.Repositories;
using PvpAnalytics.Infrastructure;
using TeamMemberEntity = PvpAnalytics.Core.Entities.TeamMember;
using TeamMemberDto = PvpAnalytics.Core.DTOs.TeamMemberDto;

namespace PvpAnalytics.Application.Services;

public interface ITeamService
{
    Task<TeamDto?> GetTeamAsync(long teamId, CancellationToken ct = default);
    Task<List<TeamDto>> GetUserTeamsAsync(Guid userId, CancellationToken ct = default);

    Task<List<TeamDto>> SearchTeamsAsync(string? bracket = null, string? region = null, bool? isPublic = true,
        CancellationToken ct = default);

    Task<TeamDto> CreateTeamAsync(CreateTeamDto dto, Guid userId, CancellationToken ct = default);
    Task<TeamDto?> UpdateTeamAsync(long teamId, UpdateTeamDto dto, Guid userId, CancellationToken ct = default);
    Task<bool> DeleteTeamAsync(long teamId, Guid userId, CancellationToken ct = default);
    Task<bool> AddMemberAsync(long teamId, long playerId, Guid userId, CancellationToken ct = default);
    Task<bool> RemoveMemberAsync(long teamId, long playerId, Guid userId, CancellationToken ct = default);
}

public class TeamService(
    IRepository<Team> teamRepo,
    IRepository<TeamMemberEntity> teamMemberRepo,
    IRepository<Player> playerRepo,
    PvpAnalyticsDbContext dbContext) : ITeamService
{
    public async Task<TeamDto?> GetTeamAsync(long teamId, CancellationToken ct = default)
    {
        var team = await dbContext.Teams
            .Include(t => t.Members)
            .ThenInclude(m => m.Player)
            .Include(t => t.TeamMatches)
            .FirstOrDefaultAsync(t => t.Id == teamId, ct);

        if (team == null) return null;

        return MapToDto(team);
    }

    public async Task<List<TeamDto>> GetUserTeamsAsync(Guid userId, CancellationToken ct = default)
    {
        var teams = await dbContext.Teams
            .Include(t => t.Members)
            .ThenInclude(m => m.Player)
            .Include(t => t.TeamMatches)
            .Where(t => t.CreatedByUserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        return teams.Select(MapToDto).ToList();
    }

    public async Task<List<TeamDto>> SearchTeamsAsync(string? bracket = null, string? region = null,
        bool? isPublic = true, CancellationToken ct = default)
    {
        var query = dbContext.Teams
            .Include(t => t.Members)
            .ThenInclude(m => m.Player)
            .Include(t => t.TeamMatches)
            .AsQueryable();

        if (!string.IsNullOrEmpty(bracket))
            query = query.Where(t => t.Bracket == bracket);

        if (!string.IsNullOrEmpty(region))
            query = query.Where(t => t.Region == region);

        if (isPublic.HasValue)
            query = query.Where(t => t.IsPublic == isPublic.Value);

        var teams = await query
            .OrderByDescending(t => t.Rating ?? 0)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        return teams.Select(MapToDto).ToList();
    }

    public async Task<TeamDto> CreateTeamAsync(CreateTeamDto dto, Guid userId, CancellationToken ct = default)
    {
        var team = new Team
        {
            Name = dto.Name,
            Bracket = dto.Bracket,
            Region = dto.Region,
            IsPublic = dto.IsPublic,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        await teamRepo.AddAsync(team, true, ct);

        foreach (var playerId in dto.PlayerIds)
        {
            var player = await playerRepo.GetByIdAsync(playerId, ct);
            if (player != null)
            {
                var member = new TeamMemberEntity
                {
                    TeamId = team.Id,
                    PlayerId = playerId,
                    JoinedAt = DateTime.UtcNow,
                    IsPrimary = false
                };
                await teamMemberRepo.AddAsync(member, true, ct);
            }
        }

        await dbContext.Entry(team).Collection(t => t.Members).LoadAsync(ct);
        foreach (var member in team.Members)
        {
            await dbContext.Entry(member).Reference(m => m.Player).LoadAsync(ct);
        }

        await dbContext.Entry(team).Collection(t => t.TeamMatches).LoadAsync(ct);

        return MapToDto(team);
    }

    public async Task<TeamDto?> UpdateTeamAsync(long teamId, UpdateTeamDto dto, Guid userId,
        CancellationToken ct = default)
    {
        var team = await dbContext.Teams
            .Include(t => t.Members)
            .ThenInclude(m => m.Player)
            .Include(t => t.TeamMatches)
            .FirstOrDefaultAsync(t => t.Id == teamId, ct);

        if (team == null) return null;

        if (team.CreatedByUserId != userId)
            return null;

        if (!string.IsNullOrEmpty(dto.Name))
            team.Name = dto.Name;

        if (dto.Bracket != null)
            team.Bracket = dto.Bracket;

        if (dto.Region != null)
            team.Region = dto.Region;

        if (dto.IsPublic.HasValue)
            team.IsPublic = dto.IsPublic.Value;

        team.UpdatedAt = DateTime.UtcNow;

        await teamRepo.UpdateAsync(team, true, ct);

        return MapToDto(team);
    }

    public async Task<bool> DeleteTeamAsync(long teamId, Guid userId, CancellationToken ct = default)
    {
        var team = await teamRepo.GetByIdAsync(teamId, ct);
        if (team == null || team.CreatedByUserId != userId)
            return false;

        await teamRepo.DeleteAsync(team, true, ct);
        return true;
    }

    public async Task<bool> AddMemberAsync(long teamId, long playerId, Guid userId, CancellationToken ct = default)
    {
        var team = await teamRepo.GetByIdAsync(teamId, ct);
        if (team == null || team.CreatedByUserId != userId)
            return false;

        var existing = await dbContext.TeamMembers
            .FirstOrDefaultAsync(tm => tm.TeamId == teamId && tm.PlayerId == playerId, ct);

        if (existing != null)
            return false;

        var member = new TeamMemberEntity
        {
            TeamId = teamId,
            PlayerId = playerId,
            JoinedAt = DateTime.UtcNow,
            IsPrimary = false
        };

        await teamMemberRepo.AddAsync(member, true, ct);
        return true;
    }

    public async Task<bool> RemoveMemberAsync(long teamId, long playerId, Guid userId, CancellationToken ct = default)
    {
        var team = await teamRepo.GetByIdAsync(teamId, ct);
        if (team == null || team.CreatedByUserId != userId)
            return false;

        var member = await dbContext.TeamMembers
            .FirstOrDefaultAsync(tm => tm.TeamId == teamId && tm.PlayerId == playerId, ct);

        if (member == null)
            return false;

        await teamMemberRepo.DeleteAsync(member, true, ct);
        return true;
    }

    private static TeamDto MapToDto(Team team)
    {
        var teamMatches = team.TeamMatches;

        var totalMatches = teamMatches.Count;
        var wins = teamMatches.Count(tm => tm.IsWin);
        var losses = totalMatches - wins;
        var winRate = WinRateSmoothing.Smooth(wins, totalMatches, GlobalCoefficients.Default);

        return new TeamDto
        {
            Id = team.Id,
            Name = team.Name,
            Rating = team.Rating,
            Bracket = team.Bracket,
            Region = team.Region,
            CreatedByUserId = team.CreatedByUserId,
            IsPublic = team.IsPublic,
            CreatedAt = team.CreatedAt,
            UpdatedAt = team.UpdatedAt,
            Members = team.Members.Select(m => new TeamMemberDto
            {
                Id = m.Id,
                TeamId = m.TeamId,
                PlayerId = m.PlayerId,
                PlayerName = m.Player.Name,
                Realm = m.Player.Realm,
                Class = m.Player.Class,
                Spec = m.Player.Spec,
                JoinedAt = m.JoinedAt,
                Role = m.Role,
                IsPrimary = m.IsPrimary
            }).ToList(),
            TotalMatches = totalMatches,
            Wins = wins,
            Losses = losses,
            WinRate = winRate
        };
    }
}