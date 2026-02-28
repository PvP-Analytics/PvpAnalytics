using PvpAnalytics.Worker.Jobs;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConnection));

var dbConnection = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Database connection string 'DefaultConnection' is not configured.");
builder.Services.AddSingleton(new DatabaseConnectionString(dbConnection));

builder.Services.AddHostedService<GlobalCoefficientsComputationJob>();

var host = builder.Build();
await host.RunAsync();

public record DatabaseConnectionString(string Value);
