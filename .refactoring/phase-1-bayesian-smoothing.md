# Phase 1: Bayesian Smoothing

## Status: IN PROGRESS

## Goal
Replace all raw win rate calculations (`wins * 100 / total`) with Bayesian-smoothed win rates across all public API endpoints and UI.

## Formula
```
smoothed_winrate = (wins + C * global_prior) / (totalMatches + C)
```
- `C` = dynamic significance threshold (percentile of match counts)
- `global_prior` = season-wide average win rate (cached in Redis)

## Changes

### New files
- [x] `Application/Statistics/GlobalCoefficients.cs` — record for cached coefficients
- [x] `Application/Statistics/IGlobalCoefficientsProvider.cs` — port interface
- [x] `Application/Statistics/WinRateSmoothing.cs` — static smoothing utility
- [x] `Infrastructure/Cache/RedisGlobalCoefficientsProvider.cs` — Redis adapter
- [x] `Workers/PvpAnalytics.Worker/Program.cs` — hosted service entry point
- [x] `Workers/PvpAnalytics.Worker/Jobs/GlobalCoefficientsComputationJob.cs` — background job

### Modified files
- [x] `Infrastructure/PvpAnalytics.Infrastructure.csproj` — add StackExchange.Redis
- [x] `Workers/PvpAnalytics.Worker/PvpAnalytics.Worker.csproj` — add dependencies
- [x] `Infrastructure/ServiceCollectionExtensions.cs` — register Redis provider
- [x] `Application/ServiceCollectionExtensions.cs` — no change needed (static util)
- [x] `compose.yaml` — add Redis service
- [x] `Api/Program.cs` — add Redis connection string env var

### Services updated (raw -> smoothed)
- [x] TeamLeaderboardService.cs
- [x] TeamService.cs
- [x] SessionAnalysisService.cs
- [x] OpponentScoutingService.cs
- [x] MatchupAnalyticsService.cs
- [x] PerformanceComparisonService.cs
- [x] TeamCompositionService.cs
- [x] TeamSynergyService.cs
- [x] MetaAnalysisService.cs
- [x] RivalService.cs

### Tests
- [x] WinRateSmoothing unit tests (convergence, edge cases)
