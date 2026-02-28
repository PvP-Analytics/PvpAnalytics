namespace PvpAnalytics.Core.Entities;

public class CombatLogEntry
{
    public long Id { get; set; }

    public long MatchId { get; set; }
    public Match Match { get; set; } = null!;

    public DateTime Timestamp { get; set; }

    public long SourcePlayerId { get; set; }
    public Player SourcePlayer { get; set; } = null!;

    public long? TargetPlayerId { get; set; }
    public Player TargetPlayer { get; set; } = null!;

    public string Ability { get; set; } = string.Empty;
    public int DamageDone { get; set; }
    public int HealingDone { get; set; }
    public string CrowdControl { get; set; } = string.Empty;

    /// <summary>Damage dealt during vulnerability windows (kills or forced defensives)</summary>
    public int EffectiveDamage { get; set; }

    /// <summary>Healing on targets below survival threshold</summary>
    public int EffectiveHealing { get; set; }
}
