import { useEffect, useMemo, useRef, useState } from 'react'
import { useShallow } from 'zustand/react/shallow'
import Card from '../components/Card/Card'
import ToggleGroup from '../components/ToggleGroup/ToggleGroup'
import Sparkline from '../components/Sparkline/Sparkline'
import WinRateList from '../components/WinRateList/WinRateList'
import MatchesTable from '../components/MatchesTable/MatchesTable'
import MatchHighlight from '../components/MatchHighlight/MatchHighlight'
import MetricCard from '../components/MetricCard/MetricCard'
import Tooltip from '../components/Tooltip/Tooltip'
import ComparisonToggle from '../components/ComparisonToggle/ComparisonToggle'
import AnomalyBadge from '../components/AnomalyBadge/AnomalyBadge'
import ForecastCard from '../components/ForecastCard/ForecastCard'
import { getErrorStyles } from '../utils/themeColors'
import { useStatsStore } from '../store/statsStore'
import { detectWinRateAnomaly, generateForecast, smoothedWinRate, sampleSizeLabel } from '../utils/statisticsUtils'

const StatsPage = () => {
  const [activeTopTab, setActiveTopTab] = useState('rating')
  const [activeFilter, setActiveFilter] = useState('recent')
  const [showComparison, setShowComparison] = useState(false)
  const { data, loading, error } = useStatsStore(
    useShallow((state) => ({
      data: state.data,
      loading: state.loading,
      error: state.error,
    })),
  )
  const loadStats = useStatsStore((state) => state.loadStats)
  const hasRequestedInitialLoad = useRef(false)

  useEffect(() => {
    if (hasRequestedInitialLoad.current) {
      return
    }
    hasRequestedInitialLoad.current = true
    void loadStats()
  }, [loadStats])

  const stats = data

  const winRate = useMemo(() => {
    if (!stats) return 0
    const wins = stats.matches.filter((m) => m.result === 'Victory').length
    const total = stats.matches.length
    return smoothedWinRate(wins, total)
  }, [stats])

  const matchSampleLabel = useMemo(() => {
    if (!stats) return ''
    return sampleSizeLabel(stats.matches.length)
  }, [stats])

  // Detect win rate anomaly
  const winRateAnomaly = useMemo(() => {
    if (!stats || stats.matches.length === 0) return null
    const wins = stats.matches.filter((m) => m.result === 'Victory').length
    const winRateDecimal = wins / stats.matches.length
    return detectWinRateAnomaly(winRateDecimal, stats.matches.length)
  }, [stats])

  // Calculate average duration
  const avgDuration = useMemo(() => {
    if (!stats) return '0:00'
    const durations = stats.matches.map((m) => {
      const [mins, secs] = m.duration.split(':').map(Number)
      return mins * 60 + secs
    })
    const avg = durations.length > 0 ? durations.reduce((a, b) => a + b, 0) / durations.length : 0
    const mins = Math.floor(avg / 60)
    const secs = Math.round(avg % 60)
    return `${mins}:${secs.toString().padStart(2, '0')}`
  }, [stats])

  // Calculate rating trend
  const ratingTrend = useMemo(() => {
    if (!stats || stats.overviewTrend.length < 2) return 'neutral'
    const current = stats.overviewTrend.at(-1) ?? 0
    const previous = stats.overviewTrend.at(-2) ?? current
    if (current > previous) return 'up'
    if (current < previous) return 'down'
    return 'neutral'
  }, [stats])

  const ratingChange = useMemo(() => {
    if (!stats || stats.overviewTrend.length < 2) return undefined
    const current = stats.overviewTrend.at(-1) ?? 0
    const previous = stats.overviewTrend.at(-2) ?? current
    const change = current - previous

    let label = '0'
    if (change > 0) {
      label = `+${change}`
    } else if (change < 0) {
      label = `${change}`
    }

    return label
  }, [stats])

  // Generate rating forecast
  const ratingForecast = useMemo(() => {
    if (!stats || stats.overviewTrend.length < 5) return null
    const currentRating = stats.overviewTrend.at(-1) || 0
    // Target: next rating milestone (e.g., 2000, 2100, etc.)
    const nextMilestone = Math.ceil(currentRating / 100) * 100
    return generateForecast(stats.overviewTrend, 7, nextMilestone)
  }, [stats])

  const content = stats ? (
    <>
      <div className="flex justify-end overflow-x-auto">
        <ToggleGroup
          options={[
            { id: 'rating', label: 'Top Player by Rating' },
            { id: 'dps', label: 'Top by DPS / HPS' },
            { id: 'recent', label: 'Recent Matches' },
          ]}
          activeId={activeTopTab}
          onChange={setActiveTopTab}
        />
      </div>

      {/* Player Header */}
      <section className="flex flex-col gap-6 rounded-3xl border border-accent-muted/30 bg-gradient-to-br from-background/80 to-surface/70 p-4 sm:p-6 lg:p-8 shadow-inner shadow-black/20 backdrop-blur">
        <div className="flex flex-col sm:flex-row items-start sm:items-center gap-4 sm:gap-6">
          <div className="grid h-16 w-16 sm:h-20 sm:w-20 place-items-center rounded-2xl bg-gradient-to-br from-accent to-sky-400 text-xl sm:text-2xl font-bold text-white flex-shrink-0" aria-hidden={true}>
            {stats.player.name.substring(0, 1)}
          </div>
          <div className="flex-1 min-w-0">
            <span className="block text-xs font-semibold uppercase tracking-[0.14em] text-text-muted">
              {stats.player.title}
            </span>
            <h1 className="mt-1 text-2xl sm:text-3xl font-semibold text-text truncate">{stats.player.name}</h1>
          </div>
          <div className="text-left sm:text-right w-full sm:w-auto">
            <span className="block text-xs font-semibold uppercase tracking-[0.12em] text-text-muted">Rating</span>
            <span className="mt-1 block text-3xl sm:text-4xl font-bold text-white">{stats.player.rating}</span>
          </div>
        </div>
      </section>

      {/* Key Metrics Cards */}
      <div className="grid gap-4 sm:gap-6 grid-cols-1 sm:grid-cols-2 lg:grid-cols-4">
        {ratingForecast && (
          <div className="sm:col-span-2 lg:col-span-4">
            <ForecastCard
              forecast={ratingForecast}
              title="Rating Forecast"
              currentValue={stats.overviewTrend.at(-1) ?? 0}
            />
          </div>
        )}
        <MetricCard
          title="Win Rate"
          value={`${winRate}%`}
          subtitle={
            <div className="flex items-center gap-2">
              <span>{`${stats.matches.filter((m) => m.result === 'Victory').length}W - ${stats.matches.filter((m) => m.result === 'Defeat').length}L`}</span>
              {winRateAnomaly && <AnomalyBadge anomaly={winRateAnomaly} />}
            </div>
          }
          trend={winRate >= 50 ? 'up' : 'down'}
          trendValue={winRate >= 50 ? 'Above 50%' : 'Below 50%'}
          tooltip={`Bayesian-smoothed win rate (${matchSampleLabel}). Accounts for sample size to reduce noise from small match counts.`}
        />
        <MetricCard
          title="Total Matches"
          value={stats.matches.length}
          subtitle="All time"
          trend="neutral"
          tooltip="Total number of matches recorded for this player."
        />
        <MetricCard
          title="Avg. Duration"
          value={avgDuration}
          subtitle="Per match"
          trend="neutral"
          tooltip="Average match duration calculated from all recorded matches."
        />
        <MetricCard
          title="Rating Change"
          value={ratingChange || '0'}
          subtitle="Recent trend"
          trend={ratingTrend}
          trendValue={ratingChange}
          tooltip="Rating change based on recent performance trend."
        />
      </div>

      {error && (
        <div className={`rounded-2xl border px-4 py-3 text-sm ${getErrorStyles()}`}>
          {error}
        </div>
      )}

      {/* Charts Section */}
      <div className="grid gap-6 sm:gap-8 grid-cols-1 sm:grid-cols-2 xl:grid-cols-3" aria-busy={loading}>
        <Tooltip content="Rating trend over time. Shows performance progression across recent matches. Enable comparison to see previous period.">
          <Card
            title="Rating Trend"
            subtitle="Performance over time"
            actions={
              <ComparisonToggle
                enabled={showComparison}
                onToggle={setShowComparison}
                currentPeriod="Last 7 days"
                comparisonPeriod="Previous 7 days"
              />
            }
          >
            <div className="py-4">
              <Sparkline
                values={stats.overviewTrend}
                comparisonValues={showComparison ? stats.overviewTrend.map((v) => v - 50) : undefined}
                showComparison={showComparison}
              />
            </div>
          </Card>
        </Tooltip>
        <Tooltip content="Win rate percentage broken down by game mode (2v2, 3v3, RBG).">
          <Card title="Winrate by Bracket" subtitle="Performance across game modes">
            <WinRateList title="Winrate by Bracket" entries={stats.winRateByBracket} />
          </Card>
        </Tooltip>
        <Tooltip content="Win rate percentage broken down by arena map location.">
          <Card title="Winrate by Map" subtitle="Performance by arena location">
            <WinRateList title="Winrate by Map" entries={stats.winRateByMap} />
          </Card>
        </Tooltip>
      </div>

      <Card
        title="Match History"
        subtitle="Recent match results and statistics"
        actions={
          <ToggleGroup
            options={[
              { id: 'recent', label: 'Recent' },
              { id: 'favorites', label: 'Favorites' },
            ]}
            activeId={activeFilter}
            onChange={setActiveFilter}
          />
        }
      >
        <MatchesTable matches={stats.matches} />
      </Card>

      <Card
        title={`${stats.highlight.mode} • ${stats.highlight.map}`}
        subtitle={`${stats.highlight.result} • Rating ${stats.highlight.ratingDelta > 0 ? '+' : ''}${stats.highlight.ratingDelta}`}
      >
        <MatchHighlight match={stats.highlight} />
      </Card>

      {loading && (
        <div className="rounded-2xl border border-accent-muted/40 border-dashed bg-accent-muted/10 px-4 py-3 text-center text-sm text-text">
          Loading latest data…
        </div>
      )}
    </>
  ) : (
    <div className="flex flex-col gap-6">
      {error && (
        <div className={`rounded-2xl border px-4 py-3 text-sm ${getErrorStyles()}`}>
          {error}
        </div>
      )}
      {!error && loading && (
        <div className="rounded-2xl border border-accent-muted/40 border-dashed bg-accent-muted/10 px-4 py-3 text-center text-sm text-text">
          Loading latest data…
        </div>
      )}
      {!error && !loading && (
        <div className="rounded-2xl border border-accent-muted/40 bg-surface/50 px-4 py-3 text-sm text-text-muted">
          No statistics available.
        </div>
      )}
    </div>
  )

  return (
    <div className="flex flex-col gap-8">
      {content}
    </div>
  )
}

export default StatsPage

