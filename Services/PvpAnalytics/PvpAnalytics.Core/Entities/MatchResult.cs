namespace PvpAnalytics.Core.Entities;

public class MatchResult
{
    public MatchResult()
    {
        Match = null!;
        Player = null!;
        Team = string.Empty;
    }

    public long Id { get; set; }

    public long MatchId { get; set; }
    public Match Match { get; set; }

    public long PlayerId { get; set; }
    public Player Player { get; set; }

    public string Team { get; set; }
    public int RatingBefore { get; set; }
    public int RatingAfter { get; set; }
    public bool IsWinner { get; set; }
    public string? Spec { get; set; }

    /// <summary>Sum of effective damage events for this player in this match</summary>
    public int EffectiveDamageTotal { get; set; }

    /// <summary>Sum of effective healing events for this player in this match</summary>
    public int EffectiveHealingTotal { get; set; }

    /// <summary>Crowd-control events per match as a proxy for Isolated Impact</summary>
    public double CrowdControlScore { get; set; }
}