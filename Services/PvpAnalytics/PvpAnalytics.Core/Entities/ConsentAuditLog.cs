namespace PvpAnalytics.Core.Entities;

public class ConsentAuditLog
{
    public long Id { get; set; }
    public long PlayerProfileId { get; set; }
    public PlayerProfile Profile { get; set; } = null!;

    public bool PreviousConsent { get; set; }
    public bool NewConsent { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string? ChangedByIp { get; set; }
}
