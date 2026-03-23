import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import DashboardWidget from '../DashboardWidget.vue'
import type { WidgetConfig, DashboardData, KpiSettings } from '@/types/dashboard'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (k: string) => k }),
}))

vi.mock('../EntityCountWidget.vue', () => ({ default: { template: '<div data-testid="entity-count-widget" />' } }))
vi.mock('../RecentActivityWidget.vue', () => ({ default: { template: '<div data-testid="recent-activity-widget" />' } }))
vi.mock('../QuickLinksWidget.vue', () => ({ default: { template: '<div data-testid="quick-links-widget" />' } }))
vi.mock('../SystemHealthWidget.vue', () => ({ default: { template: '<div data-testid="system-health-widget" />' } }))
vi.mock('@/components/analytics/KpiCardEnhanced.vue', () => ({ default: { template: '<div data-testid="kpi-card" />' } }))
vi.mock('@/components/analytics/BarChart.vue', () => ({ default: { template: '<div data-testid="bar-chart" />', props: ['data', 'title', 'height', 'maxBars'] } }))

const globalMocks = { $t: (k: string) => k }

const mockData: DashboardData = {
  cacheStats: { entityCount: 5, moduleCount: 2, ruleCount: 0, accessControlCount: 0 },
  entityCounts: [
    { module: 'crm', entity: 'Customer', entityType: 'standard', count: 10 },
    { module: 'crm', entity: 'Order', entityType: 'standard', count: 5 },
  ],
  recentActivity: [],
  isLoading: false,
}

function makeConfig(type: WidgetConfig['type'], settings = {}): WidgetConfig {
  return { id: 'w1', type, span: 1, settings }
}

describe('DashboardWidget', () => {
  it('renders EntityCountWidget for entity-count type', () => {
    const wrapper = mount(DashboardWidget, {
      props: { config: makeConfig('entity-count'), data: mockData, editMode: false },
      global: { mocks: globalMocks },
    })
    expect(wrapper.find('[data-testid="entity-count-widget"]').exists()).toBe(true)
  })

  it('renders RecentActivityWidget for recent-activity type', () => {
    const wrapper = mount(DashboardWidget, {
      props: { config: makeConfig('recent-activity'), data: mockData, editMode: false },
      global: { mocks: globalMocks },
    })
    expect(wrapper.find('[data-testid="recent-activity-widget"]').exists()).toBe(true)
  })

  it('renders QuickLinksWidget for quick-links type', () => {
    const wrapper = mount(DashboardWidget, {
      props: { config: makeConfig('quick-links'), data: mockData, editMode: false },
      global: { mocks: globalMocks },
    })
    expect(wrapper.find('[data-testid="quick-links-widget"]').exists()).toBe(true)
  })

  it('renders SystemHealthWidget for system-health type', () => {
    const wrapper = mount(DashboardWidget, {
      props: { config: makeConfig('system-health'), data: mockData, editMode: false },
      global: { mocks: globalMocks },
    })
    expect(wrapper.find('[data-testid="system-health-widget"]').exists()).toBe(true)
  })

  it('renders KpiCardEnhanced for kpi type', () => {
    const kpiSettings: KpiSettings = { title: 'Test KPI', color: 'primary', valueSource: 'total-records' }
    const wrapper = mount(DashboardWidget, {
      props: { config: makeConfig('kpi', kpiSettings), data: mockData, editMode: false },
      global: { mocks: globalMocks },
    })
    expect(wrapper.find('[data-testid="kpi-card"]').exists()).toBe(true)
  })

  it('renders BarChart for entity-bar-chart type', () => {
    const wrapper = mount(DashboardWidget, {
      props: { config: makeConfig('entity-bar-chart'), data: mockData, editMode: false },
      global: { mocks: globalMocks },
    })
    expect(wrapper.find('[data-testid="bar-chart"]').exists()).toBe(true)
  })

  it('shows drag handle and remove button in edit mode', () => {
    const wrapper = mount(DashboardWidget, {
      props: { config: makeConfig('entity-count'), data: mockData, editMode: true },
      global: { mocks: globalMocks },
    })
    expect(wrapper.find('.widget-drag-handle').exists()).toBe(true)
    expect(wrapper.findAll('button')).toHaveLength(2)
  })

  it('emits remove event when remove button clicked', async () => {
    const wrapper = mount(DashboardWidget, {
      props: { config: makeConfig('entity-count'), data: mockData, editMode: true },
      global: { mocks: globalMocks },
    })
    // Remove button is second button (after drag handle)
    const buttons = wrapper.findAll('button')
    await buttons[1].trigger('click')
    expect(wrapper.emitted('remove')).toBeTruthy()
    expect(wrapper.emitted('remove')![0]).toEqual(['w1'])
  })

  it('does not show drag handle in view mode', () => {
    const wrapper = mount(DashboardWidget, {
      props: { config: makeConfig('entity-count'), data: mockData, editMode: false },
      global: { mocks: globalMocks },
    })
    expect(wrapper.find('.widget-drag-handle').exists()).toBe(false)
  })
})
