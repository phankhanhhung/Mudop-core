<script setup lang="ts">
import { computed, ref, type Component } from 'vue'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { Circle, AlertCircle, CheckCircle, AlertTriangle, Info } from 'lucide-vue-next'

export interface TimelineItem {
  id: string
  title: string
  content?: string
  datetime: string
  icon?: Component
  type?: 'info' | 'success' | 'warning' | 'error' | 'neutral'
  author?: string
  avatar?: string
}

interface Props {
  items: TimelineItem[]
  sortOrder?: 'asc' | 'desc'
  groupByDate?: boolean
  showConnector?: boolean
  maxItems?: number
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  sortOrder: 'desc',
  groupByDate: true,
  showConnector: true,
})

const emit = defineEmits<{
  'item-click': [item: TimelineItem]
}>()

// ── Show more state ──────────────────────────────────────────────────

const expanded = ref(false)

// ── Type color mapping ───────────────────────────────────────────────

const typeColors: Record<string, { dot: string; bg: string; ring: string }> = {
  info: { dot: 'bg-blue-500', bg: 'bg-blue-50 dark:bg-blue-950/30', ring: 'ring-blue-200 dark:ring-blue-800' },
  success: { dot: 'bg-green-500', bg: 'bg-green-50 dark:bg-green-950/30', ring: 'ring-green-200 dark:ring-green-800' },
  warning: { dot: 'bg-amber-500', bg: 'bg-amber-50 dark:bg-amber-950/30', ring: 'ring-amber-200 dark:ring-amber-800' },
  error: { dot: 'bg-red-500', bg: 'bg-red-50 dark:bg-red-950/30', ring: 'ring-red-200 dark:ring-red-800' },
  neutral: { dot: 'bg-gray-400', bg: 'bg-gray-50 dark:bg-gray-900/30', ring: 'ring-gray-200 dark:ring-gray-800' },
}

const defaultIcons: Record<string, Component> = {
  info: Info,
  success: CheckCircle,
  warning: AlertTriangle,
  error: AlertCircle,
  neutral: Circle,
}

// ── Sorted items ─────────────────────────────────────────────────────

const sortedItems = computed(() => {
  const sorted = [...props.items].sort((a, b) => {
    const diff = new Date(a.datetime).getTime() - new Date(b.datetime).getTime()
    return props.sortOrder === 'asc' ? diff : -diff
  })
  return sorted
})

// ── Visible items (respects maxItems) ────────────────────────────────

const visibleItems = computed(() => {
  if (!props.maxItems || expanded.value) return sortedItems.value
  return sortedItems.value.slice(0, props.maxItems)
})

const hasMore = computed(() => {
  if (!props.maxItems) return false
  return sortedItems.value.length > props.maxItems && !expanded.value
})

const remainingCount = computed(() => {
  if (!props.maxItems) return 0
  return sortedItems.value.length - props.maxItems
})

// ── Date grouping ────────────────────────────────────────────────────

function getDateKey(datetime: string): string {
  const date = new Date(datetime)
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

function getDateLabel(dateKey: string): string {
  const [year, month, day] = dateKey.split('-').map(Number)
  const date = new Date(year, month - 1, day)
  const now = new Date()
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  const yesterday = new Date(today)
  yesterday.setDate(yesterday.getDate() - 1)

  if (date.getTime() === today.getTime()) return 'Today'
  if (date.getTime() === yesterday.getTime()) return 'Yesterday'

  return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

interface GroupedEntries {
  dateKey: string
  label: string
  items: TimelineItem[]
}

const groupedItems = computed<GroupedEntries[]>(() => {
  if (!props.groupByDate) return []

  const groups = new Map<string, TimelineItem[]>()
  for (const item of visibleItems.value) {
    const key = getDateKey(item.datetime)
    if (!groups.has(key)) groups.set(key, [])
    groups.get(key)!.push(item)
  }

  return Array.from(groups.entries()).map(([dateKey, items]) => ({
    dateKey,
    label: getDateLabel(dateKey),
    items,
  }))
})

// ── Relative time ────────────────────────────────────────────────────

function relativeTime(datetime: string): string {
  const now = Date.now()
  const then = new Date(datetime).getTime()
  const diffMs = now - then
  const diffSec = Math.floor(diffMs / 1000)
  const diffMin = Math.floor(diffSec / 60)
  const diffHour = Math.floor(diffMin / 60)
  const diffDay = Math.floor(diffHour / 24)
  const diffWeek = Math.floor(diffDay / 7)
  const diffMonth = Math.floor(diffDay / 30)

  if (diffSec < 60) return 'just now'
  if (diffMin < 60) return `${diffMin} minute${diffMin === 1 ? '' : 's'} ago`
  if (diffHour < 24) return `${diffHour} hour${diffHour === 1 ? '' : 's'} ago`
  if (diffDay < 7) return `${diffDay} day${diffDay === 1 ? '' : 's'} ago`
  if (diffWeek < 5) return `${diffWeek} week${diffWeek === 1 ? '' : 's'} ago`
  return `${diffMonth} month${diffMonth === 1 ? '' : 's'} ago`
}

// ── Helpers ──────────────────────────────────────────────────────────

function getColors(type: string = 'neutral') {
  return typeColors[type] || typeColors.neutral
}

function getIcon(item: TimelineItem): Component {
  return item.icon || defaultIcons[item.type || 'neutral'] || Circle
}

function getInitials(name: string): string {
  return name
    .split(' ')
    .map(w => w[0])
    .slice(0, 2)
    .join('')
    .toUpperCase()
}
</script>

<template>
  <div :class="cn('relative', props.class)">
    <!-- Grouped mode -->
    <template v-if="groupByDate">
      <div v-for="group in groupedItems" :key="group.dateKey" class="mb-6 last:mb-0">
        <!-- Date header -->
        <div class="flex items-center gap-3 mb-4">
          <span class="text-sm font-semibold text-foreground">{{ group.label }}</span>
          <div class="flex-1 h-px bg-border" />
        </div>

        <!-- Entries -->
        <div class="relative ml-4">
          <!-- Connector line -->
          <div
            v-if="showConnector && group.items.length > 1"
            class="absolute left-3 top-3 bottom-3 w-px bg-border"
          />

          <div
            v-for="item in group.items"
            :key="item.id"
            class="relative flex gap-4 pb-6 last:pb-0 cursor-pointer group"
            @click="emit('item-click', item)"
          >
            <!-- Dot / Icon -->
            <div class="relative z-10 flex-shrink-0 flex items-start pt-0.5">
              <div
                class="flex items-center justify-center w-6 h-6 rounded-full ring-4 ring-background"
                :class="getColors(item.type).dot"
              >
                <component
                  :is="getIcon(item)"
                  class="h-3.5 w-3.5 text-white"
                />
              </div>
            </div>

            <!-- Content card -->
            <div
              class="flex-1 min-w-0 rounded-lg border px-4 py-3 transition-colors group-hover:border-foreground/20"
              :class="getColors(item.type).bg"
            >
              <div class="flex items-start justify-between gap-2">
                <div class="min-w-0 flex-1">
                  <p class="text-sm font-medium text-foreground truncate">{{ item.title }}</p>
                  <p v-if="item.content" class="text-sm text-muted-foreground mt-1">{{ item.content }}</p>
                </div>
                <span class="text-xs text-muted-foreground whitespace-nowrap flex-shrink-0">
                  {{ relativeTime(item.datetime) }}
                </span>
              </div>
              <!-- Author -->
              <div v-if="item.author" class="flex items-center gap-2 mt-2">
                <div
                  class="flex items-center justify-center w-5 h-5 rounded-full bg-muted text-[10px] font-medium text-muted-foreground"
                >
                  {{ getInitials(item.avatar || item.author) }}
                </div>
                <span class="text-xs text-muted-foreground">{{ item.author }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </template>

    <!-- Flat mode (no grouping) -->
    <template v-else>
      <div class="relative ml-4">
        <!-- Connector line -->
        <div
          v-if="showConnector && visibleItems.length > 1"
          class="absolute left-3 top-3 bottom-3 w-px bg-border"
        />

        <div
          v-for="item in visibleItems"
          :key="item.id"
          class="relative flex gap-4 pb-6 last:pb-0 cursor-pointer group"
          @click="emit('item-click', item)"
        >
          <!-- Dot / Icon -->
          <div class="relative z-10 flex-shrink-0 flex items-start pt-0.5">
            <div
              class="flex items-center justify-center w-6 h-6 rounded-full ring-4 ring-background"
              :class="getColors(item.type).dot"
            >
              <component
                :is="getIcon(item)"
                class="h-3.5 w-3.5 text-white"
              />
            </div>
          </div>

          <!-- Content card -->
          <div
            class="flex-1 min-w-0 rounded-lg border px-4 py-3 transition-colors group-hover:border-foreground/20"
            :class="getColors(item.type).bg"
          >
            <div class="flex items-start justify-between gap-2">
              <div class="min-w-0 flex-1">
                <p class="text-sm font-medium text-foreground truncate">{{ item.title }}</p>
                <p v-if="item.content" class="text-sm text-muted-foreground mt-1">{{ item.content }}</p>
              </div>
              <span class="text-xs text-muted-foreground whitespace-nowrap flex-shrink-0">
                {{ relativeTime(item.datetime) }}
              </span>
            </div>
            <!-- Author -->
            <div v-if="item.author" class="flex items-center gap-2 mt-2">
              <div
                class="flex items-center justify-center w-5 h-5 rounded-full bg-muted text-[10px] font-medium text-muted-foreground"
              >
                {{ getInitials(item.avatar || item.author) }}
              </div>
              <span class="text-xs text-muted-foreground">{{ item.author }}</span>
            </div>
          </div>
        </div>
      </div>
    </template>

    <!-- Show more -->
    <div v-if="hasMore" class="mt-4 ml-4 pl-10">
      <Button variant="ghost" size="sm" @click="expanded = true">
        Show {{ remainingCount }} more
      </Button>
    </div>
  </div>
</template>
