using System.Globalization;
using Npgsql;
using StackExchange.Redis;

namespace PvpAnalytics.Worker.Jobs;

internal static class ScalarConversion
{
    internal static double ToDoubleOrDefault(object? result, double defaultValue)
    {
        if (result == null) return defaultValue;
        if (result is IConvertible convertible)
        {
            try { return Convert.ToDouble(convertible, CultureInfo.InvariantCulture); }
            catch { /* fall through to TryParse */ }
        }
        if (double.TryParse(Convert.ToString(result), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return defaultValue;
    }
}

public class GlobalCoefficientsComputationJob(
    IConnectionMultiplexer redis,
    DatabaseConnectionString connectionString,
    ILogger<GlobalCoefficientsComputationJob> logger) : BackgroundService
{
    private const string GlobalPriorKey = "pvpanalytics:global_prior";
    private const string SignificanceThresholdKey = "pvpanalytics:significance_threshold";

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Global coefficients computation job started. Interval: {Interval}", Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ComputeAndCacheCoefficientsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error computing global coefficients");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task ComputeAndCacheCoefficientsAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString.Value);
        await conn.OpenAsync(ct);

        var globalPrior = await ComputeGlobalPriorAsync(conn, ct);
        var significanceThreshold = await ComputeSignificanceThresholdAsync(conn, ct);

        var db = redis.GetDatabase();
        await db.StringSetAsync(GlobalPriorKey,
            globalPrior.ToString(CultureInfo.InvariantCulture),
            TimeSpan.FromHours(2));
        await db.StringSetAsync(SignificanceThresholdKey,
            significanceThreshold.ToString(CultureInfo.InvariantCulture),
            TimeSpan.FromHours(2));

        logger.LogInformation(
            "Global coefficients updated: prior={Prior:F4}, C={Threshold:F1}",
            globalPrior, significanceThreshold);
    }

    private static async Task<double> ComputeGlobalPriorAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        const string sql = """
            SELECT CASE WHEN COUNT(*) = 0 THEN 0.5
                        ELSE CAST(SUM(CASE WHEN "IsWinner" THEN 1 ELSE 0 END) AS DOUBLE PRECISION) / COUNT(*)
                   END
            FROM "MatchResults"
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync(ct);
        return ScalarConversion.ToDoubleOrDefault(result, 0.5);
    }

    private static async Task<double> ComputeSignificanceThresholdAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        const string sql = """
            SELECT COALESCE(
                PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY match_count),
                20.0
            )
            FROM (
                SELECT "PlayerId", COUNT(*) AS match_count
                FROM "MatchResults"
                GROUP BY "PlayerId"
            ) sub
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync(ct);
        var value = ScalarConversion.ToDoubleOrDefault(result, 20.0);
        return value > 0 ? value : 20.0;
    }
}
