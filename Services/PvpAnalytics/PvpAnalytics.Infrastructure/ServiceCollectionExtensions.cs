using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PvpAnalytics.Core.Statistics;
using PvpAnalytics.Core.Repositories;
using PvpAnalytics.Infrastructure.Cache;
using PvpAnalytics.Infrastructure.Repositories;
using StackExchange.Redis;

namespace PvpAnalytics.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<PvpAnalyticsDbContext>(options =>
        {
            var provider = config["EfProvider"];
            var connectionString = config.GetConnectionString("DefaultConnection");

            if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
            {
                options.UseInMemoryDatabase("PvpAnalyticsDb");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException(
                        "Database connection string 'DefaultConnection' is not configured. " +
                        "Please provide it via appsettings.json, environment variables, or user secrets.");
                }

                options.UseNpgsql(connectionString);
            }

            options.ConfigureWarnings(warnings =>
                warnings.Log(RelationalEventId.PendingModelChangesWarning));
        });
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

        var redisConnection = config.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(redisConnection));
            services.AddSingleton<IGlobalCoefficientsProvider, RedisGlobalCoefficientsProvider>();
        }
        else
        {
            services.AddSingleton<IGlobalCoefficientsProvider, FallbackGlobalCoefficientsProvider>();
        }

        return services;
    }
}