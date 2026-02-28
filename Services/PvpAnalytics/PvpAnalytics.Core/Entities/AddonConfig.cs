namespace PvpAnalytics.Core.Entities;

public class AddonConfig
{
    public long Id { get; set; }

    /// <summary>Addon type: Gladius, Plater, OmniBar, WeakAuras</summary>
    public string AddonType { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Base64 or raw import string for the addon configuration</summary>
    public string ImportString { get; set; } = string.Empty;

    /// <summary>Optional spec association (e.g. "Arms Warrior")</summary>
    public string? AssociatedSpec { get; set; }

    /// <summary>Optional composition association (e.g. "Warrior-Paladin-Druid")</summary>
    public string? AssociatedComposition { get; set; }

    public int Version { get; set; } = 1;
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
