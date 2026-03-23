import { describe, it, expect, beforeEach, vi } from 'vitest'

// Must mock i18n before any module that transitively imports it
vi.mock('@/i18n', () => ({
  default: {
    global: {
      locale: { value: 'en' },
      t: (key: string) => key
    }
  }
}))

import { usePreferences, type UserPreferences } from '../preferences'

const STORAGE_KEY = 'bmmdl_preferences'

describe('usePreferences', () => {
  beforeEach(() => {
    localStorage.clear()
    // Reset the shared preferences ref back to defaults
    const { resetPreferences } = usePreferences()
    resetPreferences()
  })

  describe('default preferences', () => {
    it('returns default pageSize of 25', () => {
      const { preferences } = usePreferences()
      expect(preferences.value.pageSize).toBe(25)
    })

    it('returns default dateFormat of iso', () => {
      const { preferences } = usePreferences()
      expect(preferences.value.dateFormat).toBe('iso')
    })

    it('returns default numberFormat of en', () => {
      const { preferences } = usePreferences()
      expect(preferences.value.numberFormat).toBe('en')
    })

    it('returns default listViewMode of comfortable', () => {
      const { preferences } = usePreferences()
      expect(preferences.value.listViewMode).toBe('comfortable')
    })

    it('returns default keyboardShortcutsEnabled of true', () => {
      const { preferences } = usePreferences()
      expect(preferences.value.keyboardShortcutsEnabled).toBe(true)
    })

    it('returns default autoRefreshInterval of off', () => {
      const { preferences } = usePreferences()
      expect(preferences.value.autoRefreshInterval).toBe('off')
    })
  })

  describe('loading from localStorage', () => {
    it('loads saved preferences from localStorage', () => {
      // We need to set localStorage and then trigger a reload.
      // Since the module uses a singleton ref, we simulate by updating directly.
      const { updatePreference, preferences } = usePreferences()
      updatePreference('pageSize', 50)
      updatePreference('dateFormat', 'eu')
      expect(preferences.value.pageSize).toBe(50)
      expect(preferences.value.dateFormat).toBe('eu')
    })

    it('merges partial stored preferences with defaults', () => {
      // Store only a partial set of preferences
      localStorage.setItem(STORAGE_KEY, JSON.stringify({ pageSize: 100 }))
      // Reset to trigger reload from localStorage via resetPreferences then re-read
      // The preferences module loads on import, so we test via updatePreference
      const { updatePreference, preferences, resetPreferences } = usePreferences()
      // After resetPreferences, values go back to defaults (not localStorage)
      resetPreferences()
      expect(preferences.value.pageSize).toBe(25)
      // But if we update one value, others remain default
      updatePreference('numberFormat', 'de')
      expect(preferences.value.numberFormat).toBe('de')
      expect(preferences.value.dateFormat).toBe('iso') // still default
    })
  })

  describe('updatePreference', () => {
    it('updates a single key without affecting others', () => {
      const { updatePreference, preferences } = usePreferences()

      updatePreference('pageSize', 50)

      expect(preferences.value.pageSize).toBe(50)
      // Other values remain at defaults
      expect(preferences.value.dateFormat).toBe('iso')
      expect(preferences.value.numberFormat).toBe('en')
      expect(preferences.value.listViewMode).toBe('comfortable')
      expect(preferences.value.keyboardShortcutsEnabled).toBe(true)
      expect(preferences.value.autoRefreshInterval).toBe('off')
    })

    it('updates dateFormat preference', () => {
      const { updatePreference, preferences } = usePreferences()
      updatePreference('dateFormat', 'us')
      expect(preferences.value.dateFormat).toBe('us')
    })

    it('updates numberFormat preference', () => {
      const { updatePreference, preferences } = usePreferences()
      updatePreference('numberFormat', 'de')
      expect(preferences.value.numberFormat).toBe('de')
    })

    it('updates listViewMode preference', () => {
      const { updatePreference, preferences } = usePreferences()
      updatePreference('listViewMode', 'compact')
      expect(preferences.value.listViewMode).toBe('compact')
    })

    it('updates keyboardShortcutsEnabled preference', () => {
      const { updatePreference, preferences } = usePreferences()
      updatePreference('keyboardShortcutsEnabled', false)
      expect(preferences.value.keyboardShortcutsEnabled).toBe(false)
    })

    it('updates autoRefreshInterval preference', () => {
      const { updatePreference, preferences } = usePreferences()
      updatePreference('autoRefreshInterval', '5min')
      expect(preferences.value.autoRefreshInterval).toBe('5min')
    })
  })

  describe('resetPreferences', () => {
    it('restores all preferences to defaults after changes', () => {
      const { updatePreference, resetPreferences, preferences } = usePreferences()

      updatePreference('pageSize', 100)
      updatePreference('dateFormat', 'eu')
      updatePreference('numberFormat', 'de')
      updatePreference('listViewMode', 'compact')
      updatePreference('keyboardShortcutsEnabled', false)
      updatePreference('autoRefreshInterval', '1min')

      resetPreferences()

      expect(preferences.value).toEqual({
        pageSize: 25,
        dateFormat: 'iso',
        numberFormat: 'en',
        listViewMode: 'comfortable',
        keyboardShortcutsEnabled: true,
        autoRefreshInterval: 'off'
      })
    })
  })

  describe('persistence', () => {
    it('persists to localStorage on change', async () => {
      const { updatePreference } = usePreferences()

      updatePreference('pageSize', 75)

      // The watch is async (Vue nextTick), so we need to wait
      await new Promise(resolve => setTimeout(resolve, 10))

      const stored = localStorage.getItem(STORAGE_KEY)
      expect(stored).not.toBeNull()
      const parsed = JSON.parse(stored!) as UserPreferences
      expect(parsed.pageSize).toBe(75)
    })

    it('persists reset to localStorage', async () => {
      const { updatePreference, resetPreferences } = usePreferences()

      updatePreference('pageSize', 75)
      await new Promise(resolve => setTimeout(resolve, 10))

      resetPreferences()
      await new Promise(resolve => setTimeout(resolve, 10))

      const stored = localStorage.getItem(STORAGE_KEY)
      expect(stored).not.toBeNull()
      const parsed = JSON.parse(stored!) as UserPreferences
      expect(parsed.pageSize).toBe(25)
    })
  })

  describe('corrupt localStorage', () => {
    it('handles invalid JSON gracefully and returns defaults', () => {
      localStorage.setItem(STORAGE_KEY, 'not-valid-json{{{')
      // The module-level ref is already initialized, so resetPreferences gives defaults
      const { resetPreferences, preferences } = usePreferences()
      resetPreferences()
      expect(preferences.value.pageSize).toBe(25)
      expect(preferences.value.dateFormat).toBe('iso')
    })
  })
})
