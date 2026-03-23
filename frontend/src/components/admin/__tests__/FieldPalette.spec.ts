import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import FieldPalette from '../FieldPalette.vue'
import type { DesignerField } from '@/types/formLayout'

// Sortable is DOM-based; stub it in the test environment
vi.mock('sortablejs', () => ({
  default: { create: vi.fn(() => ({ destroy: vi.fn() })) },
}))

const mockFields: DesignerField[] = [
  { name: 'Name', displayName: 'Name', type: 'String', isRequired: true, isKey: false, width: 'full' },
  { name: 'Email', displayName: 'Email', type: 'String', isRequired: false, isKey: false, width: 'half' },
  { name: 'Status', displayName: 'Status', type: 'Enum', isRequired: false, isKey: false, width: 'full' },
]

const mountOptions = {
  global: {
    mocks: { $t: (k: string) => k },
    stubs: { GripVertical: { template: '<span />' } },
  },
}

describe('FieldPalette', () => {
  it('renders all provided fields', () => {
    const wrapper = mount(FieldPalette, { props: { fields: mockFields }, ...mountOptions })
    expect(wrapper.findAll('li').length).toBe(3)
  })

  it('shows display name for each field', () => {
    const wrapper = mount(FieldPalette, { props: { fields: mockFields }, ...mountOptions })
    expect(wrapper.text()).toContain('Name')
    expect(wrapper.text()).toContain('Email')
  })

  it('shows field type', () => {
    const wrapper = mount(FieldPalette, { props: { fields: mockFields }, ...mountOptions })
    expect(wrapper.text()).toContain('String')
    expect(wrapper.text()).toContain('Enum')
  })

  it('shows empty state when fields array is empty', () => {
    const wrapper = mount(FieldPalette, { props: { fields: [] }, ...mountOptions })
    expect(wrapper.find('ul').exists()).toBe(true) // ul rendered but empty
    // Empty state div is shown
    expect(wrapper.text()).toContain('admin.formDesigner.paletteEmpty')
  })

  it('emits show-in-section with field name when + button clicked', async () => {
    const wrapper = mount(FieldPalette, { props: { fields: mockFields }, ...mountOptions })
    await wrapper.findAll('button')[0].trigger('click')
    expect(wrapper.emitted('show-in-section')).toBeTruthy()
    expect(wrapper.emitted('show-in-section')![0]).toEqual(['Name'])
  })

  it('sets data-field-name attribute on list items', () => {
    const wrapper = mount(FieldPalette, { props: { fields: mockFields }, ...mountOptions })
    const items = wrapper.findAll('li')
    expect(items[0].attributes('data-field-name')).toBe('Name')
    expect(items[1].attributes('data-field-name')).toBe('Email')
  })
})
