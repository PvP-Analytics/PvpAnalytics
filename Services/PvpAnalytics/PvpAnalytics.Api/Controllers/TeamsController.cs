using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PvpAnalytics.Application.Services;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Shared.Constants;

namespace PvpAnalytics.Api.Controllers;

[ApiController]
[Route(AppConstants.RouteConstants.TeamsBase)]
public class TeamsController(ITeamService teamService) : ControllerBase
{
    private Guid? GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;

        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    [AllowAnonymous]
    [HttpGet("{id:long}")]
    public async Task<ActionResult<TeamDto>> GetTeam(long id, CancellationToken ct = default)
    {
        var team = await teamService.GetTeamAsync(id, ct);
        if (team == null)
            return NotFound();

        var userId = GetUserId();
        if (!team.IsPublic && (userId == null || team.CreatedByUserId != userId))
        {
            return NotFound(); // Return NotFound instead of Forbidden to prevent information disclosure
        }

        return Ok(team);
    }

    [Authorize]
    [HttpGet("my-teams")]
    public async Task<ActionResult<List<TeamDto>>> GetMyTeams(CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var teams = await teamService.GetUserTeamsAsync(userId.Value, ct);
        return Ok(teams);
    }

    [AllowAnonymous]
    [HttpGet("search")]
    public async Task<ActionResult<List<TeamDto>>> SearchTeams(
        [FromQuery] string? bracket = null,
        [FromQuery] string? region = null,
        [FromQuery] bool? isPublic = true,
        CancellationToken ct = default)
    {
        var teams = await teamService.SearchTeamsAsync(bracket, region, isPublic, ct);
        return Ok(teams);
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<TeamDto>> CreateTeam([FromBody] CreateTeamDto dto, CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var team = await teamService.CreateTeamAsync(dto, userId.Value, ct);
        return CreatedAtAction(nameof(GetTeam), new { id = team.Id }, team);
    }

    [Authorize]
    [HttpPut("{id:long}")]
    public async Task<ActionResult<TeamDto>> UpdateTeam(long id, [FromBody] UpdateTeamDto dto, CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var team = await teamService.UpdateTeamAsync(id, dto, userId.Value, ct);
        return team == null ? NotFound() : Ok(team);
    }

    [Authorize]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> DeleteTeam(long id, CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var deleted = await teamService.DeleteTeamAsync(id, userId.Value, ct);
        return deleted ? NoContent() : NotFound();
    }

    [Authorize]
    [HttpPost("{teamId:long}/members/{playerId:long}")]
    public async Task<IActionResult> AddMember(long teamId, long playerId, CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var added = await teamService.AddMemberAsync(teamId, playerId, userId.Value, ct);
        return added ? Ok() : NotFound();
    }

    [Authorize]
    [HttpDelete("{teamId:long}/members/{playerId:long}")]
    public async Task<IActionResult> RemoveMember(long teamId, long playerId, CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var removed = await teamService.RemoveMemberAsync(teamId, playerId, userId.Value, ct);
        return removed ? NoContent() : NotFound();
    }
}

