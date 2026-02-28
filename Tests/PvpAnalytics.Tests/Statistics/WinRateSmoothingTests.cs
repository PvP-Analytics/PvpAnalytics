using PvpAnalytics.Core.Statistics;
using Xunit;

namespace PvpAnalytics.Tests.Statistics;

public class WinRateSmoothingTests
{
    [Fact]
    public void Smooth_ZeroMatches_ReturnsGlobalPrior()
    {
        var coefficients = new GlobalCoefficients(0.5, 20);
        var result = WinRateSmoothing.Smooth(0, 0, coefficients);
        Assert.Equal(50.0, result);
    }

    [Fact]
    public void Smooth_OneMatch_RegularizesTowardPrior()
    {
        var coefficients = new GlobalCoefficients(0.5, 20);
        var result = WinRateSmoothing.Smooth(1, 1, coefficients);
        // (1 + 20 * 0.5) / (1 + 20) = 11 / 21 = 0.5238... * 100 = 52.38
        Assert.True(result < 100.0, "Should be pulled toward 50% prior");
        Assert.True(result > 50.0, "Should be above prior since we have 1 win");
    }

    [Fact]
    public void Smooth_AllWins_SmallSample_RegularizesTowardPrior()
    {
        var coefficients = new GlobalCoefficients(0.5, 20);
        var result = WinRateSmoothing.Smooth(3, 3, coefficients);
        // (3 + 10) / (3 + 20) = 13 / 23 = 56.52
        Assert.True(result < 70.0, "3/3 should be heavily regularized with C=20");
        Assert.True(result > 50.0);
    }

    [Fact]
    public void Smooth_ConvergesToRawRate_LargeSample()
    {
        var coefficients = new GlobalCoefficients(0.5, 20);
        var rawRate = 65.0;
        var result = WinRateSmoothing.Smooth(650, 1000, coefficients);
        // (650 + 10) / (1000 + 20) = 660 / 1020 = 64.71
        Assert.True(Math.Abs(result - rawRate) < 1.0,
            $"With 1000 matches, smoothed ({result:F2}) should converge to raw ({rawRate})");
    }

    [Fact]
    public void Smooth_ConvergesToPrior_VerySmallSample()
    {
        var coefficients = new GlobalCoefficients(0.5, 20);
        var result = WinRateSmoothing.Smooth(0, 1, coefficients);
        // (0 + 10) / (1 + 20) = 10 / 21 = 47.62
        Assert.True(Math.Abs(result - 50.0) < 5.0,
            "1 loss should stay near prior");
    }

    [Fact]
    public void Smooth_AllLosses_ProducesSensibleOutput()
    {
        var coefficients = new GlobalCoefficients(0.5, 20);
        var result = WinRateSmoothing.Smooth(0, 10, coefficients);
        // (0 + 10) / (10 + 20) = 10 / 30 = 33.33
        Assert.True(result > 0.0);
        Assert.True(result < 50.0);
    }

    [Fact]
    public void Smooth_AllWins_ProducesSensibleOutput()
    {
        var coefficients = new GlobalCoefficients(0.5, 20);
        var result = WinRateSmoothing.Smooth(10, 10, coefficients);
        // (10 + 10) / (10 + 20) = 20 / 30 = 66.67
        Assert.True(result > 50.0);
        Assert.True(result < 100.0);
    }

    [Fact]
    public void Smooth_DifferentPrior_AffectsResult()
    {
        var lowPrior = new GlobalCoefficients(0.3, 20);
        var highPrior = new GlobalCoefficients(0.7, 20);

        var resultLow = WinRateSmoothing.Smooth(5, 10, lowPrior);
        var resultHigh = WinRateSmoothing.Smooth(5, 10, highPrior);

        Assert.True(resultHigh > resultLow,
            "Higher prior should produce higher smoothed rate for same data");
    }

    [Fact]
    public void Smooth_HigherC_MoreRegularization()
    {
        var lowC = new GlobalCoefficients(0.5, 5);
        var highC = new GlobalCoefficients(0.5, 100);

        var resultLowC = WinRateSmoothing.Smooth(9, 10, lowC);
        var resultHighC = WinRateSmoothing.Smooth(9, 10, highC);

        Assert.True(resultLowC > resultHighC,
            "Higher C should pull result more toward prior (50%), so 90% WR gets pulled down more");
    }

    [Fact]
    public void Smooth_ExplicitOverload_MatchesRecordOverload()
    {
        var coefficients = new GlobalCoefficients(0.45, 15);
        var viaRecord = WinRateSmoothing.Smooth(7, 20, coefficients);
        var viaExplicit = WinRateSmoothing.Smooth(7, 20, 0.45, 15);
        Assert.Equal(viaRecord, viaExplicit);
    }

    [Fact]
    public void Default_HasReasonableValues()
    {
        Assert.Equal(0.5, GlobalCoefficients.Default.GlobalPrior);
        Assert.Equal(20.0, GlobalCoefficients.Default.SignificanceThreshold);
    }
}
