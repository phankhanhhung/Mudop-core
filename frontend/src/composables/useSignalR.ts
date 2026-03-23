import { ref, onUnmounted } from 'vue'
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import type { HubConnection } from '@microsoft/signalr'
import { tokenManager } from '@/services/api'

export interface EntityChangeNotification {
  entityName: string
  entityId: string
  module: string
  userId: string | null
  tenantId: string | null
  timestamp: string
  changedFields: Record<string, unknown> | null
}

export interface RecordLockNotification {
  recordKey: string
  userId: string
  displayName: string
  startedAt: string
}

export interface RecordUnlockNotification {
  recordKey: string
  userId: string
}

export interface CommentNotification {
  recordKey: string
  commentId: string
  authorId: string
  authorName: string
  content: string
  mentions: string[]
  createdAt: string
}

export interface ChangeRequestNotification {
  recordKey: string
  requestId: string
  status: string
  proposedByName: string
}

type EventCallback = (notification: EntityChangeNotification) => void

let connection: HubConnection | null = null
let connectionPromise: Promise<void> | null = null
const isConnected = ref(false)
const listeners = {
  created: new Set<EventCallback>(),
  updated: new Set<EventCallback>(),
  deleted: new Set<EventCallback>()
}

const collaborationListeners = {
  recordLocked: new Set<(n: RecordLockNotification) => void>(),
  recordUnlocked: new Set<(n: RecordUnlockNotification) => void>(),
  newComment: new Set<(n: CommentNotification) => void>(),
  changeRequestUpdated: new Set<(n: ChangeRequestNotification) => void>(),
}

function getOrCreateConnection(): HubConnection {
  if (connection) return connection

  connection = new HubConnectionBuilder()
    .withUrl('/hubs/notifications', {
      accessTokenFactory: () => tokenManager.getAccessToken() ?? ''
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Warning)
    .build()

  connection.onreconnecting(() => {
    isConnected.value = false
  })

  connection.onreconnected(() => {
    isConnected.value = true
  })

  connection.onclose(() => {
    isConnected.value = false
  })

  connection.on('EntityCreated', (notification: EntityChangeNotification) => {
    listeners.created.forEach(cb => { try { cb(notification) } catch (e) { console.error('SignalR EntityCreated callback error:', e) } })
  })

  connection.on('EntityUpdated', (notification: EntityChangeNotification) => {
    listeners.updated.forEach(cb => { try { cb(notification) } catch (e) { console.error('SignalR EntityUpdated callback error:', e) } })
  })

  connection.on('EntityDeleted', (notification: EntityChangeNotification) => {
    listeners.deleted.forEach(cb => { try { cb(notification) } catch (e) { console.error('SignalR EntityDeleted callback error:', e) } })
  })

  connection.on('RecordLocked', (n: RecordLockNotification) => {
    collaborationListeners.recordLocked.forEach(cb => { try { cb(n) } catch (e) { console.error('SignalR RecordLocked callback error:', e) } })
  })
  connection.on('RecordUnlocked', (n: RecordUnlockNotification) => {
    collaborationListeners.recordUnlocked.forEach(cb => { try { cb(n) } catch (e) { console.error('SignalR RecordUnlocked callback error:', e) } })
  })
  connection.on('NewComment', (n: CommentNotification) => {
    collaborationListeners.newComment.forEach(cb => { try { cb(n) } catch (e) { console.error('SignalR NewComment callback error:', e) } })
  })
  connection.on('ChangeRequestUpdated', (n: ChangeRequestNotification) => {
    collaborationListeners.changeRequestUpdated.forEach(cb => { try { cb(n) } catch (e) { console.error('SignalR ChangeRequestUpdated callback error:', e) } })
  })

  return connection
}

async function ensureConnected(): Promise<void> {
  const conn = getOrCreateConnection()
  if (conn.state === HubConnectionState.Connected) return
  if (conn.state === HubConnectionState.Connecting || conn.state === HubConnectionState.Reconnecting) {
    // Wait for existing connection attempt
    if (connectionPromise) await connectionPromise
    return
  }

  connectionPromise = conn.start()
    .then(() => {
      isConnected.value = true
    })
    .catch(err => {
      console.warn('SignalR connection failed:', err)
      isConnected.value = false
    })
    .finally(() => {
      connectionPromise = null
    })

  await connectionPromise
}

async function disconnect(): Promise<void> {
  if (connection && connection.state !== HubConnectionState.Disconnected) {
    await connection.stop()
  }
  connection = null
  isConnected.value = false
}

async function joinTenantGroup(tenantId: string): Promise<void> {
  await ensureConnected()
  if (connection?.state === HubConnectionState.Connected) {
    await connection.invoke('JoinTenantGroup', tenantId)
  }
}

async function leaveTenantGroup(tenantId: string): Promise<void> {
  if (connection?.state === HubConnectionState.Connected) {
    await connection.invoke('LeaveTenantGroup', tenantId)
  }
}

async function joinEntityGroup(entityName: string): Promise<void> {
  await ensureConnected()
  if (connection?.state === HubConnectionState.Connected) {
    await connection.invoke('JoinEntityGroup', entityName)
  }
}

async function leaveEntityGroup(entityName: string): Promise<void> {
  if (connection?.state === HubConnectionState.Connected) {
    await connection.invoke('LeaveEntityGroup', entityName)
  }
}

async function joinRecordGroup(recordKey: string): Promise<void> {
  await ensureConnected()
  if (connection?.state === HubConnectionState.Connected) {
    await connection.invoke('JoinRecordGroup', recordKey)
  }
}

async function leaveRecordGroup(recordKey: string): Promise<void> {
  await ensureConnected()
  if (connection?.state === HubConnectionState.Connected) {
    await connection.invoke('LeaveRecordGroup', recordKey)
  }
}

async function startEditing(recordKey: string, displayName: string): Promise<void> {
  await ensureConnected()
  if (connection?.state === HubConnectionState.Connected) {
    await connection.invoke('StartEditing', recordKey, displayName)
  }
}

async function stopEditing(recordKey: string): Promise<void> {
  await ensureConnected()
  if (connection?.state === HubConnectionState.Connected) {
    await connection.invoke('StopEditing', recordKey)
  }
}

export function useSignalR() {
  const localCallbacks: Array<{ type: keyof typeof listeners; cb: EventCallback }> = []
  const localCollabCallbacks: Array<{ type: keyof typeof collaborationListeners; cb: any }> = []

  function onEntityCreated(cb: EventCallback) {
    listeners.created.add(cb)
    localCallbacks.push({ type: 'created', cb })
  }

  function onEntityUpdated(cb: EventCallback) {
    listeners.updated.add(cb)
    localCallbacks.push({ type: 'updated', cb })
  }

  function onEntityDeleted(cb: EventCallback) {
    listeners.deleted.add(cb)
    localCallbacks.push({ type: 'deleted', cb })
  }

  function onRecordLocked(cb: (n: RecordLockNotification) => void) {
    collaborationListeners.recordLocked.add(cb)
    localCollabCallbacks.push({ type: 'recordLocked', cb })
  }

  function onRecordUnlocked(cb: (n: RecordUnlockNotification) => void) {
    collaborationListeners.recordUnlocked.add(cb)
    localCollabCallbacks.push({ type: 'recordUnlocked', cb })
  }

  function onNewComment(cb: (n: CommentNotification) => void) {
    collaborationListeners.newComment.add(cb)
    localCollabCallbacks.push({ type: 'newComment', cb })
  }

  function onChangeRequestUpdated(cb: (n: ChangeRequestNotification) => void) {
    collaborationListeners.changeRequestUpdated.add(cb)
    localCollabCallbacks.push({ type: 'changeRequestUpdated', cb })
  }

  // Auto-cleanup when component unmounts
  onUnmounted(() => {
    for (const { type, cb } of localCallbacks) {
      listeners[type].delete(cb)
    }
    for (const { type, cb } of localCollabCallbacks) {
      collaborationListeners[type].delete(cb)
    }
  })

  return {
    isConnected,
    connect: ensureConnected,
    disconnect,
    joinTenantGroup,
    leaveTenantGroup,
    joinEntityGroup,
    leaveEntityGroup,
    joinRecordGroup,
    leaveRecordGroup,
    startEditing,
    stopEditing,
    onEntityCreated,
    onEntityUpdated,
    onEntityDeleted,
    onRecordLocked,
    onRecordUnlocked,
    onNewComment,
    onChangeRequestUpdated,
  }
}
