# Phase 4: Addon UI Config Integration

## Status: IN PROGRESS

## Goal
Store and serve recommended addon UI configurations (Gladius, Plater, WeakAuras, OmniBar) linked to specs/compositions.

## Changes

### New files
- [x] `Core/Entities/AddonConfig.cs` — addon import string storage entity
- [x] `Application/Services/AddonConfigService.cs` — CRUD + spec/comp association
- [x] `Api/Controllers/AddonConfigsController.cs` — API endpoints

### Modified files
- [x] `Infrastructure/PvpAnalyticsDbContext.cs` — add AddonConfigs DbSet
