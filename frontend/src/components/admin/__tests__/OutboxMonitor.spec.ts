import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (k: string) => k }),
}))

// Stub lucide icons to prevent jsdom render issues
vi.mock('lucide-vue-next', () => ({
  RefreshCw: { template: '<span data-icon="refresh-cw" />' },
  Trash2: { template: '<span data-icon="trash2" />' },
  Loader2: { template: '<span data-icon="loader2" />' },
  Inbox: { template: '<span data-icon="inbox" />' },
}))

import OutboxMonitor from '../OutboxMonitor.vue'
import type { OutboxEntry } from '@/services/integrationService'

// ---- Helpers -------------------------------------------------------------

function makeEntry(overrides: Partial<OutboxEntry> = {}): OutboxEntry {
  return {
    id: 'oe-1',
    eventName: 'Customer.created',
    entityName: 'Customer',
    entityId: 'cust-1',
    status: 'delivered',
    retryCount: 0,
    maxRetries: 3,
    createdAt: '2024-01-01T10:00:00Z',
    isIntegration: true,
    ...overrides,
  }
}

const globalConfig = {
  global: {
    mocks: { $t: (k: string) => k },
  },
}

function mountMonitor(props: InstanceType<typeof OutboxMonitor>['$props']) {
  return mount(OutboxMonitor, { props, ...globalConfig })
}

// ---- Tests ---------------------------------------------------------------

describe('OutboxMonitor', () => {
  // 1. renders loading overlay when loading=true
  it('renders loading overlay when loading=true', () => {
    const wrapper = mountMonitor({ entries: [], loading: true })

    // Loading overlay is an absolute div with z-10 containing the spinner
    const overlay = wrapper.find('.absolute.inset-0.z-10')
    expect(overlay.exists()).toBe(true)
    // It should contain the Loader2 icon stub
    expect(overlay.find('[data-icon="loader2"]').exists()).toBe(true)
  })

  // 2. renders empty state when entries=[] and loading=false
  it('renders empty state with Inbox icon when entries=[] and loading=false', () => {
    const wrapper = mountMonitor({ entries: [], loading: false })

    // Loading overlay must NOT be visible
    expect(wrapper.find('.absolute.inset-0.z-10').exists()).toBe(false)

    // Empty-state section has the Inbox icon stub
    expect(wrapper.find('[data-icon="inbox"]').exists()).toBe(true)

    // Empty state text keys
    expect(wrapper.text()).toContain('integration.outbox.empty')
  })

  // 3. renders outbox entries with correct status badge classes
  it('renders outbox entries with correct status badge classes', () => {
    const entries: OutboxEntry[] = [
      makeEntry({ id: 'e1', status: 'pending' }),
      makeEntry({ id: 'e2', status: 'delivered' }),
      makeEntry({ id: 'e3', status: 'dead_letter' }),
    ]
    const wrapper = mountMonitor({ entries, loading: false })

    const badges = wrapper.findAll('span.rounded-full')
    expect(badges).toHaveLength(3)

    const pendingBadge = badges[0]
    expect(pendingBadge.classes()).toContain('bg-yellow-100')
    expect(pendingBadge.classes()).toContain('text-yellow-800')

    const deliveredBadge = badges[1]
    expect(deliveredBadge.classes()).toContain('bg-green-100')
    expect(deliveredBadge.classes()).toContain('text-green-800')

    const deadLetterBadge = badges[2]
    expect(deadLetterBadge.classes()).toContain('bg-red-100')
    expect(deadLetterBadge.classes()).toContain('text-red-800')
  })

  // 4. shows Retry and Dismiss buttons only for dead_letter entries
  it('shows Retry and Dismiss buttons only for dead_letter entries', () => {
    const entries: OutboxEntry[] = [
      makeEntry({ id: 'e1', status: 'pending' }),
      makeEntry({ id: 'e2', status: 'delivered' }),
      makeEntry({ id: 'e3', status: 'dead_letter' }),
    ]
    const wrapper = mountMonitor({ entries, loading: false })

    const rows = wrapper.findAll('tbody tr')
    expect(rows).toHaveLength(3)

    // pending row — no Retry / Dismiss buttons
    const pendingRow = rows[0]
    expect(pendingRow.find('[data-icon="refresh-cw"]').exists()).toBe(false)
    expect(pendingRow.find('[data-icon="trash2"]').exists()).toBe(false)

    // delivered row — no Retry / Dismiss buttons
    const deliveredRow = rows[1]
    expect(deliveredRow.find('[data-icon="refresh-cw"]').exists()).toBe(false)
    expect(deliveredRow.find('[data-icon="trash2"]').exists()).toBe(false)

    // dead_letter row — both Retry and Dismiss buttons present
    const deadRow = rows[2]
    expect(deadRow.find('[data-icon="refresh-cw"]').exists()).toBe(true)
    expect(deadRow.find('[data-icon="trash2"]').exists()).toBe(true)
  })

  // 5. emits retry with id when Retry clicked
  it('emits retry with entry id when Retry button is clicked', async () => {
    const entry = makeEntry({ id: 'dead-42', status: 'dead_letter' })
    const wrapper = mountMonitor({ entries: [entry], loading: false })

    const retryBtn = wrapper.find('button[title="integration.outbox.retry"]')
    expect(retryBtn.exists()).toBe(true)

    await retryBtn.trigger('click')

    expect(wrapper.emitted('retry')).toBeTruthy()
    expect(wrapper.emitted('retry')![0]).toEqual(['dead-42'])
  })

  // 6. emits dismiss with id when Dismiss clicked
  it('emits dismiss with entry id when Dismiss button is clicked', async () => {
    const entry = makeEntry({ id: 'dead-99', status: 'dead_letter' })
    const wrapper = mountMonitor({ entries: [entry], loading: false })

    const dismissBtn = wrapper.find('button[title="integration.outbox.dismiss"]')
    expect(dismissBtn.exists()).toBe(true)

    await dismissBtn.trigger('click')

    expect(wrapper.emitted('dismiss')).toBeTruthy()
    expect(wrapper.emitted('dismiss')![0]).toEqual(['dead-99'])
  })

  // 7. emits refresh when the refresh link in empty state is clicked
  it('emits refresh when refresh link in empty state is clicked', async () => {
    const wrapper = mountMonitor({ entries: [], loading: false })

    // The refresh link is a <button> in the empty state that contains RefreshCw icon
    const refreshBtn = wrapper.find('.text-primary')
    expect(refreshBtn.exists()).toBe(true)

    await refreshBtn.trigger('click')

    expect(wrapper.emitted('refresh')).toBeTruthy()
    expect(wrapper.emitted('refresh')).toHaveLength(1)
  })

  // 8. truncates long error messages with title attribute for tooltip
  it('truncates long error messages and sets title attribute for tooltip', () => {
    const longError =
      'Connection refused: failed to reach endpoint https://example.com/webhook after 3 retries'
    const entry = makeEntry({ id: 'e1', status: 'dead_letter', errorMessage: longError })
    const wrapper = mountMonitor({ entries: [entry], loading: false })

    // Error cell uses a <span> with class="truncate" and :title binding
    const errorSpan = wrapper.find('span.truncate')
    expect(errorSpan.exists()).toBe(true)
    expect(errorSpan.attributes('title')).toBe(longError)
    expect(errorSpan.text()).toBe(longError)
  })

  // 9. loading overlay appears (z-10 absolute) when loading prop switches to true
  it('shows loading overlay when loading prop switches from false to true', async () => {
    const entries = [makeEntry({ id: 'e1', status: 'pending' })]
    const wrapper = mountMonitor({ entries, loading: false })

    // Initially no overlay
    expect(wrapper.find('.absolute.inset-0.z-10').exists()).toBe(false)

    await wrapper.setProps({ loading: true })
    await nextTick()

    // Overlay is now rendered over the table
    const overlay = wrapper.find('.absolute.inset-0.z-10')
    expect(overlay.exists()).toBe(true)
    expect(overlay.find('[data-icon="loader2"]').exists()).toBe(true)
  })
})
