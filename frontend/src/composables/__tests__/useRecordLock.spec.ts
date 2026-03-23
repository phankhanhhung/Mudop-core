import { describe, it, expect, vi, beforeEach } from 'vitest'

// ---------------------------------------------------------------------------
// Mocks — ALL variables referenced inside vi.mock factories must be hoisted
// ---------------------------------------------------------------------------

const {
  mockJoinRecordGroup,
  mockLeaveRecordGroup,
  mockStartEditing,
  mockStopEditing,
  mockGetLockStatus,
  callbacks,
} = vi.hoisted(() => {
  const callbacks: { locked: ((n: Record<string, unknown>) => void) | null; unlocked: ((n: Record<string, unknown>) => void) | null } = {
    locked: null,
    unlocked: null,
  }
  return {
    mockJoinRecordGroup: vi.fn(),
    mockLeaveRecordGroup: vi.fn(),
    mockStartEditing: vi.fn(),
    mockStopEditing: vi.fn(),
    mockGetLockStatus: vi.fn(),
    callbacks,
  }
})

vi.mock('@/composables/useSignalR', () => ({
  useSignalR: () => ({
    joinRecordGroup: mockJoinRecordGroup,
    leaveRecordGroup: mockLeaveRecordGroup,
    startEditing: mockStartEditing,
    stopEditing: mockStopEditing,
    onRecordLocked: (cb: (n: Record<string, unknown>) => void) => {
      callbacks.locked = cb
    },
    onRecordUnlocked: (cb: (n: Record<string, unknown>) => void) => {
      callbacks.unlocked = cb
    },
  }),
}))

vi.mock('@/services/commentService', () => ({
  commentService: {
    getLockStatus: mockGetLockStatus,
  },
}))

// ---------------------------------------------------------------------------
// Import composable AFTER mocks
// ---------------------------------------------------------------------------

import { useRecordLock } from '../useRecordLock'

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('useRecordLock', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    callbacks.locked = null
    callbacks.unlocked = null
    mockJoinRecordGroup.mockResolvedValue(undefined)
    mockLeaveRecordGroup.mockResolvedValue(undefined)
    mockStartEditing.mockResolvedValue(undefined)
    mockStopEditing.mockResolvedValue(undefined)
  })

  // ── initial state ─────────────────────────────────────────────────────

  it('starts with lockState locked=false, isMe=false', () => {
    const { lockState } = useRecordLock('crm', 'Customer', 'cust-1', 'user-1')
    expect(lockState.value.locked).toBe(false)
    expect(lockState.value.isMe).toBe(false)
  })

  // ── initialize() ─────────────────────────────────────────────────────

  it('initialize() calls joinRecordGroup with the recordKey', async () => {
    mockGetLockStatus.mockResolvedValue({ locked: false })
    const { initialize } = useRecordLock('crm', 'Customer', 'cust-1', 'user-1')
    await initialize()
    expect(mockJoinRecordGroup).toHaveBeenCalledWith('crm/Customer/cust-1')
  })

  it('initialize() updates lockState from getLockStatus when locked by another user', async () => {
    mockGetLockStatus.mockResolvedValue({
      locked: true,
      userId: 'user-99',
      displayName: 'Charlie',
      startedAt: '2026-01-01T10:00:00Z',
    })
    const { initialize, lockState } = useRecordLock('crm', 'Customer', 'cust-1', 'user-1')
    await initialize()
    expect(lockState.value.locked).toBe(true)
    expect(lockState.value.isMe).toBe(false)
    expect(lockState.value.displayName).toBe('Charlie')
  })

  it('initialize() sets isMe=true when getLockStatus returns current user', async () => {
    mockGetLockStatus.mockResolvedValue({
      locked: true,
      userId: 'user-1',
      displayName: 'Alice',
      startedAt: '2026-01-01T10:00:00Z',
    })
    const { initialize, lockState } = useRecordLock('crm', 'Customer', 'cust-1', 'user-1')
    await initialize()
    expect(lockState.value.isMe).toBe(true)
  })

  it('initialize() silently ignores getLockStatus errors', async () => {
    mockGetLockStatus.mockRejectedValue(new Error('Network error'))
    const { initialize, lockState } = useRecordLock('crm', 'Customer', 'cust-1', 'user-1')
    await expect(initialize()).resolves.not.toThrow()
    expect(lockState.value.locked).toBe(false)
  })

  // ── claimLock() ───────────────────────────────────────────────────────

  it('claimLock() calls startEditing with recordKey and displayName', async () => {
    const { claimLock } = useRecordLock('crm', 'Customer', 'cust-1', 'user-1')
    await claimLock('Alice')
    expect(mockStartEditing).toHaveBeenCalledWith('crm/Customer/cust-1', 'Alice')
  })

  it('claimLock() updates lockState to locked=true, isMe=true', async () => {
    const { claimLock, lockState } = useRecordLock('crm', 'Customer', 'cust-1', 'user-1')
    await claimLock('Alice')
    expect(lockState.value.locked).toBe(true)
    expect(lockState.value.isMe).toBe(true)
    expect(lockState.value.userId).toBe('user-1')
    expect(lockState.value.displayName).toBe('Alice')
  })

  // ── releaseLock() ─────────────────────────────────────────────────────

  it('releaseLock() calls stopEditing with recordKey', async () => {
    const { claimLock, releaseLock } = useRecordLock('crm', 'Customer', 'cust-1', 'user-1')
    await claimLock('Alice')
    await releaseLock()
    expect(mockStopEditing).toHaveBeenCalledWith('crm/Customer/cust-1')
  })

  it('releaseLock() resets lockState to locked=false, isMe=false', async () => {
    const { claimLock, releaseLock, lockState } = useRecordLock('crm', 'Customer', 'cust-1', 'user-1')
    await claimLock('Alice')
    await releaseLock()
    expect(lockState.value.locked).toBe(false)
    expect(lockState.value.isMe).toBe(false)
  })

  // ── onRecordLocked SignalR handler ────────────────────────────────────

  it('updates lockState when onRecordLocked fires for matching recordKey', () => {
    const { lockState } = useRecordLock('crm', 'Customer', 'cust-1', 'user-1')
    callbacks.locked!({
      recordKey: 'crm/Customer/cust-1',
      userId: 'user-99',
      displayName: 'Bob',
      startedAt: '2026-01-01T10:00:00Z',
    })
    expect(lockState.value.locked).toBe(true)
    expect(lockState.value.isMe).toBe(false)
    expect(lockState.value.displayName).toBe('Bob')
  })

  it('ignores onRecordLocked events for a different recordKey', () => {
    const { lockState } = useRecordLock('crm', 'Customer', 'cust-1', 'user-1')
    callbacks.locked!({
      recordKey: 'crm/Order/order-99',
      userId: 'user-99',
      displayName: 'Bob',
      startedAt: '2026-01-01T10:00:00Z',
    })
    expect(lockState.value.locked).toBe(false)
  })

  // ── onRecordUnlocked SignalR handler ──────────────────────────────────

  it('clears lockState when onRecordUnlocked fires for matching recordKey', async () => {
    const { claimLock, lockState } = useRecordLock('crm', 'Customer', 'cust-1', 'user-1')
    await claimLock('Alice')
    callbacks.unlocked!({ recordKey: 'crm/Customer/cust-1' })
    expect(lockState.value.locked).toBe(false)
    expect(lockState.value.isMe).toBe(false)
  })

  it('ignores onRecordUnlocked events for a different recordKey', async () => {
    const { claimLock, lockState } = useRecordLock('crm', 'Customer', 'cust-1', 'user-1')
    await claimLock('Alice')
    callbacks.unlocked!({ recordKey: 'crm/Order/order-99' })
    expect(lockState.value.locked).toBe(true) // unchanged
  })
})
