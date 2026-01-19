using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PvpAnalytics.Application.Services;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Shared.Constants;

namespace PvpAnalytics.Api.Controllers;

[ApiController]
[Route(AppConstants.RouteConstants.RivalsBase)]
[Authorize]
public class RivalsController(IRivalService service) : ControllerBase
{
    private Guid? GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;

        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    [HttpGet]
    public async Task<ActionResult<List<RivalDto>>> GetRivals(CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var rivals = await service.GetRivalsAsync(userId.Value, ct);
        return Ok(rivals);
    }

    [HttpPost]
    public async Task<ActionResult<RivalDto>> AddRival([FromBody] CreateRivalDto dto, CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var rival = await service.AddRivalAsync(userId.Value, dto, ct);
        return rival == null ? BadRequest("Player not found or already a rival") : Ok(rival);
    }

    [HttpPut("{rivalId:long}")]
    public async Task<ActionResult<RivalDto>> UpdateRival(long rivalId, [FromBody] UpdateRivalDto dto, CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var rival = await service.UpdateRivalAsync(userId.Value, rivalId, dto, ct);
        return rival == null ? NotFound() : Ok(rival);
    }

    [HttpDelete("{rivalId:long}")]
    public async Task<IActionResult> RemoveRival(long rivalId, CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var removed = await service.RemoveRivalAsync(userId.Value, rivalId, ct);
        return removed ? NoContent() : NotFound();
    }
}

