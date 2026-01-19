using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PvpAnalytics.Application.Services;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Shared.Constants;

namespace PvpAnalytics.Api.Controllers;

[ApiController]
[Route(AppConstants.RouteConstants.KeyMomentsBase)]
public class KeyMomentController(IKeyMomentService service, ILogger<KeyMomentController> logger) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("match/{matchId:long}")]
    public async Task<ActionResult> GetMatchKeyMoments(long matchId, CancellationToken ct = default)
    {
        try
        {
            var result = await service.GetKeyMomentsForMatchAsync(matchId, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve key moments for match {MatchId}", matchId);
            return Ok(new KeyMomentDto
            {
                MatchId = matchId,
                MatchDate = DateTime.MinValue,
                Moments = []
            });
        }
    }

    [AllowAnonymous]
    [HttpGet("player/{playerId:long}/recent")]
    public async Task<ActionResult> GetRecentKeyMoments(
        long playerId,
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
    {
        var result = await service.GetRecentKeyMomentsAsync(playerId, limit, ct);
        return Ok(result);
    }
}