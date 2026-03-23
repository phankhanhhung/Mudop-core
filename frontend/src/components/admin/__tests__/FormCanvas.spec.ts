import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import FormCanvas from '../FormCanvas.vue'
import type { DesignerSection } from '@/types/formLayout'

vi.mock('sortablejs', () => ({
  default: { create: vi.fn(() => ({ destroy: vi.fn() })) },
}))

const mockSections: DesignerSection[] = [
  {
    id: 'general',
    title: 'General',
    collapsed: false,
    fields: [
      { name: 'Name', displayName: 'Name', type: 'String', isRequired: true, isKey: false, width: 'full' },
      { name: 'Email', displayName: 'Email', type: 'String', isRequired: false, isKey: false, width: 'half' },
    ],
  },
  {
    id: 'extra',
    title: 'Extra',
    collapsed: false,
    fields: [],
  },
]

const mountOptions = {
  global: {
    mocks: { $t: (k: string) => k },
    stubs: { GripVertical: { template: '<span />' }, ChevronDown: { template: '<span />' }, ChevronRight: { template: '<span />' }, X: { template: '<span />' }, Plus: { template: '<span />' } },
  },
}

describe('FormCanvas', () => {
  it('renders all sections', () => {
    const wrapper = mount(FormCanvas, { props: { sections: mockSections, columns: 2 }, ...mountOptions })
    expect(wrapper.text()).toContain('General')
    expect(wrapper.text()).toContain('Extra')
  })

  it('renders all fields in their sections', () => {
    const wrapper = mount(FormCanvas, { props: { sections: mockSections, columns: 2 }, ...mountOptions })
    expect(wrapper.text()).toContain('Name')
    expect(wrapper.text()).toContain('Email')
  })

  it('shows empty state hint for sections with no fields', () => {
    const wrapper = mount(FormCanvas, { props: { sections: mockSections, columns: 2 }, ...mountOptions })
    expect(wrapper.text()).toContain('admin.formDesigner.paletteHint')
  })

  it('emits add-section when add button clicked', async () => {
    const wrapper = mount(FormCanvas, { props: { sections: mockSections, columns: 2 }, ...mountOptions })
    const addBtn = wrapper.find('button[aria-label]') // add-section button
    const buttons = wrapper.findAll('button')
    // Find the add section button by text
    const addSectionBtn = buttons.find((b) => b.text().includes('admin.formDesigner.addSection'))
    await addSectionBtn!.trigger('click')
    expect(wrapper.emitted('add-section')).toBeTruthy()
  })

  it('emits set-columns when column selector clicked', async () => {
    const wrapper = mount(FormCanvas, { props: { sections: mockSections, columns: 2 }, ...mountOptions })
    // Find column "3" button
    const colButtons = wrapper.findAll('button').filter((b) => ['1', '2', '3'].includes(b.text().trim()))
    const col3 = colButtons.find((b) => b.text().trim() === '3')
    await col3!.trigger('click')
    expect(wrapper.emitted('set-columns')).toBeTruthy()
    expect(wrapper.emitted('set-columns')![0]).toEqual([3])
  })

  it('highlights the active column button', () => {
    const wrapper = mount(FormCanvas, { props: { sections: mockSections, columns: 2 }, ...mountOptions })
    const colButtons = wrapper.findAll('button').filter((b) => ['1', '2', '3'].includes(b.text().trim()))
    const col2 = colButtons.find((b) => b.text().trim() === '2')
    expect(col2!.classes()).toContain('bg-primary')
  })

  it('emits hide-field when X button clicked on a field', async () => {
    const wrapper = mount(FormCanvas, { props: { sections: mockSections, columns: 2 }, ...mountOptions })
    // X buttons per field (stubbed as <span>) - find via title
    const hideButtons = wrapper.findAll('button[title="admin.formDesigner.hideField"]')
    await hideButtons[0].trigger('click')
    expect(wrapper.emitted('hide-field')).toBeTruthy()
    expect(wrapper.emitted('hide-field')![0]).toEqual(['general', 'Name'])
  })

  it('emits remove-section when section X button clicked', async () => {
    const wrapper = mount(FormCanvas, { props: { sections: mockSections, columns: 2 }, ...mountOptions })
    const removeButtons = wrapper.findAll('button[title="admin.formDesigner.removeSection"]')
    await removeButtons[0].trigger('click')
    expect(wrapper.emitted('remove-section')).toBeTruthy()
    expect(wrapper.emitted('remove-section')![0]).toEqual(['general'])
  })

  it('emits set-field-width when width button clicked', async () => {
    const wrapper = mount(FormCanvas, { props: { sections: mockSections, columns: 2 }, ...mountOptions })
    // Half width button for first field - find by title admin.formDesigner.widthHalf
    const halfBtn = wrapper.find('button[title="admin.formDesigner.widthHalf"]')
    await halfBtn.trigger('click')
    expect(wrapper.emitted('set-field-width')).toBeTruthy()
    expect(wrapper.emitted('set-field-width')![0]).toEqual(['general', 'Name', 'half'])
  })
})
