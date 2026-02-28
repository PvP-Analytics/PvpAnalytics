namespace PvpAnalytics.Core.Statistics;

public interface IGlobalCoefficientsProvider
{
    Task<GlobalCoefficients> GetCoefficientsAsync(CancellationToken ct = default);
}
