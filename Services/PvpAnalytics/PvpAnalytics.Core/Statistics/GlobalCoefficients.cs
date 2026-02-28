namespace PvpAnalytics.Core.Statistics;

/// <param name="GlobalPrior">Season-wide average win rate as a decimal (e.g. 0.5 = 50%)</param>
/// <param name="SignificanceThreshold">Minimum match count for statistical significance (C parameter)</param>
public record GlobalCoefficients(double GlobalPrior, double SignificanceThreshold)
{
    public static readonly GlobalCoefficients Default = new(0.5, 20.0);
}
