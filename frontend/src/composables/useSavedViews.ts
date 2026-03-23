import { ref, computed, toValue, type Ref, type ComputedRef, type MaybeRefOrGetter } from 'vue'
import type { FilterCondition, SortOption } from '@/types/odata'
import { userPreferenceService } from '@/services/userPreferenceService'
import type { UserPreference } from '@/services/userPreferenceService'

export interface SavedView {
  id: string
  name: string
  isDefault: boolean
  createdAt: number
  filters: FilterCondition[]
  sort: SortOption[]
  pageSize: number
  search: string
  visibleColumns?: string[]
}

export interface UseSavedViewsOptions {
  entityKey: string | MaybeRefOrGetter<string>
}

export interface UseSavedViewsReturn {
  views: Ref<SavedView[]>
  currentViewId: Ref<string | null>
  currentView: ComputedRef<SavedView | null>
  defaultView: ComputedRef<SavedView | null>
  loading: Ref<boolean>
  saveView: (
    name: string,
    state: Omit<SavedView, 'id' | 'name' | 'isDefault' | 'createdAt'>
  ) => Promise<SavedView>
  updateView: (id: string, state: Partial<Omit<SavedView, 'id' | 'createdAt'>>) => Promise<void>
  deleteView: (id: string) => Promise<void>
  renameView: (id: string, name: string) => Promise<void>
  setDefault: (id: string | null) => Promise<void>
  selectView: (id: string | null) => void
  loadViews: () => Promise<void>
}

const CATEGORY = 'entity_list_view'

function prefToView(pref: UserPreference): SavedView {
  const s = pref.settings as Record<string, unknown>
  return {
    id: pref.id,
    name: pref.name,
    isDefault: pref.isDefault,
    createdAt: new Date(pref.createdAt).getTime(),
    filters: (s.filters as FilterCondition[]) ?? [],
    sort: (s.sort as SortOption[]) ?? [],
    pageSize: (s.pageSize as number) ?? 20,
    search: (s.search as string) ?? '',
    visibleColumns: s.visibleColumns as string[] | undefined,
  }
}

export function useSavedViews(options: UseSavedViewsOptions): UseSavedViewsReturn {
  const { entityKey } = options

  const views = ref<SavedView[]>([])
  const currentViewId = ref<string | null>(null)
  const loading = ref(false)

  const currentView = computed<SavedView | null>(() => {
    if (currentViewId.value == null) return null
    return views.value.find((v) => v.id === currentViewId.value) ?? null
  })

  const defaultView = computed<SavedView | null>(() => {
    return views.value.find((v) => v.isDefault) ?? null
  })

  async function loadViews(): Promise<void> {
    loading.value = true
    try {
      const prefs = await userPreferenceService.list(CATEGORY, toValue(entityKey))
      views.value = prefs.map(prefToView)
    } catch {
      // Silently fail — views stay empty
      views.value = []
    } finally {
      loading.value = false
    }
  }

  async function saveView(
    name: string,
    state: Omit<SavedView, 'id' | 'name' | 'isDefault' | 'createdAt'>
  ): Promise<SavedView> {
    const pref = await userPreferenceService.create({
      category: CATEGORY,
      entityKey: toValue(entityKey),
      name,
      isDefault: false,
      settings: {
        filters: state.filters,
        sort: state.sort,
        pageSize: state.pageSize,
        search: state.search,
        visibleColumns: state.visibleColumns,
      },
    })
    const view = prefToView(pref)
    views.value = [...views.value, view]
    return view
  }

  async function updateView(
    id: string,
    state: Partial<Omit<SavedView, 'id' | 'createdAt'>>
  ): Promise<void> {
    const req: Record<string, unknown> = {}
    if (state.name !== undefined) req.name = state.name
    if (state.isDefault !== undefined) req.isDefault = state.isDefault

    // Build settings only from view-state fields that are provided
    const settingsFields = ['filters', 'sort', 'pageSize', 'search', 'visibleColumns'] as const
    const hasSettingsUpdate = settingsFields.some((k) => (state as Record<string, unknown>)[k] !== undefined)
    if (hasSettingsUpdate) {
      // Merge with existing settings from current view
      const existing = views.value.find((v) => v.id === id)
      const settings: Record<string, unknown> = {
        filters: existing?.filters ?? [],
        sort: existing?.sort ?? [],
        pageSize: existing?.pageSize ?? 20,
        search: existing?.search ?? '',
        visibleColumns: existing?.visibleColumns,
      }
      for (const k of settingsFields) {
        if ((state as Record<string, unknown>)[k] !== undefined) {
          settings[k] = (state as Record<string, unknown>)[k]
        }
      }
      req.settings = settings
    }

    const updated = await userPreferenceService.update(id, req as { name?: string; isDefault?: boolean; settings?: Record<string, unknown> })
    const view = prefToView(updated)
    views.value = views.value.map((v) => (v.id === id ? view : v))
  }

  async function deleteView(id: string): Promise<void> {
    await userPreferenceService.remove(id)
    views.value = views.value.filter((v) => v.id !== id)
    if (currentViewId.value === id) {
      currentViewId.value = null
    }
  }

  async function renameView(id: string, name: string): Promise<void> {
    await updateView(id, { name })
  }

  async function setDefault(id: string | null): Promise<void> {
    if (id) {
      await userPreferenceService.setDefault(id)
    } else {
      // Clear the current default on the server
      const currentDefault = views.value.find((v) => v.isDefault)
      if (currentDefault) {
        await userPreferenceService.update(currentDefault.id, { isDefault: false })
      }
    }
    // Update local state
    views.value = views.value.map((v) => ({
      ...v,
      isDefault: v.id === id,
    }))
  }

  function selectView(id: string | null): void {
    currentViewId.value = id
  }

  return {
    views,
    currentViewId,
    currentView,
    defaultView,
    loading,
    saveView,
    updateView,
    deleteView,
    renameView,
    setDefault,
    selectView,
    loadViews,
  }
}
