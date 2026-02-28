# PvpAnalytics Ingestion Daemon

Lightweight client that parses a combat log file locally and sends match payloads to the PvpAnalytics gRPC Ingress API (Phase 5 streaming ingestion).

## Configuration

- **Ingestion:LogPath** (or env `LOG_PATH`): Path to the combat log file (required).
- **Ingestion:IngressUrl** (or env `INGRESS_URL`): gRPC Ingress base URL (default `http://localhost:8080`).
- **Ingestion:Source** / **Ingestion:Version**: Audit tags sent with each payload (default `daemon` / `1.0`).

## Usage

```bash
export LOG_PATH=/path/to/CombatLog.txt
export INGRESS_URL=http://localhost:8080
dotnet run
```

Or set in `appsettings.json`:

```json
{
  "Ingestion": {
    "LogPath": "C:\\Games\\WoW\\Logs\\CombatLog.txt",
    "IngressUrl": "https://pvpanalytics.example.com:8080"
  }
}
```

## Flow

1. Reads the log file line by line.
2. Detects match boundaries (`ARENA_MATCH_START` / `ZONE_CHANGE`).
3. Builds a `MatchPayload` (Protobuf) with participants and combat entries.
4. Computes an idempotency key (hash of participants + start + end + arena match id).
5. Sends the payload via gRPC to the Ingress API (which publishes to Kafka).

Ensure the API has streaming ingestion enabled (`Ingestion:StreamingEnabled=true`) and Kafka is configured.
