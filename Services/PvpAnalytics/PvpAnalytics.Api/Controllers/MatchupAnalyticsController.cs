using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PvpAnalytics.Application.Services;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Shared.Constants;

namespace PvpAnalytics.Api.Controllers;

[ApiController]
[Route(AppConstants.RouteConstants.MatchupAnalyticsBase)]
public class MatchupAnalyticsController(IMatchupAnalyticsService service) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult> GetMatchup(
        [FromQuery] MatchupQueryDto query,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.Class1) || string.IsNullOrWhiteSpace(query.Class2))
        {
            return BadRequest("class1 and class2 parameters are required");
        }
        

        var result = await service.GetMatchupAsync(query, ct);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("{playerId:long}/weaknesses")]
    public async Task<ActionResult> GetWeaknesses(long playerId, CancellationToken ct = default)
    {
        var result = await service.GetPlayerMatchupSummaryAsync(playerId, ct);
        return Ok(result.Weaknesses);
    }

    [AllowAnonymous]
    [HttpGet("{playerId:long}/strengths")]
    public async Task<ActionResult> GetStrengths(long playerId, CancellationToken ct = default)
    {
        var result = await service.GetPlayerMatchupSummaryAsync(playerId, ct);
        return Ok(result.Strengths);
    }

    [AllowAnonymous]
    [HttpGet("{playerId:long}/summary")]
    public async Task<ActionResult> GetSummary(long playerId, CancellationToken ct = default)
    {
        var result = await service.GetPlayerMatchupSummaryAsync(playerId, ct);
        return Ok(result);
    }
}

