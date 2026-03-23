import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'

// Mock lucide-vue-next icon
vi.mock('lucide-vue-next', () => ({
  Lock: { template: '<span data-icon="Lock" />' },
}))

import RecordLockBadge from '../RecordLockBadge.vue'
import type { LockState } from '@/composables/useRecordLock'

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function mountBadge(lockState: LockState, currentUserId = 'user-1') {
  return mount(RecordLockBadge, {
    props: { lockState, currentUserId },
  })
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('RecordLockBadge', () => {
  // ── unlocked ──────────────────────────────────────────────────────────

  it('renders nothing when lockState.locked is false', () => {
    const wrapper = mountBadge({ locked: false, isMe: false })
    expect(wrapper.find('span').exists()).toBe(false)
  })

  // ── locked by me ─────────────────────────────────────────────────────

  it('shows amber badge when locked by current user (isMe=true)', () => {
    const wrapper = mountBadge({
      locked: true,
      isMe: true,
      userId: 'user-1',
      displayName: 'Alice',
    })
    const badge = wrapper.find('span')
    expect(badge.exists()).toBe(true)
    expect(badge.classes()).toContain('bg-amber-100')
    expect(badge.classes()).toContain('text-amber-700')
  })

  it('shows "Editing (you)" text when locked by current user', () => {
    const wrapper = mountBadge({
      locked: true,
      isMe: true,
      userId: 'user-1',
      displayName: 'Alice',
    })
    expect(wrapper.text()).toContain('Editing (you)')
  })

  // ── locked by another user ────────────────────────────────────────────

  it('shows red badge when locked by another user (isMe=false)', () => {
    const wrapper = mountBadge({
      locked: true,
      isMe: false,
      userId: 'user-99',
      displayName: 'Bob',
    })
    const badge = wrapper.find('span')
    expect(badge.classes()).toContain('bg-red-100')
    expect(badge.classes()).toContain('text-red-700')
  })

  it('shows "{displayName} is editing" when locked by another user', () => {
    const wrapper = mountBadge({
      locked: true,
      isMe: false,
      userId: 'user-99',
      displayName: 'Charlie',
    })
    expect(wrapper.text()).toContain('Charlie is editing')
  })

  // ── time ago ──────────────────────────────────────────────────────────

  it('renders the Lock icon when locked', () => {
    const wrapper = mountBadge({
      locked: true,
      isMe: true,
      userId: 'user-1',
      displayName: 'Alice',
    })
    expect(wrapper.find('[data-icon="Lock"]').exists()).toBe(true)
  })

  it('does not show time ago suffix when startedAt is absent', () => {
    const wrapper = mountBadge({
      locked: true,
      isMe: true,
      userId: 'user-1',
      displayName: 'Alice',
      // no startedAt
    })
    expect(wrapper.text()).not.toContain('ago')
    expect(wrapper.text()).not.toContain('now')
  })
})
