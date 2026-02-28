using PvpAnalytics.Worker.Jobs;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (string.IsNullOrWhiteSpace(redisConnection))
    throw new InvalidOperationException(
        "Redis connection string is not configured. Set 'ConnectionStrings:Redis' in configuration (e.g. appsettings.json or environment).");

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConnection!));

var dbConnection = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Database connection string 'DefaultConnection' is not configured.");
builder.Services.AddSingleton(new DatabaseConnectionString(dbConnection));

builder.Services.AddHostedService<GlobalCoefficientsComputationJob>();

var host = builder.Build();
await host.RunAsync();

public record DatabaseConnectionString(string Value);
