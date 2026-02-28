# Phase 2: Privacy-by-Design

## Status: IN PROGRESS

## Goal
Implement opt-in profile visibility, consent audit logging, and JWT-based profile sharing. Filter leaderboards by consent.

## Changes

### New files
- [x] `Core/Entities/PlayerProfile.cs` — profile with PublicConsent flag
- [x] `Core/Entities/ConsentAuditLog.cs` — audit trail for consent changes
- [x] `Application/Services/ProfileSharingService.cs` — JWT share token generation
- [x] `Api/Controllers/ProfileSharingController.cs` — sharing endpoints

### Modified files
- [x] `Infrastructure/PvpAnalyticsDbContext.cs` — add PlayerProfiles, ConsentAuditLogs DbSets
- [x] `TeamLeaderboardService.cs` — filter by public_consent
- [x] `CommunityRankingService.cs` — filter by public_consent
- [x] `PlayersController.cs` — respect consent in public endpoints
