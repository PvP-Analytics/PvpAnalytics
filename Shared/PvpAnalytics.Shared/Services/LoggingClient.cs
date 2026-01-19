using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PvpAnalytics.Shared.Protos;

namespace PvpAnalytics.Shared.Services;

public sealed class LogRequest
{
    public required string Level { get; init; }
    public required string Message { get; init; }
    public string? Exception { get; init; }
    public Guid? UserId { get; init; }
    public string? RequestPath { get; init; }
    public string? RequestMethod { get; init; }
    public int? StatusCode { get; init; }
    public double? Duration { get; init; }
    public string? Properties { get; init; }
}

public sealed class LoggingClient : ILoggingClient
{
    private readonly GrpcChannel _channel;
    private readonly LoggingService.LoggingServiceClient _client;
    private readonly string _serviceName;
    private readonly ILogger<LoggingClient>? _logger;
    private readonly Lock _timerLock = new();
    private Timer? _heartbeatTimer;
    private CancellationTokenSource? _heartbeatCts;
    private int _disposed;

    public LoggingClient(IConfiguration configuration, ILogger<LoggingClient>? logger = null)
    {
        var endpoint = configuration["LoggingService:GrpcEndpoint"] 
            ?? throw new InvalidOperationException("LoggingService:GrpcEndpoint not configured");
        
        _serviceName = configuration["LoggingService:ServiceName"] 
            ?? throw new InvalidOperationException("LoggingService:ServiceName not configured");
        
        _logger = logger;
        _channel = GrpcChannel.ForAddress(endpoint);
        _client = new LoggingService.LoggingServiceClient(_channel);
    }

    public async Task LogAsync(LogRequest logRequest, CancellationToken ct = default)
    {
        try
        {
            var request = new CreateLogRequest
            {
                Level = logRequest.Level,
                ServiceName = _serviceName,
                Message = logRequest.Message,
                Exception = logRequest.Exception ?? string.Empty,
                UserId = logRequest.UserId?.ToString() ?? string.Empty,
                RequestPath = logRequest.RequestPath ?? string.Empty,
                RequestMethod = logRequest.RequestMethod ?? string.Empty,
                StatusCode = logRequest.StatusCode ?? 0,
                Duration = logRequest.Duration ?? 0,
                Properties = ParsePropertiesOrEmpty(logRequest.Properties)
            };

            await _client.CreateLogAsync(request, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send log to LoggingService");
        }
    }

    public async Task RegisterServiceAsync(string serviceName, string endpoint, string version, CancellationToken ct = default)
    {
        try
        {
            var request = new ServiceRegistrationRequest
            {
                ServiceName = serviceName,
                Endpoint = endpoint,
                Version = version
            };

            var response = await _client.RegisterServiceAsync(request, cancellationToken: ct);
            _logger?.LogInformation("Service {ServiceName} registered with LoggingService: {Message}", 
                serviceName, response.Message);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to register service {ServiceName} with LoggingService", serviceName);
        }
    }

    public async Task SendHeartbeatAsync(string serviceName, CancellationToken ct = default)
    {
        try
        {
            var request = new HeartbeatRequest
            {
                ServiceName = serviceName
            };

            await _client.HeartbeatAsync(request, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to send heartbeat for service {ServiceName}", serviceName);
        }
    }

    public void StartHeartbeat(string serviceName, TimeSpan interval)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        Timer? oldTimer;
        CancellationTokenSource? oldCts;
        lock (_timerLock)
        {
            oldTimer = Interlocked.Exchange(ref _heartbeatTimer, null);
            oldCts = Interlocked.Exchange(ref _heartbeatCts, null);
            
            var newCts = new CancellationTokenSource();
            _heartbeatCts = newCts;
            
            _heartbeatTimer = new Timer(_ =>
            {
                if (newCts.Token.IsCancellationRequested)
                    return;
                
                Task.Run(async () =>
                {
                    try
                    {
                        if (!newCts.Token.IsCancellationRequested)
                        {
                            await SendHeartbeatAsync(serviceName, newCts.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancelled, ignore
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error in heartbeat callback for service {ServiceName}", serviceName);
                    }
                }, newCts.Token);
            }, null, TimeSpan.Zero, interval);
        }
        
        oldCts?.Cancel();
        oldCts?.Dispose();
        oldTimer?.Dispose();
    }

    private void StopHeartbeat()
    {
        Timer? timerToDispose;
        CancellationTokenSource? ctsToDispose;
        lock (_timerLock)
        {
            timerToDispose = Interlocked.Exchange(ref _heartbeatTimer, null);
            ctsToDispose = Interlocked.Exchange(ref _heartbeatCts, null);
        }
        
        if (ctsToDispose != null)
        {
            ctsToDispose.Cancel();
            ctsToDispose.Dispose();
        }
        timerToDispose?.Dispose();
    }

    private Struct ParsePropertiesOrEmpty(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return new Struct();
        }

        try
        {
            return Struct.Parser.ParseJson(json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to parse log properties JSON: {Properties}", json);
            return new Struct();
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
    }

    private void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;

        if (!disposing)
        {
            return;
        }

        StopHeartbeat();
        _channel.Dispose();
    }
}

