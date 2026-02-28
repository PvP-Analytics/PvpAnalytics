using System.Globalization;
using Microsoft.Extensions.Logging;
using PvpAnalytics.Core.Statistics;
using StackExchange.Redis;

namespace PvpAnalytics.Infrastructure.Cache;

public class RedisGlobalCoefficientsProvider(
    IConnectionMultiplexer redis,
    ILogger<RedisGlobalCoefficientsProvider> logger) : IGlobalCoefficientsProvider
{
    private const string GlobalPriorKey = "pvpanalytics:global_prior";
    private const string SignificanceThresholdKey = "pvpanalytics:significance_threshold";

    public async Task<GlobalCoefficients> GetCoefficientsAsync(CancellationToken ct = default)
    {
        try
        {
            var db = redis.GetDatabase();
            var priorValue = await db.StringGetAsync(GlobalPriorKey);
            var thresholdValue = await db.StringGetAsync(SignificanceThresholdKey);

            if (priorValue.IsNullOrEmpty || thresholdValue.IsNullOrEmpty)
            {
                logger.LogWarning(
                    "Global coefficients not found in Redis, using defaults (prior={Prior}, C={C})",
                    GlobalCoefficients.Default.GlobalPrior,
                    GlobalCoefficients.Default.SignificanceThreshold);
                return GlobalCoefficients.Default;
            }

            var prior = double.Parse(priorValue!, CultureInfo.InvariantCulture);
            var threshold = double.Parse(thresholdValue!, CultureInfo.InvariantCulture);

            return new GlobalCoefficients(prior, threshold);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read global coefficients from Redis, falling back to defaults");
            return GlobalCoefficients.Default;
        }
    }
}
