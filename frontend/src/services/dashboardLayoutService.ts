import { userPreferenceService } from './userPreferenceService'
import type { DashboardLayout } from '@/types/dashboard'

const CATEGORY = 'dashboard-layout'
const ENTITY_KEY = 'global:default'

export interface SavedDashboard {
  id: string
  settings: DashboardLayout
}

export const dashboardLayoutService = {
  async getLayout(): Promise<SavedDashboard | null> {
    const prefs = await userPreferenceService.list(CATEGORY, ENTITY_KEY)
    const pref = prefs.find((p) => p.isDefault) ?? prefs[0]
    if (!pref) return null
    return { id: pref.id, settings: pref.settings as unknown as DashboardLayout }
  },

  async saveLayout(layout: DashboardLayout, existingId?: string): Promise<string> {
    if (existingId) {
      const updated = await userPreferenceService.update(existingId, {
        settings: layout as unknown as Record<string, unknown>,
      })
      return updated.id
    }
    const created = await userPreferenceService.create({
      category: CATEGORY,
      entityKey: ENTITY_KEY,
      name: 'Dashboard Layout',
      isDefault: true,
      settings: layout as unknown as Record<string, unknown>,
    })
    return created.id
  },

  async resetLayout(id: string): Promise<void> {
    await userPreferenceService.remove(id)
  },
}
