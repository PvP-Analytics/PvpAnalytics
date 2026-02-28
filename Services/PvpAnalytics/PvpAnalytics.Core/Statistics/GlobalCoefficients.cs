namespace PvpAnalytics.Core.Statistics;

/// <summary>Global coefficients for Bayesian win-rate smoothing: prior and significance threshold (C).</summary>
public record GlobalCoefficients
{
    /// <summary>Season-wide average win rate as a decimal (e.g. 0.5 = 50%). Must be in [0, 1].</summary>
    public double GlobalPrior { get; }

    /// <summary>Minimum match count for statistical significance (C parameter). Must be finite and non-negative.</summary>
    public double SignificanceThreshold { get; }

    public GlobalCoefficients(double globalPrior, double significanceThreshold)
    {
        if (double.IsNaN(globalPrior) || double.IsNaN(significanceThreshold))
            throw new ArgumentException("GlobalPrior and SignificanceThreshold must not be NaN.");
        if (globalPrior < 0.0 || globalPrior > 1.0)
            throw new ArgumentOutOfRangeException(nameof(globalPrior), globalPrior, "GlobalPrior must be between 0.0 and 1.0 inclusive.");
        if (!double.IsFinite(significanceThreshold) || significanceThreshold < 0)
            throw new ArgumentOutOfRangeException(nameof(significanceThreshold), significanceThreshold, "SignificanceThreshold must be finite and non-negative.");

        GlobalPrior = globalPrior;
        SignificanceThreshold = significanceThreshold;
    }

    public static readonly GlobalCoefficients Default = new(0.5, 20.0);
}
