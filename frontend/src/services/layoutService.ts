import { userPreferenceService } from './userPreferenceService'
import type { FormLayoutSettings } from '@/types/formLayout'

const CATEGORY = 'form-layout'

function entityKey(namespace: string, entityName: string): string {
  return `${namespace}:${entityName}`
}

export interface SavedLayout {
  id: string
  name: string
  settings: FormLayoutSettings
}

export const layoutService = {
  async getLayout(namespace: string, entityName: string): Promise<SavedLayout | null> {
    const prefs = await userPreferenceService.list(CATEGORY, entityKey(namespace, entityName))
    const defaultPref = prefs.find((p) => p.isDefault) ?? prefs[0]
    if (!defaultPref) return null
    return {
      id: defaultPref.id,
      name: defaultPref.name,
      settings: defaultPref.settings as unknown as FormLayoutSettings,
    }
  },

  async listLayouts(namespace: string, entityName: string): Promise<SavedLayout[]> {
    const prefs = await userPreferenceService.list(CATEGORY, entityKey(namespace, entityName))
    return prefs.map((p) => ({
      id: p.id,
      name: p.name,
      settings: p.settings as unknown as FormLayoutSettings,
    }))
  },

  async saveLayout(
    namespace: string,
    entityName: string,
    settings: FormLayoutSettings,
    existingId?: string,
    name = 'Default Layout',
  ): Promise<string> {
    if (existingId) {
      const updated = await userPreferenceService.update(existingId, {
        settings: settings as unknown as Record<string, unknown>,
      })
      return updated.id
    }
    const created = await userPreferenceService.create({
      category: CATEGORY,
      entityKey: entityKey(namespace, entityName),
      name,
      isDefault: true,
      settings: settings as unknown as Record<string, unknown>,
    })
    return created.id
  },

  async renameLayout(id: string, name: string): Promise<void> {
    await userPreferenceService.update(id, { name })
  },

  async deleteLayout(id: string): Promise<void> {
    await userPreferenceService.remove(id)
  },

  async resetLayout(id: string): Promise<void> {
    await userPreferenceService.remove(id)
  },
}
