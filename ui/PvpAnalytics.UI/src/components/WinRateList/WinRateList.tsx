import Tooltip from '../Tooltip/Tooltip'
import type { WinRateEntry } from '../../types/stats'

interface WinRateListProps {
  title: string
  entries: WinRateEntry[]
}

const WinRateList = ({ title, entries }: WinRateListProps) => (
  <div className="flex flex-col gap-3">
    <h3 className="text-xs font-semibold uppercase tracking-[0.08em] text-text-muted">
      <Tooltip content="Win rates are Bayesian-smoothed to account for sample size. Small samples are pulled toward the global average.">
        {title}
      </Tooltip>
    </h3>
    <ul className="flex flex-col gap-3">
      {entries.map((entry) => (
        <li
          key={entry.label}
          className="grid grid-cols-[1fr_auto] items-center gap-2 gap-x-4 text-sm text-text"
        >
          <span>{entry.label}</span>
          <span className="font-semibold text-accent">{entry.value}%</span>
          <div className="col-span-2 h-1.5 rounded-full bg-white/10">
            <progress
              aria-valuenow={entry.value}
              aria-valuemin={0}
              aria-valuemax={100}
              aria-valuetext={`Win rate ${entry.value}%`}
              className="h-full rounded-full bg-gradient-to-r from-accent to-sky-400 transition-all duration-300"
              style={{ width: `${entry.value}%` }}
            />
          </div>
        </li>
      ))}
    </ul>
  </div>
)

export default WinRateList

