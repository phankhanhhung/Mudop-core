<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, nextTick, watch, type Component } from 'vue'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { MoreHorizontal } from 'lucide-vue-next'

export interface ToolbarItem {
  id: string
  label: string
  icon?: Component
  variant?: 'default' | 'outline' | 'ghost' | 'destructive'
  disabled?: boolean
  priority?: number
  separator?: boolean
}

interface Props {
  items: ToolbarItem[]
  class?: string
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'item-click': [id: string]
}>()

const containerRef = ref<HTMLElement | null>(null)
const itemWidths = ref<Map<string, number>>(new Map())
const containerWidth = ref(0)
const overflowButtonWidth = 40 // width of the "..." button
const measured = ref(false)
const overflowOpen = ref(false)
const overflowMenuRef = ref<HTMLElement | null>(null)
const overflowButtonRef = ref<HTMLElement | null>(null)

// Sort items by priority (higher priority = stays visible longer)
const sortedItems = computed(() => {
  return [...props.items].map((item, index) => ({ ...item, _originalIndex: index }))
})

// Items sorted by priority for overflow purposes (lowest priority overflows first)
const itemsByOverflowOrder = computed(() => {
  return [...sortedItems.value]
    .filter(i => !i.separator)
    .sort((a, b) => (a.priority ?? 0) - (b.priority ?? 0))
})

// Calculate which items are visible vs overflowed
const visibleItemIds = computed(() => {
  if (!measured.value) {
    // Before measurement, show all
    return new Set(props.items.map(i => i.id))
  }

  let availableWidth = containerWidth.value
  const allNonSepItems = sortedItems.value.filter(i => !i.separator)
  const totalNeeded = allNonSepItems.reduce((sum, item) => sum + (itemWidths.value.get(item.id) ?? 0), 0)
  // Add separator widths
  const sepWidth = sortedItems.value.filter(i => i.separator).reduce((sum, item) => sum + (itemWidths.value.get(item.id) ?? 0), 0)

  if (totalNeeded + sepWidth <= availableWidth) {
    // Everything fits
    return new Set(props.items.map(i => i.id))
  }

  // Need overflow — subtract overflow button width
  availableWidth -= overflowButtonWidth

  // Start with all items visible, then remove lowest priority first
  const visible = new Set(allNonSepItems.map(i => i.id))

  for (const item of itemsByOverflowOrder.value) {
    if (getTotalVisibleWidth(visible) <= availableWidth) break
    visible.delete(item.id)
  }

  // Also include separators that are between two visible items
  for (const item of sortedItems.value) {
    if (item.separator) {
      // A separator is visible if items on both sides have visible items
      const idx = item._originalIndex
      const hasBefore = sortedItems.value.slice(0, idx).some(i => !i.separator && visible.has(i.id))
      const hasAfter = sortedItems.value.slice(idx + 1).some(i => !i.separator && visible.has(i.id))
      if (hasBefore && hasAfter) {
        visible.add(item.id)
      }
    }
  }

  return visible
})

function getTotalVisibleWidth(visibleIds: Set<string>): number {
  let total = 0
  for (const item of sortedItems.value) {
    if (item.separator) {
      // Count separator width if between visible items
      const idx = item._originalIndex
      const hasBefore = sortedItems.value.slice(0, idx).some(i => !i.separator && visibleIds.has(i.id))
      const hasAfter = sortedItems.value.slice(idx + 1).some(i => !i.separator && visibleIds.has(i.id))
      if (hasBefore && hasAfter) {
        total += itemWidths.value.get(item.id) ?? 0
      }
    } else if (visibleIds.has(item.id)) {
      total += itemWidths.value.get(item.id) ?? 0
    }
  }
  return total
}

const overflowItems = computed(() => {
  return sortedItems.value.filter(i => !visibleItemIds.value.has(i.id))
})

const hasOverflow = computed(() => overflowItems.value.length > 0)

// Measure item widths
function measureItems() {
  if (!containerRef.value) return
  const items = containerRef.value.querySelectorAll<HTMLElement>('[data-toolbar-item]')
  const widths = new Map<string, number>()
  items.forEach(el => {
    const id = el.dataset.toolbarItem
    if (id) {
      // Use marginLeft + offsetWidth + marginRight + gap (8px)
      widths.set(id, el.offsetWidth + 8)
    }
  })
  itemWidths.value = widths
  containerWidth.value = containerRef.value.offsetWidth
  measured.value = true
}

// ResizeObserver
let resizeObserver: ResizeObserver | null = null

onMounted(async () => {
  await nextTick()
  measureItems()

  if (containerRef.value) {
    resizeObserver = new ResizeObserver(() => {
      containerWidth.value = containerRef.value?.offsetWidth ?? 0
    })
    resizeObserver.observe(containerRef.value)
  }

  document.addEventListener('click', handleClickOutside)
})

onUnmounted(() => {
  resizeObserver?.disconnect()
  document.removeEventListener('click', handleClickOutside)
})

watch(() => props.items, async () => {
  measured.value = false
  await nextTick()
  measureItems()
}, { deep: true })

function handleItemClick(id: string) {
  emit('item-click', id)
}

function handleOverflowItemClick(id: string) {
  overflowOpen.value = false
  emit('item-click', id)
}

function toggleOverflow() {
  overflowOpen.value = !overflowOpen.value
}

function handleClickOutside(e: MouseEvent) {
  if (
    overflowOpen.value &&
    overflowMenuRef.value &&
    overflowButtonRef.value &&
    !overflowMenuRef.value.contains(e.target as Node) &&
    !overflowButtonRef.value.contains(e.target as Node)
  ) {
    overflowOpen.value = false
  }
}

function handleOverflowKeydown(e: KeyboardEvent) {
  if (!overflowOpen.value || !overflowMenuRef.value) return

  const focusable = Array.from(
    overflowMenuRef.value.querySelectorAll<HTMLElement>('button:not(:disabled)')
  )
  const currentIndex = focusable.indexOf(document.activeElement as HTMLElement)

  if (e.key === 'ArrowDown') {
    e.preventDefault()
    const next = currentIndex < focusable.length - 1 ? currentIndex + 1 : 0
    focusable[next]?.focus()
  } else if (e.key === 'ArrowUp') {
    e.preventDefault()
    const prev = currentIndex > 0 ? currentIndex - 1 : focusable.length - 1
    focusable[prev]?.focus()
  } else if (e.key === 'Escape') {
    overflowOpen.value = false
    overflowButtonRef.value?.focus()
  }
}

async function handleOverflowOpen() {
  toggleOverflow()
  if (overflowOpen.value) {
    await nextTick()
    const first = overflowMenuRef.value?.querySelector<HTMLElement>('button:not(:disabled)')
    first?.focus()
  }
}
</script>

<template>
  <div
    ref="containerRef"
    :class="cn('flex items-center gap-2 overflow-hidden', props.class)"
    role="toolbar"
    :aria-label="'Toolbar'"
  >
    <!-- Visible items -->
    <template v-for="item in sortedItems" :key="item.id">
      <!-- Separator -->
      <div
        v-if="item.separator && visibleItemIds.has(item.id)"
        :data-toolbar-item="item.id"
        class="h-6 w-px shrink-0 bg-border"
        role="separator"
      />

      <!-- Button item -->
      <Button
        v-else-if="!item.separator && visibleItemIds.has(item.id)"
        :data-toolbar-item="item.id"
        :variant="item.variant ?? 'outline'"
        size="sm"
        :disabled="item.disabled"
        class="shrink-0"
        @click="handleItemClick(item.id)"
      >
        <component
          :is="item.icon"
          v-if="item.icon"
          class="h-4 w-4"
          :class="{ 'mr-1.5': item.label }"
        />
        {{ item.label }}
      </Button>

      <!-- Hidden measurement slots (only during measurement phase) -->
      <Button
        v-else-if="!item.separator && !measured"
        :data-toolbar-item="item.id"
        :variant="item.variant ?? 'outline'"
        size="sm"
        class="shrink-0"
        :disabled="item.disabled"
      >
        <component
          :is="item.icon"
          v-if="item.icon"
          class="h-4 w-4"
          :class="{ 'mr-1.5': item.label }"
        />
        {{ item.label }}
      </Button>

      <div
        v-else-if="item.separator && !measured"
        :data-toolbar-item="item.id"
        class="h-6 w-px shrink-0 bg-border"
      />
    </template>

    <!-- Overflow button -->
    <div v-if="hasOverflow" class="relative shrink-0">
      <Button
        ref="overflowButtonRef"
        variant="ghost"
        size="sm"
        class="h-8 w-8 p-0"
        aria-haspopup="true"
        :aria-expanded="overflowOpen"
        aria-label="More actions"
        @click="handleOverflowOpen"
      >
        <MoreHorizontal class="h-4 w-4" />
      </Button>

      <!-- Overflow dropdown -->
      <Transition
        enter-active-class="transition duration-100 ease-out"
        enter-from-class="opacity-0 scale-95"
        enter-to-class="opacity-100 scale-100"
        leave-active-class="transition duration-75 ease-in"
        leave-from-class="opacity-100 scale-100"
        leave-to-class="opacity-0 scale-95"
      >
        <div
          v-if="overflowOpen"
          ref="overflowMenuRef"
          class="absolute right-0 top-full z-50 mt-1 min-w-[180px] rounded-md border bg-popover p-1 shadow-md"
          role="menu"
          @keydown="handleOverflowKeydown"
        >
          <template v-for="item in overflowItems" :key="item.id">
            <div v-if="item.separator" class="my-1 h-px bg-border" role="separator" />
            <button
              v-else
              role="menuitem"
              :disabled="item.disabled"
              class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm outline-none transition-colors hover:bg-accent hover:text-accent-foreground focus:bg-accent focus:text-accent-foreground disabled:pointer-events-none disabled:opacity-50"
              @click="handleOverflowItemClick(item.id)"
            >
              <component :is="item.icon" v-if="item.icon" class="h-4 w-4" />
              <span>{{ item.label }}</span>
            </button>
          </template>
        </div>
      </Transition>
    </div>
  </div>
</template>
