import { useState, useCallback } from 'react'
import Card from '../components/Card/Card'
import Tooltip from '../components/Tooltip/Tooltip'
import {
  getAddonConfigsBySpec,
  getAddonConfigsByComposition,
} from '../services/addonConfigs'
import type { AddonConfig } from '../types/addonConfig'

const SPEC_OPTIONS: string[] = [
  'Arms Warrior',
  'Fury Warrior',
  'Protection Warrior',
  'Holy Paladin',
  'Protection Paladin',
  'Retribution Paladin',
  'Beast Mastery Hunter',
  'Marksmanship Hunter',
  'Survival Hunter',
  'Assassination Rogue',
  'Outlaw Rogue',
  'Subtlety Rogue',
  'Discipline Priest',
  'Holy Priest',
  'Shadow Priest',
  'Blood Death Knight',
  'Frost Death Knight',
  'Unholy Death Knight',
  'Elemental Shaman',
  'Enhancement Shaman',
  'Restoration Shaman',
  'Arcane Mage',
  'Fire Mage',
  'Frost Mage',
  'Affliction Warlock',
  'Demonology Warlock',
  'Destruction Warlock',
  'Brewmaster Monk',
  'Mistweaver Monk',
  'Windwalker Monk',
  'Balance Druid',
  'Feral Druid',
  'Guardian Druid',
  'Restoration Druid',
  'Havoc Demon Hunter',
  'Vengeance Demon Hunter',
  'Devastation Evoker',
  'Preservation Evoker',
  'Augmentation Evoker',
]

const ADDON_LABELS: Record<string, string> = {
  Gladius: 'Gladius (arena frames)',
  Plater: 'Plater (nameplates)',
  WeakAuras: 'WeakAuras',
  OmniBar: 'OmniBar (cooldown tracking)',
}

function groupConfigsByType(configs: AddonConfig[]): Map<string, AddonConfig[]> {
  const map = new Map<string, AddonConfig[]>()
  for (const c of configs) {
    const key = c.addonType || 'Other'
    if (!map.has(key)) map.set(key, [])
    map.get(key)!.push(c)
  }
  return map
}

function CopyButton({
  config,
  onCopied,
}: {
  config: AddonConfig
  onCopied: (addonType: string) => void
}) {
  const label = ADDON_LABELS[config.addonType] ?? config.addonType
  const handleCopy = useCallback(async () => {
    try {
      await navigator.clipboard.writeText(config.importString)
      onCopied(config.addonType)
    } catch {
      onCopied('')
    }
  }, [config.importString, config.addonType, onCopied])

  return (
    <Tooltip
      content={`Copy import string. Then in-game open ${config.addonType} and paste via its import option.`}
      position="top"
    >
      <button
        type="button"
        onClick={handleCopy}
        className="px-3 py-1.5 rounded-lg bg-accent/20 text-accent hover:bg-accent/30 text-sm font-medium transition-colors"
      >
        Copy for {label}
      </button>
    </Tooltip>
  )
}

const BuildsPage = () => {
  const [contextType, setContextType] = useState<'spec' | 'composition'>('spec')
  const [selectedSpec, setSelectedSpec] = useState<string>(SPEC_OPTIONS[0] ?? '')
  const [compositionQuery, setCompositionQuery] = useState('')
  const [configs, setConfigs] = useState<AddonConfig[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [copiedAddon, setCopiedAddon] = useState<string | null>(null)

  const loadConfigs = useCallback(async () => {
    setError(null)
    setLoading(true)
    try {
      if (contextType === 'spec') {
        const list = await getAddonConfigsBySpec(selectedSpec)
        setConfigs(list)
      } else {
        const comp = compositionQuery.trim()
        if (!comp) {
          setConfigs([])
          setError('Enter a composition (e.g. Warrior-Paladin-Druid)')
          return
        }
        const list = await getAddonConfigsByComposition(comp)
        setConfigs(list)
      }
    } catch (err) {
      setError('Failed to load addon configs')
      setConfigs([])
    } finally {
      setLoading(false)
    }
  }, [contextType, selectedSpec, compositionQuery])

  const handleCopied = useCallback((addonType: string) => {
    setCopiedAddon(addonType)
    setTimeout(() => setCopiedAddon(null), 2500)
  }, [])

  const grouped = groupConfigsByType(configs)
  const hasConfigs = configs.length > 0

  return (
    <div className="flex flex-col gap-6 p-4 sm:p-6">
      <h1 className="text-2xl sm:text-3xl font-bold">Builds & Addon Configs</h1>
      <p className="text-text-muted text-sm">
        Load recommended addon configurations (Gladius, Plater, WeakAuras, OmniBar) for a spec or
        composition, then copy and import in-game.
      </p>

      <Card title="Select context" subtitle="Load configs by spec or by team composition">
        <div className="flex flex-col gap-4">
          <div className="flex flex-wrap gap-4 items-center">
            <span className="text-sm font-medium text-text-muted">Context:</span>
            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="radio"
                name="contextType"
                checked={contextType === 'spec'}
                onChange={() => setContextType('spec')}
                className="rounded border-accent-muted text-accent"
              />
              By spec
            </label>
            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="radio"
                name="contextType"
                checked={contextType === 'composition'}
                onChange={() => setContextType('composition')}
                className="rounded border-accent-muted text-accent"
              />
              By composition
            </label>
          </div>

          {contextType === 'spec' ? (
            <div className="flex flex-col sm:flex-row gap-2 items-start sm:items-center">
              <label className="text-sm font-medium text-text-muted shrink-0">Spec:</label>
              <select
                value={selectedSpec}
                onChange={(e) => setSelectedSpec(e.target.value)}
                className="min-w-[200px] px-3 py-2 rounded-lg border border-accent-muted/40 bg-surface text-text"
              >
                {SPEC_OPTIONS.map((spec) => (
                  <option key={spec} value={spec}>
                    {spec}
                  </option>
                ))}
              </select>
            </div>
          ) : (
            <div className="flex flex-col sm:flex-row gap-2 items-start sm:items-center">
              <label className="text-sm font-medium text-text-muted shrink-0">Composition:</label>
              <input
                type="text"
                value={compositionQuery}
                onChange={(e) => setCompositionQuery(e.target.value)}
                placeholder="e.g. Warrior-Paladin-Druid"
                className="min-w-[220px] px-3 py-2 rounded-lg border border-accent-muted/40 bg-surface text-text"
              />
            </div>
          )}

          <button
            type="button"
            onClick={loadConfigs}
            disabled={loading}
            className="self-start px-4 py-2 rounded-lg bg-accent text-white hover:bg-accent/90 disabled:opacity-50 transition-colors"
          >
            {loading ? 'Loading…' : 'Load configs'}
          </button>
        </div>
      </Card>

      {error && (
        <div className="rounded-lg border border-red-500/40 bg-red-500/10 px-4 py-2 text-sm text-red-600 dark:text-red-400">
          {error}
        </div>
      )}

      {copiedAddon && (
        <div
          className="rounded-lg border border-green-500/40 bg-green-500/10 px-4 py-2 text-sm text-green-700 dark:text-green-300"
          role="status"
        >
          Copied. Paste the string in {copiedAddon} in-game to import.
        </div>
      )}

      {!loading && hasConfigs && (
        <Card
          title="Addon configs"
          subtitle={`${configs.length} config(s). Copy and paste into each addon's import option in-game.`}
        >
          <div className="flex flex-col gap-6">
            {Array.from(grouped.entries()).map(([addonType, list]) => (
              <div key={addonType}>
                <h3 className="text-base font-semibold text-text mb-2">
                  {ADDON_LABELS[addonType] ?? addonType}
                </h3>
                <ul className="flex flex-col gap-2">
                  {list.map((config) => (
                    <li
                      key={config.id}
                      className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-accent-muted/30 bg-background/50 p-3"
                    >
                      <span className="font-medium text-text">{config.name}</span>
                      <CopyButton config={config} onCopied={handleCopied} />
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </div>
        </Card>
      )}

      {!loading && !hasConfigs && !error && configs.length === 0 && (
        <Card>
          <p className="text-text-muted text-sm">
            Select a spec or composition above and click &quot;Load configs&quot; to see recommended
            addon configs. If none appear, no configs are linked yet for that context.
          </p>
        </Card>
      )}

      {loading && (
        <div className="text-text-muted text-sm">Loading addon configs…</div>
      )}
    </div>
  )
}

export default BuildsPage
