using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PvpAnalytics.Application.Logs;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Core.Entities;
using PvpAnalytics.Core.Enum;
using PvpAnalytics.Core.Repositories;
using Xunit;

namespace PvpAnalytics.Tests.Logs;

public class MatchPersistServiceTests
{
    [Fact]
    public async Task PersistAsync_WithSameDedupKey_ReturnsExistingMatch_NoDuplicate()
    {
        var matchRepo = new DuplicateAwareMatchRepository();
        var resultRepo = new InMemoryRepository<MatchResult>(r => r.Id);
        var entryRepo = new InMemoryRepository<CombatLogEntry>(e => e.Id);
        var sut = new MatchPersistService(matchRepo, resultRepo, entryRepo, NullLogger<MatchPersistService>.Instance);

        var playerA = new Player { Id = 1, Name = "Alpha" };
        var playerB = new Player { Id = 2, Name = "Bravo" };
        var playersByKey = new Dictionary<string, Player>(StringComparer.OrdinalIgnoreCase)
        {
            ["Alpha"] = playerA,
            ["Bravo"] = playerB
        };
        var participants = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Alpha", "Bravo" };
        var start = new DateTime(2024, 1, 2, 19, 10, 2, DateTimeKind.Utc);
        var end = new DateTime(2024, 1, 2, 19, 10, 30, DateTimeKind.Utc);
        var entries = new List<CombatLogEntry>();
        var context = new MatchIngestionContext(
            ArenaZone.NagrandArena,
            start,
            end,
            participants,
            entries,
            playersByKey,
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase),
            GameMode.TwoVsTwo,
            "match-123",
            "Nagrand Arena");

        const string dedupKey = "SAME_KEY_FOR_IDEMPOTENCY";

        var first = await sut.PersistAsync(context, dedupKey);
        var second = await sut.PersistAsync(context, dedupKey);

        first.Id.Should().BeGreaterThan(0);
        second.Id.Should().Be(first.Id, "second call with same dedup key must return existing match (idempotent)");
        matchRepo.Entities.Should().ContainSingle(m => m.UniqueHash == dedupKey);
    }

    /// <summary>In-memory match repo that throws DbUpdateException (23505) when adding a match with duplicate UniqueHash so idempotency is exercised.</summary>
    private sealed class DuplicateAwareMatchRepository : InMemoryRepository<Match>
    {
        public DuplicateAwareMatchRepository() : base(m => m.Id) { }

        public override Task<Match> AddAsync(Match entity, bool autoSave = true, CancellationToken ct = default)
        {
            if (!string.IsNullOrEmpty(entity.UniqueHash) && Entities.Any(m => m.UniqueHash == entity.UniqueHash))
                throw new DbUpdateException("23505 duplicate key value violates unique constraint \"IX_Matches_UniqueHash\"");
            return base.AddAsync(entity, autoSave, ct);
        }
    }

    private class InMemoryRepository<TEntity> : IRepository<TEntity> where TEntity : class, new()
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

        public IQueryable<TEntity> Query() => throw new NotImplementedException();

        public Task<TEntity?> GetByIdAsync(long id, CancellationToken ct = default) =>
            Task.FromResult(_entities.FirstOrDefault(e => _getId(e) == id));

        public Task<IReadOnlyList<TEntity>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TEntity>>(_entities.ToList());

        public Task<IReadOnlyList<TEntity>> ListAsync(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
        {
            var compiled = predicate.Compile();
            return Task.FromResult<IReadOnlyList<TEntity>>(_entities.Where(compiled).ToList());
        }

        public virtual Task<TEntity> AddAsync(TEntity entity, bool autoSave = true, CancellationToken ct = default)
        {
            if (_getId(entity) <= 0)
            {
                var newId = Interlocked.Increment(ref _currentId);
                _setId(entity, newId);
            }
            _entities.Add(entity);
            return Task.FromResult(entity);
        }

        public Task UpdateAsync(TEntity entity, bool autoSave = true, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(TEntity entity, bool autoSave = true, CancellationToken ct = default)
        {
            _entities.Remove(entity);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<TEntity> entities, bool autoSave = true, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateRangeAsync(IEnumerable<TEntity> entities, bool autoSave = true, CancellationToken ct = default) => Task.CompletedTask;
    }
}
