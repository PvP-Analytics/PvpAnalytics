namespace PvpAnalytics.Core.Entities;

public class PlayerProfile
{
    public long Id { get; set; }
    public long PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    public Guid? LinkedUserId { get; set; }
    public bool PublicConsent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
