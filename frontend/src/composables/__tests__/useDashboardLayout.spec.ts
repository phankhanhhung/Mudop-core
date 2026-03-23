import { describe, it, expect, vi, beforeEach } from 'vitest'
import { nextTick } from 'vue'
import { DEFAULT_DASHBOARD_LAYOUT } from '@/types/dashboard'

vi.mock('@/services/dashboardLayoutService', () => ({
  dashboardLayoutService: {
    getLayout: vi.fn(),
    saveLayout: vi.fn(),
    resetLayout: vi.fn(),
  },
}))

import { useDashboardLayout } from '../useDashboardLayout'
import { dashboardLayoutService } from '@/services/dashboardLayoutService'

const mockSavedLayout = {
  id: 'layout-1',
  settings: { version: 1 as const, columns: 4 as const, widgets: [{ id: 'w1', type: 'entity-count' as const, span: 2 as const, settings: {} }] },
}

describe('useDashboardLayout', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(dashboardLayoutService.getLayout).mockResolvedValue(null)
    vi.mocked(dashboardLayoutService.saveLayout).mockResolvedValue('new-id')
    vi.mocked(dashboardLayoutService.resetLayout).mockResolvedValue(undefined)
  })

  it('loads default layout when server returns null', async () => {
    const { layout, loadLayout } = useDashboardLayout()
    await loadLayout()
    expect(layout.value).not.toBeNull()
    expect(layout.value!.columns).toBe(DEFAULT_DASHBOARD_LAYOUT.columns)
    expect(layout.value!.widgets.length).toBe(DEFAULT_DASHBOARD_LAYOUT.widgets.length)
  })

  it('loads saved layout from server', async () => {
    vi.mocked(dashboardLayoutService.getLayout).mockResolvedValue(mockSavedLayout)
    const { layout, savedLayoutId, loadLayout } = useDashboardLayout()
    await loadLayout()
    expect(layout.value!.columns).toBe(4)
    expect(savedLayoutId.value).toBe('layout-1')
  })

  it('isDirty is false after loadLayout', async () => {
    const { isDirty, loadLayout } = useDashboardLayout()
    await loadLayout()
    expect(isDirty.value).toBe(false)
  })

  it('isLoading toggles during load', async () => {
    let resolveLoad!: (val: null) => void
    vi.mocked(dashboardLayoutService.getLayout).mockReturnValue(new Promise((r) => { resolveLoad = r }))
    const { isLoading, loadLayout } = useDashboardLayout()
    const promise = loadLayout()
    await nextTick()
    expect(isLoading.value).toBe(true)
    resolveLoad(null)
    await promise
    expect(isLoading.value).toBe(false)
  })

  it('sets error on load failure but uses default layout', async () => {
    vi.mocked(dashboardLayoutService.getLayout).mockRejectedValue(new Error('Network error'))
    const { error, layout, loadLayout } = useDashboardLayout()
    await loadLayout()
    expect(error.value).toBe('Network error')
    expect(layout.value).not.toBeNull()
  })

  it('saves layout and clears isDirty', async () => {
    const { layout, isDirty, saveLayout, loadLayout } = useDashboardLayout()
    await loadLayout()
    isDirty.value = true
    await saveLayout()
    expect(isDirty.value).toBe(false)
    expect(dashboardLayoutService.saveLayout).toHaveBeenCalled()
  })

  it('reorderWidgets updates widget order and sets isDirty', async () => {
    vi.mocked(dashboardLayoutService.getLayout).mockResolvedValue(mockSavedLayout)
    const multiWidgetLayout = {
      id: 'l1',
      settings: {
        version: 1 as const, columns: 3 as const,
        widgets: [
          { id: 'w1', type: 'entity-count' as const, span: 1 as const, settings: {} },
          { id: 'w2', type: 'system-health' as const, span: 1 as const, settings: {} },
        ],
      },
    }
    vi.mocked(dashboardLayoutService.getLayout).mockResolvedValue(multiWidgetLayout)
    const { layout, isDirty, reorderWidgets, loadLayout } = useDashboardLayout()
    await loadLayout()
    reorderWidgets(0, 1)
    expect(layout.value!.widgets[0].id).toBe('w2')
    expect(layout.value!.widgets[1].id).toBe('w1')
    expect(isDirty.value).toBe(true)
  })

  it('removeWidget removes the correct widget and sets isDirty', async () => {
    vi.mocked(dashboardLayoutService.getLayout).mockResolvedValue({
      id: 'l1',
      settings: {
        version: 1 as const, columns: 3 as const,
        widgets: [
          { id: 'w1', type: 'entity-count' as const, span: 1 as const, settings: {} },
          { id: 'w2', type: 'system-health' as const, span: 1 as const, settings: {} },
        ],
      },
    })
    const { layout, isDirty, removeWidget, loadLayout } = useDashboardLayout()
    await loadLayout()
    removeWidget('w1')
    expect(layout.value!.widgets).toHaveLength(1)
    expect(layout.value!.widgets[0].id).toBe('w2')
    expect(isDirty.value).toBe(true)
  })

  it('addWidget appends a widget and sets isDirty', async () => {
    const { layout, isDirty, addWidget, loadLayout } = useDashboardLayout()
    await loadLayout()
    const initialCount = layout.value!.widgets.length
    addWidget('kpi')
    expect(layout.value!.widgets).toHaveLength(initialCount + 1)
    expect(layout.value!.widgets.at(-1)!.type).toBe('kpi')
    expect(isDirty.value).toBe(true)
  })

  it('setColumns updates columns and sets isDirty', async () => {
    const { layout, isDirty, setColumns, loadLayout } = useDashboardLayout()
    await loadLayout()
    setColumns(4)
    expect(layout.value!.columns).toBe(4)
    expect(isDirty.value).toBe(true)
  })

  it('resetToDefault restores default layout and calls resetLayout if saved', async () => {
    vi.mocked(dashboardLayoutService.getLayout).mockResolvedValue(mockSavedLayout)
    const { layout, savedLayoutId, resetToDefault, loadLayout } = useDashboardLayout()
    await loadLayout()
    await resetToDefault()
    expect(dashboardLayoutService.resetLayout).toHaveBeenCalledWith('layout-1')
    expect(savedLayoutId.value).toBeUndefined()
    expect(layout.value!.columns).toBe(DEFAULT_DASHBOARD_LAYOUT.columns)
  })

  it('setWidgetSpan updates the span of a specific widget', async () => {
    vi.mocked(dashboardLayoutService.getLayout).mockResolvedValue({
      id: 'l1',
      settings: {
        version: 1 as const, columns: 3 as const,
        widgets: [{ id: 'w1', type: 'entity-count' as const, span: 1 as const, settings: {} }],
      },
    })
    const { layout, setWidgetSpan, loadLayout } = useDashboardLayout()
    await loadLayout()
    setWidgetSpan('w1', 3)
    expect(layout.value!.widgets[0].span).toBe(3)
  })
})
