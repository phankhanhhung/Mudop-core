<script setup lang="ts">
import { ref, computed, watch, type Component } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useNotificationStore, type Notification } from '@/stores/notification'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import {
  Bell,
  Plus,
  Pencil,
  Trash2,
  Check,
  X
} from 'lucide-vue-next'

const props = defineProps<{ open: boolean }>()
const emit = defineEmits<{ (e: 'close'): void }>()

const { t } = useI18n()
const router = useRouter()
const notificationStore = useNotificationStore()

type FilterTab = 'all' | 'created' | 'updated' | 'deleted'
const activeFilter = ref<FilterTab>('all')

const filterTabs: { key: FilterTab; labelKey: string }[] = [
  { key: 'all', labelKey: 'shell.notifications.all' },
  { key: 'created', labelKey: 'shell.notifications.created' },
  { key: 'updated', labelKey: 'shell.notifications.updated' },
  { key: 'deleted', labelKey: 'shell.notifications.deleted' }
]

const hasUnread = computed(() => notificationStore.unreadCount > 0)

// Filter notifications
const filteredNotifications = computed(() => {
  if (activeFilter.value === 'all') {
    return notificationStore.notifications
  }
  return notificationStore.notifications.filter(
    (n) => n.type === activeFilter.value
  )
})

// Group by date
interface NotificationGroup {
  label: string
  notifications: Notification[]
}

const groupedNotifications = computed<NotificationGroup[]>(() => {
  const now = new Date()
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  const yesterday = new Date(today.getTime() - 86400000)
  const thisWeekStart = new Date(today.getTime() - today.getDay() * 86400000)

  const todayGroup: Notification[] = []
  const yesterdayGroup: Notification[] = []
  const thisWeekGroup: Notification[] = []
  const olderGroup: Notification[] = []

  for (const notif of filteredNotifications.value) {
    const notifDate = new Date(notif.timestamp)
    if (notifDate >= today) {
      todayGroup.push(notif)
    } else if (notifDate >= yesterday) {
      yesterdayGroup.push(notif)
    } else if (notifDate >= thisWeekStart) {
      thisWeekGroup.push(notif)
    } else {
      olderGroup.push(notif)
    }
  }

  const groups: NotificationGroup[] = []
  if (todayGroup.length > 0) {
    groups.push({ label: t('shell.notifications.today'), notifications: todayGroup })
  }
  if (yesterdayGroup.length > 0) {
    groups.push({ label: t('shell.notifications.yesterday'), notifications: yesterdayGroup })
  }
  if (thisWeekGroup.length > 0) {
    groups.push({ label: t('shell.notifications.thisWeek'), notifications: thisWeekGroup })
  }
  if (olderGroup.length > 0) {
    groups.push({ label: t('shell.notifications.older'), notifications: olderGroup })
  }

  return groups
})

// Reset filter when panel opens
watch(
  () => props.open,
  (isOpen) => {
    if (isOpen) {
      activeFilter.value = 'all'
    }
  }
)

function getTypeIcon(type: string): Component {
  switch (type) {
    case 'created':
      return Plus
    case 'updated':
      return Pencil
    case 'deleted':
      return Trash2
    default:
      return Bell
  }
}

function getTypeColor(type: string): string {
  switch (type) {
    case 'created':
      return 'text-green-500'
    case 'updated':
      return 'text-blue-500'
    case 'deleted':
      return 'text-red-500'
    default:
      return 'text-muted-foreground'
  }
}

function getTypeLabel(type: string): string {
  switch (type) {
    case 'created':
      return t('shell.notifications.typeCreated')
    case 'updated':
      return t('shell.notifications.typeUpdated')
    case 'deleted':
      return t('shell.notifications.typeDeleted')
    default:
      return type
  }
}

function formatTimeAgo(date: Date): string {
  const now = new Date()
  const diff = now.getTime() - new Date(date).getTime()
  const seconds = Math.floor(diff / 1000)

  if (seconds < 60) return t('shell.notifications.justNow')
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return t('shell.notifications.minutesAgo', { count: minutes })
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return t('shell.notifications.hoursAgo', { count: hours })
  const days = Math.floor(hours / 24)
  return t('shell.notifications.daysAgo', { count: days })
}

function truncateId(id: string): string {
  if (id.length <= 8) return id
  return id.substring(0, 8) + '...'
}

function navigateToEntity(notification: Notification): void {
  notificationStore.markRead(notification.id)
  router.push(`/odata/${notification.module}/${notification.entityName}/${notification.entityId}`)
  emit('close')
}

function handleMarkAllRead(): void {
  notificationStore.markAllRead()
}

function handleClearAll(): void {
  notificationStore.clearAll()
}
</script>

<template>
  <!-- Backdrop -->
  <Teleport to="body">
    <Transition name="nc-backdrop">
      <div
        v-if="open"
        class="fixed inset-0 z-[80] bg-black/30"
        @click="emit('close')"
      />
    </Transition>

    <!-- Panel -->
    <Transition name="nc-panel">
      <div
        v-if="open"
        class="fixed inset-y-0 right-0 z-[90] w-80 sm:w-96 flex flex-col border-l bg-background shadow-xl"
        role="dialog"
        :aria-label="t('shell.notifications.title')"
      >
        <!-- Header -->
        <div class="flex items-center justify-between border-b px-4 py-3">
          <div class="flex items-center gap-2">
            <Bell class="h-5 w-5 text-foreground" aria-hidden="true" />
            <h2 class="text-base font-semibold">{{ t('shell.notifications.title') }}</h2>
            <Badge v-if="hasUnread" variant="destructive" class="text-xs px-1.5 py-0">
              {{ notificationStore.unreadCount }}
            </Badge>
          </div>
          <Button variant="ghost" size="icon" class="h-8 w-8" :aria-label="t('shell.notifications.close')" @click="emit('close')">
            <X class="h-4 w-4" />
          </Button>
        </div>

        <!-- Filter tabs -->
        <div class="flex border-b px-2">
          <button
            v-for="tab in filterTabs"
            :key="tab.key"
            :class="[
              'flex-1 py-2.5 text-xs font-medium transition-colors border-b-2',
              activeFilter === tab.key
                ? 'border-primary text-primary'
                : 'border-transparent text-muted-foreground hover:text-foreground'
            ]"
            @click="activeFilter = tab.key"
          >
            {{ t(tab.labelKey) }}
          </button>
        </div>

        <!-- Actions bar -->
        <div v-if="notificationStore.notifications.length > 0" class="flex items-center justify-end gap-1 px-3 py-2 border-b">
          <Button
            v-if="hasUnread"
            variant="ghost"
            size="sm"
            class="h-7 text-xs"
            @click="handleMarkAllRead"
          >
            <Check class="mr-1 h-3 w-3" aria-hidden="true" />
            {{ t('shell.notifications.markAllRead') }}
          </Button>
          <Button
            variant="ghost"
            size="sm"
            class="h-7 text-xs text-destructive hover:text-destructive"
            @click="handleClearAll"
          >
            <X class="mr-1 h-3 w-3" aria-hidden="true" />
            {{ t('shell.notifications.clearAll') }}
          </Button>
        </div>

        <!-- Notification list -->
        <div class="flex-1 overflow-y-auto">
          <!-- Empty state -->
          <div
            v-if="filteredNotifications.length === 0"
            class="flex flex-col items-center justify-center py-16 text-center px-4"
          >
            <Bell class="h-10 w-10 text-muted-foreground/40 mb-3" />
            <p class="text-sm text-muted-foreground">{{ t('shell.notifications.empty') }}</p>
          </div>

          <!-- Grouped notifications -->
          <template v-for="group in groupedNotifications" :key="group.label">
            <div class="sticky top-0 bg-muted/50 backdrop-blur-sm px-4 py-1.5 text-xs font-semibold text-muted-foreground">
              {{ group.label }}
            </div>
            <button
              v-for="notification in group.notifications"
              :key="notification.id"
              class="flex w-full items-start gap-3 px-4 py-3 text-left hover:bg-accent/50 transition-colors border-b border-border/50"
              :class="{ 'bg-accent/20': !notification.read }"
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
                <div class="text-xs text-muted-foreground mt-0.5">
                  {{ truncateId(notification.entityId) }}
                  <span class="mx-1">&middot;</span>
                  {{ formatTimeAgo(notification.timestamp) }}
                </div>
              </div>
              <span
                v-if="!notification.read"
                class="mt-1.5 h-2 w-2 shrink-0 rounded-full bg-primary"
              />
            </button>
          </template>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.nc-backdrop-enter-active,
.nc-backdrop-leave-active {
  transition: opacity 0.2s ease;
}
.nc-backdrop-enter-from,
.nc-backdrop-leave-to {
  opacity: 0;
}

.nc-panel-enter-active,
.nc-panel-leave-active {
  transition: transform 0.25s ease;
}
.nc-panel-enter-from,
.nc-panel-leave-to {
  transform: translateX(100%);
}
</style>
