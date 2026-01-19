using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using PvpAnalytics.Core.Entities;
using PvpAnalytics.Core.Enum;
using PvpAnalytics.Shared.Constants;
using Xunit;

namespace PvpAnalytics.Tests.Integration;

public class LogsControllerTests : IClassFixture<PvpAnalyticsApiFactory>, IDisposable
{
    private readonly HttpClient _client;
    private readonly TestIngestionState _state;

    public LogsControllerTests(PvpAnalyticsApiFactory factory)
    {
        _state = factory.IngestionState;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthHandler.AuthenticationScheme);
    }

    public void Dispose()
    {
        _state.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Upload_ReturnsBadRequest_WhenFileMissing()
    {
        using var content = new MultipartFormDataContent();

        var response = await _client.PostAsync($"/{AppConstants.RouteConstants.LogsBase}/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_ReturnsOk_WithListOfMatches()
    {
        _state.Handler = async (stream, ct) =>
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            await reader.ReadToEndAsync(ct);
            return
            [
                new Match
                {
                    Id = 42,
                    ArenaZone = ArenaZone.NagrandArena,
                    MapName = nameof(ArenaZone.NagrandArena),
                    CreatedOn = DateTime.UtcNow,
                    UniqueHash = Guid.NewGuid().ToString("N"),
                    GameMode = GameMode.ThreeVsThree,
                    Duration = 95,
                    IsRanked = true
                }
            ];
        };

        const string log = """
                           # Header
                           1/2/2024 19:10:03.100  ZONE_CHANGE,559,Nagrand Arena,,,,,,,,,,,,
                           1/2/2024 19:10:04.200  SPELL_DAMAGE,0x0100,Alpha-Illidan,0x0,0x0,0x0200,Bravo-Illidan,0x0,0x0,1337,Chaos Bolt,0x0,1200,0,0,0,0,0,0,0
                           1/2/2024 19:10:05.300  SPELL_DAMAGE,0x0200,Bravo-Illidan,0x0,0x0,0x0100,Alpha-Illidan,0x0,0x0,6789,Shadow Bolt,0x0,1300,0,0,0,0,0,0,0
                           1/2/2024 19:10:36.000  ZONE_CHANGE,84,Ironforge,,,,,,,,,,,,
                           """;

        using var content = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes(log);
        content.Add(new ByteArrayContent(bytes)
        {
            Headers =
            {
                ContentType = new MediaTypeHeaderValue("text/plain")
            }
        }, "file", "combatlog.txt");

        var response = await _client.PostAsync($"/{AppConstants.RouteConstants.LogsBase}/upload", content);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"body: {body}");
        
        var matches = await response.Content.ReadFromJsonAsync<List<Match>>();
        matches.Should().NotBeNull().And.NotBeEmpty();
        matches![0].Id.Should().Be(42);
        matches[0].ArenaZone.Should().Be(ArenaZone.NagrandArena);
    }
}

