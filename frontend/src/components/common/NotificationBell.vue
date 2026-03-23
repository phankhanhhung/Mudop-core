<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useNotificationStore } from '@/stores/notification'
import { Button } from '@/components/ui/button'
import { Bell, Plus, Pencil, Trash2, Check, X } from 'lucide-vue-next'

const emit = defineEmits<{ (e: 'open-all'): void }>()

const router = useRouter()
const notificationStore = useNotificationStore()
const isOpen = ref(false)

const hasUnread = computed(() => notificationStore.unreadCount > 0)

function toggleDropdown() {
  isOpen.value = !isOpen.value
}

function closeDropdown() {
  isOpen.value = false
}

function navigateToEntity(notification: { entityName: string; entityId: string; module: string }) {
  notificationStore.markRead(
    notificationStore.notifications.find(
      n => n.entityName === notification.entityName && n.entityId === notification.entityId
    )?.id ?? ''
  )
  closeDropdown()
  router.push(`/odata/${notification.module}/${notification.entityName}/${notification.entityId}`)
}

function formatTime(date: Date): string {
  const now = new Date()
  const diff = now.getTime() - new Date(date).getTime()
  const seconds = Math.floor(diff / 1000)

  if (seconds < 60) return 'just now'
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)
  return `${days}d ago`
}

function truncateId(id: string): string {
  if (id.length <= 8) return id
  return id.substring(0, 8) + '...'
}

function getTypeIcon(type: string) {
  switch (type) {
    case 'created': return Plus
    case 'updated': return Pencil
    case 'deleted': return Trash2
    default: return Bell
  }
}

function getTypeColor(type: string): string {
  switch (type) {
    case 'created': return 'text-green-500'
    case 'updated': return 'text-blue-500'
    case 'deleted': return 'text-red-500'
    default: return 'text-muted-foreground'
  }
}

function getTypeLabel(type: string): string {
  switch (type) {
    case 'created': return 'created'
    case 'updated': return 'updated'
    case 'deleted': return 'deleted'
    default: return type
  }
}
</script>

<template>
  <div class="relative">
    <Button
      variant="ghost"
      size="icon"
      :aria-label="hasUnread ? $t('accessibility.unreadNotifications', { count: notificationStore.unreadCount }) : $t('accessibility.noUnreadNotifications')"
      :aria-expanded="isOpen"
      aria-haspopup="true"
      @click="toggleDropdown"
    >
      <Bell class="h-5 w-5" aria-hidden="true" />
      <span
        v-if="hasUnread"
        class="absolute -top-0.5 -right-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-destructive px-1 text-[10px] font-bold text-destructive-foreground"
        aria-hidden="true"
      >
        {{ notificationStore.unreadCount > 99 ? '99+' : notificationStore.unreadCount }}
      </span>
    </Button>

    <!-- Backdrop -->
    <div
      v-if="isOpen"
      class="fixed inset-0 z-40"
      @click="closeDropdown"
    />

    <!-- Dropdown -->
    <Transition
      enter-active-class="transition ease-out duration-100"
      enter-from-class="transform opacity-0 scale-95"
      enter-to-class="transform opacity-100 scale-100"
      leave-active-class="transition ease-in duration-75"
      leave-from-class="transform opacity-100 scale-100"
      leave-to-class="transform opacity-0 scale-95"
    >
      <div
        v-if="isOpen"
        class="absolute right-0 z-50 mt-2 w-80 rounded-md border bg-popover shadow-lg"
      >
        <!-- Header -->
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h3 class="text-sm font-semibold">Notifications</h3>
          <div class="flex gap-1">
            <Button
              v-if="hasUnread"
              variant="ghost"
              size="sm"
              class="h-7 text-xs"
              :aria-label="$t('accessibility.markAllRead')"
              @click="notificationStore.markAllRead()"
            >
              <Check class="mr-1 h-3 w-3" aria-hidden="true" />
              Mark read
            </Button>
            <Button
              v-if="notificationStore.notifications.length > 0"
              variant="ghost"
              size="sm"
              class="h-7 text-xs"
              :aria-label="$t('accessibility.clearAllNotifications')"
              @click="notificationStore.clearAll()"
            >
              <X class="mr-1 h-3 w-3" aria-hidden="true" />
              Clear
            </Button>
          </div>
        </div>

        <!-- View all button -->
        <div class="border-b px-4 py-2">
          <Button
            variant="ghost"
            size="sm"
            class="w-full text-xs"
            @click="closeDropdown(); emit('open-all')"
          >
            {{ $t('shell.notifications.viewAll') }}
          </Button>
        </div>

        <!-- Notification list -->
        <div class="max-h-80 overflow-y-auto">
          <div
            v-if="notificationStore.notifications.length === 0"
            class="px-4 py-8 text-center text-sm text-muted-foreground"
          >
            No notifications
          </div>

          <button
            v-for="notification in notificationStore.notifications"
            :key="notification.id"
            class="flex w-full items-start gap-3 px-4 py-3 text-left hover:bg-accent/50 transition-colors"
            :class="{ 'bg-accent/30': !notification.read }"
            @click="navigateToEntity(notification)"
          >
            <component
              :is="getTypeIcon(notification.type)"
              class="mt-0.5 h-4 w-4 shrink-0"
              :class="getTypeColor(notification.type)"
            />
            <div class="min-w-0 flex-1">
              <div class="text-sm">
                <span class="font-medium">{{ notification.entityName }}</span>
                <span class="text-muted-foreground">
                  {{ ' ' + getTypeLabel(notification.type) }}
                </span>
              </div>
              <div class="text-xs text-muted-foreground">
                {{ truncateId(notification.entityId) }}
                <span class="mx-1">&middot;</span>
                {{ formatTime(notification.timestamp) }}
              </div>
            </div>
            <span
              v-if="!notification.read"
              class="mt-1.5 h-2 w-2 shrink-0 rounded-full bg-primary"
            />
          </button>
        </div>
      </div>
    </Transition>
  </div>
</template>
