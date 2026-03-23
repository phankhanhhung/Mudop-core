import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import DashboardConfigurator from '../DashboardConfigurator.vue'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (k: string) => k }),
}))

const globalMocks = { $t: (k: string) => k }

describe('DashboardConfigurator', () => {
  it('shows Customize button in view mode', () => {
    const wrapper = mount(DashboardConfigurator, {
      props: { editMode: false, isDirty: false, isSaving: false, columns: 3 },
      global: { mocks: globalMocks },
    })
    expect(wrapper.text()).toContain('dashboard.builder.customize')
    expect(wrapper.text()).not.toContain('dashboard.builder.editingMode')
  })

  it('shows editing toolbar in edit mode', () => {
    const wrapper = mount(DashboardConfigurator, {
      props: { editMode: true, isDirty: false, isSaving: false, columns: 3 },
      global: { mocks: globalMocks },
    })
    expect(wrapper.text()).toContain('dashboard.builder.editingMode')
    expect(wrapper.text()).toContain('dashboard.builder.addWidget')
    expect(wrapper.text()).toContain('dashboard.builder.done')
  })

  it('emits update:editMode true when Customize button clicked', async () => {
    const wrapper = mount(DashboardConfigurator, {
      props: { editMode: false, isDirty: false, isSaving: false, columns: 3 },
      global: { mocks: globalMocks },
    })
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('update:editMode')).toBeTruthy()
    expect(wrapper.emitted('update:editMode')![0]).toEqual([true])
  })

  it('emits add-widget event when Add Widget button clicked in edit mode', async () => {
    const wrapper = mount(DashboardConfigurator, {
      props: { editMode: true, isDirty: false, isSaving: false, columns: 3 },
      global: { mocks: globalMocks },
    })
    const addBtn = wrapper.findAll('button').find((b) => b.text().includes('dashboard.builder.addWidget'))
    await addBtn!.trigger('click')
    expect(wrapper.emitted('add-widget')).toBeTruthy()
  })

  it('emits set-columns event when column button clicked', async () => {
    const wrapper = mount(DashboardConfigurator, {
      props: { editMode: true, isDirty: false, isSaving: false, columns: 3 },
      global: { mocks: globalMocks },
    })
    const colButtons = wrapper.findAll('button[type="button"]')
    const btn4 = colButtons.find((b) => b.text() === '4')
    await btn4!.trigger('click')
    expect(wrapper.emitted('set-columns')).toBeTruthy()
    expect(wrapper.emitted('set-columns')![0]).toEqual([4])
  })
})
