using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PvpAnalytics.Application.Services;
using PvpAnalytics.Shared.Constants;

namespace PvpAnalytics.Api.Controllers;

[ApiController]
[Route(AppConstants.RouteConstants.TeamLeaderboardsBase)]
public class TeamLeaderboardController(ITeamLeaderboardService service) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("{bracket}")]
    public async Task<ActionResult> GetLeaderboard(
        string bracket,
        [FromQuery] string? region = null,
        [FromQuery] int? limit = 100,
        CancellationToken ct = default)
    {
        var result = await service.GetLeaderboardAsync(bracket, region, limit, ct);
        return Ok(result);
    }
}

