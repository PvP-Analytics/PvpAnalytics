using Microsoft.EntityFrameworkCore;
using PvpAnalytics.Core.Statistics;
using PvpAnalytics.Core.DTOs;
using PvpAnalytics.Core.Entities;
using PvpAnalytics.Infrastructure;

namespace PvpAnalytics.Application.Services;

public interface ISessionAnalysisService
{
    Task<SessionAnalysisDto> GetSessionAnalysisAsync(
        long playerId,
        int thresholdMinutes = 60,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken ct = default);
    Task<OptimalPlayTimes> GetOptimalPlayTimesAsync(long playerId, CancellationToken ct = default);
    Task<FatigueAnalysis> GetFatigueAnalysisAsync(long playerId, CancellationToken ct = default);
}

public class SessionAnalysisService(PvpAnalyticsDbContext dbContext) : ISessionAnalysisService
{
    public async Task<SessionAnalysisDto> GetSessionAnalysisAsync(
        long playerId,
        int thresholdMinutes = 60,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken ct = default)
    {
        var player = await dbContext.Players.FindAsync([playerId], ct);
        var dto = CreateInitialDto(playerId, player?.Name, thresholdMinutes, startDate, endDate);

        var matchResults = await LoadMatchResultsAsync(playerId, startDate, endDate, ct);
        if (matchResults.Count == 0)
            return dto;

        var sessions = GroupMatchesIntoSessions(matchResults, thresholdMinutes);
        dto.Sessions = sessions;
        dto.Summary = CalculateSessionSummary(sessions);
        dto.OptimalTimes = CalculateOptimalPlayTimes(sessions);
        dto.Fatigue = CalculateFatigueAnalysis(sessions);

        return dto;
    }

    private static SessionAnalysisDto CreateInitialDto(
        long playerId,
        string? playerName,
        int thresholdMinutes,
        DateTime? startDate,
        DateTime? endDate)
    {
        return new SessionAnalysisDto
        {
            PlayerId = playerId,
            PlayerName = playerName ?? "Unknown",
            ThresholdMinutes = thresholdMinutes,
            StartDate = startDate,
            EndDate = endDate
        };
    }

    private async Task<List<MatchResult>> LoadMatchResultsAsync(
        long playerId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken ct)
    {
        var query = dbContext.MatchResults
            .Include(mr => mr.Match)
            .Where(mr => mr.PlayerId == playerId)
            .OrderBy(mr => mr.Match.CreatedOn)
            .AsQueryable();

        query = ApplyDateFilters(query, startDate, endDate);
        return await query.ToListAsync(ct);
    }

    private static IQueryable<MatchResult> ApplyDateFilters(
        IQueryable<MatchResult> query,
        DateTime? startDate,
        DateTime? endDate)
    {
        if (startDate.HasValue)
        {
            query = query.Where(mr => mr.Match.CreatedOn >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(mr => mr.Match.CreatedOn <= endDate.Value);
        }

        return query;
    }

    private static List<SessionData> GroupMatchesIntoSessions(
        List<MatchResult> matchResults,
        int thresholdMinutes)
    {
        var sessions = new List<SessionData>();
        var currentSession = new List<(DateTime matchDate, int ratingBefore, int ratingAfter, bool isWinner, long matchId, long duration)>();

        foreach (var current in matchResults)
        {
            var matchDate = current.Match.CreatedOn;
            var matchData = (matchDate, current.RatingBefore, current.RatingAfter, current.IsWinner, current.MatchId, current.Match.Duration);

            if (ShouldStartNewSession(currentSession, matchDate, thresholdMinutes))
            {
                FinalizeCurrentSession(currentSession, sessions);
                currentSession = [matchData];
            }
            else
            {
                currentSession.Add(matchData);
            }
        }

        FinalizeCurrentSession(currentSession, sessions);
        return sessions;
    }

    private static bool ShouldStartNewSession(
        List<(DateTime matchDate, int ratingBefore, int ratingAfter, bool isWinner, long matchId, long duration)> currentSession,
        DateTime matchDate,
        int thresholdMinutes)
    {
        if (currentSession.Count == 0)
            return false;

        var lastMatchDate = currentSession[^1].matchDate;
        var timeDiff = (matchDate - lastMatchDate).TotalMinutes;
        return timeDiff > thresholdMinutes;
    }

    private static void FinalizeCurrentSession(
        List<(DateTime matchDate, int ratingBefore, int ratingAfter, bool isWinner, long matchId, long duration)> currentSession,
        List<SessionData> sessions)
    {
        if (currentSession.Count != 0)
        {
            sessions.Add(CreateSessionData(currentSession, sessions.Count + 1));
        }
    }

    private static SessionSummary CalculateSessionSummary(List<SessionData> sessions)
    {
        if (sessions.Count == 0)
        {
            return new SessionSummary();
        }

        return new SessionSummary
        {
            TotalSessions = sessions.Count,
            AverageSessionDuration = Math.Round(sessions.Average(s => s.Duration.TotalMinutes), 2),
            AverageMatchesPerSession = Math.Round(sessions.Average(s => (double)s.MatchCount), 2),
            AverageWinRate = Math.Round(sessions.Average(s => s.WinRate), 2),
            TotalRatingGain = CalculateTotalRatingGain(sessions),
            TotalRatingLoss = CalculateTotalRatingLoss(sessions),
            NetRatingChange = sessions.Sum(s => s.RatingChange)
        };
    }

    private static int CalculateTotalRatingGain(List<SessionData> sessions)
    {
        return sessions.Sum(s => s.RatingChange > 0 ? s.RatingChange : 0);
    }

    private static int CalculateTotalRatingLoss(List<SessionData> sessions)
    {
        return Math.Abs(sessions.Sum(s => s.RatingChange < 0 ? s.RatingChange : 0));
    }

    private static SessionData CreateSessionData(
        List<(DateTime matchDate, int ratingBefore, int ratingAfter, bool isWinner, long matchId, long duration)> matches,
        long sessionId)
    {
        var startTime = matches[0].matchDate;
        var endTime = matches[^1].matchDate;
        var wins = matches.Count(m => m.isWinner);
        var ratingStart = matches[0].ratingBefore;
        var ratingEnd = matches[^1].ratingAfter;

        return new SessionData
        {
            SessionId = sessionId,
            StartTime = startTime,
            EndTime = endTime,
            Duration = endTime - startTime,
            MatchCount = matches.Count,
            Wins = wins,
            Losses = matches.Count - wins,
            WinRate = WinRateSmoothing.Smooth(wins, matches.Count, GlobalCoefficients.Default),
            RatingStart = ratingStart,
            RatingEnd = ratingEnd,
            RatingChange = ratingEnd - ratingStart,
            AverageMatchDuration = Math.Round(matches.Average(m => (double)m.duration), 2),
            DayOfWeek = startTime.DayOfWeek.ToString(),
            HourOfDay = startTime.Hour
        };
    }

    public async Task<OptimalPlayTimes> GetOptimalPlayTimesAsync(long playerId, CancellationToken ct = default)
    {
        var matchResults = await LoadMatchResultsAsync(playerId, null, null, ct);
        if (matchResults.Count == 0)
            return new OptimalPlayTimes();

        var sessions = GroupMatchesIntoSessions(matchResults, 60);
        return CalculateOptimalPlayTimes(sessions);
    }

    private static OptimalPlayTimes CalculateOptimalPlayTimes(List<SessionData> sessionData)
    {
        if (sessionData.Count == 0)
            return new OptimalPlayTimes();

        // Group by hour
        var byHour = sessionData
            .GroupBy(s => s.HourOfDay)
            .Select(g => new TimeSlotPerformance
            {
                TimeSlot = $"{g.Key}-{g.Key + 1}",
                Sessions = g.Count(),
                WinRate = g.Any() ? Math.Round(g.Average(s => s.WinRate), 2) : 0,
                AverageRatingChange = g.Any() ? Math.Round(g.Average(s => (double)s.RatingChange), 2) : 0
            })
            .OrderByDescending(t => t.WinRate)
            .ToList();

        // Group by day of week
        var byDay = sessionData
            .GroupBy(s => s.DayOfWeek)
            .Select(g => new TimeSlotPerformance
            {
                TimeSlot = g.Key,
                Sessions = g.Count(),
                WinRate = g.Any() ? Math.Round(g.Average(s => s.WinRate), 2) : 0,
                AverageRatingChange = g.Any() ? Math.Round(g.Average(s => (double)s.RatingChange), 2) : 0
            })
            .OrderByDescending(t => t.WinRate)
            .ToList();

        var bestHour = byHour.OrderByDescending(h => h.WinRate).First();
        var bestDay = byDay.OrderByDescending(d => d.WinRate).First();

        return new OptimalPlayTimes
        {
            ByHour = byHour,
            ByDayOfWeek = byDay,
            BestHour = bestHour.TimeSlot,
            BestDay = bestDay.TimeSlot
        };
    }

    public async Task<FatigueAnalysis> GetFatigueAnalysisAsync(long playerId, CancellationToken ct = default)
    {
        var matchResults = await LoadMatchResultsAsync(playerId, null, null, ct);
        if (matchResults.Count == 0)
            return new FatigueAnalysis();

        var sessions = GroupMatchesIntoSessions(matchResults, 60);
        return CalculateFatigueAnalysis(sessions);
    }

    private static FatigueAnalysis CalculateFatigueAnalysis(List<SessionData> sessionData)
    {
        if (sessionData.Count == 0)
            return new FatigueAnalysis();

        // Group sessions by date and order within day
        var sessionsByDate = sessionData
            .GroupBy(s => s.StartTime.Date)
            .SelectMany(g => g.OrderBy(s => s.StartTime).Select((s, index) => new
            {
                SessionOrder = index + 1,
                Session = s
            }))
            .ToList();

        // Group by session order
        var performanceByOrder = sessionsByDate
            .GroupBy(s => s.SessionOrder)
            .Select(g => new SessionPerformanceByOrder
            {
                SessionOrder = g.Key,
                SessionCount = g.Count(),
                AverageWinRate = g.Any() ? Math.Round(g.Average(s => s.Session.WinRate), 2) : 0,
                AverageRatingChange = g.Any() ? Math.Round(g.Average(s => (double)s.Session.RatingChange), 2) : 0
            })
            .OrderBy(p => p.SessionOrder)
            .ToList();

        // Determine fatigue pattern
        var firstHalf = performanceByOrder.Take(performanceByOrder.Count / 2).ToList();
        var secondHalf = performanceByOrder.Skip(performanceByOrder.Count / 2).ToList();

        var firstHalfAvg = firstHalf.Count != 0 ? firstHalf.Average(p => p.AverageWinRate) : 0;
        var secondHalfAvg = secondHalf.Count != 0 ? secondHalf.Average(p => p.AverageWinRate) : 0;

        var showsFatigue = secondHalfAvg < firstHalfAvg - 5; // 5% drop indicates fatigue
        string fatiguePattern;
        if (showsFatigue)
        {
            fatiguePattern = "Declining";
        }
        else
        {
            fatiguePattern = secondHalfAvg > firstHalfAvg + 5 ? "Improving" : "Stable";
        }

        // Calculate optimal session length (average duration of best performing sessions)
        var bestSessions = sessionData
            .OrderByDescending(s => s.WinRate)
            .Take(Math.Max(1, sessionData.Count / 4))
            .ToList();

        var optimalLength = bestSessions.Count != 0 ? (int)Math.Round(bestSessions.Average(s => s.Duration.TotalMinutes)) : 60;

        return new FatigueAnalysis
        {
            PerformanceByOrder = performanceByOrder,
            ShowsFatigue = showsFatigue,
            FatiguePattern = fatiguePattern,
            OptimalSessionLength = optimalLength
        };
    }
}

