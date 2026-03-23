import { ref, watch } from 'vue'

export type DateFormat = 'iso' | 'us' | 'eu'
export type NumberFormat = 'en' | 'de'
export type ListViewMode = 'compact' | 'comfortable'
export type AutoRefreshInterval = 'off' | '30s' | '1min' | '5min'

export interface UserPreferences {
  pageSize: number
  dateFormat: DateFormat
  numberFormat: NumberFormat
  listViewMode: ListViewMode
  keyboardShortcutsEnabled: boolean
  autoRefreshInterval: AutoRefreshInterval
}

const STORAGE_KEY = 'bmmdl_preferences'

const defaults: UserPreferences = {
  pageSize: 25,
  dateFormat: 'iso',
  numberFormat: 'en',
  listViewMode: 'comfortable',
  keyboardShortcutsEnabled: true,
  autoRefreshInterval: 'off',
}

const VALID_DATE_FORMATS: DateFormat[] = ['iso', 'us', 'eu']
const VALID_NUMBER_FORMATS: NumberFormat[] = ['en', 'de']
const VALID_LIST_VIEW_MODES: ListViewMode[] = ['compact', 'comfortable']
const VALID_AUTO_REFRESH_INTERVALS: AutoRefreshInterval[] = ['off', '30s', '1min', '5min']

function loadPreferences(): UserPreferences {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored) {
      const parsed = JSON.parse(stored)
      if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
        return { ...defaults }
      }

      const validated: UserPreferences = { ...defaults }

      if (typeof parsed.pageSize === 'number' && parsed.pageSize > 0 && parsed.pageSize <= 1000) {
        validated.pageSize = parsed.pageSize
      }
      if (VALID_DATE_FORMATS.includes(parsed.dateFormat)) {
        validated.dateFormat = parsed.dateFormat
      }
      if (VALID_NUMBER_FORMATS.includes(parsed.numberFormat)) {
        validated.numberFormat = parsed.numberFormat
      }
      if (VALID_LIST_VIEW_MODES.includes(parsed.listViewMode)) {
        validated.listViewMode = parsed.listViewMode
      }
      if (typeof parsed.keyboardShortcutsEnabled === 'boolean') {
        validated.keyboardShortcutsEnabled = parsed.keyboardShortcutsEnabled
      }
      if (VALID_AUTO_REFRESH_INTERVALS.includes(parsed.autoRefreshInterval)) {
        validated.autoRefreshInterval = parsed.autoRefreshInterval
      }

      return validated
    }
  } catch {
    // Ignore parse errors, return defaults
  }
  return { ...defaults }
}

function savePreferences(prefs: UserPreferences): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(prefs))
}

const preferences = ref<UserPreferences>(loadPreferences())

// Auto-save on change
watch(preferences, (newVal) => {
  savePreferences(newVal)
}, { deep: true })

export function usePreferences() {
  function updatePreference<K extends keyof UserPreferences>(
    key: K,
    value: UserPreferences[K]
  ): void {
    preferences.value = { ...preferences.value, [key]: value }
  }

  function resetPreferences(): void {
    preferences.value = { ...defaults }
  }

  return {
    preferences,
    updatePreference,
    resetPreferences,
  }
}
