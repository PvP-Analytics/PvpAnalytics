# PvpAnalytics Service

PvP combat analytics service for World of Warcraft combat logs. Parses arena matches, stores player data, match results, and combat log entries.

## Overview

PvpAnalytics is the core analytics service that processes World of Warcraft combat log files, extracts arena match data, enriches player information via Blizzard's WoW API, and provides RESTful APIs for querying the data.

## Architecture

The service follows Clean Architecture principles:

```
PvpAnalytics.Api/              # Presentation layer (Controllers, Program.cs)
PvpAnalytics.Application/      # Business logic (Services, Log Parsing, WoW API integration)
PvpAnalytics.Infrastructure/   # Data access (EF Core, DbContext, Repositories)
PvpAnalytics.Core/            # Domain entities (Player, Match, MatchResult, CombatLogEntry)
```

## Features

- **Combat Log Parsing**: Parse WoW combat log files and extract arena match data
- **Player Management**: Store and query player information (name, realm, class, spec, faction)
- **Match Tracking**: Track arena matches with metadata (map, game mode, duration, ratings)
- **Combat Log Storage**: Store granular combat log entries (damage, healing, crowd control)
- **WoW API Integration**: Enrich player data using Blizzard's World of Warcraft API
- **RESTful API**: Full CRUD operations for all entities
- **Health Monitoring**: Health check endpoint for service monitoring

## API Endpoints

Base URL: `http://localhost:8080/api`

### Health Check

**GET /api/health**

Returns service health status (unauthenticated).

### Players

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/players` | List all players |
| GET | `/api/players/{id}` | Get player by ID |
| POST | `/api/players` | Create a player |
| PUT | `/api/players/{id}` | Update a player |
| DELETE | `/api/players/{id}` | Delete a player |

### Matches

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/matches` | List all matches |
| GET | `/api/matches/{id}` | Get match by ID |
| POST | `/api/matches` | Create a match |
| PUT | `/api/matches/{id}` | Update a match |
| DELETE | `/api/matches/{id}` | Delete a match |

### Match Results

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/matchresults` | List all match results |
| GET | `/api/matchresults/{id}` | Get match result by ID |
| POST | `/api/matchresults` | Create a match result |
| PUT | `/api/matchresults/{id}` | Update a match result |
| DELETE | `/api/matchresults/{id}` | Delete a match result |

### Combat Log Entries

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/combatlogentries` | List all combat log entries |
| GET | `/api/combatlogentries/{id}` | Get combat log entry by ID |
| POST | `/api/combatlogentries` | Create a combat log entry |
| PUT | `/api/combatlogentries/{id}` | Update a combat log entry |
| DELETE | `/api/combatlogentries/{id}` | Delete a combat log entry |

### Log Upload

**POST /api/logs/upload**

Upload a World of Warcraft combat log file for processing.

**Request:** `multipart/form-data` with field `file`

**Response:** `200 OK` with list of persisted matches
```json
[
  {
    "id": 1,
    "uniqueHash": "abc123...",
    "createdOn": "2025-11-18T12:00:00Z",
    "mapName": "Nagrand Arena",
    "gameMode": "TwoVsTwo",
    "duration": 180,
    "isRanked": true
  }
]
```

**Behavior:**
- Processes the entire log file
- Detects arena matches automatically
- Enriches player data via WoW API
- Persists matches, players, and combat log entries
- Returns all matches found in the file

### Streaming ingestion (Phase 5)

In addition to file upload, matches can be submitted via gRPC (Protobuf) to an Ingress API that publishes to Kafka. A Worker then consumes and persists to the same PostgreSQL schema (idempotent by `match_dedup_key`).

**Enable:**
- Set `Ingestion:StreamingEnabled=true` and `Kafka:BootstrapServers` (e.g. `localhost:9092`) for the API.
- Set `Ingestion:StreamingConsumerEnabled=true` and Kafka settings for the Worker.
- Start Kafka (e.g. `docker compose up -d kafka`); the API and Worker use the same topic `pvpanalytics.ingestion.matches`.

**Daemon:** A .NET daemon under `Clients/PvpAnalytics.IngestionDaemon` parses a local combat log file and sends match payloads via gRPC. Run with `LOG_PATH=/path/to/CombatLog.txt` and `INGRESS_URL=http://localhost:8080`. See that project’s README. To run the daemon with Docker Compose: `docker compose --profile daemon up -d ingestion-daemon` (mount the log file under `/logs`).

## Combat Log Parsing

### Parser Features

- **Language-agnostic**: Uses numeric IDs and structured field mappings
- **Arena Detection**: Automatically detects arena matches via zone changes
- **Player Resolution**: Normalizes player names and resolves to database entities
- **Match Finalization**: Groups combat log entries by match and creates match results
- **Data Enrichment**: Fetches player class, spec, and faction from WoW API

### Match Detection Logic

1. **Zone Change Detection**: Monitors `ZONE_CHANGE` events for known arena zone IDs
2. **Buffering**: Buffers combat log entries during an arena session
3. **Participant Tracking**: Tracks all players involved in the match
4. **Finalization**: Creates `Match` and `MatchResult` entities on zone exit or EOF
5. **Game Mode Detection**: Determines game mode based on participant count:
   - 4 players → TwoVsTwo
   - 6 players → ThreeVsThree (or Shuffle if detected)
   - 10 players → Skirmish
   - Default → TwoVsTwo

### Spell Mappings

Player attributes (class, spec, faction) are detected using static spell mappings in `PvpAnalytics.Core/Logs/PlayerAttributeMappings.cs`. These mappings are updated when new patches are released. See `SPELL_MAINTENANCE.md` for maintenance procedures.

## Configuration

### Environment Variables

Required environment variables (set in `.env` file or Docker Compose):

- `ConnectionStrings__DefaultConnection`: PostgreSQL connection string
- `Jwt__Issuer`: JWT issuer (must match AuthService)
- `Jwt__Audience`: JWT audience (must match AuthService)
- `Jwt__SigningKey`: JWT signing key (must match AuthService)
- `WowApi__ClientId`: Blizzard API client ID
- `WowApi__ClientSecret`: Blizzard API client secret
- `WowApi__BaseUrl`: Blizzard API base URL (default: https://us.api.blizzard.com)
- `WowApi__OAuthUrl`: Blizzard OAuth URL (default: https://us.battle.net/oauth/token)

### AppSettings

Non-sensitive configuration in `appsettings.json`:

```json
{
  "WowApi": {
    "BaseUrl": "https://us.api.blizzard.com",
    "OAuthUrl": "https://us.battle.net/oauth/token"
  }
}
```

**Note:** `ClientId` and `ClientSecret` should NOT be in appsettings files. They must be provided via environment variables.

## Database

- **Database**: PostgreSQL 16+
- **Schema**: Managed via EF Core Migrations
- **Auto-migration**: Migrations run automatically on startup
- **Entities**:
  - `Players`: Player information (name, realm, class, spec, faction)
  - `Matches`: Arena match metadata
  - `MatchResults`: Player results per match (ratings, win/loss)
  - `CombatLogEntries`: Granular combat log data

## Running Locally

### Prerequisites

- .NET 9 SDK
- PostgreSQL 16+ (or Docker)

### Steps

1. **Start PostgreSQL** (if not using Docker):
   ```bash
   docker run -e POSTGRES_USER=pvp -e POSTGRES_PASSWORD=pvp123 \
        -e POSTGRES_DB=pvpdb -p 5442:5432 --name pvpdb \
        postgres:16
   ```

2. **Configure connection string** in `appsettings.Development.json`:
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Host=localhost;Port=5442;Username=pvp;Password=pvp123;Database=pvpdb"
     }
   }
   ```

3. **Configure WoW API credentials** via User Secrets:
   ```bash
   cd Services/PvpAnalytics/PvpAnalytics.Api
   dotnet user-secrets set "WowApi:ClientId" "your-client-id"
   dotnet user-secrets set "WowApi:ClientSecret" "your-client-secret"
   ```

4. **Run the service**:
   ```bash
   dotnet run --project Services/PvpAnalytics/PvpAnalytics.Api --urls http://localhost:8080
   ```

## Running with Docker

The service is included in the main `docker-compose.yaml`. Ensure your `.env` file contains:

```bash
JWT_SIGNING_KEY=YourSecretKeyHere
WowApi__ClientId=your-client-id
WowApi__ClientSecret=your-client-secret
```

Then start all services:
```bash
docker compose up -d pvpanalytics
```

## WoW API Integration

The service integrates with Blizzard's World of Warcraft API to enrich player data:

- **Player Lookup**: Fetches player information by name and realm
- **Character Data**: Retrieves class, specialization, and faction
- **Caching**: Player data is cached to reduce API calls
- **Error Handling**: Gracefully handles API failures and continues processing

### Getting WoW API Credentials

1. Go to [Blizzard Developer Portal](https://develop.battle.net/)
2. Create a new application
3. Copy the Client ID and Client Secret
4. Add them to your `.env` file

## Testing

Run tests:
```bash
dotnet test Tests/PvpAnalytics.Tests/PvpAnalytics.Tests.csproj
```

Test coverage includes:
- Combat log parsing unit tests
- API integration tests
- Log ingestion service tests

## Development Notes

- **Generic Repository Pattern**: Uses `IRepository<TEntity>` for data access
- **CRUD Services**: Implements `ICrudService<TEntity>` for business logic
- **Structured Logging**: Comprehensive logging throughout the application
- **OpenAPI**: Swagger documentation available at `/openapi/v1.json` in Development mode

## Data Model

### Player
- `Id`: Primary key
- `Name`: Player character name
- `Realm`: Server/realm name
- `Class`: Character class (e.g., "Warrior", "Mage")
- `Faction`: Alliance or Horde

### Match
- `Id`: Primary key
- `UniqueHash`: Unique identifier for duplicate detection
- `CreatedOn`: Timestamp
- `MapName`: Arena map name
- `GameMode`: TwoVsTwo, ThreeVsThree, etc.
- `Duration`: Match duration in seconds
- `IsRanked`: Whether the match was ranked

### MatchResult
- `Id`: Primary key
- `MatchId`: Foreign key to Match
- `PlayerId`: Foreign key to Player
- `Team`: Team number (0 or 1)
- `RatingBefore`: Rating before match
- `RatingAfter`: Rating after match
- `IsWinner`: Whether the player won

### CombatLogEntry
- `Id`: Primary key
- `MatchId`: Foreign key to Match
- `Timestamp`: Event timestamp
- `SourcePlayerId`: Player who performed the action
- `TargetPlayerId`: Player who received the action
- `Ability`: Spell or ability name
- `DamageDone`: Damage amount (if applicable)
- `HealingDone`: Healing amount (if applicable)
- `CrowdControl`: CC type (if applicable)

