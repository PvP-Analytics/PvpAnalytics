# Phase 5: Streaming Ingestion

## Status: IMPLEMENTED

## Goal
Replace manual file upload with P2P daemon + message broker pipeline for real-time telemetry. File upload remains the primary path; streaming is an additional path.

## Architecture (implemented)
1. Client daemon (.NET) parses combat logs locally.
2. Sends compact Protobuf payloads via gRPC to Ingress API.
3. Ingress publishes to Kafka topic (`pvpanalytics.ingestion.matches`).
4. .NET Worker consumes from Kafka and persists to PostgreSQL (idempotent by `match_dedup_key`).
5. Results written to **PostgreSQL** (same schema as file ingestion). ClickHouse/TimescaleDB deferred to a later phase.

## New / modified files

### New files
- `Shared/PvpAnalytics.Shared/Protos/ingestion.proto` — Protobuf contract (MatchPayload, CombatEntryProto, SubmitResult, IngestionService).
- `Services/PvpAnalytics/PvpAnalytics.Core/Configuration/KafkaOptions.cs` — Kafka config (bootstrap, topics, group id).
- `Services/PvpAnalytics/PvpAnalytics.Core/Configuration/IngestionOptions.cs` — Feature flags (StreamingEnabled, StreamingConsumerEnabled).
- `Services/PvpAnalytics/PvpAnalytics.Api/Health/KafkaHealthCheck.cs` — Health check for Kafka when streaming enabled.
- `Services/PvpAnalytics/PvpAnalytics.Api/Health/HealthCheckExtensions.cs` — Registers Kafka health check.
- `Services/PvpAnalytics/PvpAnalytics.Api/Services/IIngestionPublisher.cs`, `KafkaIngestionPublisher.cs` — Publish match payloads to Kafka.
- `Services/PvpAnalytics/PvpAnalytics.Api/Controllers/IngestionGrpcService.cs` — gRPC Ingress (SubmitMatch).
- `Services/PvpAnalytics/PvpAnalytics.Application/Logs/IMatchPersistService.cs`, `MatchPersistService.cs` — Shared persist logic for file and stream (idempotent by dedup key).
- `Workers/PvpAnalytics.Worker/Jobs/IngestionKafkaConsumerJob.cs` — Kafka consumer, maps proto to context, calls IMatchPersistService.
- `Workers/PvpAnalytics.Worker/appsettings.json` — Kafka and Ingestion config.
- `Clients/PvpAnalytics.IngestionDaemon/` — Daemon project (Program.cs, appsettings.json, README.md, Dockerfile).

### Modified files
- `Shared/PvpAnalytics.Shared/PvpAnalytics.Shared.csproj` — Added `Protos/ingestion.proto` (GrpcServices=Both).
- `compose.yaml` — Kafka service (Bitnami KRaft), pvpanalytics + worker env (Kafka, Ingestion), optional `ingestion-daemon` profile.
- `.env.example` — Kafka and Ingestion env vars.
- `Services/PvpAnalytics/PvpAnalytics.Api/` — Grpc.AspNetCore, Confluent.Kafka, Kestrel HTTP/2, gRPC + Kafka registration, Kafka/Ingestion appsettings.
- `Services/PvpAnalytics/PvpAnalytics.Application/` — IMatchPersistService + MatchPersistService; CombatLogIngestionService uses IMatchPersistService.
- `Workers/PvpAnalytics.Worker/` — Confluent.Kafka, Application + Infrastructure refs, IngestionKafkaConsumerJob, Kafka/Ingestion config.
- `Tests/PvpAnalytics.Tests/Logs/CombatLogIngestionServiceTests.cs` — Use MatchPersistService.
- `Tests/PvpAnalytics.Tests/Logs/MatchPersistServiceTests.cs` — Unit test for idempotency (same dedup key → no duplicate).

## Feature flags
- **Ingestion:StreamingEnabled** (API): When `true`, gRPC Ingress accepts SubmitMatch and publishes to Kafka. When `false`, returns "Streaming ingestion is disabled."
- **Ingestion:StreamingConsumerEnabled** (Worker): When `true`, Worker consumes from Kafka and persists. When `false`, consumer exits without subscribing.

## Idempotency
- Client sends `match_dedup_key` (e.g. hash of arena_match_id + start_utc + participants). Consumer uses it as `UniqueHash`. Duplicate key → return existing match, no second insert.

## Prerequisites (product / ops)
- Phases 1–3 stable.
- GDPR/privacy review for background data collection if daemon runs continuously.
- Infrastructure: Kafka (in compose or external cluster).

## Notes
- Current file-upload ingestion remains the primary path. Streaming is additive.
- ClickHouse/TimescaleDB for hot telemetry is a future step; first iteration writes to PostgreSQL only.
