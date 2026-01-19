using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PvpAnalytics.Application.Services;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Shared.Constants;

namespace PvpAnalytics.Api.Controllers;

[ApiController]
[Route(AppConstants.RouteConstants.FavoritesBase)]
[Authorize]
public class FavoritePlayersController(IFavoritePlayerService service) : ControllerBase
{
    private Guid? GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;

        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    [HttpGet]
    public async Task<ActionResult<List<FavoritePlayerDto>>> GetFavorites(CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var favorites = await service.GetFavoritesAsync(userId.Value, ct);
        return Ok(favorites);
    }

    [HttpPost]
    public async Task<ActionResult<FavoritePlayerDto>> AddFavorite([FromBody] CreateFavoritePlayerDto dto, CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var favorite = await service.AddFavoriteAsync(userId.Value, dto.PlayerId, ct);
        return favorite == null ? BadRequest("Player not found or already favorited") : Ok(favorite);
    }

    [HttpDelete("{playerId:long}")]
    public async Task<IActionResult> RemoveFavorite(long playerId, CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var removed = await service.RemoveFavoriteAsync(userId.Value, playerId, ct);
        return removed ? NoContent() : NotFound();
    }

    [HttpGet("{playerId:long}/is-favorite")]
    public async Task<ActionResult<bool>> IsFavorite(long playerId, CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        var isFavorite = await service.IsFavoriteAsync(userId.Value, playerId, ct);
        return Ok(isFavorite);
    }
}

