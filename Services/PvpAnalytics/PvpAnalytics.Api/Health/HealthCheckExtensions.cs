using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PvpAnalytics.Api.Health;
using PvpAnalytics.Core.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

internal static class HealthCheckExtensions
{
    public static IHealthChecksBuilder AddKafkaHealthCheck(this IHealthChecksBuilder builder, IConfiguration _)
    {
        return builder.AddCheck<KafkaHealthCheck>("kafka", tags: new[] { "ready", "kafka" });
    }
}
