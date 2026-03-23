<script setup lang="ts">
import { watch, onMounted } from 'vue'
import { useSignalR } from '@/composables/useSignalR'
import { useNotificationStore } from '@/stores/notification'
import { useAuthStore } from '@/stores/auth'
import { useTenantStore } from '@/stores/tenant'
import { useUiStore } from '@/stores/ui'
import type { EntityChangeNotification } from '@/composables/useSignalR'

const signalR = useSignalR()
const notificationStore = useNotificationStore()
const authStore = useAuthStore()
const tenantStore = useTenantStore()
const uiStore = useUiStore()

let currentTenantGroupId: string | null = null

function handleCreated(notification: EntityChangeNotification) {
  notificationStore.addNotification({
    type: 'created',
    entityName: notification.entityName,
    entityId: notification.entityId,
    module: notification.module,
    timestamp: new Date(notification.timestamp)
  })
  uiStore.info(
    `${notification.entityName} created`,
    `ID: ${notification.entityId.substring(0, 8)}...`
  )
}

function handleUpdated(notification: EntityChangeNotification) {
  notificationStore.addNotification({
    type: 'updated',
    entityName: notification.entityName,
    entityId: notification.entityId,
    module: notification.module,
    timestamp: new Date(notification.timestamp)
  })
  uiStore.info(
    `${notification.entityName} updated`,
    `ID: ${notification.entityId.substring(0, 8)}...`
  )
}

function handleDeleted(notification: EntityChangeNotification) {
  notificationStore.addNotification({
    type: 'deleted',
    entityName: notification.entityName,
    entityId: notification.entityId,
    module: notification.module,
    timestamp: new Date(notification.timestamp)
  })
  uiStore.warning(
    `${notification.entityName} deleted`,
    `ID: ${notification.entityId.substring(0, 8)}...`
  )
}

// Register event listeners
signalR.onEntityCreated(handleCreated)
signalR.onEntityUpdated(handleUpdated)
signalR.onEntityDeleted(handleDeleted)

// Connect when authenticated
onMounted(async () => {
  if (authStore.isAuthenticated) {
    await signalR.connect()
    if (tenantStore.currentTenantId) {
      currentTenantGroupId = tenantStore.currentTenantId
      await signalR.joinTenantGroup(currentTenantGroupId)
    }
  }
})

// Watch auth state — connect/disconnect accordingly
watch(() => authStore.isAuthenticated, async (isAuth) => {
  if (isAuth) {
    await signalR.connect()
  } else {
    await signalR.disconnect()
    currentTenantGroupId = null
  }
})

// Watch tenant changes — switch groups
watch(() => tenantStore.currentTenantId, async (newTenantId, oldTenantId) => {
  if (oldTenantId && currentTenantGroupId) {
    await signalR.leaveTenantGroup(currentTenantGroupId)
  }
  if (newTenantId) {
    currentTenantGroupId = newTenantId
    await signalR.joinTenantGroup(newTenantId)
  } else {
    currentTenantGroupId = null
  }
})
</script>

<template>
  <!-- Renderless provider component -->
  <slot />
</template>
