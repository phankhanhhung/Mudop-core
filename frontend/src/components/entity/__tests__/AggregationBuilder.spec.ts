import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

// vi.mock MUST be declared before imports (hoisted by Vitest)
vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (k: string) => k }),
}))

vi.mock('lucide-vue-next', () => ({
  Play: { template: '<span data-icon="Play" />' },
  Plus: { template: '<span data-icon="Plus" />' },
  X: { template: '<span data-icon="X" />' },
  Layers: { template: '<span data-icon="Layers" />' },
  Calculator: { template: '<span data-icon="Calculator" />' },
  Filter: { template: '<span data-icon="Filter" />' },
  ChevronDown: { template: '<span data-icon="ChevronDown" />' },
  ChevronUp: { template: '<span data-icon="ChevronUp" />' },
}))

import AggregationBuilder from '../AggregationBuilder.vue'
import type { FieldMetadata } from '@/types/metadata'
import type { AggregationConfig } from '@/types/aggregation'

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeField(
  name: string,
  type: FieldMetadata['type'],
  displayName?: string,
): FieldMetadata {
  return {
    name,
    type,
    displayName,
    isRequired: false,
    isReadOnly: false,
    isComputed: false,
    annotations: {},
  }
}

const defaultFields: FieldMetadata[] = [
  makeField('Name', 'String', 'Name'),
  makeField('Age', 'Integer', 'Age'),
  makeField('Salary', 'Decimal', 'Salary'),
  makeField('IsActive', 'Boolean', 'Is Active'),
  makeField('CreatedAt', 'DateTime', 'Created At'),
]

function mountBuilder(overrides: { fields?: FieldMetadata[]; isLoading?: boolean } = {}) {
  return mount(AggregationBuilder, {
    props: {
      fields: overrides.fields ?? defaultFields,
      ...(overrides.isLoading !== undefined ? { isLoading: overrides.isLoading } : {}),
    },
    global: { mocks: { $t: (k: string) => k } },
  })
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('AggregationBuilder', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  // Test 1
  it('renders without error with field list', () => {
    const wrapper = mountBuilder()
    // Root div with aria-label="Aggregation configuration"
    expect(wrapper.find('[aria-label="Aggregation configuration"]').exists()).toBe(true)
  })

  // Test 2
  it('shows only numeric fields for aggregation target selects', async () => {
    const wrapper = mountBuilder()

    // Add an aggregation with a non-count function (e.g. sum) so the field selector appears
    const addAggregationButton = wrapper
      .findAll('button')
      .find((b) => b.attributes('aria-label') === 'Add aggregation')
    expect(addAggregationButton).toBeTruthy()
    await addAggregationButton!.trigger('click')
    await wrapper.vm.$nextTick()

    // The aggregation item container has aria-label "Aggregation 1: ..."
    const aggItem = wrapper.find('[aria-label^="Aggregation 1"]')
    expect(aggItem.exists()).toBe(true)

    // Change the function to 'sum' so it needs a numeric field
    const funcSelect = aggItem.find('select')
    await funcSelect.setValue('sum')
    await wrapper.vm.$nextTick()

    // After setting to 'sum', the field selector should appear inside the agg item
    const selects = aggItem.findAll('select')
    // The second select is the field selector (first is func, second is field)
    expect(selects.length).toBeGreaterThanOrEqual(2)
    const fieldSelect = selects[1]
    const optionValues = fieldSelect.findAll('option').map((o) => o.element.value)
    // Numeric fields must be present
    expect(optionValues).toContain('Age')
    expect(optionValues).toContain('Salary')
    // Non-numeric fields must not be present (sum/avg only allow numericFields)
    expect(optionValues).not.toContain('Name')
    expect(optionValues).not.toContain('IsActive')
  })

  // Test 3
  it('adds group by field when add button clicked', async () => {
    const wrapper = mountBuilder()

    // Select a field in the group-by select
    const groupBySelect = wrapper.find('select[aria-label="Select group by field"]')
    await groupBySelect.setValue('Name')
    await wrapper.vm.$nextTick()

    // Click Add Field button
    const addFieldButton = wrapper
      .findAll('button')
      .find((b) => b.attributes('aria-label') === 'Add group by field')
    expect(addFieldButton).toBeTruthy()
    await addFieldButton!.trigger('click')
    await wrapper.vm.$nextTick()

    // The badge for the field should now appear
    expect(wrapper.text()).toContain('Name')
    // The remove button for this field should be present
    const removeButton = wrapper
      .findAll('button')
      .find((b) => b.attributes('aria-label')?.includes('Remove Name from group by'))
    expect(removeButton).toBeTruthy()
  })

  // Test 4
  it('removes group by field when remove button clicked', async () => {
    const wrapper = mountBuilder()

    // Add a field first
    const groupBySelect = wrapper.find('select[aria-label="Select group by field"]')
    await groupBySelect.setValue('Age')
    await wrapper.vm.$nextTick()

    const addFieldButton = wrapper
      .findAll('button')
      .find((b) => b.attributes('aria-label') === 'Add group by field')
    await addFieldButton!.trigger('click')
    await wrapper.vm.$nextTick()

    // The field label should appear as a badge
    const removeButton = wrapper
      .findAll('button')
      .find((b) => b.attributes('aria-label')?.includes('Remove Age from group by'))
    expect(removeButton).toBeTruthy()

    // Remove it
    await removeButton!.trigger('click')
    await wrapper.vm.$nextTick()

    // Remove button should be gone
    const removedButton = wrapper
      .findAll('button')
      .find((b) => b.attributes('aria-label')?.includes('Remove Age from group by'))
    expect(removedButton).toBeUndefined()
  })

  // Test 5
  it('prevents duplicate group by field additions', async () => {
    const wrapper = mountBuilder()
    const vm = wrapper.vm as unknown as { groupByFields: string[] }

    const groupBySelect = wrapper.find('select[aria-label="Select group by field"]')
    const addFieldButton = wrapper
      .findAll('button')
      .find((b) => b.attributes('aria-label') === 'Add group by field')

    // Add 'Name' once
    await groupBySelect.setValue('Name')
    await wrapper.vm.$nextTick()
    await addFieldButton!.trigger('click')
    await wrapper.vm.$nextTick()

    expect(vm.groupByFields).toEqual(['Name'])
    expect(vm.groupByFields.filter((f) => f === 'Name').length).toBe(1)

    // Attempt to add 'Name' again (should not be possible as it's excluded from available list)
    // The available select options no longer include 'Name'
    const options = groupBySelect.findAll('option')
    const nameOption = options.find((o) => o.element.value === 'Name')
    expect(nameOption).toBeUndefined()
  })

  // Test 6
  it('adds aggregation item when plus button clicked', async () => {
    const wrapper = mountBuilder()
    const vm = wrapper.vm as unknown as { aggregations: unknown[] }

    expect(vm.aggregations.length).toBe(0)

    const addAggButton = wrapper
      .findAll('button')
      .find((b) => b.attributes('aria-label') === 'Add aggregation')
    await addAggButton!.trigger('click')
    await wrapper.vm.$nextTick()

    expect(vm.aggregations.length).toBe(1)
  })

  // Test 7
  it('removes aggregation item when remove button clicked', async () => {
    const wrapper = mountBuilder()
    const vm = wrapper.vm as unknown as { aggregations: { id: string }[] }

    // Add an aggregation
    const addAggButton = wrapper
      .findAll('button')
      .find((b) => b.attributes('aria-label') === 'Add aggregation')
    await addAggButton!.trigger('click')
    await wrapper.vm.$nextTick()

    expect(vm.aggregations.length).toBe(1)

    // Click the remove button for aggregation 1
    const removeAggButton = wrapper
      .findAll('button')
      .find((b) => b.attributes('aria-label') === 'Remove aggregation 1')
    expect(removeAggButton).toBeTruthy()
    await removeAggButton!.trigger('click')
    await wrapper.vm.$nextTick()

    expect(vm.aggregations.length).toBe(0)
  })

  // Test 8
  it('emits execute with aggregation config when run clicked', async () => {
    const wrapper = mountBuilder()
    const vm = wrapper.vm as unknown as {
      groupByFields: string[]
      aggregations: { id: string; func: string; field: string; alias: string }[]
    }

    // Add a group-by field
    const groupBySelect = wrapper.find('select[aria-label="Select group by field"]')
    await groupBySelect.setValue('Name')
    const addFieldButton = wrapper
      .findAll('button')
      .find((b) => b.attributes('aria-label') === 'Add group by field')
    await addFieldButton!.trigger('click')
    await wrapper.vm.$nextTick()

    // Add a count aggregation (count doesn't need a field)
    const addAggButton = wrapper
      .findAll('button')
      .find((b) => b.attributes('aria-label') === 'Add aggregation')
    await addAggButton!.trigger('click')
    await wrapper.vm.$nextTick()

    // The alias default is "Value1" — it is non-empty, so canExecute should be true for count
    // Execute button should now be enabled
    const executeButton = wrapper
      .findAll('button')
      .find((b) => !b.attributes('disabled') && b.text().includes('analytics.runAggregation'))
    expect(executeButton).toBeTruthy()
    await executeButton!.trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('execute')).toBeTruthy()
  })

  // Test 9
  it('execute config includes groupBy fields and aggregations', async () => {
    const wrapper = mountBuilder()

    // Set up group-by
    const groupBySelect = wrapper.find('select[aria-label="Select group by field"]')
    await groupBySelect.setValue('Name')
    const addFieldButton = wrapper
      .findAll('button')
      .find((b) => b.attributes('aria-label') === 'Add group by field')
    await addFieldButton!.trigger('click')
    await wrapper.vm.$nextTick()

    // Add a count aggregation
    const addAggButton = wrapper
      .findAll('button')
      .find((b) => b.attributes('aria-label') === 'Add aggregation')
    await addAggButton!.trigger('click')
    await wrapper.vm.$nextTick()

    // Trigger execute via vm directly since button may be enabled
    const vm = wrapper.vm as unknown as { handleExecute: () => void; canExecute: boolean }
    expect(vm.canExecute).toBe(true)
    vm.handleExecute()
    await wrapper.vm.$nextTick()

    const emitted = wrapper.emitted('execute')
    expect(emitted).toBeTruthy()
    const config = emitted![0][0] as AggregationConfig
    expect(config.groupByFields).toContain('Name')
    expect(config.aggregations).toHaveLength(1)
    expect(config.aggregations[0].func).toBe('count')
  })

  // Test 10
  it('shows pre-filter toggle', () => {
    const wrapper = mountBuilder()
    // The pre-filter section is a button with aria-expanded
    const filterToggle = wrapper.find('button[aria-controls="pre-filter-panel"]')
    expect(filterToggle.exists()).toBe(true)
    // Contains the pre-filter i18n key text
    expect(filterToggle.text()).toContain('analytics.preFilter')
  })

  // Test 11
  it('isLoading=true disables execute button', () => {
    const wrapper = mountBuilder({ isLoading: true })
    // Find all buttons and locate the execute button (it has min-w-[160px] and is disabled)
    const buttons = wrapper.findAll('button')
    const executeButton = buttons.find((b) =>
      b.text().includes('analytics.runAggregation'),
    )
    expect(executeButton).toBeTruthy()
    expect(executeButton!.attributes('disabled')).toBeDefined()
  })
})
