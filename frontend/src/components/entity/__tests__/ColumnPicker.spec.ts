import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

// vi.mock MUST be declared before imports (hoisted by Vitest)
vi.mock('radix-vue', () => ({
  PopoverRoot: { template: '<div><slot /></div>' },
  PopoverTrigger: { template: '<div><slot /></div>' },
  PopoverContent: { template: '<div><slot /></div>' },
  PopoverPortal: { template: '<div><slot /></div>' },
}))

vi.mock('lucide-vue-next', () => ({
  Columns3: { template: '<span data-icon="Columns3" />' },
  // Check is used by Checkbox.vue which is rendered inside ColumnPicker
  Check: { template: '<span data-icon="Check" />' },
}))

import ColumnPicker from '../ColumnPicker.vue'
import type { ColumnConfig } from '@/composables/useColumnConfig'

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeColumns(specs: { field: string; visible: boolean }[]): ColumnConfig[] {
  return specs.map((s, i) => ({
    field: s.field,
    visible: s.visible,
    width: 150,
    order: i,
  }))
}

const defaultColumns = makeColumns([
  { field: 'Name', visible: true },
  { field: 'Email', visible: true },
  { field: 'Status', visible: false },
])

function mountPicker(
  columns: ColumnConfig[] = defaultColumns,
  totalFields = 5,
) {
  return mount(ColumnPicker, {
    props: { columns, totalFields },
    global: { mocks: { $t: (k: string) => k } },
  })
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('ColumnPicker', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  // Test 1
  it('renders columns trigger button with Columns text', () => {
    const wrapper = mountPicker()
    expect(wrapper.text()).toContain('Columns')
    expect(wrapper.find('button').exists()).toBe(true)
  })

  // Test 2
  it('shows badge with visible/total count', () => {
    // 2 of defaultColumns are visible; totalFields = 5
    const wrapper = mountPicker()
    expect(wrapper.text()).toContain('2/5')
  })

  // Test 3
  it('computes visibleCount correctly', () => {
    const columns = makeColumns([
      { field: 'A', visible: true },
      { field: 'B', visible: false },
      { field: 'C', visible: true },
      { field: 'D', visible: true },
    ])
    const wrapper = mountPicker(columns, 4)
    // 3 visible out of 4 total
    expect(wrapper.text()).toContain('3/4')
  })

  // Test 4
  it('emits toggle with field name when column clicked', async () => {
    const wrapper = mountPicker()
    // Column buttons are rendered with role="checkbox"
    const columnButtons = wrapper.findAll('[role="checkbox"]')
    expect(columnButtons.length).toBeGreaterThan(0)
    // Click the first column (Name)
    await columnButtons[0].trigger('click')
    expect(wrapper.emitted('toggle')).toBeTruthy()
    expect(wrapper.emitted('toggle')![0]).toEqual(['Name'])
  })

  // Test 5
  it('emits show-all when show all button clicked', async () => {
    const wrapper = mountPicker()
    const showAllButton = wrapper.findAll('button').find((b) =>
      b.attributes('aria-label') === 'Show all columns',
    )
    expect(showAllButton).toBeTruthy()
    await showAllButton!.trigger('click')
    expect(wrapper.emitted('show-all')).toBeTruthy()
  })

  // Test 6
  it('emits hide-all when hide all button clicked', async () => {
    const wrapper = mountPicker()
    const hideAllButton = wrapper.findAll('button').find((b) =>
      b.attributes('aria-label') === 'Hide all columns',
    )
    expect(hideAllButton).toBeTruthy()
    await hideAllButton!.trigger('click')
    expect(wrapper.emitted('hide-all')).toBeTruthy()
  })

  // Test 7
  it('emits reset when reset button clicked', async () => {
    const wrapper = mountPicker()
    const resetButton = wrapper.findAll('button').find((b) =>
      b.attributes('aria-label') === 'Reset column visibility to defaults',
    )
    expect(resetButton).toBeTruthy()
    await resetButton!.trigger('click')
    expect(wrapper.emitted('reset')).toBeTruthy()
  })

  // Test 8
  it('each column button has role=checkbox', () => {
    const wrapper = mountPicker()
    const checkboxButtons = wrapper.findAll('[role="checkbox"]')
    // defaultColumns has 3 entries — each gets its own role=checkbox button
    // Note: Checkbox component itself also renders role="checkbox", so we filter
    // by the column container buttons specifically via the group
    const group = wrapper.find('[role="group"]')
    const columnCheckboxes = group.findAll('[role="checkbox"]')
    // Each column renders a button with role=checkbox plus a Checkbox child with role=checkbox
    // The outer buttons (one per column) are direct children of the group div
    expect(columnCheckboxes.length).toBeGreaterThanOrEqual(defaultColumns.length)
  })

  // Test 9
  it('aria-checked reflects column visible state', () => {
    const columns = makeColumns([
      { field: 'Name', visible: true },
      { field: 'Hidden', visible: false },
    ])
    const wrapper = mountPicker(columns, 2)
    // The outer column buttons in the group have role="checkbox" and aria-checked.
    // The Checkbox sub-component also renders role="checkbox" but with aria-hidden="true".
    // We look for buttons that are NOT aria-hidden to find the outer column toggle buttons.
    const group = wrapper.find('[role="group"]')
    // Find all role=checkbox buttons that are NOT aria-hidden (i.e., the outer column toggles)
    const allWithRole = group.findAll('button[role="checkbox"]')
    // Outer column toggle buttons do NOT have aria-hidden="true"; Checkbox sub-components do.
    const outerToggles = allWithRole.filter(
      (b) => b.attributes('aria-hidden') !== 'true',
    )
    // Should have exactly 2: Name (visible) and Hidden (not visible)
    expect(outerToggles.length).toBe(2)
    expect(outerToggles[0].attributes('aria-checked')).toBe('true')
    expect(outerToggles[1].attributes('aria-checked')).toBe('false')
  })

  // Test 10
  it('updates badge count when visible columns change', async () => {
    const columns = makeColumns([
      { field: 'Name', visible: true },
      { field: 'Email', visible: true },
      { field: 'Status', visible: false },
    ])
    const wrapper = mountPicker(columns, 3)
    expect(wrapper.text()).toContain('2/3')

    // Update props to simulate a column being toggled visible
    const updatedColumns = makeColumns([
      { field: 'Name', visible: true },
      { field: 'Email', visible: true },
      { field: 'Status', visible: true },
    ])
    await wrapper.setProps({ columns: updatedColumns, totalFields: 3 })
    expect(wrapper.text()).toContain('3/3')
  })
})
