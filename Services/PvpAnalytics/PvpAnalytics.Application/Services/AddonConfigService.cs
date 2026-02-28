using Microsoft.EntityFrameworkCore;
using PvpAnalytics.Core.Entities;
using PvpAnalytics.Infrastructure;

namespace PvpAnalytics.Application.Services;

public interface IAddonConfigService
{
    Task<List<AddonConfig>> GetBySpecAsync(string spec, CancellationToken ct = default);
    Task<List<AddonConfig>> GetByCompositionAsync(string composition, CancellationToken ct = default);
    Task<List<AddonConfig>> GetByTypeAsync(string addonType, CancellationToken ct = default);
    Task<AddonConfig?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<AddonConfig> CreateAsync(AddonConfig config, CancellationToken ct = default);
    Task<bool> UpdateAsync(long id, AddonConfig updated, Guid userId, CancellationToken ct = default);
    Task<bool> DeleteAsync(long id, Guid userId, CancellationToken ct = default);
}

public class AddonConfigService(PvpAnalyticsDbContext dbContext) : IAddonConfigService
{
    public async Task<List<AddonConfig>> GetBySpecAsync(string spec, CancellationToken ct = default)
    {
        return await dbContext.AddonConfigs
            .Where(ac => ac.AssociatedSpec == spec)
            .OrderByDescending(ac => ac.Version)
            .ToListAsync(ct);
    }

    public async Task<List<AddonConfig>> GetByCompositionAsync(string composition, CancellationToken ct = default)
    {
        return await dbContext.AddonConfigs
            .Where(ac => ac.AssociatedComposition == composition)
            .OrderByDescending(ac => ac.Version)
            .ToListAsync(ct);
    }

    public async Task<List<AddonConfig>> GetByTypeAsync(string addonType, CancellationToken ct = default)
    {
        return await dbContext.AddonConfigs
            .Where(ac => ac.AddonType == addonType)
            .OrderByDescending(ac => ac.Version)
            .ToListAsync(ct);
    }

    public async Task<AddonConfig?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        return await dbContext.AddonConfigs.FindAsync([id], ct);
    }

    public async Task<AddonConfig> CreateAsync(AddonConfig config, CancellationToken ct = default)
    {
        config.CreatedAt = DateTime.UtcNow;
        dbContext.AddonConfigs.Add(config);
        await dbContext.SaveChangesAsync(ct);
        return config;
    }

    public async Task<bool> UpdateAsync(long id, AddonConfig updated, Guid userId, CancellationToken ct = default)
    {
        var existing = await dbContext.AddonConfigs.FindAsync([id], ct);
        if (existing == null || existing.CreatedByUserId != userId)
            return false;

        existing.Name = updated.Name;
        existing.ImportString = updated.ImportString;
        existing.AssociatedSpec = updated.AssociatedSpec;
        existing.AssociatedComposition = updated.AssociatedComposition;
        existing.Version++;
        existing.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(long id, Guid userId, CancellationToken ct = default)
    {
        var existing = await dbContext.AddonConfigs.FindAsync([id], ct);
        if (existing == null || existing.CreatedByUserId != userId)
            return false;

        dbContext.AddonConfigs.Remove(existing);
        await dbContext.SaveChangesAsync(ct);
        return true;
    }
}
