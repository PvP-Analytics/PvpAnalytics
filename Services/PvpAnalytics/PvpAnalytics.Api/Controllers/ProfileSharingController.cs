using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PvpAnalytics.Application.Services;

namespace PvpAnalytics.Api.Controllers;

[ApiController]
[Route("api/profiles")]
public class ProfileSharingController(
    IProfileSharingService profileService,
    IOpponentScoutingService scoutingService) : ControllerBase
{
    /// <summary>
    /// Get a player's public profile (only if consent is granted).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{playerId:long}")]
    public async Task<ActionResult> GetPublicProfile(long playerId, CancellationToken ct)
    {
        var profile = await profileService.GetProfileAsync(playerId, ct);
        if (profile == null || !profile.PublicConsent)
            return NotFound("Profile is private or does not exist.");

        var scout = await scoutingService.GetScoutingDataAsync(playerId, ct);
        return Ok(scout);
    }

    /// <summary>
    /// Update consent status for a player profile (authenticated owner only).
    /// </summary>
    [Authorize]
    [HttpPut("{playerId:long}/consent")]
    public async Task<ActionResult> UpdateConsent(long playerId, [FromBody] ConsentUpdateRequest request, CancellationToken ct)
    {
        var ownership = await CheckOwnershipAsync(playerId, ct);
        if (ownership != null)
            return ownership;

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var updated = await profileService.UpdateConsentAsync(playerId, request.PublicConsent, ipAddress, ct);
        if (!updated)
            return NotFound();

        return Ok(new { playerId, request.PublicConsent });
    }

    /// <summary>
    /// Generate a short-lived share token for a player profile.
    /// </summary>
    [Authorize]
    [HttpPost("{playerId:long}/share")]
    public async Task<ActionResult> GenerateShareToken(long playerId, CancellationToken ct)
    {
        var ownership = await CheckOwnershipAsync(playerId, ct);
        if (ownership != null)
            return ownership;

        var token = profileService.GenerateShareToken(playerId);
        return Ok(new { token, expiresIn = "2 hours" });
    }

    /// <summary>
    /// Returns NotFound if profile is missing, Forbid if current user is not the linked owner, Unauthorized if user id claim is missing/invalid; otherwise null (caller may proceed).
    /// </summary>
    private async Task<ActionResult?> CheckOwnershipAsync(long playerId, CancellationToken ct)
    {
        var profile = await profileService.GetProfileAsync(playerId, ct);
        if (profile == null)
            return NotFound();

        if (!TryGetUserId(out var userId))
            return Unauthorized();

        if (!profile.LinkedUserId.HasValue || profile.LinkedUserId.Value != userId)
            return Forbid();

        return null;
    }

    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(value) || !Guid.TryParse(value, out userId))
        {
            userId = default;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Access a player's detailed profile via a share token.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("shared")]
    public async Task<ActionResult> GetSharedProfile([FromQuery] string token, CancellationToken ct)
    {
        var playerId = profileService.ValidateShareToken(token);
        if (playerId == null)
            return Unauthorized("Invalid or expired share token.");

        var scout = await scoutingService.GetScoutingDataAsync(playerId.Value, ct);
        if (scout == null)
            return NotFound();

        return Ok(scout);
    }
}

public record ConsentUpdateRequest(bool PublicConsent);
