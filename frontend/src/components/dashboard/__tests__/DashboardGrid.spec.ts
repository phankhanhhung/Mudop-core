import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import DashboardGrid from '../DashboardGrid.vue'
import type { WidgetConfig, DashboardData } from '@/types/dashboard'

vi.mock('sortablejs', () => ({
  default: { create: vi.fn(() => ({ option: vi.fn(), destroy: vi.fn() })) },
}))

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (k: string) => k }),
}))

vi.mock('../DashboardWidget.vue', () => ({
  default: {
    template: '<div class="dashboard-widget" :data-id="config.id" />',
    props: ['config', 'data', 'editMode'],
    emits: ['remove'],
  },
}))

const globalMocks = { $t: (k: string) => k }

const mockData: DashboardData = {
  cacheStats: null,
  entityCounts: [],
  recentActivity: [],
  isLoading: false,
}

const sampleWidgets: WidgetConfig[] = [
  { id: 'w1', type: 'entity-count', span: 2, settings: {} },
  { id: 'w2', type: 'system-health', span: 1, settings: {} },
]

describe('DashboardGrid', () => {
  it('renders one DashboardWidget per widget config', () => {
    const wrapper = mount(DashboardGrid, {
      props: { widgets: sampleWidgets, columns: 3, editMode: false, dashboardData: mockData },
      global: { mocks: globalMocks },
    })
    expect(wrapper.findAll('.dashboard-widget')).toHaveLength(2)
  })

  it('sets gridColumn span style on each widget container', () => {
    const wrapper = mount(DashboardGrid, {
      props: { widgets: sampleWidgets, columns: 3, editMode: false, dashboardData: mockData },
      global: { mocks: globalMocks },
    })
    const containers = wrapper.findAll('[style*="grid-column"]')
    expect(containers[0].attributes('style')).toContain('span 2')
    expect(containers[1].attributes('style')).toContain('span 1')
  })

  it('shows empty state message in edit mode when no widgets', () => {
    const wrapper = mount(DashboardGrid, {
      props: { widgets: [], columns: 3, editMode: true, dashboardData: mockData },
      global: { mocks: globalMocks },
    })
    expect(wrapper.text()).toContain('dashboard.builder.emptyHint')
  })

  it('does not show empty state in view mode when no widgets', () => {
    const wrapper = mount(DashboardGrid, {
      props: { widgets: [], columns: 3, editMode: false, dashboardData: mockData },
      global: { mocks: globalMocks },
    })
    expect(wrapper.text()).not.toContain('emptyHint')
  })

  it('uses 4-column class when columns prop is 4', () => {
    const wrapper = mount(DashboardGrid, {
      props: { widgets: sampleWidgets, columns: 4, editMode: false, dashboardData: mockData },
      global: { mocks: globalMocks },
    })
    const grid = wrapper.find('.grid')
    expect(grid.classes().join(' ')).toContain('lg:grid-cols-4')
  })

  it('uses 3-column class when columns prop is 3', () => {
    const wrapper = mount(DashboardGrid, {
      props: { widgets: sampleWidgets, columns: 3, editMode: false, dashboardData: mockData },
      global: { mocks: globalMocks },
    })
    const grid = wrapper.find('.grid')
    expect(grid.classes().join(' ')).toContain('lg:grid-cols-3')
  })

  it('forwards remove event from widget', async () => {
    const wrapper = mount(DashboardGrid, {
      props: { widgets: sampleWidgets, columns: 3, editMode: true, dashboardData: mockData },
      global: { mocks: globalMocks },
    })
    await wrapper.findComponent({ name: 'DashboardWidget' }).vm.$emit('remove', 'w1')
    expect(wrapper.emitted('remove')).toBeTruthy()
    expect(wrapper.emitted('remove')![0]).toEqual(['w1'])
  })
})
