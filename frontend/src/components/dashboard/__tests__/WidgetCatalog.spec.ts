import { describe, it, expect, vi, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import WidgetCatalog from '../WidgetCatalog.vue'
import { WIDGET_CATALOG } from '@/types/dashboard'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (k: string) => k }),
}))

const globalMocks = { $t: (k: string) => k }

let wrapper: ReturnType<typeof mount>

afterEach(() => {
  wrapper?.unmount()
})

describe('WidgetCatalog', () => {
  it('does not render dialog content when closed', () => {
    wrapper = mount(WidgetCatalog, {
      props: { open: false },
      global: { mocks: globalMocks },
      attachTo: document.body,
    })
    expect(document.body.querySelector('[role="dialog"]')).toBeNull()
  })

  it('renders dialog content when open', () => {
    wrapper = mount(WidgetCatalog, {
      props: { open: true },
      global: { mocks: globalMocks },
      attachTo: document.body,
    })
    expect(document.body.querySelector('[role="dialog"]')).not.toBeNull()
  })

  it('renders all 6 widget catalog entries when open', () => {
    wrapper = mount(WidgetCatalog, {
      props: { open: true },
      global: { mocks: globalMocks },
      attachTo: document.body,
    })
    const allButtons = Array.from(document.body.querySelectorAll('button'))
    const addButtons = allButtons.filter((b) => b.textContent?.trim() === 'common.add')
    expect(addButtons).toHaveLength(WIDGET_CATALOG.length)
  })

  it('emits add event with widget type when Add button clicked', async () => {
    wrapper = mount(WidgetCatalog, {
      props: { open: true },
      global: { mocks: globalMocks },
      attachTo: document.body,
    })
    const allButtons = Array.from(document.body.querySelectorAll('button'))
    const addButtons = allButtons.filter((b) => b.textContent?.trim() === 'common.add')
    addButtons[0].click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('add')).toBeTruthy()
    expect(wrapper.emitted('add')![0][0]).toBe(WIDGET_CATALOG[0].type)
  })

  it('emits close event when close button clicked', async () => {
    wrapper = mount(WidgetCatalog, {
      props: { open: true },
      global: { mocks: globalMocks },
      attachTo: document.body,
    })
    const closeBtn = document.body.querySelector('button[aria-label="common.close"]') as HTMLButtonElement
    expect(closeBtn).not.toBeNull()
    closeBtn.click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('close')).toBeTruthy()
  })
})
