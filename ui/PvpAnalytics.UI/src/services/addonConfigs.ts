import axios from 'axios'
import type { AddonConfig } from '../types/addonConfig'

const apiClient = axios.create({ timeout: 10000 })

const getBaseUrl = (): string =>
  import.meta.env.VITE_ANALYTICS_API_BASE_URL || 'http://localhost:8080/api'

export async function getAddonConfigsBySpec(spec: string): Promise<AddonConfig[]> {
  const baseUrl = getBaseUrl()
  const { data } = await apiClient.get<AddonConfig[]>(
    `${baseUrl}/addon-configs/by-spec/${encodeURIComponent(spec)}`
  )
  return data
}

export async function getAddonConfigsByComposition(composition: string): Promise<AddonConfig[]> {
  const baseUrl = getBaseUrl()
  const { data } = await apiClient.get<AddonConfig[]>(
    `${baseUrl}/addon-configs/by-composition/${encodeURIComponent(composition)}`
  )
  return data
}

export async function getAddonConfigsByType(addonType: string): Promise<AddonConfig[]> {
  const baseUrl = getBaseUrl()
  const { data } = await apiClient.get<AddonConfig[]>(
    `${baseUrl}/addon-configs/by-type/${encodeURIComponent(addonType)}`
  )
  return data
}

export async function getAddonConfigById(id: number): Promise<AddonConfig | null> {
  const baseUrl = getBaseUrl()
  try {
    const { data } = await apiClient.get<AddonConfig>(`${baseUrl}/addon-configs/${id}`)
    return data
  } catch (err) {
    if (axios.isAxiosError(err) && err.response?.status === 404) return null
    throw err
  }
}
