using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PvpAnalytics.Shared.Security;
using PvpAnalytics.Application;
using PvpAnalytics.Infrastructure;
using PvpAnalytics.Shared.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
    options.ConfigureEndpointDefaults(lo => lo.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.Configure<PvpAnalytics.Core.Configuration.KafkaOptions>(
    builder.Configuration.GetSection(PvpAnalytics.Core.Configuration.KafkaOptions.SectionName));
builder.Services.Configure<PvpAnalytics.Core.Configuration.IngestionOptions>(
    builder.Configuration.GetSection(PvpAnalytics.Core.Configuration.IngestionOptions.SectionName));

builder.Services.AddHealthChecks()
    .AddKafkaHealthCheck(builder.Configuration);

builder.Services.AddSingleton<PvpAnalytics.Api.Services.IIngestionPublisher, PvpAnalytics.Api.Services.KafkaIngestionPublisher>();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddGrpc();

// JWT Configuration and Security
// 
// IMPORTANT: The JWT signing key MUST be managed securely:
// 
// DEVELOPMENT:
// - Set JWT__SigningKey via environment variable: 
//   export JWT__SigningKey="your-very-long-random-key-at-least-32-chars-long-here"
// - Or use User Secrets: dotnet user-secrets set "Jwt:SigningKey" "your-strong-key-here"
// - NEVER commit the signing key to source control
// 
// PRODUCTION:
// - Use a secure secret store:
//   * Azure Key Vault: Configure via Azure App Configuration or Managed Identity
//   * AWS Secrets Manager: Use AWS Systems Manager Parameter Store
//   * HashiCorp Vault: Integrate via Vault provider
//   * Docker Secrets: Mount as /run/secrets/jwt-signing-key
// - The key must be at least 32 characters for HMAC-SHA256 security
// - Rotate keys regularly and implement key versioning
// 
// The application will fail to start with a clear error if:
// 1. The signing key is missing or empty
// 2. The signing key is too short (< 32 characters)
// 3. Required JWT configuration (Issuer, Audience) is missing

var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwtOptions = jwtSection.Get<JwtOptions>() ??
                 throw new InvalidOperationException("Jwt configuration section is missing.");

if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
{
    throw new InvalidOperationException(
        "JWT signing key is not configured. " +
        "DEVELOPMENT: Set the 'JWT__SigningKey' environment variable with a strong key (32+ characters). " +
        "Example: export JWT__SigningKey='your-very-long-random-key-at-least-32-chars-long-here' " +
        "Or use User Secrets: dotnet user-secrets set \"Jwt:SigningKey\" \"your-strong-key-here\" " +
        "PRODUCTION: Configure a secure secret store (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault). " +
        "The application cannot start without a valid signing key for security.");
}

if (jwtOptions.SigningKey.Length < 32)
{
    throw new InvalidOperationException(
        $"JWT signing key is too weak for production use. " +
        $"Minimum length: 32 characters (256 bits). Current length: {jwtOptions.SigningKey.Length} " +
        $"DEVELOPMENT: Generate a strong key using: openssl rand -base64 32 " +
        $"PRODUCTION: Use your secret management system to store a cryptographically secure key. " +
        $"The application cannot start with an insufficiently secure signing key.");
}

if (string.IsNullOrWhiteSpace(jwtOptions.Issuer))
{
    throw new InvalidOperationException("JWT Issuer is not configured. Set 'Jwt__Issuer' in configuration.");
}

if (string.IsNullOrWhiteSpace(jwtOptions.Audience))
{
    throw new InvalidOperationException("JWT Audience is not configured. Set 'Jwt__Audience' in configuration.");
}

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "https://localhost:3000", "http://localhost:5173")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey))
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddSingleton<ILoggingClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<LoggingClient>>();
    return new LoggingClient(config, logger);
});

var app = builder.Build();

var skipMigrations = builder.Configuration.GetValue<bool?>("EfMigrations:Skip") ?? false;
if (!skipMigrations)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PvpAnalyticsDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGrpcService<PvpAnalytics.Api.Controllers.IngestionGrpcService>();

app.MapHealthChecks("/health");

await app.RunAsync();


namespace PvpAnalytics.Api
{
    public interface IProgram;
}