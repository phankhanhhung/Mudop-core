import { ref } from 'vue'
import type { DashboardLayout, WidgetConfig, WidgetType, WidgetSettings } from '@/types/dashboard'
import { DEFAULT_DASHBOARD_LAYOUT, WIDGET_CATALOG } from '@/types/dashboard'
import { dashboardLayoutService } from '@/services/dashboardLayoutService'

export function useDashboardLayout() {
  const layout = ref<DashboardLayout | null>(null)
  const savedLayoutId = ref<string | undefined>(undefined)
  const isLoading = ref(false)
  const isSaving = ref(false)
  const isDirty = ref(false)
  const error = ref<string | null>(null)

  async function loadLayout(): Promise<void> {
    isLoading.value = true
    error.value = null
    try {
      const saved = await dashboardLayoutService.getLayout()
      if (saved) {
        layout.value = saved.settings
        savedLayoutId.value = saved.id
      } else {
        layout.value = structuredClone(DEFAULT_DASHBOARD_LAYOUT)
        savedLayoutId.value = undefined
      }
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to load layout'
      layout.value = structuredClone(DEFAULT_DASHBOARD_LAYOUT)
    } finally {
      isLoading.value = false
      isDirty.value = false
    }
  }

  async function saveLayout(): Promise<void> {
    if (!layout.value) return
    isSaving.value = true
    error.value = null
    try {
      const id = await dashboardLayoutService.saveLayout(layout.value, savedLayoutId.value)
      savedLayoutId.value = id
      isDirty.value = false
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to save layout'
      throw e
    } finally {
      isSaving.value = false
    }
  }

  async function resetToDefault(): Promise<void> {
    if (savedLayoutId.value) {
      await dashboardLayoutService.resetLayout(savedLayoutId.value)
      savedLayoutId.value = undefined
    }
    layout.value = structuredClone(DEFAULT_DASHBOARD_LAYOUT)
    isDirty.value = false
  }

  function reorderWidgets(from: number, to: number): void {
    if (!layout.value) return
    const widgets = [...layout.value.widgets]
    const [moved] = widgets.splice(from, 1)
    widgets.splice(to, 0, moved)
    layout.value = { ...layout.value, widgets }
    isDirty.value = true
  }

  function removeWidget(widgetId: string): void {
    if (!layout.value) return
    layout.value = {
      ...layout.value,
      widgets: layout.value.widgets.filter((w) => w.id !== widgetId),
    }
    isDirty.value = true
  }

  function addWidget(type: WidgetType): void {
    if (!layout.value) return
    const catalogEntry = WIDGET_CATALOG.find((e) => e.type === type)
    if (!catalogEntry) return
    const newWidget: WidgetConfig = {
      id: `widget-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
      type,
      span: catalogEntry.defaultSpan,
      settings: { ...catalogEntry.defaultSettings } as WidgetSettings,
    }
    layout.value = {
      ...layout.value,
      widgets: [...layout.value.widgets, newWidget],
    }
    isDirty.value = true
  }

  function setColumns(columns: 3 | 4): void {
    if (!layout.value) return
    layout.value = { ...layout.value, columns }
    isDirty.value = true
  }

  function setWidgetSpan(widgetId: string, span: 1 | 2 | 3): void {
    if (!layout.value) return
    layout.value = {
      ...layout.value,
      widgets: layout.value.widgets.map((w) => w.id === widgetId ? { ...w, span } : w),
    }
    isDirty.value = true
  }

  return {
    layout,
    savedLayoutId,
    isLoading,
    isSaving,
    isDirty,
    error,
    loadLayout,
    saveLayout,
    resetToDefault,
    reorderWidgets,
    removeWidget,
    addWidget,
    setColumns,
    setWidgetSpan,
  }
}
