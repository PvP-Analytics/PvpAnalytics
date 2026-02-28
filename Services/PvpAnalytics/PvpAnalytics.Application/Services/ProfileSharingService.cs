using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PvpAnalytics.Core.Entities;
using PvpAnalytics.Infrastructure;
using PvpAnalytics.Shared.Security;

namespace PvpAnalytics.Application.Services;

public interface IProfileSharingService
{
    Task<PlayerProfile?> GetProfileAsync(long playerId, CancellationToken ct = default);
    Task<PlayerProfile> EnsureProfileAsync(long playerId, Guid? userId = null, CancellationToken ct = default);
    Task<bool> UpdateConsentAsync(long playerId, bool publicConsent, string? ipAddress = null, CancellationToken ct = default);
    string GenerateShareToken(long playerId);
    long? ValidateShareToken(string token);
}

public class ProfileSharingService(
    PvpAnalyticsDbContext dbContext,
    IOptions<JwtOptions> jwtOptions) : IProfileSharingService
{
    public async Task<PlayerProfile?> GetProfileAsync(long playerId, CancellationToken ct = default)
    {
        return await dbContext.PlayerProfiles
            .Include(pp => pp.Player)
            .FirstOrDefaultAsync(pp => pp.PlayerId == playerId, ct);
    }

    public async Task<PlayerProfile> EnsureProfileAsync(long playerId, Guid? userId = null, CancellationToken ct = default)
    {
        var existing = await dbContext.PlayerProfiles
            .FirstOrDefaultAsync(pp => pp.PlayerId == playerId, ct);

        if (existing != null)
        {
            if (userId.HasValue && existing.LinkedUserId == null)
            {
                existing.LinkedUserId = userId;
                existing.UpdatedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(ct);
            }
            return existing;
        }

        var profile = new PlayerProfile
        {
            PlayerId = playerId,
            LinkedUserId = userId,
            PublicConsent = false,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.PlayerProfiles.Add(profile);
        await dbContext.SaveChangesAsync(ct);
        return profile;
    }

    public async Task<bool> UpdateConsentAsync(long playerId, bool publicConsent, string? ipAddress = null,
        CancellationToken ct = default)
    {
        var profile = await dbContext.PlayerProfiles
            .FirstOrDefaultAsync(pp => pp.PlayerId == playerId, ct);

        if (profile == null)
            return false;

        var auditEntry = new ConsentAuditLog
        {
            PlayerProfileId = profile.Id,
            PreviousConsent = profile.PublicConsent,
            NewConsent = publicConsent,
            ChangedAt = DateTime.UtcNow,
            ChangedByIp = ipAddress
        };

        profile.PublicConsent = publicConsent;
        profile.UpdatedAt = DateTime.UtcNow;

        dbContext.ConsentAuditLogs.Add(auditEntry);
        await dbContext.SaveChangesAsync(ct);
        return true;
    }

    public string GenerateShareToken(long playerId)
    {
        var opts = jwtOptions.Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("player_id", playerId.ToString()),
            new Claim("scope", "read:advanced_stats"),
            new Claim("purpose", "profile_share")
        };

        var token = new JwtSecurityToken(
            issuer: opts.Issuer,
            audience: opts.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public long? ValidateShareToken(string token)
    {
        var opts = jwtOptions.Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.SigningKey));
        var handler = new JwtSecurityTokenHandler();

        try
        {
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ValidIssuer = opts.Issuer,
                ValidAudience = opts.Audience,
                IssuerSigningKey = key
            }, out _);

            var playerIdClaim = principal.FindFirst("player_id")?.Value;
            var purposeClaim = principal.FindFirst("purpose")?.Value;

            if (purposeClaim != "profile_share" || playerIdClaim == null)
                return null;

            return long.TryParse(playerIdClaim, out var playerId) ? playerId : null;
        }
        catch
        {
            return null;
        }
    }
}
