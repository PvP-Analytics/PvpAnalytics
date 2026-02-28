namespace PvpAnalytics.Core.Statistics;

public static class WinRateSmoothing
{
    /// <summary>
    /// Computes the Bayesian-smoothed win rate as a percentage (0–100).
    /// Formula: smoothed = (wins + C * globalPrior) / (totalMatches + C) * 100
    /// </summary>
    public static double Smooth(int wins, int totalMatches, GlobalCoefficients coefficients)
    {
        if (totalMatches <= 0)
            return Math.Round(coefficients.GlobalPrior * 100, 2);

        var smoothed = (wins + coefficients.SignificanceThreshold * coefficients.GlobalPrior)
                       / (totalMatches + coefficients.SignificanceThreshold);

        return Math.Round(smoothed * 100, 2);
    }

    /// <summary>
    /// Computes the Bayesian-smoothed win rate as a percentage (0–100)
    /// using explicit parameters.
    /// </summary>
    public static double Smooth(int wins, int totalMatches, double globalPrior, double c)
    {
        if (totalMatches <= 0)
            return Math.Round(globalPrior * 100, 2);

        var smoothed = (wins + c * globalPrior) / (totalMatches + c);
        return Math.Round(smoothed * 100, 2);
    }
}
