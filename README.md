# PvpAnalytics

**Author:** Rmpriest

**Discord:** consonante_priest

PvP combat analytics platform for World of Warcraft combat logs. A microservices-based solution that parses arena matches, stores player data, and provides analytics through REST APIs.

## Table of Contents

- [Project Overview](#project-overview)
- [Quick Start](#quick-start)
- [Services](#services)
- [API Reference](#api-reference)
- [Setup & Configuration](#setup--configuration)
- [Testing](#testing)
- [License](#license)

## Project Overview

PvpAnalytics is a platform that processes World of Warcraft combat log files to extract and analyze PvP arena match data. The platform consists of multiple services working together:

- **AuthService**: Handles user authentication and authorization
- **PvpAnalytics Service**: Processes combat logs and provides analytics APIs
- **PaymentService**: Handles payment transactions and payment management
- **LoggingService**: Centralized logging service for application logs
- **UI**: React-based frontend dashboard

### Key Features

- **Combat Log Parsing**: Automatically parse WoW combat log files and extract arena matches
- **Player Management**: Store and query player information with automatic enrichment via WoW API
- **Match Analytics**: Track arena matches with ratings, results, and detailed combat data
- **Bayesian-Smoothed Win Rates**: All public win rate metrics use sample-size-aware Bayesian smoothing to prevent misleading statistics from small datasets
- **Contextual Semantic Indices**: Effective Damage (vulnerability-window damage), Effective Healing (critical-threshold healing), and Crowd Control scoring
- **Privacy-by-Design**: Opt-in public visibility with consent audit logging and JWT-based profile sharing
- **Addon Config Integration**: One-click import for Gladius, Plater, WeakAuras, OmniBar configurations linked to specs/compositions
- **Redis-Backed Coefficient Cache**: Background worker computes global statistical coefficients (prior, significance threshold) and caches in Redis
- **RESTful APIs**: Full CRUD operations for all entities
- **JWT Authentication**: Secure token-based authentication with refresh tokens
- **Modern UI**: React + TypeScript dashboard with Bayesian-smoothed metrics and sample-size tooltips

## Recent Changes (Refactoring)

See `.refactoring/` folder for detailed phase-by-phase tracking. Summary:

1. **Phase 1 — Bayesian Smoothing**: All 10 services computing raw win rates (`wins*100/total`) now use `WinRateSmoothing.Smooth()` from `PvpAnalytics.Core.Statistics`. A background worker (`PvpAnalytics.Worker`) computes `global_prior` and `C` from the database and caches them in Redis. Frontend displays smoothed rates with sample-size tooltips.

2. **Phase 2 — Privacy-by-Design**: `PlayerProfile` entity with `PublicConsent` flag (default `false`). `ConsentAuditLog` records all consent changes. Community rankings filtered by consent. JWT-based profile sharing via `/api/profiles/{id}/share`.

3. **Phase 3 — Contextual Indices**: `EffectiveDamage` and `EffectiveHealing` fields added to `CombatLogEntry` and `MatchResult`. Combat log ingestion computes effective metrics during parsing. DTOs extended with contextual fields.

4. **Phase 4 — Addon Config Integration**: `AddonConfig` entity for storing import strings (Gladius, Plater, WeakAuras, OmniBar). CRUD API at `/api/addon-configs` with spec/composition associations.

5. **Phase 5 — Streaming Ingestion**: gRPC Ingress accepts Protobuf match payloads and publishes to Kafka. A .NET Worker consumes and persists to PostgreSQL (idempotent by `match_dedup_key`). Optional .NET daemon parses logs locally and sends to Ingress. Feature flags: `Ingestion:StreamingEnabled` (API), `Ingestion:StreamingConsumerEnabled` (Worker). See `.refactoring/phase-5-streaming-ingestion.md` and `Clients/PvpAnalytics.IngestionDaemon/README.md`.

## Quick Start

### Docker Compose (Recommended)

1. Create a `.env` file in the project root:
   ```bash
   SA_PASSWORD=[YOUR_SA_PASSWORD]
   JWT_SIGNING_KEY=[YOUR_JWT_SIGNING_KEY]
   POSTGRES_USER=[YOUR_POSTGRES_USER]
   POSTGRES_PASSWORD=[YOUR_POSTGRES_PASSWORD]
   POSTGRES_DB=[YOUR_POSTGRES_DB]
   POSTGRES_PAYMENT_DB=[YOUR_POSTGRES_PAYMENT_DB]
   WowApi__ClientId=[YOUR_WOW_API_CLIENT_ID]
   WowApi__ClientSecret=[YOUR_WOW_API_CLIENT_SECRET]
   # Optional: Phase 5 streaming ingestion (Kafka + feature flags)
   # Copy from .env.example: Kafka__BootstrapServers, Ingestion__StreamingEnabled, Ingestion__StreamingConsumerEnabled
   ```

2. Start all services:
   ```bash
   docker compose build
   docker compose up -d
   ```

3. Access the services:
   - Auth API: `http://localhost:8081`
   - Analytics API: `http://localhost:8080`
   - Payment API: `http://localhost:8082`
   - Logging API: `http://localhost:8083`
   - UI: `http://localhost:3000`

### Local Development

**Prerequisites:** .NET 9 SDK, PostgreSQL 16+, SQL Server 2022+

See [Setup & Configuration](#setup--configuration) for detailed setup instructions.

## Services

### AuthService

Authentication and authorization service. Handles user registration, login, and token management.

**Key Features:**
- User registration and login
- JWT access tokens and refresh tokens
- Profile management

**Endpoints:**
- `POST /api/auth/register` - Register new user
- `POST /api/auth/login` - Authenticate user
- `POST /api/auth/refresh` - Refresh access token
- `GET /api/profile` - Get user profile
- `PUT /api/profile` - Update user profile

### PvpAnalytics Service

Core analytics service for processing combat logs and providing data APIs.

**Key Features:**
- Combat log parsing and ingestion
- Player data management
- Match tracking and analytics
- WoW API integration for player enrichment

**Endpoints:**
- Full CRUD APIs for Players, Matches, MatchResults, CombatLogEntries
- `POST /api/logs/upload` - Upload and process combat log files
- `GET /api/players/{id}/stats` - Get player statistics
- `GET /api/players/{id}/matches` - Get player matches

### PaymentService

Payment processing service for handling payment transactions and payment management.

**Key Features:**
- Payment transaction management
- Payment status tracking
- User-scoped payment access

**Endpoints:**
- Full CRUD APIs for Payments
- User-scoped access (users see only their payments, admins see all)
- Payment status management

### LoggingService

Centralized logging service for capturing and storing application logs.

**Key Features:**
- Application log storage
- Log querying and filtering
- Service-level log aggregation

**Endpoints:**
- `POST /api/logs` - Create log entry (anonymous)
- `GET /api/logs` - Query logs (authorized users only)
- `GET /api/logs/{id}` - Get log by ID
- `GET /api/logs/service/{serviceName}` - Get logs by service
- `GET /api/logs/level/{level}` - Get logs by level

## API Reference

### Authentication

Authentication is handled by the **AuthService**. 

**Quick Start:**
1. Register a user: `POST http://localhost:8081/api/auth/register`
2. Login: `POST http://localhost:8081/api/auth/login`
3. Use access token: `Authorization: Bearer <token>`
4. Refresh token: `POST http://localhost:8081/api/auth/refresh`

### Endpoints

**AuthService** (`http://localhost:8081/api/auth`):
- `POST /register` - Register new user
- `POST /login` - Authenticate user
- `POST /refresh` - Refresh access token

**PvpAnalytics** (`http://localhost:8080/api`):
- `GET /api/health` - Health check
- `GET /api/players` - List players
- `GET /api/matches` - List matches
- `POST /api/logs/upload` - Upload combat log file
- Full CRUD for Players, Matches, MatchResults, CombatLogEntries

**PaymentService** (`http://localhost:8082/api/payment`):
- `GET /api/payment` - List payments (with pagination, filtering, sorting)
- `GET /api/payment/{id}` - Get payment by ID
- `POST /api/payment` - Create payment
- `PUT /api/payment/{id}` - Update payment
- `DELETE /api/payment/{id}` - Delete payment

**LoggingService** (`http://localhost:8083/api/logs`):
- `POST /api/logs` - Create log entry
- `GET /api/logs` - Query logs with filters
- `GET /api/logs/{id}` - Get log by ID
- `GET /api/logs/service/{serviceName}` - Get logs by service
- `GET /api/logs/level/{level}` - Get logs by level

**OpenAPI Documentation:**
- Available at `/openapi/v1.json` in Development mode

## Setup & Configuration

### Environment Variables

Create a `.env` file in the project root with the following variables:

```bash
# SQL Server (AuthService)
SA_PASSWORD=YourStrong@Password123

# PostgreSQL
POSTGRES_USER=postgres
POSTGRES_PASSWORD=YourPassword
POSTGRES_DB=PvpAnalytics
POSTGRES_PAYMENT_DB=PaymentService

# JWT Configuration (shared between services)
JWT_SIGNING_KEY=YourSecretKeyHere

# WoW API (PvpAnalytics Service)
WowApi__ClientId=your-client-id
WowApi__ClientSecret=your-client-secret
```

**Important:** The `.env` file is gitignored and should never be committed. Keep your secrets secure!

### Docker Compose Setup

1. **Create `.env` file** with the variables above

2. **Start all services:**
   ```bash
   docker compose build
   docker compose up -d
   ```

3. **Verify services are running:**
   ```bash
   docker compose ps
   ```

**Services:**
- `auth` - AuthService API (port 8081)
- `pvpanalytics` - PvpAnalytics API (port 8080)
- `payment` - PaymentService API (port 8082)
- `logging` - LoggingService API (port 8083)
- `ui` - Nginx serving React UI (port 3000)
- `db` - PostgreSQL for analytics (port 5442)
- `logging-postgres` - PostgreSQL for logging (port 5443)
- `auth-sql` - SQL Server for authentication (port 1433)

### Local Development

For local development, configure connection strings in each service's `appsettings.Development.json`:

- **AuthService**: SQL Server connection string
- **PvpAnalytics**: PostgreSQL connection string
- **PaymentService**: PostgreSQL connection string
- **LoggingService**: PostgreSQL connection string

## Testing

Run all tests:
```bash
dotnet test
```

**Test Coverage:**
- Integration tests for API endpoints
- Authentication and authorization tests
- Combat log parsing tests

## License

MIT
