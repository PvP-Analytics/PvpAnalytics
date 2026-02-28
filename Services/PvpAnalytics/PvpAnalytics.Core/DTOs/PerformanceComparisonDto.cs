namespace PvpAnalytics.Core.DTOs;

public class PerformanceComparisonDto
{
    public long PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public int? RatingMin { get; set; }
    public PlayerMetrics PlayerMetrics { get; set; } = new();
    public TopPlayerMetrics TopPlayerMetrics { get; set; } = new();
    public ComparisonGaps Gaps { get; set; } = new();
    public PercentileRankings Percentiles { get; set; } = new();
}

public class PlayerMetrics
{
    public double WinRate { get; set; }
    public double AverageDamage { get; set; }
    public double AverageHealing { get; set; }
    public double AverageCC { get; set; }
    public int CurrentRating { get; set; }
    public int PeakRating { get; set; }
    public double AverageMatchDuration { get; set; }

    public double AverageEffectiveDamage { get; set; }
    public double AverageEffectiveHealing { get; set; }
}

public class TopPlayerMetrics
{
    public double AverageWinRate { get; set; }
    public double AverageDamage { get; set; }
    public double AverageHealing { get; set; }
    public double AverageCC { get; set; }
    public double AverageRating { get; set; }
    public double AverageMatchDuration { get; set; }
}

public class ComparisonGaps
{
    public double WinRateGap { get; set; }
    public double DamageGap { get; set; }
    public double HealingGap { get; set; }
    public double CCGap { get; set; }
    public double RatingGap { get; set; }
    public List<string> Strengths { get; set; } = new();
    public List<string> Weaknesses { get; set; } = new();
}

public class PercentileRankings
{
    public double WinRatePercentile { get; set; }
    public double DamagePercentile { get; set; }
    public double HealingPercentile { get; set; }
    public double CCPercentile { get; set; }
    public double RatingPercentile { get; set; }
}

