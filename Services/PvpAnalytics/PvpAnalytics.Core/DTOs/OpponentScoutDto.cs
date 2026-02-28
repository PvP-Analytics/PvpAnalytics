namespace PvpAnalytics.Core.DTOs;

public class OpponentScoutDto
{
    public long PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string Realm { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public string? CurrentSpec { get; set; }
    public int CurrentRating { get; set; }
    public int PeakRating { get; set; }
    public int TotalMatches { get; set; }
    public double WinRate { get; set; }
    public List<CompositionWinRate> CommonCompositions { get; set; } = new();
    public List<MapPreference> PreferredMaps { get; set; } = new();
    public PlaystylePattern Playstyle { get; set; } = new();
    public List<ClassMatchup> ClassMatchups { get; set; } = new();
}

public class CompositionWinRate
{
    public string Composition { get; set; } = string.Empty; // e.g., "Rogue-Mage-Priest"
    public int Matches { get; set; }
    public int Wins { get; set; }
    public double WinRate { get; set; }
    public double AverageRating { get; set; }
}

public class MapPreference
{
    public string MapName { get; set; } = string.Empty;
    public int Matches { get; set; }
    public int Wins { get; set; }
    public double WinRate { get; set; }
}

public class PlaystylePattern
{
    public double AverageDamagePerMatch { get; set; }
    public double AverageHealingPerMatch { get; set; }
    public double AverageCCPerMatch { get; set; }
    public string Style { get; set; } = string.Empty;
    public double AverageMatchDuration { get; set; }

    public double AverageEffectiveDamage { get; set; }
    public double AverageEffectiveHealing { get; set; }
}

public class ClassMatchup
{
    public string OpponentClass { get; set; } = string.Empty;
    public string? OpponentSpec { get; set; }
    public int Matches { get; set; }
    public int Wins { get; set; }
    public double WinRate { get; set; }
}

