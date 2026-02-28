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
    public ActionResult GenerateShareToken(long playerId)
    {
        var token = profileService.GenerateShareToken(playerId);
        return Ok(new { token, expiresIn = "2 hours" });
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
