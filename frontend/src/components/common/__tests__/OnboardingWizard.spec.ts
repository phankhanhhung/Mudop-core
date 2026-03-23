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

import OnboardingWizard from '../OnboardingWizard.vue'
import { useOnboarding } from '@/composables/useOnboarding'

const globalConfig = {
  mocks: {
    $t: (k: string) => k,
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
  },
}

describe('OnboardingWizard', () => {
  beforeEach(() => {
    localStorage.clear()
    const { resetOnboarding } = useOnboarding()
    resetOnboarding()
  })

  it('renders when isFirstRun is true', () => {
    const wrapper = mount(OnboardingWizard, { global: globalConfig })
    // isFirstRun is true after reset; the dialog div should be present
    expect(wrapper.find('[role="dialog"]').exists()).toBe(true)
  })

  it('does not render when onboarding is completed', async () => {
    const { completeOnboarding } = useOnboarding()
    completeOnboarding()
    const wrapper = mount(OnboardingWizard, { global: globalConfig })
    await wrapper.vm.$nextTick()
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
  })

  it('shows welcome title on step 0', () => {
    const wrapper = mount(OnboardingWizard, { global: globalConfig })
    // Step 0 heading uses key onboarding.welcome.title; $t mock returns key literally
    expect(wrapper.text()).toContain('onboarding.welcome.title')
  })

  it('shows Skip button on step 0', () => {
    const wrapper = mount(OnboardingWizard, { global: globalConfig })
    const buttons = wrapper.findAll('button')
    const buttonTexts = buttons.map(b => b.text())
    expect(buttonTexts.some(t => t.includes('onboarding.skip'))).toBe(true)
  })

  it('clicking Skip completes onboarding and hides wizard', async () => {
    const wrapper = mount(OnboardingWizard, { global: globalConfig })
    expect(wrapper.find('[role="dialog"]').exists()).toBe(true)

    // Find the Skip button (contains 'onboarding.skip' text)
    const buttons = wrapper.findAll('button')
    const skipButton = buttons.find(b => b.text().includes('onboarding.skip'))
    expect(skipButton).toBeDefined()

    await skipButton!.trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
  })

  it('clicking Next advances to step 1', async () => {
    const wrapper = mount(OnboardingWizard, { global: globalConfig })

    // Step 0: find Next button (contains 'common.next')
    const buttons = wrapper.findAll('button')
    const nextButton = buttons.find(b => b.text().includes('common.next'))
    expect(nextButton).toBeDefined()

    await nextButton!.trigger('click')
    await wrapper.vm.$nextTick()

    // Step 1 shows tenant title
    expect(wrapper.text()).toContain('onboarding.tenant.title')
  })

  it('clicking Next on step 1 advances to step 2', async () => {
    const wrapper = mount(OnboardingWizard, { global: globalConfig })

    // Advance from step 0 to step 1
    let buttons = wrapper.findAll('button')
    let nextButton = buttons.find(b => b.text().includes('common.next'))
    await nextButton!.trigger('click')
    await wrapper.vm.$nextTick()

    // Now on step 1, advance to step 2
    buttons = wrapper.findAll('button')
    nextButton = buttons.find(b => b.text().includes('common.next'))
    expect(nextButton).toBeDefined()
    await nextButton!.trigger('click')
    await wrapper.vm.$nextTick()

    // Step 2 shows entities title
    expect(wrapper.text()).toContain('onboarding.entities.title')
  })

  it('clicking Back on step 1 goes back to step 0', async () => {
    const wrapper = mount(OnboardingWizard, { global: globalConfig })

    // Advance to step 1
    let buttons = wrapper.findAll('button')
    const nextButton = buttons.find(b => b.text().includes('common.next'))
    await nextButton!.trigger('click')
    await wrapper.vm.$nextTick()

    // Confirm we are on step 1
    expect(wrapper.text()).toContain('onboarding.tenant.title')

    // Click Back
    buttons = wrapper.findAll('button')
    const backButton = buttons.find(b => b.text().includes('common.back'))
    expect(backButton).toBeDefined()
    await backButton!.trigger('click')
    await wrapper.vm.$nextTick()

    // Back on step 0
    expect(wrapper.text()).toContain('onboarding.welcome.title')
  })

  it('shows Get Started button on step 3', async () => {
    const wrapper = mount(OnboardingWizard, { global: globalConfig })

    // Advance through steps 0 → 1 → 2 → 3
    for (let i = 0; i < 3; i++) {
      const buttons = wrapper.findAll('button')
      const nextButton = buttons.find(b => b.text().includes('common.next'))
      await nextButton!.trigger('click')
      await wrapper.vm.$nextTick()
    }

    const buttons = wrapper.findAll('button')
    const getStartedButton = buttons.find(b => b.text().includes('onboarding.complete.action'))
    expect(getStartedButton).toBeDefined()
  })

  it('clicking Get Started completes onboarding', async () => {
    const wrapper = mount(OnboardingWizard, { global: globalConfig })

    // Advance to step 3
    for (let i = 0; i < 3; i++) {
      const buttons = wrapper.findAll('button')
      const nextButton = buttons.find(b => b.text().includes('common.next'))
      await nextButton!.trigger('click')
      await wrapper.vm.$nextTick()
    }

    // Click Get Started
    const buttons = wrapper.findAll('button')
    const getStartedButton = buttons.find(b => b.text().includes('onboarding.complete.action'))
    expect(getStartedButton).toBeDefined()
    await getStartedButton!.trigger('click')
    await wrapper.vm.$nextTick()

    // Wizard should no longer be visible
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
  })

  it('progress dots: active dot corresponds to current step', async () => {
    const wrapper = mount(OnboardingWizard, { global: globalConfig })

    // Step 0: first dot should be active (bg-primary), others inactive
    let dots = wrapper.findAll('.h-2.w-2')
    expect(dots).toHaveLength(4)
    expect(dots[0].classes()).toContain('bg-primary')
    expect(dots[1].classes()).not.toContain('bg-primary')

    // Advance to step 1
    const buttons = wrapper.findAll('button')
    const nextButton = buttons.find(b => b.text().includes('common.next'))
    await nextButton!.trigger('click')
    await wrapper.vm.$nextTick()

    dots = wrapper.findAll('.h-2.w-2')
    expect(dots[0].classes()).not.toContain('bg-primary')
    expect(dots[1].classes()).toContain('bg-primary')
  })

  it('dialog wrapper has role=dialog and aria-modal=true', () => {
    const wrapper = mount(OnboardingWizard, { global: globalConfig })
    const dialog = wrapper.find('[role="dialog"]')
    expect(dialog.exists()).toBe(true)
    expect(dialog.attributes('aria-modal')).toBe('true')
  })

  it('dialog wrapper has aria-labelledby=onboarding-title', () => {
    const wrapper = mount(OnboardingWizard, { global: globalConfig })
    const dialog = wrapper.find('[role="dialog"]')
    expect(dialog.attributes('aria-labelledby')).toBe('onboarding-title')
  })

  it('step heading has id=onboarding-title', () => {
    const wrapper = mount(OnboardingWizard, { global: globalConfig })
    const heading = wrapper.find('#onboarding-title')
    expect(heading.exists()).toBe(true)
    expect(heading.element.tagName).toBe('H2')
  })
})
