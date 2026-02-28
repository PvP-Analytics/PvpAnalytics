using System.Security.Cryptography;
using System.Text;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using PvpAnalytics.Application.Logs;
using PvpAnalytics.Core.Enum;
using PvpAnalytics.Core.Logs;
using PvpAnalytics.Shared.Protos.Ingestion;
using Google.Protobuf.WellKnownTypes;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var logPath = config["Ingestion:LogPath"] ?? config["LOG_PATH"] ?? "";
var ingressUrl = config["Ingestion:IngressUrl"] ?? config["INGRESS_URL"] ?? "http://localhost:8080";
var sourceTag = config["Ingestion:Source"] ?? "daemon";
var versionTag = config["Ingestion:Version"] ?? "1.0";

if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath))
{
    Console.WriteLine("Usage: set Ingestion:LogPath (or LOG_PATH) to a combat log file. Optionally set IngressUrl (default http://localhost:8080).");
    return 1;
}

using var channel = GrpcChannel.ForAddress(ingressUrl.TrimEnd('/'), new GrpcChannelOptions { HttpHandler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true } });
var client = new IngestionService.IngestionServiceClient(channel);

await using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

var state = new DaemonMatchState();
string? line;
while ((line = await reader.ReadLineAsync()) != null)
{
    if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
    var parsed = CombatLogParser.ParseLine(line);
    if (parsed == null) continue;

    if (parsed.EventType == CombatLogEventTypes.ArenaMatchStart)
    {
        state.Reset();
        state.MatchStart = parsed.Timestamp;
        state.ArenaMatchId = parsed.ArenaMatchId ?? string.Empty;
        if (parsed.ZoneId.HasValue) state.ZoneId = parsed.ZoneId.Value;
        state.Active = true;
        continue;
    }

    if (parsed.EventType == CombatLogEventTypes.ZoneChange && state.Active && !string.IsNullOrEmpty(state.ArenaMatchId))
    {
        state.MatchEnd = parsed.Timestamp;
        var payload = BuildPayload(state, sourceTag, versionTag);
        if (payload != null)
        {
            try
            {
                var result = await client.SubmitMatchAsync(payload);
                Console.WriteLine(result.Accepted ? $"Accepted: {result.CorrelationId}" : $"Rejected: {result.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Send failed: {ex.Message}");
            }
        }
        state.Reset();
        continue;
    }

    if (state.Active)
    {
        if (!string.IsNullOrEmpty(parsed.SourceName)) state.Participants.Add(parsed.SourceName.Trim('"'));
        if (!string.IsNullOrEmpty(parsed.TargetName)) state.Participants.Add(parsed.TargetName.Trim('"'));
        state.Entries.Add(new DaemonCombatEntry
        {
            Timestamp = parsed.Timestamp,
            SourceName = parsed.SourceName ?? string.Empty,
            TargetName = parsed.TargetName ?? string.Empty,
            Ability = parsed.SpellName ?? parsed.EventType ?? string.Empty,
            DamageDone = parsed.Damage ?? 0,
            HealingDone = parsed.Healing ?? 0,
            EffectiveDamage = parsed.Damage ?? 0,
            EffectiveHealing = parsed.Healing ?? 0,
            CrowdControl = string.Empty
        });
        state.MatchEnd = parsed.Timestamp;
    }
}

Console.WriteLine("Done.");
return 0;

static string ComputeDedupKey(HashSet<string> participants, DateTime? start, DateTime? end, string arenaMatchId)
{
    var baseStr = string.Join('|', participants.OrderBy(x => x)) + $"|{start:O}|{end:O}|{arenaMatchId}";
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(baseStr)));
}

static MatchPayload? BuildPayload(DaemonMatchState state, string source, string version)
{
    if (state.MatchStart == null || state.MatchEnd == null) return null;
    var arenaZone = state.ZoneId > 0 ? (ArenaZone)state.ZoneId : ArenaZone.Unknown;
    var mapName = state.ZoneId > 0 ? ArenaZoneIds.GetDisplayName(arenaZone) : "Unknown";
    var participantCount = state.Participants.Count;
    var gameMode = participantCount <= 4 ? GameMode.TwoVsTwo : (participantCount <= 6 ? GameMode.ThreeVsThree : GameMode.Shuffle);

    var dedupKey = ComputeDedupKey(state.Participants, state.MatchStart, state.MatchEnd, state.ArenaMatchId);

    var payload = new MatchPayload
    {
        MatchDedupKey = dedupKey,
        Source = source,
        Version = version,
        ArenaMatchId = state.ArenaMatchId,
        StartUtc = Timestamp.FromDateTime(state.MatchStart.Value.ToUniversalTime()),
        EndUtc = Timestamp.FromDateTime(state.MatchEnd.Value.ToUniversalTime()),
        MapName = mapName,
        ArenaZone = (int)arenaZone,
        GameMode = (int)gameMode
    };
    payload.ParticipantKeys.AddRange(state.Participants);
    foreach (var e in state.Entries)
    {
        payload.Entries.Add(new CombatEntryProto
        {
            Timestamp = Timestamp.FromDateTime(e.Timestamp.ToUniversalTime()),
            SourceName = e.SourceName,
            TargetName = e.TargetName,
            Ability = e.Ability,
            DamageDone = e.DamageDone,
            HealingDone = e.HealingDone,
            EffectiveDamage = e.EffectiveDamage,
            EffectiveHealing = e.EffectiveHealing,
            CrowdControl = e.CrowdControl
        });
    }
    return payload;
}

file sealed class DaemonMatchState
{
    public bool Active { get; set; }
    public DateTime? MatchStart { get; set; }
    public DateTime? MatchEnd { get; set; }
    public string ArenaMatchId { get; set; } = string.Empty;
    public int ZoneId { get; set; }
    public HashSet<string> Participants { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DaemonCombatEntry> Entries { get; } = [];

    public void Reset()
    {
        Active = false;
        MatchStart = null;
        MatchEnd = null;
        ArenaMatchId = string.Empty;
        ZoneId = 0;
        Participants.Clear();
        Entries.Clear();
    }
}

file sealed class DaemonCombatEntry
{
    public DateTime Timestamp { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string Ability { get; set; } = string.Empty;
    public int DamageDone { get; set; }
    public int HealingDone { get; set; }
    public int EffectiveDamage { get; set; }
    public int EffectiveHealing { get; set; }
    public string CrowdControl { get; set; } = string.Empty;
}
