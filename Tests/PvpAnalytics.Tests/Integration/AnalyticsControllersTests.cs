using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PvpAnalytics.Core.Entities;
using PvpAnalytics.Core.Enum;
using PvpAnalytics.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using PvpAnalytics.Shared.Constants;
using Xunit;

namespace PvpAnalytics.Tests.Integration;

public class AnalyticsControllersTests(PvpAnalyticsApiFactory factory) : IClassFixture<PvpAnalyticsApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task OpponentScouting_SearchPlayers_ReturnsResults()
    {
        // Arrange
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PvpAnalyticsDbContext>();
        
        var player = new Player
        {
            Name = "TestPlayer",
            Realm = "TestRealm",
            Class = "Rogue",
            Faction = "Horde",
            Spec = "Assassination"
        };
        dbContext.Players.Add(player);
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/{AppConstants.RouteConstants.OpponentScoutingBase}/search?name=Test");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var results = await response.Content.ReadFromJsonAsync<List<object>>();
        results.Should().NotBeNull();
    }

    [Fact]
    public async Task OpponentScouting_GetScoutingData_ReturnsData()
    {
        // Arrange
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PvpAnalyticsDbContext>();
        
        var player = new Player
        {
            Name = "TestPlayer",
            Realm = "TestRealm",
            Class = "Rogue",
            Faction = "Horde",
            Spec = "Assassination"
        };
        dbContext.Players.Add(player);
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/{AppConstants.RouteConstants.OpponentScoutingBase}/{player.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RatingProgression_GetProgression_ReturnsData()
    {
        // Arrange
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PvpAnalyticsDbContext>();
        
        var player = new Player
        {
            Name = "TestPlayer",
            Realm = "TestRealm",
            Class = "Rogue",
            Faction = "Horde",
            Spec = "Assassination"
        };
        dbContext.Players.Add(player);
        
        var match = new Match
        {
            UniqueHash = Guid.NewGuid().ToString("N"),
            MapName = "Nagrand Arena",
            ArenaZone = ArenaZone.NagrandArena,
            GameMode = GameMode.TwoVsTwo,
            Duration = 120,
            IsRanked = true,
            CreatedOn = DateTime.UtcNow
        };
        dbContext.Matches.Add(match);
        await dbContext.SaveChangesAsync();

        var matchResult = new MatchResult
        {
            MatchId = match.Id,
            PlayerId = player.Id,
            Team = "Team1",
            RatingBefore = 1500,
            RatingAfter = 1520,
            IsWinner = true,
            Spec = "Assassination"
        };
        dbContext.MatchResults.Add(matchResult);
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/{AppConstants.RouteConstants.RatingProgressionBase}/{player.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<object>();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task KeyMoment_GetMatchKeyMoments_ReturnsData()
    {
        // Arrange
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PvpAnalyticsDbContext>();
        
        var player = new Player
        {
            Name = "TestPlayer",
            Realm = "TestRealm",
            Class = "Rogue",
            Faction = "Horde",
            Spec = "Assassination"
        };
        dbContext.Players.Add(player);
        
        var match = new Match
        {
            UniqueHash = Guid.NewGuid().ToString("N"),
            MapName = "Nagrand Arena",
            ArenaZone = ArenaZone.NagrandArena,
            GameMode = GameMode.TwoVsTwo,
            Duration = 120,
            IsRanked = true,
            CreatedOn = DateTime.UtcNow
        };
        dbContext.Matches.Add(match);
        await dbContext.SaveChangesAsync();

        var combatLog = new CombatLogEntry
        {
            MatchId = match.Id,
            SourcePlayerId = player.Id,
            Timestamp = match.CreatedOn,
            Ability = "Chaos Bolt",
            DamageDone = 150000,
            HealingDone = 0,
            CrowdControl = string.Empty
        };
        dbContext.CombatLogEntries.Add(combatLog);
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/{AppConstants.RouteConstants.KeyMomentsBase}/match/{match.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MetaAnalysis_GetMetaAnalysis_ReturnsData()
    {
        // Act
        var response = await _client.GetAsync($"/{AppConstants.RouteConstants.MetaAnalysisBase}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<object>();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SessionAnalysis_GetSessionAnalysis_ReturnsData()
    {
        // Arrange
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PvpAnalyticsDbContext>();
        
        var player = new Player
        {
            Name = "TestPlayer",
            Realm = "TestRealm",
            Class = "Rogue",
            Faction = "Horde",
            Spec = "Assassination"
        };
        dbContext.Players.Add(player);
        
        var match = new Match
        {
            UniqueHash = Guid.NewGuid().ToString("N"),
            MapName = "Nagrand Arena",
            ArenaZone = ArenaZone.NagrandArena,
            GameMode = GameMode.TwoVsTwo,
            Duration = 120,
            IsRanked = true,
            CreatedOn = DateTime.UtcNow
        };
        dbContext.Matches.Add(match);
        await dbContext.SaveChangesAsync();

        var matchResult = new MatchResult
        {
            MatchId = match.Id,
            PlayerId = player.Id,
            Team = "Team1",
            RatingBefore = 1500,
            RatingAfter = 1520,
            IsWinner = true,
            Spec = "Assassination"
        };
        dbContext.MatchResults.Add(matchResult);
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/{AppConstants.RouteConstants.SessionAnalysisBase}/{player.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

