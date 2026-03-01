using Grpc.Core;
using Microsoft.Extensions.Options;
using PvpAnalytics.Api.Services;
using PvpAnalytics.Core.Configuration;
using PvpAnalytics.Shared.Protos.Ingestion;

namespace PvpAnalytics.Api.Controllers;

internal sealed class IngestionGrpcService : IngestionService.IngestionServiceBase
{
    private readonly IIngestionPublisher _publisher;
    private readonly IngestionOptions _ingestionOptions;

    public IngestionGrpcService(IIngestionPublisher publisher, IOptions<IngestionOptions> ingestionOptions)
    {
        _publisher = publisher;
        _ingestionOptions = ingestionOptions.Value;
    }

    public override async Task<SubmitResult> SubmitMatch(MatchPayload request, ServerCallContext context)
    {
        if (!_ingestionOptions.StreamingEnabled)
        {
            return new SubmitResult
            {
                Accepted = false,
                Message = "Streaming ingestion is disabled."
            };
        }

        var correlationId = Guid.NewGuid().ToString("N");

        if (string.IsNullOrWhiteSpace(request.MatchDedupKey))
        {
            return new SubmitResult
            {
                Accepted = false,
                Message = "match_dedup_key is required for idempotency.",
                CorrelationId = correlationId
            };
        }

        if (request.StartUtc == null || request.StartUtc == default)
        {
            return new SubmitResult
            {
                Accepted = false,
                Message = "start_utc is required.",
                CorrelationId = correlationId
            };
        }

        if (request.EndUtc == null || request.EndUtc == default)
        {
            return new SubmitResult
            {
                Accepted = false,
                Message = "end_utc is required.",
                CorrelationId = correlationId
            };
        }

        var accepted = await _publisher.PublishAsync(request, correlationId, context.CancellationToken).ConfigureAwait(false);
        if (!accepted)
        {
            return new SubmitResult
            {
                Accepted = false,
                Message = "Kafka producer not available.",
                CorrelationId = correlationId
            };
        }

        return new SubmitResult
        {
            Accepted = true,
            Message = "Match payload accepted for processing.",
            CorrelationId = correlationId
        };
    }
}
