import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'

// vi.mock calls must come before imports (Vitest hoists them)
vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  useRoute: () => ({ params: {} }),
}))

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (k: string, params?: Record<string, unknown>) => {
      if (params) return `${k}:${JSON.stringify(params)}`
      return k
    }
  })
}))

vi.mock('lucide-vue-next', () => ({
  Sparkles: { template: '<span data-icon="sparkles" />' },
  Building2: { template: '<span data-icon="building2" />' },
  Database: { template: '<span data-icon="database" />' },
  CheckCircle2: { template: '<span data-icon="check-circle2" />' },
  ArrowRight: { template: '<span data-icon="arrow-right" />' },
  ArrowLeft: { template: '<span data-icon="arrow-left" />' },
  X: { template: '<span data-icon="x" />' },
}))

import WhatsNewModal from '../WhatsNewModal.vue'
import { useOnboarding, CURRENT_VERSION } from '@/composables/useOnboarding'

const globalConfig = {
  mocks: {
    $t: (k: string, params?: Record<string, unknown>) => {
      if (params) return `${k}:${JSON.stringify(params)}`
      return k
    },
  },
  stubs: {
    Teleport: { template: '<div><slot /></div>' },
    Transition: { template: '<div><slot /></div>' },
    Button: {
      template: '<button v-bind="$attrs" @click="$emit(\'click\')"><slot /></button>',
      emits: ['click'],
      inheritAttrs: false,
    },
    Card: { template: '<div><slot /></div>' },
    CardContent: { template: '<div><slot /></div>' },
    // Checkbox stub that supports :checked and @update:checked bindings
    Checkbox: {
      template: '<button type="button" role="checkbox" :aria-checked="checked" data-testid="checkbox" @click="$emit(\'update:checked\', !checked)" />',
      props: ['checked', 'id'],
      emits: ['update:checked'],
    },
    Label: { template: '<label><slot /></label>' },
  },
}

describe('WhatsNewModal', () => {
  beforeEach(() => {
    localStorage.clear()
    const { resetOnboarding } = useOnboarding()
    resetOnboarding()
  })

  it('renders when open=true', () => {
    const wrapper = mount(WhatsNewModal, {
      props: { open: true },
      global: globalConfig,
    })
    expect(wrapper.find('[role="dialog"]').exists()).toBe(true)
  })

  it('does not render when open=false', () => {
    const wrapper = mount(WhatsNewModal, {
      props: { open: false },
      global: globalConfig,
    })
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
  })

  it('shows title with version number', () => {
    const wrapper = mount(WhatsNewModal, {
      props: { open: true },
      global: globalConfig,
    })
    // $t mock returns "whatsNew.title:{"version":"1.0.0"}"
    const titleEl = wrapper.find('#whats-new-title')
    expect(titleEl.exists()).toBe(true)
    expect(titleEl.text()).toContain('whatsNew.title')
    expect(titleEl.text()).toContain(CURRENT_VERSION)
  })

  it('renders 10 feature items', () => {
    const wrapper = mount(WhatsNewModal, {
      props: { open: true },
      global: globalConfig,
    })
    const items = wrapper.findAll('li')
    expect(items).toHaveLength(10)
  })

  it('X button emits close', async () => {
    const wrapper = mount(WhatsNewModal, {
      props: { open: true },
      global: globalConfig,
    })

    // The X button has aria-label="common.cancel" (from $t('common.cancel'))
    const buttons = wrapper.findAll('button')
    // Find button with aria-label containing 'common.cancel'
    const xButton = buttons.find(b => b.attributes('aria-label') === 'common.cancel')
    expect(xButton).toBeDefined()

    await xButton!.trigger('click')
    expect(wrapper.emitted('close')).toBeTruthy()
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('Got it button emits close', async () => {
    const wrapper = mount(WhatsNewModal, {
      props: { open: true },
      global: globalConfig,
    })

    // "Got it" button text is $t('whatsNew.close') → 'whatsNew.close'
    const buttons = wrapper.findAll('button')
    const gotItButton = buttons.find(b => b.text().includes('whatsNew.close'))
    expect(gotItButton).toBeDefined()

    await gotItButton!.trigger('click')
    expect(wrapper.emitted('close')).toBeTruthy()
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('does not call dismissWhatsNew when closed without checkbox checked', async () => {
    const wrapper = mount(WhatsNewModal, {
      props: { open: true },
      global: globalConfig,
    })

    // Close without checking the checkbox
    const buttons = wrapper.findAll('button')
    const gotItButton = buttons.find(b => b.text().includes('whatsNew.close'))
    await gotItButton!.trigger('click')

    // hasNewVersion should still be true (dismissWhatsNew was not called)
    const { hasNewVersion } = useOnboarding()
    expect(hasNewVersion.value).toBe(true)
  })

  it('calls dismissWhatsNew when closed with dontShowAgain checked', async () => {
    const wrapper = mount(WhatsNewModal, {
      props: { open: true },
      global: globalConfig,
    })

    // Find and click the checkbox to check it
    const checkbox = wrapper.find('[data-testid="checkbox"]')
    expect(checkbox.exists()).toBe(true)
    await checkbox.trigger('click')
    await wrapper.vm.$nextTick()

    // Now close via Got it button
    const buttons = wrapper.findAll('button')
    const gotItButton = buttons.find(b => b.text().includes('whatsNew.close'))
    await gotItButton!.trigger('click')

    // dismissWhatsNew should have been called → hasNewVersion is false
    const { hasNewVersion } = useOnboarding()
    expect(hasNewVersion.value).toBe(false)
  })

  it('dialog has role=dialog and aria-modal=true', () => {
    const wrapper = mount(WhatsNewModal, {
      props: { open: true },
      global: globalConfig,
    })
    const dialog = wrapper.find('[role="dialog"]')
    expect(dialog.exists()).toBe(true)
    expect(dialog.attributes('aria-modal')).toBe('true')
  })

  it('dialog has aria-labelledby=whats-new-title', () => {
    const wrapper = mount(WhatsNewModal, {
      props: { open: true },
      global: globalConfig,
    })
    const dialog = wrapper.find('[role="dialog"]')
    expect(dialog.attributes('aria-labelledby')).toBe('whats-new-title')
  })

  it('title heading has id=whats-new-title', () => {
    const wrapper = mount(WhatsNewModal, {
      props: { open: true },
      global: globalConfig,
    })
    const heading = wrapper.find('#whats-new-title')
    expect(heading.exists()).toBe(true)
    expect(heading.element.tagName).toBe('H2')
  })

  it('dontShowAgain checkbox is unchecked by default', () => {
    const wrapper = mount(WhatsNewModal, {
      props: { open: true },
      global: globalConfig,
    })
    const checkbox = wrapper.find('[data-testid="checkbox"]')
    expect(checkbox.exists()).toBe(true)
    expect(checkbox.attributes('aria-checked')).toBe('false')
  })
})
