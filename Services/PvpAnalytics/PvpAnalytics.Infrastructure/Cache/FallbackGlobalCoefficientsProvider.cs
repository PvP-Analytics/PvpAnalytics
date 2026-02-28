using Microsoft.Extensions.Logging;
using PvpAnalytics.Core.Statistics;

namespace PvpAnalytics.Infrastructure.Cache;

public class FallbackGlobalCoefficientsProvider(ILogger<FallbackGlobalCoefficientsProvider> logger)
    : IGlobalCoefficientsProvider
{
    private bool _warned;

    public Task<GlobalCoefficients> GetCoefficientsAsync(CancellationToken ct = default)
    {
        if (!_warned)
        {
            logger.LogWarning(
                "Redis is not configured. Using default global coefficients (prior={Prior}, C={C}). " +
                "Set ConnectionStrings:Redis to enable dynamic coefficient caching.",
                GlobalCoefficients.Default.GlobalPrior,
                GlobalCoefficients.Default.SignificanceThreshold);
            _warned = true;
        }

        return Task.FromResult(GlobalCoefficients.Default);
    }
}
