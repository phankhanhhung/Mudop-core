import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { setActivePinia, createPinia } from 'pinia'

// vi.mock declarations must come before all imports that use them

const mockExecuteAction = vi.fn()
const mockCallFunction = vi.fn()
const mockExecuteUnboundAction = vi.fn()

vi.mock('@/services/odataService', () => ({
  odataService: {
    executeAction: (...args: unknown[]) => mockExecuteAction(...args),
    callFunction: (...args: unknown[]) => mockCallFunction(...args),
    executeUnboundAction: (...args: unknown[]) => mockExecuteUnboundAction(...args)
  }
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  useRoute: () => ({ params: {} })
}))

import ActionDialog from '../ActionDialog.vue'
import type { ActionMetadata, ParameterMetadata } from '@/types/metadata'

// ActionDialog uses Teleport to="body", so all rendered content lands in document.body.
// wrapper.find() only traverses the component's own vnode tree.
// Use document.body.querySelector / querySelectorAll to locate teleported elements.

// Helper to build a minimal ActionMetadata object
const makeAction = (overrides: Partial<ActionMetadata> = {}): ActionMetadata => ({
  name: 'TestAction',
  parameters: [],
  isBound: false,
  ...overrides
})

// Helper to build a ParameterMetadata object
const makeParam = (overrides: Partial<ParameterMetadata> = {}): ParameterMetadata => ({
  name: 'customerId',
  type: 'String',
  isRequired: false,
  ...overrides
})

const globalPlugins = {
  config: {
    globalProperties: {
      $t: (k: string) => k
    }
  }
}

// Click a DOM element and wait for Vue to flush
async function clickEl(el: Element) {
  el.dispatchEvent(new MouseEvent('click', { bubbles: true }))
  await nextTick()
  await nextTick()
}

describe('ActionDialog', () => {
  let wrappers: ReturnType<typeof mount>[] = []

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    wrappers = []
  })

  afterEach(() => {
    for (const w of wrappers) {
      w.unmount()
    }
    // Clean up any leftover DOM nodes written by Teleport
    document.body.innerHTML = ''
  })

  function mountDialog(props: InstanceType<typeof ActionDialog>['$props']) {
    const wrapper = mount(ActionDialog, {
      props,
      attachTo: document.body,
      global: globalPlugins
    })
    wrappers.push(wrapper)
    return wrapper
  }

  // 1. renders nothing (or hidden) when open=false
  it('renders nothing (or hidden) when open=false', () => {
    mountDialog({
      open: false,
      action: makeAction(),
      module: 'crm',
      entitySet: 'Customers',
      mode: 'bound'
    })

    // v-if="open && action" on outer div — nothing teleported into body
    const dialog = document.body.querySelector('[role="dialog"]')
    expect(dialog).toBeNull()
  })

  // 2. renders action title when open=true
  it('renders action title when open=true', async () => {
    const action = makeAction({ name: 'ActivateCustomer' })
    mountDialog({
      open: true,
      action,
      module: 'crm',
      entitySet: 'Customers',
      entityId: '123',
      mode: 'bound'
    })

    await nextTick()

    const dialog = document.body.querySelector('[role="dialog"]')
    expect(dialog).not.toBeNull()
    expect(dialog!.textContent).toContain('ActivateCustomer')
  })

  // 3. renders input for each non-binding parameter
  it('renders input for each non-binding parameter', async () => {
    const action = makeAction({
      name: 'ProcessOrder',
      isBound: true,
      bindingParameter: 'order',
      parameters: [
        makeParam({ name: 'order', type: 'String' }),  // binding param — excluded
        makeParam({ name: 'reason', type: 'String' }),
        makeParam({ name: 'priority', type: 'Integer' })
      ]
    })

    mountDialog({
      open: true,
      action,
      module: 'crm',
      entitySet: 'Orders',
      entityId: '42',
      mode: 'bound'
    })

    await nextTick()

    // Only non-binding params render inputs: reason + priority
    const inputs = document.body.querySelectorAll('input')
    expect(inputs.length).toBe(2)

    const ids = Array.from(inputs).map((i) => i.id)
    expect(ids).toContain('param-reason')
    expect(ids).toContain('param-priority')
  })

  // 4. renders checkbox for Boolean parameter
  it('renders checkbox for Boolean parameter', async () => {
    const action = makeAction({
      name: 'SetFlag',
      parameters: [makeParam({ name: 'active', type: 'Boolean' })]
    })

    mountDialog({
      open: true,
      action,
      module: 'crm',
      entitySet: 'Items',
      mode: 'unbound'
    })

    await nextTick()

    const checkbox = document.body.querySelector('input[type="checkbox"]')
    expect(checkbox).not.toBeNull()
  })

  // 5. renders number input for Integer parameter
  it('renders number input for Integer parameter', async () => {
    const action = makeAction({
      name: 'SetQuantity',
      parameters: [makeParam({ name: 'qty', type: 'Integer' })]
    })

    mountDialog({
      open: true,
      action,
      module: 'crm',
      entitySet: 'Items',
      mode: 'unbound'
    })

    await nextTick()

    const numberInput = document.body.querySelector('input[type="number"]')
    expect(numberInput).not.toBeNull()
    expect(numberInput!.id).toBe('param-qty')
  })

  // 6. renders date input for Date parameter
  it('renders date input for Date parameter', async () => {
    const action = makeAction({
      name: 'ScheduleTask',
      parameters: [makeParam({ name: 'dueDate', type: 'Date' })]
    })

    mountDialog({
      open: true,
      action,
      module: 'crm',
      entitySet: 'Tasks',
      mode: 'unbound'
    })

    await nextTick()

    const dateInput = document.body.querySelector('input[type="date"]')
    expect(dateInput).not.toBeNull()
    expect(dateInput!.id).toBe('param-dueDate')
  })

  // 7. execute button triggers action call
  it('execute button triggers action call', async () => {
    mockExecuteAction.mockResolvedValue({ status: 'ok' })

    const action = makeAction({ name: 'Activate', parameters: [] })
    mountDialog({
      open: true,
      action,
      module: 'crm',
      entitySet: 'Customers',
      entityId: 'cust-1',
      mode: 'bound'
    })

    await nextTick()

    // Find Execute button in the teleported body content
    const buttons = Array.from(document.body.querySelectorAll('button'))
    const executeBtn = buttons.find((b) => b.textContent?.includes('Execute'))
    expect(executeBtn).toBeDefined()

    await clickEl(executeBtn!)

    expect(mockExecuteAction).toHaveBeenCalledOnce()
    expect(mockExecuteAction).toHaveBeenCalledWith('crm', 'Customers', 'cust-1', 'Activate', undefined)
  })

  // 8. emits executed with result after successful action call
  it('emits executed with result after successful action call', async () => {
    const actionResult = { message: 'Customer activated' }
    mockExecuteAction.mockResolvedValue(actionResult)

    const action = makeAction({ name: 'Activate', parameters: [] })
    const wrapper = mountDialog({
      open: true,
      action,
      module: 'crm',
      entitySet: 'Customers',
      entityId: 'cust-1',
      mode: 'bound'
    })

    await nextTick()

    const buttons = Array.from(document.body.querySelectorAll('button'))
    const executeBtn = buttons.find((b) => b.textContent?.includes('Execute'))
    await clickEl(executeBtn!)

    const emitted = wrapper.emitted('executed')
    expect(emitted).toBeTruthy()
    expect(emitted![0][0]).toEqual(actionResult)
  })

  // 9. shows error message when action call fails
  it('shows error message when action call fails', async () => {
    mockExecuteAction.mockRejectedValue(new Error('Service unavailable'))

    const action = makeAction({ name: 'Activate', parameters: [] })
    const wrapper = mountDialog({
      open: true,
      action,
      module: 'crm',
      entitySet: 'Customers',
      entityId: 'cust-1',
      mode: 'bound'
    })

    await nextTick()

    const buttons = Array.from(document.body.querySelectorAll('button'))
    const executeBtn = buttons.find((b) => b.textContent?.includes('Execute'))
    await clickEl(executeBtn!)

    // Allow the microtask queue to drain after promise rejection
    await new Promise<void>((r) => setTimeout(r, 0))
    await nextTick()

    // Error paragraph should appear inside the error div
    const errorEl = document.body.querySelector('.text-destructive')
    expect(errorEl).not.toBeNull()
    expect(errorEl!.textContent).toContain('Service unavailable')

    // executed event should NOT be emitted on failure
    expect(wrapper.emitted('executed')).toBeFalsy()
  })

  // 10. close button emits close
  it('close button emits close', async () => {
    const action = makeAction({ name: 'TestAction', parameters: [] })
    const wrapper = mountDialog({
      open: true,
      action,
      module: 'crm',
      entitySet: 'Customers',
      mode: 'bound'
    })

    await nextTick()

    // X button has aria-label="Close"
    const closeBtn = document.body.querySelector('button[aria-label="Close"]')
    expect(closeBtn).not.toBeNull()

    await clickEl(closeBtn!)

    expect(wrapper.emitted('close')).toBeTruthy()
    expect(wrapper.emitted('close')!.length).toBe(1)
  })

  // 11. resets form when dialog reopens (open changes false→true)
  it('resets form when dialog reopens (open changes false→true)', async () => {
    mockExecuteAction.mockResolvedValue({ ok: true })

    const action = makeAction({
      name: 'Activate',
      parameters: [makeParam({ name: 'note', type: 'String' })]
    })

    const wrapper = mountDialog({
      open: true,
      action,
      module: 'crm',
      entitySet: 'Customers',
      entityId: 'cust-1',
      mode: 'bound'
    })

    await nextTick()

    // Execute to populate result state
    const buttons = Array.from(document.body.querySelectorAll('button'))
    const executeBtn = buttons.find((b) => b.textContent?.includes('Execute'))
    await clickEl(executeBtn!)
    await new Promise<void>((r) => setTimeout(r, 0))
    await nextTick()

    // Result <pre> should be present
    const preAfterExec = document.body.querySelector('pre')
    expect(preAfterExec).not.toBeNull()

    // Close dialog (open → false)
    await wrapper.setProps({ open: false })
    await nextTick()

    // Reopen dialog (open → true)
    await wrapper.setProps({ open: true })
    await nextTick()

    // result display should be reset — pre element should be gone
    const preAfterReset = document.body.querySelector('pre')
    expect(preAfterReset).toBeNull()
  })

  // 12. unbound mode calls unbound action endpoint
  it('unbound mode calls unbound action endpoint', async () => {
    mockExecuteUnboundAction.mockResolvedValue({ success: true })

    const action = makeAction({
      name: 'GenerateReport',
      parameters: [makeParam({ name: 'format', type: 'String' })]
    })

    mountDialog({
      open: true,
      action,
      module: 'reporting',
      entitySet: '',
      mode: 'unbound',
      operationType: 'action',
      serviceName: 'ReportingService'
    })

    await nextTick()

    const buttons = Array.from(document.body.querySelectorAll('button'))
    const executeBtn = buttons.find((b) => b.textContent?.includes('Execute'))
    await clickEl(executeBtn!)
    await new Promise<void>((r) => setTimeout(r, 0))
    await nextTick()

    // Should call executeUnboundAction, not executeAction or callFunction
    expect(mockExecuteUnboundAction).toHaveBeenCalledOnce()
    expect(mockExecuteAction).not.toHaveBeenCalled()
    expect(mockCallFunction).not.toHaveBeenCalled()

    // serviceName should be used as first arg
    expect(mockExecuteUnboundAction).toHaveBeenCalledWith(
      'ReportingService',
      'GenerateReport',
      undefined  // no param values filled in
    )
  })
})
