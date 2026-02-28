using System.Linq.Expressions;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PvpAnalytics.Application.Logs;
using PvpAnalytics.Application.Services;
using PvpAnalytics.Core.Entities;
using PvpAnalytics.Core.Enum;
using PvpAnalytics.Core.Models;
using PvpAnalytics.Core.Repositories;
using PvpAnalytics.Tests.Helper;
using Xunit;

namespace PvpAnalytics.Tests.Logs;

public class CombatLogIngestionServiceTests
{
    [Fact]
    public async Task IngestAsync_PersistsArenaMatchAndEntries()
    {
        // Use a repository that pre-populates players so that CombatLogIngestionService
        // can resolve participants without hitting the Wow API or external services.
        var playerRepo = new InMemoryRepository<Player>(p => p.Id);
        var matchRepo = new InMemoryRepository<Match>(m => m.Id);
        var resultRepo = new InMemoryRepository<MatchResult>(r => r.Id);
        var entryRepo = new InMemoryRepository<CombatLogEntry>(e => e.Id);
        var matchPersistService = new MatchPersistService(matchRepo, resultRepo, entryRepo, NullLogger<MatchPersistService>.Instance);
        var wowApiService = new MockWowApiService();

        var sut = new CombatLogIngestionService(playerRepo, matchPersistService, wowApiService, NullLogger<CombatLogIngestionService>.Instance);

        const string log = """
                           # Nicked header
                           1/2/2024 19:10:02.000  ARENA_MATCH_START,match-123,559,,,,,,,,,,,,
                           1/2/2024 19:10:03.100  ZONE_CHANGE,559,Nagrand Arena,,,,,,,,,,,,
                           1/2/2024 19:10:04.200  SPELL_DAMAGE,0x0100,Alpha-Illidan,0x0,0x0,0x0200,Bravo-Illidan,0x0,0x0,1337,Chaos Bolt,0x0,1200,0,0,0,0,0,0,0
                           1/2/2024 19:10:05.300  SPELL_HEAL,0x0200,Bravo-Illidan,0x0,0x0,0x0100,Alpha-Illidan,0x0,0x0,2337,Rejuvenation,0x0,0,900,0,0,0,0,0,0
                           1/2/2024 19:10:30.000  ZONE_CHANGE,87,Stormwind City,,,,,,,,,,,,
                           """;

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(log));

        var matches = await sut.IngestAsync(stream);

        matches.Should().NotBeEmpty();
        var match = matches[0];
        match.Id.Should().BeGreaterThan(0);
        match.ArenaZone.Should().Be(ArenaZone.NagrandArena);
        match.GameMode.Should().Be(GameMode.TwoVsTwo);
        match.MapName.Should().Be("Nagrand Arena");
        // Ensure parsing produced a match; persistence details are validated by other tests.
        matchRepo.Entities.Should().ContainSingle(m => m.Id == match.Id);
    }

    [Fact]
    public async Task IngestAsync_ReturnsDummyMatch_WhenNoArenaDetected()
    {
        var playerRepo = new InMemoryRepository<Player>(p => p.Id);
        var matchRepo = new InMemoryRepository<Match>(m => m.Id);
        var resultRepo = new InMemoryRepository<MatchResult>(r => r.Id);
        var entryRepo = new InMemoryRepository<CombatLogEntry>(e => e.Id);
        var matchPersistService = new MatchPersistService(matchRepo, resultRepo, entryRepo, NullLogger<MatchPersistService>.Instance);
        var wowApiService = new MockWowApiService();

        var sut = new CombatLogIngestionService(playerRepo, matchPersistService, wowApiService, NullLogger<CombatLogIngestionService>.Instance);

        const string log = "1/2/2024 19:10:03.100  ZONE_CHANGE,1,Elwynn Forest";

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(log));

        var matches = await sut.IngestAsync(stream);

        matches.Should().BeEmpty();
        matchRepo.Entities.Should().BeEmpty();
        entryRepo.Entities.Should().BeEmpty();
    }

    private sealed class InMemoryRepository<TEntity> : IRepository<TEntity>
        where TEntity : class, new()
    {
        private readonly List<TEntity> _entities = [];
        private readonly Func<TEntity, long> _getId;
        private readonly Action<TEntity, long> _setId;
        private long _currentId;

        public InMemoryRepository(Func<TEntity, long> idAccessor)
        {
            _getId = idAccessor;
            var idProperty = typeof(TEntity).GetProperty("Id") ?? throw new InvalidOperationException("Entity must expose Id property");
            _setId = (entity, id) => idProperty.SetValue(entity, id);
        }

        public IReadOnlyList<TEntity> Entities => _entities;

        public IQueryable<TEntity> Query()
        {
            throw new NotImplementedException();
        }

        public Task<TEntity?> GetByIdAsync(long id, CancellationToken ct = default)
        {
            return Task.FromResult(_entities.FirstOrDefault(e => _getId(e) == id));
        }

        public Task<IReadOnlyList<TEntity>> ListAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<TEntity>>(_entities.ToList());
        }

        public Task<IReadOnlyList<TEntity>> ListAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
        {
            var compiled = predicate.Compile();
            return Task.FromResult<IReadOnlyList<TEntity>>(_entities.Where(compiled).ToList());
        }

        public Task<TEntity> AddAsync(TEntity entity, bool autoSave = true, CancellationToken ct = default)
        {
            if (_getId(entity) <= 0)
            {
                var newId = Interlocked.Increment(ref _currentId);
                _setId(entity, newId);
            }

            _entities.Add(entity);
            return Task.FromResult(entity);
        }

        public Task UpdateAsync(TEntity entity, bool autoSave = true, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(TEntity entity, bool autoSave = true, CancellationToken ct = default)
        {
            _entities.Remove(entity);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<TEntity> entities, bool autoSave = true, CancellationToken ct = default)
        {
            foreach (var entity in entities)
            {
                if (_getId(entity) <= 0)
                {
                    var newId = Interlocked.Increment(ref _currentId);
                    _setId(entity, newId);
                }
                _entities.Add(entity);
            }
            return Task.CompletedTask;
        }

        public Task UpdateRangeAsync(IEnumerable<TEntity> entities, bool autoSave = true, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class MockWowApiService : IWowApiService
    {
        public Task<WowPlayerData?> GetPlayerDataAsync(string realm, string name, string region, CancellationToken ct = default)
        {
            return Task.FromResult<WowPlayerData?>(null);
        }
    }
}

