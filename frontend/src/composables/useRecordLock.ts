import { ref, onUnmounted } from 'vue'
import { useSignalR } from './useSignalR'
import { commentService } from '@/services/commentService'

export interface LockState {
  locked: boolean
  isMe: boolean
  userId?: string
  displayName?: string
  startedAt?: string
}

export function useRecordLock(
  module: string,
  entityType: string,
  entityId: string,
  currentUserId: string
) {
  const lockState = ref<LockState>({ locked: false, isMe: false })
  const { joinRecordGroup, leaveRecordGroup, startEditing, stopEditing,
          onRecordLocked, onRecordUnlocked } = useSignalR()

  const recordKey = `${module}/${entityType}/${entityId}`

  async function initialize() {
    await joinRecordGroup(recordKey)
    try {
      const status = await commentService.getLockStatus(module, entityType, entityId)
      lockState.value = {
        locked: status.locked,
        isMe: status.userId === currentUserId,
        userId: status.userId,
        displayName: status.displayName,
        startedAt: status.startedAt,
      }
    } catch {
      // ignore — server may not have a lock entry yet
    }
  }

  async function claimLock(displayName: string) {
    await startEditing(recordKey, displayName)
    lockState.value = {
      locked: true,
      isMe: true,
      userId: currentUserId,
      displayName,
      startedAt: new Date().toISOString(),
    }
  }

  async function releaseLock() {
    await stopEditing(recordKey)
    lockState.value = { locked: false, isMe: false }
  }

  onRecordLocked((n) => {
    if (n.recordKey === recordKey) {
      lockState.value = {
        locked: true,
        isMe: n.userId === currentUserId,
        userId: n.userId,
        displayName: n.displayName,
        startedAt: n.startedAt,
      }
    }
  })

  onRecordUnlocked((n) => {
    if (n.recordKey === recordKey) {
      lockState.value = { locked: false, isMe: false }
    }
  })

  onUnmounted(async () => {
    if (lockState.value.isMe) await releaseLock()
    await leaveRecordGroup(recordKey)
  })

  return { lockState, initialize, claimLock, releaseLock }
}
