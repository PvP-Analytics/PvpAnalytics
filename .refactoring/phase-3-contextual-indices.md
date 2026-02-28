# Phase 3: Contextual Semantic Indices

## Status: IN PROGRESS

## Goal
Add Effective Damage, Resource Efficiency, and proxy Clutch Factor metrics to combat log processing and API responses.

## Changes

### New fields on CombatLogEntry
- `EffectiveDamage` (int) — damage in vulnerability windows that led to kills/forced defensives
- `EffectiveHealing` (int) — healing on targets below survival threshold

### New aggregation fields on MatchResult
- `EffectiveDamageTotal` (int)
- `EffectiveHealingTotal` (int)
- `CrowdControlScore` (double) — proxy for Isolated Impact

### Modified files
- [x] `Core/Entities/CombatLogEntry.cs` — add EffectiveDamage, EffectiveHealing
- [x] `Core/Entities/MatchResult.cs` — add aggregated contextual fields
- [x] `Application/Logs/CombatLogIngestionService.cs` — compute effective metrics during ingestion
- [x] `Core/DTOs/PerformanceComparisonDto.cs` — add contextual metric fields
- [x] `Core/DTOs/OpponentScoutDto.cs` — add contextual metric fields
