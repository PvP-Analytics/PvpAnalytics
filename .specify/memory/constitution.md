# PvpAnalytics Constitution

## Core Principles

### I. Domain Mission

PvpAnalytics is a high-load competitive telemetry analytics platform for World of Warcraft PvP. The mission is to transform raw combat log events into mathematically sound, contextual metrics that help players and teams improve — without amplifying toxicity, gatekeeping, or social stigmatization.

All engineering decisions must be evaluated against three axes:
- **Statistical correctness** — no raw, unsmoothed metrics in public-facing outputs.
- **Scalability** — architecture must support crowdsourced streaming telemetry, not just manual log uploads.
- **Ethical data usage** — Privacy-by-Design; personal stats are private by default.

### II. Type Safety Across the Stack

- Backend services are written in C# / .NET with strict nullable reference types enabled.
- Frontend is React + TypeScript with `strict: true` in `tsconfig.json`. 
- No `dynamic`, untyped `object`, or `any` at service boundaries without a documented exception.
- All public APIs and inter-service contracts are formalized through explicit schemas (OpenAPI/Swagger for REST, protobuf/gRPC where applicable). DTOs/contracts are shared from a common `Shared/` project.

### III. Clean Architecture (NON-NEGOTIABLE)

Every microservice follows the four-layer Clean Architecture pattern already established:
- **Core** — domain entities, value objects, enums, domain exceptions. Zero external dependencies.
- **Application** — use cases, interfaces (ports), DTOs, validation. Depends only on Core.
- **Infrastructure** — EF Core DbContext, repository implementations, external API clients. Implements Application interfaces.
- **Api** — controllers, middleware, DI wiring, configuration. Depends on Application and Infrastructure.

Dependency direction is strictly inward: Api → Infrastructure → Application → Core. Violations are build errors, not suggestions.

### IV. Microservices Isolation

- Each service (AuthService, PvpAnalytics, PaymentService, LoggingService) owns its database and schema. No cross-service direct DB access.
- Inter-service communication happens through well-defined HTTP APIs or message queues — never through shared database tables.
- A growing monolith is prohibited. New bounded contexts become new services or at minimum new solution folders with their own Clean Architecture layers.

### V. Idempotency and Resilience

- All data ingestion operations (combat log upload, event streaming) must be idempotent — re-submitting the same payload produces no duplicates or side effects.
- Stream processing and aggregation jobs support retry, backpressure, and graceful degradation.
- API endpoints return deterministic results for identical inputs.

### VI. Test-First for Critical Paths

- Combat log parsing, Bayesian smoothing algorithms, and statistical index calculations require property-based or statistical tests that verify distribution correctness, not just happy-path assertions.
- All API endpoints require integration tests covering authentication, authorization, and core business logic.
- Red-Green-Refactor cycle is enforced for the mathematical core — tests define expected distributions before implementation.

## Data Architecture

### Data Ingestion Hierarchy

Data sources are ranked by trust and representativeness:

1. **Crowdsourced streaming telemetry** (future P2P daemon) — highest priority, most representative sample.
2. **Combat log file uploads** (current primary) — high detail, but subject to survivorship bias and manual effort.
3. **Official Blizzard Web API** — legal and stable, but shallow metrics and strict rate limits.

Every data record carries metadata indicating its source vector, enabling downstream algorithms to weight and contextualize appropriately.

### Storage Separation

- **Hot telemetry / time-series data** — columnar or time-series store (ClickHouse, TimescaleDB) for high-throughput event streams. Not in the transactional PostgreSQL.
- **Transactional OLTP** — PostgreSQL for user accounts, match records, player profiles, consent flags.
- **Aggregated analytics** — materialized views or pre-computed tables. Never computed on-the-fly via expensive SQL scans in synchronous API handlers.
- **UI configuration storage** — document-oriented store (or JSONB columns in PostgreSQL) for addon profiles (Gladius, Plater, WeakAuras import strings).
- **Cache layer** — Redis or equivalent in-memory store for global statistical parameters (global prior win rate, significance thresholds, season metadata).

### Raw vs. Aggregated Data

- Raw events and aggregated metrics are always stored in separate tables/schemas.
- Aggregated views are rebuilt by asynchronous background workers (cron jobs, Hangfire, or dedicated worker services), not by user-facing API requests.

## Metrics and Statistical Rigor

### No Raw Metrics in Public Interfaces

Public APIs and the UI are **prohibited** from displaying unsmoothed raw win rates as primary metrics. Every displayed win rate, pick rate, or effectiveness rating must pass through smoothing algorithms with sample-size awareness.

### Bayesian Smoothing (Mandatory)

The core statistical model for win rate calculation uses the Bayesian posterior formula:

```
smoothed_winrate = (n * raw_winrate + C * global_prior) / (n + C)
```

Where:
- `n` = number of matches for the subject (local sample size)
- `raw_winrate` = observed win rate for the subject
- `C` = dynamic significance threshold (computed from percentiles of match counts across all subjects)
- `global_prior` = global average win rate across all classes/compositions for the current season

Global coefficients (`C` and `global_prior`) are computed by background workers and cached in Redis — never recalculated per-request.

### Contextual Semantic Indices

The platform evaluates player effectiveness through contextual indices, not primitive sums:

| Instead of              | Use                                                                                       |
|-------------------------|-------------------------------------------------------------------------------------------|
| Total Damage (DPS)      | **Effective Damage** — only damage dealt during vulnerability windows that led to kills or forced defensive cooldowns |
| Raw Win Rate            | **Isolated Impact** — useful actions (interrupts, positioning, debuffs) that statistically increase win probability, independent of match outcome |
| Total Healing (HPS)     | **Resource Efficiency / Lives Saved** — healing on targets below a computed survival threshold, weighted by mana spent |
| Kill Count (KDA)        | **Synergy Coefficient / Clutch Factor** — success rate in numerical disadvantage situations and effectiveness of coordinated ability usage |

Every new KPI must be reviewed for:
- Stability on small samples
- Sensitivity to MMR variance and luck
- Proper credit for support roles and team synergy

## Privacy, Ethics, and Anti-Toxicity

### Privacy-by-Design (Default)

- All aggregated macro-statistics (class win rates, composition rankings, movement heatmaps) are permanently decoupled from individual player/session identifiers in the analytics store.
- Detailed personal profiles are stored in an isolated schema with Row-Level Security (RLS) policies in PostgreSQL.

### Opt-In Profile Visibility

- Every new user profile defaults to `personal_only` — visible only to the owner.
- Inclusion in public leaderboards, public profile pages, or external integrations requires explicit Opt-In (a `public_consent` boolean flag + an audit log entry).
- Leaderboards are populated exclusively from profiles where `public_consent = true`.

### Cryptographic Sharing

- Sharing detailed personal statistics with third parties is done via short-lived, signed JWT tokens with scoped permissions (e.g., `read:advanced_stats`) and an expiration claim.
- Endpoints serving these tokens are isolated from the public API and undergo dedicated security review.

### Anti-Gatekeeping UX

- The public UI never displays individual raw win rates or metrics that can trivially become harassment tools.
- Any player-level rankings or tables include contextual qualifiers (sample size, role, MMR bracket, map/mode) and cannot serve as the sole filter in LFG interfaces.

## UI/UX and Game Client Integration

### Educational Hub, Not a Scoreboard

- The primary UI scenario is skill improvement: guides, mistake breakdowns, interface and build recommendations.
- Bare tables without explanations are insufficient UX. All key metrics are accompanied by interactive tooltips and links to explanatory materials.

### Addon Configuration Integration

- The platform provides a versioned cloud store for UI configurations (import strings for Gladius, Plater, WeakAuras, OmniBar, and similar addons).
- The link "meta build → recommended UI profile" is mandatory: viewing a build allows one-click import of the associated UI config.
- API responses for builds include extended dependency graphs: not just `{ talent_id, winrate }` but `{ talents, recommended_ui_strings: { plater, omnibar, ... } }`.

### In-Game Module Performance

- Any in-game addons or overlays developed by the project must be profiled for minimal FPS impact and latency.
- Live analyzer algorithms (overheal prevention, dispel/CC recommendations) must have strict per-frame processing budgets and be benchmarked on real arena encounters.

## Technology Stack

### Backend
- **Runtime**: .NET 10 (C#), Clean Architecture per service
- **API style**: RESTful with OpenAPI/Swagger documentation
- **Primary database**: PostgreSQL 16
- **Auth database**: Oracle XE 23 / SQL Server (AuthService)
- **ORM**: Entity Framework Core with code-first migrations
- **Background processing**: Worker services or Hangfire for async aggregation jobs
- **Future streaming**: Apache Kafka or RabbitMQ for event-driven ingestion pipeline

### Frontend
- **Framework**: React 19 with TypeScript (strict mode)
- **Build tool**: Vite
- **Styling**: Tailwind CSS
- **State management**: Zustand
- **Testing**: Vitest + Testing Library
- **Linting**: ESLint with TypeScript plugin

### Infrastructure
- **Containerization**: Docker Compose for local development and deployment
- **Services**: AuthService (port 8081), PvpAnalytics (8080), PaymentService (8082), LoggingService (8083), UI (3000)
- **Databases**: PostgreSQL containers (ports 5442, 5443), SQL Server container (port 1433)

## Code Quality and Processes

### Code Standards
- C# follows .NET conventions with `dotnet format` enforcement. Nullable reference types enabled project-wide.
- TypeScript frontend enforces ESLint + Prettier. `strict: true` in tsconfig.
- No unjustified `dynamic`/`any`/`unsafe` without a written rationale in a code comment or ADR.

### Testing Requirements
- All new modules must have unit tests on critical logic branches and integration tests on key user scenarios (ingestion → aggregation → API → UI).
- Statistical/property-based tests for the mathematical core (Bayesian algorithms, effectiveness indices) to prevent distribution regressions.

### Mandatory Code Review
- No PR touching data protocols, metrics calculations, security, or privacy can be merged without review from the architecture/data owner.
- All changes to public APIs and data models are accompanied by EF Core migrations and a changelog entry.

## Observability and Operations

### Logging and Metrics
- All services publish structured logs to the centralized LoggingService.
- Key scenarios (ingestion pipeline, global coefficient computation, API responses) have explicit SLOs for latency, availability, and acceptable event loss rate.
- Future: Prometheus/OpenTelemetry integration for system-level metrics (latency percentiles, error rates, ingestion throughput).

### Feature Flags and Migrations
- New metrics and algorithm changes are rolled out behind feature flags and/or API versioning — never as hard-swapped behavior.
- Expensive data migrations are executed in stages with rollback capability.

## Governance

- This Constitution supersedes all other informal practices and ad-hoc decisions.
- Amendments require: (1) a written rationale, (2) review by the project owner, and (3) a migration plan for any code that becomes non-compliant.
- All PRs and code reviews must verify compliance with this Constitution. Complexity must be justified.
- Any divergence between the actual implementation and this Constitution must result in either a code refactoring or an explicit, documented amendment to the Constitution.

**Version**: 1.0.0 | **Ratified**: 2026-02-28 | **Last Amended**: 2026-02-28
