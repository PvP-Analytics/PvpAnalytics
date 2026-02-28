using Microsoft.EntityFrameworkCore;
using PvpAnalytics.Core.Entities;


namespace PvpAnalytics.Infrastructure;

public class PvpAnalyticsDbContext(DbContextOptions<PvpAnalyticsDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players { get; set; }
    public DbSet<Match> Matches { get; set; }
    public DbSet<MatchResult> MatchResults { get; set; }
    public DbSet<CombatLogEntry> CombatLogEntries { get; set; }
    public DbSet<Team> Teams { get; set; }
    public DbSet<TeamMember> TeamMembers { get; set; }
    public DbSet<TeamMatch> TeamMatches { get; set; }
    public DbSet<FavoritePlayer> FavoritePlayers { get; set; }
    public DbSet<Rival> Rivals { get; set; }
    public DbSet<UserBadge> UserBadges { get; set; }
    public DbSet<FeaturedMatch> FeaturedMatches { get; set; }
    public DbSet<CommunityRanking> CommunityRankings { get; set; }
    public DbSet<MatchDiscussionThread> MatchDiscussionThreads { get; set; }
    public DbSet<MatchDiscussionPost> MatchDiscussionPosts { get; set; }
    public DbSet<PlayerProfile> PlayerProfiles { get; set; }
    public DbSet<ConsentAuditLog> ConsentAuditLogs { get; set; }
    public DbSet<AddonConfig> AddonConfigs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MatchResult>()
            .HasIndex(mr => new { mr.MatchId, mr.PlayerId })
            .IsUnique();

        modelBuilder.Entity<CombatLogEntry>()
            .HasOne(c => c.SourcePlayer)
            .WithMany(p => p.SourceCombatLogs)
            .HasForeignKey(c => c.SourcePlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CombatLogEntry>()
            .HasOne(c => c.TargetPlayer)
            .WithMany(p => p.TargetCombatLogs)
            .HasForeignKey(c => c.TargetPlayerId)
            .OnDelete(DeleteBehavior.Restrict);
        
        
        modelBuilder.Entity<Match>()
            .HasIndex(m => m.UniqueHash)
            .IsUnique();

        modelBuilder.Entity<TeamMember>()
            .HasOne(tm => tm.Team)
            .WithMany(t => t.Members)
            .HasForeignKey(tm => tm.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TeamMember>()
            .HasOne(tm => tm.Player)
            .WithMany()
            .HasForeignKey(tm => tm.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TeamMember>()
            .HasIndex(tm => new { tm.TeamId, tm.PlayerId })
            .IsUnique();

        modelBuilder.Entity<TeamMatch>()
            .HasOne(tm => tm.Team)
            .WithMany(t => t.TeamMatches)
            .HasForeignKey(tm => tm.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TeamMatch>()
            .HasOne(tm => tm.Match)
            .WithMany()
            .HasForeignKey(tm => tm.MatchId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TeamMatch>()
            .HasIndex(tm => new { tm.TeamId, tm.MatchId })
            .IsUnique();

        modelBuilder.Entity<Team>()
            .HasIndex(t => t.CreatedByUserId);

        modelBuilder.Entity<FavoritePlayer>()
            .HasOne(fp => fp.TargetPlayer)
            .WithMany()
            .HasForeignKey(fp => fp.TargetPlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<FavoritePlayer>()
            .HasIndex(fp => new { fp.OwnerUserId, fp.TargetPlayerId })
            .IsUnique();

        modelBuilder.Entity<FavoritePlayer>()
            .HasIndex(fp => fp.OwnerUserId);

        modelBuilder.Entity<Rival>()
            .HasOne(r => r.OpponentPlayer)
            .WithMany()
            .HasForeignKey(r => r.OpponentPlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Rival>()
            .HasIndex(r => new { r.OwnerUserId, r.OpponentPlayerId })
            .IsUnique();

        modelBuilder.Entity<Rival>()
            .HasIndex(r => r.OwnerUserId);

        modelBuilder.Entity<Rival>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_Rival_IntensityScore",
                "\"IntensityScore\" >= 1 AND \"IntensityScore\" <= 10"));

        modelBuilder.Entity<UserBadge>()
            .HasIndex(ub => ub.UserId);

        modelBuilder.Entity<FeaturedMatch>()
            .HasOne(fm => fm.Match)
            .WithMany()
            .HasForeignKey(fm => fm.MatchId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<FeaturedMatch>()
            .HasIndex(fm => fm.FeaturedAt);

        modelBuilder.Entity<CommunityRanking>()
            .HasOne(cr => cr.Player)
            .WithMany()
            .HasForeignKey(cr => cr.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CommunityRanking>()
            .HasOne(cr => cr.Team)
            .WithMany()
            .HasForeignKey(cr => cr.TeamId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CommunityRanking>()
            .HasIndex(cr => new { cr.RankingType, cr.Period, cr.Rank });

        modelBuilder.Entity<CommunityRanking>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_CommunityRankings_PlayerOrTeam",
                "\"PlayerId\" IS NOT NULL OR \"TeamId\" IS NOT NULL"));

        modelBuilder.Entity<MatchDiscussionThread>()
            .HasOne(dt => dt.Match)
            .WithMany()
            .HasForeignKey(dt => dt.MatchId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MatchDiscussionThread>()
            .HasIndex(dt => dt.MatchId);

        modelBuilder.Entity<MatchDiscussionPost>()
            .HasOne(dp => dp.Thread)
            .WithMany(dt => dt.Posts)
            .HasForeignKey(dp => dp.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MatchDiscussionPost>()
            .HasOne(dp => dp.ParentPost)
            .WithMany()
            .HasForeignKey(dp => dp.ParentPostId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MatchDiscussionPost>()
            .HasIndex(dp => dp.ThreadId);

        modelBuilder.Entity<PlayerProfile>()
            .HasOne(pp => pp.Player)
            .WithMany()
            .HasForeignKey(pp => pp.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PlayerProfile>()
            .HasIndex(pp => pp.PlayerId)
            .IsUnique();

        modelBuilder.Entity<PlayerProfile>()
            .HasIndex(pp => pp.LinkedUserId);

        modelBuilder.Entity<PlayerProfile>()
            .HasIndex(pp => pp.PublicConsent);

        modelBuilder.Entity<ConsentAuditLog>()
            .HasOne(ca => ca.Profile)
            .WithMany()
            .HasForeignKey(ca => ca.PlayerProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ConsentAuditLog>()
            .HasIndex(ca => ca.PlayerProfileId);

        modelBuilder.Entity<AddonConfig>()
            .HasIndex(ac => ac.AddonType);

        modelBuilder.Entity<AddonConfig>()
            .HasIndex(ac => ac.AssociatedSpec);

        modelBuilder.Entity<AddonConfig>()
            .HasIndex(ac => ac.AssociatedComposition);
    }
}