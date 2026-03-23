<script setup lang="ts">
import { ref, computed } from 'vue'
import { messageManager, type Message, type MessageType } from '@/odata/MessageManager'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import {
  AlertCircle,
  AlertTriangle,
  Info,
  CheckCircle2,
  X,
  Bell,
} from 'lucide-vue-next'

interface Props {
  /** Position anchor alignment */
  align?: 'start' | 'center' | 'end'
}

withDefaults(defineProps<Props>(), {
  align: 'end',
})

const emit = defineEmits<{
  'navigate-to-target': [target: string]
}>()

const isOpen = ref(false)

type TabType = 'all' | 'error' | 'warning' | 'info' | 'success'
const activeTab = ref<TabType>('all')

// ---------------------------------------------------------------------------
// Reactive state from singleton
// ---------------------------------------------------------------------------

const messages = messageManager.messages
const unreadCount = messageManager.unreadCount
const errorCount = messageManager.errorCount
const warningCount = messageManager.warningCount

const infoCount = computed(() => messageManager.infoMessages.value.length)
const successCount = computed(() => messageManager.successMessages.value.length)

// ---------------------------------------------------------------------------
// Tabs
// ---------------------------------------------------------------------------

interface TabDef {
  type: TabType
  label: string
  count: number
  badgeVariant: 'default' | 'secondary' | 'destructive' | 'outline'
}

const tabs = computed<TabDef[]>(() => [
  { type: 'all', label: 'All', count: messages.value.length, badgeVariant: 'secondary' },
  { type: 'error', label: 'Errors', count: errorCount.value, badgeVariant: 'destructive' },
  { type: 'warning', label: 'Warnings', count: warningCount.value, badgeVariant: 'secondary' },
  { type: 'info', label: 'Info', count: infoCount.value, badgeVariant: 'secondary' },
  { type: 'success', label: 'Success', count: successCount.value, badgeVariant: 'secondary' },
])

// ---------------------------------------------------------------------------
// Filtered messages
// ---------------------------------------------------------------------------

const filteredMessages = computed(() => {
  if (activeTab.value === 'all') return messages.value
  return messages.value.filter((m) => m.type === activeTab.value)
})

// ---------------------------------------------------------------------------
// Severity-based trigger styling
// ---------------------------------------------------------------------------

const highestSeverity = computed(() => messageManager.getHighestSeverity())

const severityIcon = computed(() => {
  switch (highestSeverity.value) {
    case 'error':
      return AlertCircle
    case 'warning':
      return AlertTriangle
    case 'info':
      return Info
    case 'success':
      return CheckCircle2
    default:
      return Bell
  }
})

const severityColor = computed(() => {
  switch (highestSeverity.value) {
    case 'error':
      return 'text-destructive'
    case 'warning':
      return 'text-amber-500'
    case 'info':
      return 'text-blue-500'
    case 'success':
      return 'text-emerald-500'
    default:
      return 'text-muted-foreground'
  }
})

const severityBadgeVariant = computed<'default' | 'secondary' | 'destructive' | 'outline'>(() => {
  switch (highestSeverity.value) {
    case 'error':
      return 'destructive'
    case 'warning':
      return 'secondary'
    default:
      return 'default'
  }
})

// ---------------------------------------------------------------------------
// Icon / color helpers
// ---------------------------------------------------------------------------

function getMessageIcon(type: MessageType) {
  switch (type) {
    case 'error':
      return AlertCircle
    case 'warning':
      return AlertTriangle
    case 'info':
      return Info
    case 'success':
      return CheckCircle2
  }
}

function getMessageColor(type: MessageType): string {
  switch (type) {
    case 'error':
      return 'text-destructive'
    case 'warning':
      return 'text-amber-500'
    case 'info':
      return 'text-blue-500'
    case 'success':
      return 'text-emerald-500'
  }
}

// ---------------------------------------------------------------------------
// Time formatting
// ---------------------------------------------------------------------------

function formatTimeAgo(date: Date): string {
  const now = Date.now()
  const diff = now - date.getTime()
  const seconds = Math.floor(diff / 1000)
  const minutes = Math.floor(seconds / 60)
  const hours = Math.floor(minutes / 60)
  const days = Math.floor(hours / 24)

  if (seconds < 60) return 'Just now'
  if (minutes < 60) return `${minutes}m ago`
  if (hours < 24) return `${hours}h ago`
  if (days === 1) return 'Yesterday'
  return `${days}d ago`
}

// ---------------------------------------------------------------------------
// Actions
// ---------------------------------------------------------------------------

function markAllAsRead() {
  messageManager.markAllAsRead()
}

function clearAll() {
  messageManager.clearAll()
  isOpen.value = false
}

function removeMessage(id: string) {
  messageManager.removeMessage(id)
  if (messages.value.length === 0) {
    isOpen.value = false
  }
}

function handleMessageClick(msg: Message) {
  messageManager.markAsRead(msg.id)
  if (msg.target) {
    emit('navigate-to-target', msg.target)
  }
}
</script>

<template>
  <div class="relative">
    <!-- Trigger button -->
    <Button variant="outline" size="sm" class="relative" @click="isOpen = !isOpen">
      <component :is="severityIcon" class="h-4 w-4" :class="severityColor" />
      <Badge
        v-if="unreadCount > 0"
        class="absolute -top-1.5 -right-1.5 h-5 w-5 p-0 flex items-center justify-center text-[10px]"
        :variant="severityBadgeVariant"
      >
        {{ unreadCount > 99 ? '99+' : unreadCount }}
      </Badge>
    </Button>

    <!-- Popover backdrop + panel -->
    <Teleport to="body">
      <div v-if="isOpen" class="fixed inset-0 z-50" @click="isOpen = false">
        <div
          class="absolute right-4 top-16 w-[400px] max-h-[500px] rounded-lg border bg-popover text-popover-foreground shadow-lg flex flex-col"
          @click.stop
        >
          <!-- Header -->
          <div class="flex items-center justify-between px-4 py-3 border-b">
            <h3 class="font-semibold text-sm">Messages</h3>
            <div class="flex gap-1">
              <Button
                v-if="unreadCount > 0"
                variant="ghost"
                size="sm"
                @click="markAllAsRead"
              >
                Mark all read
              </Button>
              <Button
                variant="ghost"
                size="sm"
                class="text-destructive hover:text-destructive"
                @click="clearAll"
              >
                Clear all
              </Button>
            </div>
          </div>

          <!-- Tabs -->
          <div class="flex border-b">
            <button
              v-for="tab in tabs"
              :key="tab.type"
              :class="[
                'flex-1 py-2 text-xs border-b-2 transition-colors',
                activeTab === tab.type
                  ? 'border-primary text-primary font-medium'
                  : 'border-transparent text-muted-foreground hover:text-foreground',
              ]"
              @click="activeTab = tab.type"
            >
              {{ tab.label }}
              <Badge
                v-if="tab.count > 0"
                :variant="tab.badgeVariant"
                class="ml-1 text-[10px] px-1.5 py-0"
              >
                {{ tab.count }}
              </Badge>
            </button>
          </div>

          <!-- Message list -->
          <div class="flex-1 overflow-auto">
            <!-- Empty state -->
            <div
              v-if="filteredMessages.length === 0"
              class="flex flex-col items-center justify-center py-12 text-muted-foreground"
            >
              <CheckCircle2 class="h-8 w-8 mb-2" />
              <p class="text-sm">No messages</p>
            </div>

            <!-- Messages -->
            <div
              v-for="msg in filteredMessages"
              :key="msg.id"
              class="flex gap-3 px-4 py-3 border-b last:border-0 hover:bg-muted/50 cursor-pointer transition-colors"
              :class="{ 'opacity-60': msg.read }"
              @click="handleMessageClick(msg)"
            >
              <component
                :is="getMessageIcon(msg.type)"
                class="h-4 w-4 mt-0.5 flex-shrink-0"
                :class="getMessageColor(msg.type)"
              />
              <div class="flex-1 min-w-0">
                <p class="text-sm font-medium">{{ msg.title }}</p>
                <p
                  v-if="msg.description"
                  class="text-xs text-muted-foreground mt-0.5"
                >
                  {{ msg.description }}
                </p>
                <div class="flex items-center gap-2 mt-1">
                  <span
                    v-if="msg.target"
                    class="text-xs text-primary font-medium"
                  >
                    {{ msg.target }}
                  </span>
                  <span
                    v-if="msg.technical"
                    class="text-[10px] text-muted-foreground font-mono"
                  >
                    {{ msg.technical }}
                  </span>
                  <span class="text-[10px] text-muted-foreground">
                    {{ formatTimeAgo(msg.timestamp) }}
                  </span>
                </div>
              </div>
              <button
                class="flex-shrink-0 p-1 rounded hover:bg-muted"
                @click.stop="removeMessage(msg.id)"
              >
                <X class="h-3 w-3 text-muted-foreground" />
              </button>
            </div>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>
