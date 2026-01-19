using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PvpAnalytics.Application.Services;
using PvpAnalytics.Shared.Constants;

namespace PvpAnalytics.Api.Controllers;

[ApiController]
[Route(AppConstants.RouteConstants.TeamSynergyBase)]
public class TeamSynergyController(ITeamSynergyService service) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("team/{teamId:long}")]
    public async Task<ActionResult> GetTeamSynergy(long teamId, CancellationToken ct = default)
    {
        var result = await service.GetTeamSynergyAsync(teamId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("partner/{player1Id:long}/{player2Id:long}")]
    public async Task<ActionResult> GetPartnerSynergy(long player1Id, long player2Id, CancellationToken ct = default)
    {
        var result = await service.GetPartnerSynergyAsync(player1Id, player2Id, ct);
        return result == null ? NotFound() : Ok(result);
    }
}

