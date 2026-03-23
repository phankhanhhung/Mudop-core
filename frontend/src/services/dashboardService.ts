import api from './api'
import { odataService } from './odataService'
import { useMetadataStore } from '@/stores/metadata'

export interface CacheStats {
  entityCount: number
  ruleCount: number
  accessControlCount: number
  moduleCount: number
  lastReloadAt?: string
}

export interface EntityCount {
  module: string
  entity: string
  entityType: string
  count: number
}

export const dashboardService = {
  async getCacheStats(): Promise<CacheStats> {
    try {
      const response = await api.get<CacheStats>('/admin/cache-stats')
      return response.data
    } catch {
      return { entityCount: 0, ruleCount: 0, accessControlCount: 0, moduleCount: 0 }
    }
  },

  async getEntityCounts(): Promise<EntityCount[]> {
    const metadataStore = useMetadataStore()

    // Build flat list of all entities to count
    const entries: { module: string; entity: string; entityType: string }[] = []
    for (const module of metadataStore.modules) {
      for (const service of module.services) {
        for (const entity of service.entities) {
          entries.push({ module: module.name, entity: entity.name, entityType: entity.entityType })
        }
      }
    }

    // Fire all count requests in parallel instead of sequential waterfall
    const results = await Promise.allSettled(
      entries.map((e) => odataService.count(e.module, e.entityType))
    )

    return entries.map((e, i) => ({
      ...e,
      count: results[i].status === 'fulfilled' ? results[i].value : 0,
    }))
  },

  async getRecentActivity(): Promise<any[]> {
    try {
      const response = await api.get('/audit-logs', { params: { top: 10 } })
      return response.data?.value || []
    } catch {
      return []
    }
  }
}

export default dashboardService
