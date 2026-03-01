import { describe, it, expect, vi, beforeEach } from 'vitest'
import {
  getAddonConfigsBySpec,
  getAddonConfigsByComposition,
  getAddonConfigsByType,
  getAddonConfigById,
} from './addonConfigs'

const { mockGet } = vi.hoisted(() => ({ mockGet: vi.fn() }))
vi.mock('axios', () => ({
  default: {
    create: () => ({ get: mockGet }),
    isAxiosError: (err: unknown): err is { response?: { status?: number } } =>
      typeof err === 'object' && err !== null && 'response' in err,
  },
}))

const baseUrl = 'http://localhost:8080/api'

describe('addonConfigs service', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('getAddonConfigsBySpec encodes spec and returns data', async () => {
    const configs = [
      {
        id: 1,
        addonType: 'Plater',
        name: 'Arena nameplates',
        importString: 'base64data',
        associatedSpec: 'Arms Warrior',
        associatedComposition: null,
        version: 1,
        createdAt: '2025-01-01T00:00:00Z',
        updatedAt: null,
      },
    ]
    mockGet.mockResolvedValueOnce({ data: configs })

    const result = await getAddonConfigsBySpec('Arms Warrior')

    expect(mockGet).toHaveBeenCalledWith(
      `${baseUrl}/addon-configs/by-spec/Arms%20Warrior`
    )
    expect(result).toEqual(configs)
  })

  it('getAddonConfigsByComposition encodes composition and returns data', async () => {
    const configs: unknown[] = []
    mockGet.mockResolvedValueOnce({ data: configs })

    await getAddonConfigsByComposition('Warrior-Paladin-Druid')

    expect(mockGet).toHaveBeenCalledWith(
      `${baseUrl}/addon-configs/by-composition/Warrior-Paladin-Druid`
    )
  })

  it('getAddonConfigsByType returns data', async () => {
    const configs = [
      {
        id: 2,
        addonType: 'OmniBar',
        name: 'Cooldowns',
        importString: 'xyz',
        associatedSpec: null,
        associatedComposition: null,
        version: 1,
        createdAt: '2025-01-01T00:00:00Z',
        updatedAt: null,
      },
    ]
    mockGet.mockResolvedValueOnce({ data: configs })

    const result = await getAddonConfigsByType('OmniBar')

    expect(mockGet).toHaveBeenCalledWith(
      `${baseUrl}/addon-configs/by-type/OmniBar`
    )
    expect(result).toEqual(configs)
  })

  it('getAddonConfigById returns null on 404', async () => {
    mockGet.mockRejectedValueOnce({ response: { status: 404 } })

    const result = await getAddonConfigById(999)

    expect(result).toBeNull()
  })

  it('getAddonConfigById returns config on success', async () => {
    const config = {
      id: 3,
      addonType: 'WeakAuras',
      name: 'Trinket',
      importString: 'wa:...',
      associatedSpec: null,
      associatedComposition: null,
      version: 1,
      createdAt: '2025-01-01T00:00:00Z',
      updatedAt: null,
    }
    mockGet.mockResolvedValueOnce({ data: config })

    const result = await getAddonConfigById(3)

    expect(mockGet).toHaveBeenCalledWith(`${baseUrl}/addon-configs/3`)
    expect(result).toEqual(config)
  })
})
