import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'

// vi.hoisted ensures these variables are initialized before vi.mock factories run
const { mockGetDetailSections, mockGetCustomViews, mockGetColumnRenderer } = vi.hoisted(() => ({
  mockGetDetailSections: vi.fn().mockReturnValue([]),
  mockGetCustomViews: vi.fn().mockReturnValue([]),
  mockGetColumnRenderer: vi.fn().mockReturnValue(null),
}))

vi.mock('@/lib/plugins', () => ({
  pluginRegistry: {
    getDetailSections: mockGetDetailSections,
    getCustomViews: mockGetCustomViews,
    getColumnRenderer: mockGetColumnRenderer,
  },
}))

import PluginSlot from '../PluginSlot.vue'

// ── Shared mock objects ────────────────────────────────────────────────────────

const MockSectionComponent = { name: 'MockSection', template: '<div class="mock-section" />' }
const MockViewComponent = { name: 'MockView', template: '<div class="mock-view" />' }
const MockCellComponent = { name: 'MockCell', template: '<span class="mock-cell" />' }

const makeMockSection = (overrides: Partial<{
  id: string; label: string; entityType: string; pluginId: string; order: number
}> = {}) => ({
  id: 'sec1',
  label: 'Test Section',
  entityType: 'Customer',
  component: MockSectionComponent,
  pluginId: 'my-plugin',
  position: 'after' as const,
  order: 1,
  ...overrides,
})

const makeMockView = (overrides: Partial<{
  id: string; label: string; entityType: string; pluginId: string
}> = {}) => ({
  id: 'view1',
  label: 'Test View',
  entityType: 'Customer',
  component: MockViewComponent,
  pluginId: 'my-plugin',
  ...overrides,
})

describe('PluginSlot', () => {
  beforeEach(() => {
    mockGetDetailSections.mockReturnValue([])
    mockGetCustomViews.mockReturnValue([])
    mockGetColumnRenderer.mockReturnValue(null)
  })

  // ── detail-sections ──────────────────────────────────────────────────────────

  describe("slotType='detail-sections'", () => {
    it('renders nothing when no sections are registered', () => {
      mockGetDetailSections.mockReturnValue([])

      const wrapper = mount(PluginSlot, {
        props: { slotType: 'detail-sections', entityType: 'Customer' },
      })

      expect(wrapper.find('.plugin-detail-section').exists()).toBe(false)
    })

    it('renders a .plugin-detail-section div for each registered section', () => {
      const sections = [
        makeMockSection({ id: 'sec1' }),
        makeMockSection({ id: 'sec2', label: 'Another Section' }),
      ]
      mockGetDetailSections.mockReturnValue(sections)

      const wrapper = mount(PluginSlot, {
        props: { slotType: 'detail-sections', entityType: 'Customer' },
      })

      const divs = wrapper.findAll('.plugin-detail-section')
      expect(divs).toHaveLength(2)
    })

    it('sets data-section-id and data-plugin-id attributes on each section wrapper', () => {
      const section = makeMockSection({ id: 'sec1', pluginId: 'my-plugin' })
      mockGetDetailSections.mockReturnValue([section])

      const wrapper = mount(PluginSlot, {
        props: { slotType: 'detail-sections', entityType: 'Customer' },
      })

      const div = wrapper.find('.plugin-detail-section')
      expect(div.attributes('data-section-id')).toBe('sec1')
      expect(div.attributes('data-plugin-id')).toBe('my-plugin')
    })

    it('passes entityType and label props to each section component', () => {
      const ProbeComponent = {
        name: 'ProbeSection',
        props: ['entityType', 'label'],
        template: '<div class="probe" :data-entity-type="entityType" :data-label="label" />',
      }
      const section = { ...makeMockSection({ label: 'My Label' }), component: ProbeComponent }
      mockGetDetailSections.mockReturnValue([section])

      const wrapper = mount(PluginSlot, {
        props: { slotType: 'detail-sections', entityType: 'Customer' },
      })

      const probe = wrapper.find('.probe')
      expect(probe.attributes('data-entity-type')).toBe('Customer')
      expect(probe.attributes('data-label')).toBe('My Label')
    })

    it('forwards action events emitted by section components', async () => {
      const EmittingComponent = {
        name: 'EmittingSection',
        template: '<div />',
        mounted() { (this as any).$emit('action', { type: 'test', payload: 42 }) },
      }
      const section = { ...makeMockSection(), component: EmittingComponent }
      mockGetDetailSections.mockReturnValue([section])

      const wrapper = mount(PluginSlot, {
        props: { slotType: 'detail-sections', entityType: 'Customer' },
      })

      await wrapper.vm.$nextTick()

      const emitted = wrapper.emitted('action')
      expect(emitted).toBeTruthy()
      expect(emitted![0]).toEqual([{ type: 'test', payload: 42 }])
    })
  })

  // ── custom-views ─────────────────────────────────────────────────────────────

  describe("slotType='custom-views'", () => {
    it('renders nothing when no views are registered', () => {
      mockGetCustomViews.mockReturnValue([])

      const wrapper = mount(PluginSlot, {
        props: { slotType: 'custom-views', entityType: 'Customer' },
      })

      expect(wrapper.find('.plugin-custom-view').exists()).toBe(false)
    })

    it('renders a .plugin-custom-view div for each registered view', () => {
      const views = [
        makeMockView({ id: 'v1' }),
        makeMockView({ id: 'v2', label: 'Second View' }),
      ]
      mockGetCustomViews.mockReturnValue(views)

      const wrapper = mount(PluginSlot, {
        props: { slotType: 'custom-views', entityType: 'Customer' },
      })

      const divs = wrapper.findAll('.plugin-custom-view')
      expect(divs).toHaveLength(2)
    })

    it('sets data-view-id and data-plugin-id attributes on each view wrapper', () => {
      const view = makeMockView({ id: 'view1', pluginId: 'my-plugin' })
      mockGetCustomViews.mockReturnValue([view])

      const wrapper = mount(PluginSlot, {
        props: { slotType: 'custom-views', entityType: 'Customer' },
      })

      const div = wrapper.find('.plugin-custom-view')
      expect(div.attributes('data-view-id')).toBe('view1')
      expect(div.attributes('data-plugin-id')).toBe('my-plugin')
    })

    it('forwards action events emitted by view components', async () => {
      const EmittingComponent = {
        name: 'EmittingView',
        template: '<div />',
        mounted() { (this as any).$emit('action', { type: 'navigate', payload: 'detail' }) },
      }
      const view = { ...makeMockView(), component: EmittingComponent }
      mockGetCustomViews.mockReturnValue([view])

      const wrapper = mount(PluginSlot, {
        props: { slotType: 'custom-views', entityType: 'Customer' },
      })

      await wrapper.vm.$nextTick()

      const emitted = wrapper.emitted('action')
      expect(emitted).toBeTruthy()
      expect(emitted![0]).toEqual([{ type: 'navigate', payload: 'detail' }])
    })
  })

  // ── cell ─────────────────────────────────────────────────────────────────────

  describe("slotType='cell'", () => {
    it('renders slot content when no cell renderer is registered', () => {
      mockGetColumnRenderer.mockReturnValue(null)

      const wrapper = mount(PluginSlot, {
        props: { slotType: 'cell', entityType: 'Customer', fieldType: 'String', fieldName: 'name' },
        slots: { default: '<span class="default-cell">raw value</span>' },
      })

      expect(wrapper.find('.default-cell').exists()).toBe(true)
      expect(wrapper.find('.default-cell').text()).toBe('raw value')
    })

    it('renders the cell renderer component when a renderer is registered', () => {
      mockGetColumnRenderer.mockReturnValue({
        pluginId: 'p1',
        fieldType: 'Enum',
        component: MockCellComponent,
        priority: 10,
      })

      const wrapper = mount(PluginSlot, {
        props: { slotType: 'cell', entityType: 'Customer', fieldType: 'Enum', fieldName: 'status' },
        slots: { default: '<span class="default-cell">raw</span>' },
      })

      expect(wrapper.find('.mock-cell').exists()).toBe(true)
    })

    it('passes value, row, fieldType, fieldName, and entityType props to the renderer component', () => {
      const ProbeRenderer = {
        name: 'ProbeRenderer',
        props: ['value', 'row', 'fieldType', 'fieldName', 'entityType'],
        template: `<span
          class="probe-renderer"
          :data-value="value"
          :data-field-type="fieldType"
          :data-field-name="fieldName"
          :data-entity-type="entityType"
        />`,
      }
      mockGetColumnRenderer.mockReturnValue({
        pluginId: 'p1',
        fieldType: 'Decimal',
        component: ProbeRenderer,
        priority: 5,
      })

      const row = { id: '1', amount: 99.5 }

      const wrapper = mount(PluginSlot, {
        props: {
          slotType: 'cell',
          entityType: 'Invoice',
          fieldType: 'Decimal',
          fieldName: 'amount',
          value: 99.5,
          row,
        },
      })

      const probe = wrapper.find('.probe-renderer')
      expect(probe.attributes('data-value')).toBe('99.5')
      expect(probe.attributes('data-field-type')).toBe('Decimal')
      expect(probe.attributes('data-field-name')).toBe('amount')
      expect(probe.attributes('data-entity-type')).toBe('Invoice')
    })

    it('does NOT render slot content when a renderer is present', () => {
      mockGetColumnRenderer.mockReturnValue({
        pluginId: 'p1',
        fieldType: 'Boolean',
        component: MockCellComponent,
        priority: 10,
      })

      const wrapper = mount(PluginSlot, {
        props: { slotType: 'cell', entityType: 'Customer', fieldType: 'Boolean', fieldName: 'active' },
        slots: { default: '<span class="default-cell">raw</span>' },
      })

      expect(wrapper.find('.default-cell').exists()).toBe(false)
      expect(wrapper.find('.mock-cell').exists()).toBe(true)
    })
  })

  // ── General ───────────────────────────────────────────────────────────────────

  describe('general behavior', () => {
    it("does not render custom-views or cell renderer when slotType is 'detail-sections'", () => {
      const section = makeMockSection()
      mockGetDetailSections.mockReturnValue([section])
      mockGetCustomViews.mockReturnValue([makeMockView()])
      mockGetColumnRenderer.mockReturnValue({
        pluginId: 'p1',
        fieldType: 'String',
        component: MockCellComponent,
        priority: 10,
      })

      const wrapper = mount(PluginSlot, {
        props: { slotType: 'detail-sections', entityType: 'Customer', fieldType: 'String' },
      })

      expect(wrapper.find('.plugin-detail-section').exists()).toBe(true)
      expect(wrapper.find('.plugin-custom-view').exists()).toBe(false)
      expect(wrapper.find('.mock-cell').exists()).toBe(false)
    })

    it('spreads context prop onto section components via v-bind', () => {
      const ProbeSection = {
        name: 'ProbeSection',
        props: ['entityType', 'label', 'recordId', 'readOnly'],
        template: `<div
          class="probe-section"
          :data-record-id="recordId"
          :data-read-only="readOnly"
        />`,
      }
      const section = { ...makeMockSection(), component: ProbeSection }
      mockGetDetailSections.mockReturnValue([section])

      const context = { recordId: 'rec-42', readOnly: true }

      const wrapper = mount(PluginSlot, {
        props: {
          slotType: 'detail-sections',
          entityType: 'Customer',
          context,
        },
      })

      const probe = wrapper.find('.probe-section')
      expect(probe.attributes('data-record-id')).toBe('rec-42')
      expect(probe.attributes('data-read-only')).toBe('true')
    })
  })
})
