/**
 * Addon config DTO matching backend PvpAnalytics.Core.Entities.AddonConfig.
 * Used for Gladius, Plater, WeakAuras, OmniBar import strings.
 */
export interface AddonConfig {
  id: number
  addonType: string
  name: string
  importString: string
  associatedSpec: string | null
  associatedComposition: string | null
  version: number
  createdByUserId?: string | null
  createdAt: string
  updatedAt: string | null
}
