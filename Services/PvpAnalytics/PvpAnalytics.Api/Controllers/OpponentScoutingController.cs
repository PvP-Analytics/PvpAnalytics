using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PvpAnalytics.Application.Services;

namespace PvpAnalytics.Api.Controllers;

[ApiController]
[Route("api/opponent-scouting")]
public class OpponentScoutingController(IOpponentScoutingService service) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("search")]
    public async Task<ActionResult> SearchPlayers(
        [FromQuery] string name,
        [FromQuery] string? realm = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest("Name parameter is required");
        }

        var results = await service.SearchPlayersAsync(name, realm, ct);
        return Ok(results);
    }

    [AllowAnonymous]
    [HttpGet("{playerId:long}")]
    public async Task<ActionResult> GetScoutingData(long playerId, CancellationToken ct = default)
    {
        var result = await service.GetScoutingDataAsync(playerId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("{playerId:long}/compositions")]
    public async Task<ActionResult> GetCompositions(long playerId, CancellationToken ct = default)
    {
        var result = await service.GetPlayerCompositionsAsync(playerId, null, ct);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("{playerId:long}/matchups")]
    public async Task<ActionResult> GetMatchups(long playerId, CancellationToken ct = default)
    {
        var result = await service.GetPlayerMatchupsAsync(playerId, null, ct);
        return Ok(result);
    }
}

