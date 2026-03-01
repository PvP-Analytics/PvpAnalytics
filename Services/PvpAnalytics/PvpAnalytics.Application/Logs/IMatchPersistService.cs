using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Core.Entities;

namespace PvpAnalytics.Application.Logs;

/// <summary>
/// Persists a match from ingestion context (file or stream). Idempotent when dedupKeyOverride is used as UniqueHash.
/// </summary>
public interface IMatchPersistService
{
    /// <param name="context">Match metadata, participants, entries, players.</param>
    /// <param name="dedupKeyOverride">When set, used as UniqueHash for idempotency (e.g. from stream payload). When null, hash is computed from context.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The persisted or existing match.</returns>
    Task<Match> PersistAsync(MatchIngestionContext context, string? dedupKeyOverride, CancellationToken ct = default);
}
