import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

export interface Notification {
  id: string
  type: 'created' | 'updated' | 'deleted'
  entityName: string
  entityId: string
  module: string
  timestamp: Date
  read: boolean
}

const MAX_NOTIFICATIONS = 50

export const useNotificationStore = defineStore('notification', () => {
  const notifications = ref<Notification[]>([])

  const unreadCount = computed(() =>
    notifications.value.filter(n => !n.read).length
  )

  function addNotification(notification: Omit<Notification, 'id' | 'read'>) {
    const id = `notif-${Date.now()}-${Math.random().toString(36).substring(2, 9)}`
    notifications.value.unshift({
      ...notification,
      id,
      read: false
    })

    // Keep only the latest notifications
    if (notifications.value.length > MAX_NOTIFICATIONS) {
      notifications.value = notifications.value.slice(0, MAX_NOTIFICATIONS)
    }
  }

  function markAllRead() {
    for (const n of notifications.value) {
      n.read = true
    }
  }

  function markRead(id: string) {
    const n = notifications.value.find(n => n.id === id)
    if (n) n.read = true
  }

  function clearAll() {
    notifications.value = []
  }

  return {
    notifications,
    unreadCount,
    addNotification,
    markAllRead,
    markRead,
    clearAll
  }
})
