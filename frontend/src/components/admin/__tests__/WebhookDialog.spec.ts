import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { nextTick } from 'vue'
import { setActivePinia, createPinia } from 'pinia'

// ---- Hoisted mocks -------------------------------------------------------
// vi.hoisted ensures these refs are available before the vi.mock factories run.
const { mockCreateWebhook, mockUpdateWebhook } = vi.hoisted(() => ({
  mockCreateWebhook: vi.fn(),
  mockUpdateWebhook: vi.fn(),
}))

vi.mock('@/services/integrationService', () => ({
  integrationService: {
    createWebhook: (...args: unknown[]) => mockCreateWebhook(...args),
    updateWebhook: (...args: unknown[]) => mockUpdateWebhook(...args),
  },
}))

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (k: string) => k }),
}))

// Stub lucide icons to lightweight elements so jsdom doesn't error on SVG
vi.mock('lucide-vue-next', () => ({
  AlertCircle: { template: '<span data-icon="alert-circle" />' },
  Eye: { template: '<span data-icon="eye" />' },
  EyeOff: { template: '<span data-icon="eye-off" />' },
  X: { template: '<span data-icon="x" />' },
  Webhook: { template: '<span data-icon="webhook" />' },
}))

// ---- Component import (must be AFTER vi.mock calls) ----------------------
import WebhookDialog from '../WebhookDialog.vue'
import type { WebhookConfig } from '@/services/integrationService'

// ---- Stubs ---------------------------------------------------------------
// Only stub Radix-Vue dialog primitives — they use Teleport/portal which
// doesn't work in jsdom.  All other shadcn UI components (Input, Button,
// Label, Alert, Spinner) are left to render for real so that attribute
// forwarding (e.g. id="webhook-name") works correctly.
//
// The DialogRoot provides a close() function via Vue's provide/inject so
// that DialogClose stubs can call it when clicked — mirroring what the
// real Radix DialogClose does.

import { defineComponent, provide, inject, h, ref } from 'vue'

const DIALOG_CLOSE_KEY = Symbol('dialogClose')

const radixStubs = {
  // DialogRoot: gate on :open and provide a close callback for children
  DialogRoot: defineComponent({
    props: ['open'],
    emits: ['update:open'],
    setup(props, { slots, emit }) {
      provide(DIALOG_CLOSE_KEY, () => emit('update:open', false))
      return () => (props.open ? h('div', slots.default?.()) : null)
    },
  }),
  DialogPortal: { template: '<div><slot /></div>' },
  DialogOverlay: { template: '<span />' },
  DialogContent: { template: '<div><slot /></div>' },
  DialogTitle: { template: '<h2><slot /></h2>' },
  DialogDescription: { template: '<p><slot /></p>' },
  // DialogClose: inject the close callback from DialogRoot and call it on click.
  // Renders a transparent wrapper so the slotted child is the actual button.
  DialogClose: defineComponent({
    setup(_, { slots }) {
      const close = inject<() => void>(DIALOG_CLOSE_KEY, () => {})
      return () =>
        h(
          'div',
          {
            onClick: (e: MouseEvent) => {
              // Only fire close when clicking the wrapper itself or a button
              // inside it that has no other handler (the cancel and X buttons).
              // Prevent double-fire from the form's submit button.
              const target = e.target as HTMLElement
              if (target.tagName !== 'INPUT') {
                close()
              }
            },
          },
          slots.default?.()
        )
    },
  }),
  // Spinner used inside the save button
  Spinner: { template: '<span data-stub="spinner" />' },
  // Input: a transparent pass-through so id/placeholder/type attrs reach
  // the underlying <input> AND the component exposes a focus() method that
  // the WebhookDialog watch calls via nextTick.
  Input: defineComponent({
    inheritAttrs: true,
    props: {
      modelValue: { default: '' },
      type: { default: 'text' },
      placeholder: { default: '' },
      disabled: { default: false },
    },
    emits: ['update:modelValue'],
    setup(props, { attrs, emit, expose }) {
      const el = ref<HTMLInputElement | null>(null)
      expose({ focus: () => el.value?.focus() })
      return () =>
        h('input', {
          ref: el,
          ...attrs,
          type: props.type,
          value: props.modelValue,
          placeholder: props.placeholder,
          disabled: props.disabled,
          onInput: (e: Event) =>
            emit('update:modelValue', (e.target as HTMLInputElement).value),
        })
    },
  }),
}

const globalConfig = {
  global: {
    mocks: { $t: (k: string) => k },
    stubs: radixStubs,
  },
}

// ---- Helpers -------------------------------------------------------------

function makeWebhook(overrides: Partial<WebhookConfig> = {}): WebhookConfig {
  return {
    id: 'wh-1',
    name: 'My Webhook',
    targetUrl: 'https://example.com/hook',
    hasSecret: false,
    eventFilter: ['Customer.created', 'Order.*'],
    isActive: true,
    createdAt: '2024-01-01T00:00:00Z',
    ...overrides,
  }
}

function mountDialog(props: InstanceType<typeof WebhookDialog>['$props']) {
  return mount(WebhookDialog, {
    props,
    attachTo: document.body,
    global: {
      ...globalConfig.global,
      plugins: [createPinia()],
    },
  })
}

// ---- Tests ---------------------------------------------------------------

describe('WebhookDialog', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  afterEach(() => {
    document.body.innerHTML = ''
  })

  // 1. renders nothing when open=false
  it('renders nothing when open=false', () => {
    const wrapper = mountDialog({ open: false })
    // DialogRoot stub uses v-if="open" — the entire form tree is absent
    expect(wrapper.find('form').exists()).toBe(false)
    expect(wrapper.find('h2').exists()).toBe(false)
  })

  // 2. renders dialog with empty form fields for new webhook
  it('renders dialog with empty form fields for new webhook', async () => {
    const wrapper = mountDialog({ open: true })
    await nextTick()

    expect(wrapper.find('form').exists()).toBe(true)

    // The real Input component forwards id as an inherited attr to its <input>
    const nameInput = wrapper.find('#webhook-name')
    const urlInput = wrapper.find('#webhook-url')
    const secretInput = wrapper.find('#webhook-secret')

    expect(nameInput.exists()).toBe(true)
    expect(urlInput.exists()).toBe(true)
    expect(secretInput.exists()).toBe(true)

    expect((nameInput.element as HTMLInputElement).value).toBe('')
    expect((urlInput.element as HTMLInputElement).value).toBe('')
    expect((secretInput.element as HTMLInputElement).value).toBe('')
  })

  // 3. pre-fills form when webhook prop is provided (edit mode)
  it('renders dialog with pre-filled form fields when webhook prop is provided', async () => {
    const webhook = makeWebhook({
      name: 'Sales Hook',
      targetUrl: 'https://sales.example.com/events',
      eventFilter: ['Order.created'],
    })
    // Mount closed first so the watch fires when we open it
    const wrapper = mountDialog({ open: false, webhook })
    await wrapper.setProps({ open: true })
    await nextTick()

    const nameInput = wrapper.find('#webhook-name')
    const urlInput = wrapper.find('#webhook-url')

    expect((nameInput.element as HTMLInputElement).value).toBe('Sales Hook')
    expect((urlInput.element as HTMLInputElement).value).toBe(
      'https://sales.example.com/events'
    )
  })

  // 4. secret placeholder differs in edit mode
  it('secret field placeholder shows edit key in edit mode', async () => {
    const webhook = makeWebhook()
    const wrapper = mountDialog({ open: true, webhook })
    await nextTick()

    const secretInput = wrapper.find('#webhook-secret')
    expect(secretInput.exists()).toBe(true)
    // In edit mode the component binds: $t('integration.webhook.secretPlaceholderEdit')
    // and our $t mock returns the key as-is
    expect((secretInput.element as HTMLInputElement).placeholder).toBe(
      'integration.webhook.secretPlaceholderEdit'
    )
  })

  // 5. emits close when cancel button is clicked
  it('emits close when cancel button is clicked', async () => {
    const wrapper = mountDialog({ open: true })
    await nextTick()

    // The cancel button is wrapped in <DialogClose as-child>.
    // Our DialogClose stub injects a click handler that calls
    // onOpenChange(false) via provide/inject, which in turn emits 'close'.
    const buttons = wrapper.findAll('button')
    const cancelBtn = buttons.find((b) => b.text().includes('common.cancel'))
    expect(cancelBtn).toBeDefined()

    await cancelBtn!.trigger('click')
    await nextTick()

    expect(wrapper.emitted('close')).toBeTruthy()
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  // 6. shows validation error when submitted without a name
  it('shows validation error if form submitted without name', async () => {
    const wrapper = mountDialog({ open: true })
    await nextTick()

    // Leave name empty, fill url so the URL-missing check doesn't fire first
    const urlInput = wrapper.find('#webhook-url')
    await urlInput.setValue('https://example.com/hook')

    await wrapper.find('form').trigger('submit')
    await nextTick()

    // validationError is shown inside Alert > AlertDescription
    expect(wrapper.text()).toContain('integration.webhook.nameRequired')
  })

  // 7. shows validation error for invalid URL
  it('shows validation error for invalid URL (not http:// or https://)', async () => {
    const wrapper = mountDialog({ open: true })
    await nextTick()

    await wrapper.find('#webhook-name').setValue('My Hook')
    await wrapper.find('#webhook-url').setValue('ftp://bad-protocol.example.com')

    await wrapper.find('form').trigger('submit')
    await nextTick()

    expect(wrapper.text()).toContain('integration.webhook.urlInvalid')
  })

  // 8. calls createWebhook on submit for new webhook, emits saved
  it('calls integrationService.createWebhook on submit for new webhook and emits saved', async () => {
    const createdWebhook = makeWebhook({ id: 'new-1', name: 'New Hook' })
    mockCreateWebhook.mockResolvedValue(createdWebhook)

    const wrapper = mountDialog({ open: true })
    await nextTick()

    await wrapper.find('#webhook-name').setValue('New Hook')
    await wrapper.find('#webhook-url').setValue('https://example.com/hook')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockCreateWebhook).toHaveBeenCalledOnce()
    expect(mockCreateWebhook).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'New Hook',
        targetUrl: 'https://example.com/hook',
      })
    )

    expect(wrapper.emitted('saved')).toBeTruthy()
    expect(wrapper.emitted('saved')![0][0]).toEqual(createdWebhook)
    expect(wrapper.emitted('close')).toBeTruthy()
  })

  // 9. calls updateWebhook on submit for existing webhook, emits saved
  it('calls integrationService.updateWebhook on submit for edit webhook and emits saved', async () => {
    const existing = makeWebhook({ id: 'wh-42', name: 'Old Name' })
    const updated = makeWebhook({ id: 'wh-42', name: 'Updated Name' })
    mockUpdateWebhook.mockResolvedValue(updated)

    // Mount closed first so the watch fires when we open (form gets pre-filled)
    const wrapper = mountDialog({ open: false, webhook: existing })
    await wrapper.setProps({ open: true })
    await nextTick()

    // The form is now pre-filled with "Old Name"; change it
    await wrapper.find('#webhook-name').setValue('Updated Name')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockUpdateWebhook).toHaveBeenCalledOnce()
    expect(mockUpdateWebhook).toHaveBeenCalledWith(
      'wh-42',
      expect.objectContaining({ name: 'Updated Name' })
    )
    expect(mockCreateWebhook).not.toHaveBeenCalled()

    expect(wrapper.emitted('saved')).toBeTruthy()
    expect(wrapper.emitted('saved')![0][0]).toEqual(updated)
  })

  // 10. secret toggle shows/hides secret input
  it('secret toggle button toggles secret input type between password and text', async () => {
    const wrapper = mountDialog({ open: true })
    await nextTick()

    const secretInput = wrapper.find('#webhook-secret')
    expect(secretInput.exists()).toBe(true)
    // Initial state: type="password" (showSecret=false)
    expect((secretInput.element as HTMLInputElement).type).toBe('password')

    // The toggle is a <button type="button" tabindex="-1"> next to the secret input
    const toggleBtn = wrapper.find('button[tabindex="-1"]')
    expect(toggleBtn.exists()).toBe(true)

    await toggleBtn.trigger('click')
    await nextTick()

    expect((secretInput.element as HTMLInputElement).type).toBe('text')

    // Toggle back
    await toggleBtn.trigger('click')
    await nextTick()

    expect((secretInput.element as HTMLInputElement).type).toBe('password')
  })

  // 11. event filter: adding a tag on Enter key
  it('adds event filter tag when Enter is pressed in tag input', async () => {
    const wrapper = mountDialog({ open: true })
    await nextTick()

    // The tag input is a native <input type="text"> without an id (not an Input component)
    const tagInputEl = wrapper.find('input[type="text"]:not([id])')
    expect(tagInputEl.exists()).toBe(true)

    await tagInputEl.setValue('Customer.created')
    await tagInputEl.trigger('keydown', { key: 'Enter', keyCode: 13 })
    await nextTick()

    // A chip with the event name should now appear
    expect(wrapper.text()).toContain('Customer.created')
  })

  // 12. adding duplicate tag is ignored
  it('does not add duplicate event filter tag', async () => {
    const webhook = makeWebhook({ eventFilter: ['Order.created'] })
    const wrapper = mountDialog({ open: true, webhook })
    await nextTick()

    const tagInputEl = wrapper.find('input[type="text"]:not([id])')
    await tagInputEl.setValue('Order.created')
    await tagInputEl.trigger('keydown', { key: 'Enter', keyCode: 13 })
    await nextTick()

    // Count chips: should still have exactly one "Order.created" chip
    const chips = wrapper.findAll('span.rounded-full')
    const orderChips = chips.filter((s) => s.text().includes('Order.created'))
    expect(orderChips).toHaveLength(1)
  })
})
