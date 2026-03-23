<script setup lang="ts">
import { ref, watch, onUnmounted } from 'vue'
import { useDeltaTracking } from '@/composables/useDeltaTracking'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Activity } from 'lucide-vue-next'

const props = defineProps<{
  module: string
  entitySet: string
}>()

const emit = defineEmits<{
  refresh: []
}>()

const {
  isTracking,
  changes,
  isPolling,
  error,
  changeCount,
  startTracking,
  stopTracking,
  clearChanges,
  applyChanges
} = useDeltaTracking({
  module: props.module,
  entitySet: props.entitySet
})

const isDropdownOpen = ref(false)

// Close dropdown on outside click
function handleClickOutside(event: MouseEvent) {
  const target = event.target as HTMLElement
  if (!target.closest('[data-change-tracker]')) {
    isDropdownOpen.value = false
  }
}

watch(isDropdownOpen, (open) => {
  if (open) {
    document.addEventListener('click', handleClickOutside, { capture: true })
  } else {
    document.removeEventListener('click', handleClickOutside, { capture: true })
  }
})

onUnmounted(() => {
  document.removeEventListener('click', handleClickOutside, { capture: true })
})

function toggleTracking() {
  if (isTracking.value) {
    stopTracking()
    isDropdownOpen.value = false
  } else {
    startTracking()
  }
}

function toggleDropdown() {
  if (isTracking.value && changeCount.value > 0) {
    isDropdownOpen.value = !isDropdownOpen.value
  }
}

function handleApply() {
  applyChanges()
  isDropdownOpen.value = false
  emit('refresh')
}

function handleClear() {
  clearChanges()
  isDropdownOpen.value = false
}

function formatTimestamp(date: Date): string {
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffSec = Math.floor(diffMs / 1000)
  if (diffSec < 60) return `${diffSec}s ago`
  const diffMin = Math.floor(diffSec / 60)
  if (diffMin < 60) return `${diffMin}m ago`
  const diffHr = Math.floor(diffMin / 60)
  return `${diffHr}h ago`
}

function typeBadgeVariant(type: 'added' | 'modified' | 'removed'): 'default' | 'secondary' | 'destructive' {
  if (type === 'removed') return 'destructive'
  return 'default'
}

function typeBadgeClass(type: 'added' | 'modified' | 'removed'): string {
  if (type === 'added') return 'bg-green-600 text-white border-green-600'
  if (type === 'modified') return 'bg-yellow-500 text-white border-yellow-500'
  return '' // destructive variant handles red
}
</script>

<template>
  <div class="relative" data-change-tracker>
    <!-- Toggle tracking + badge -->
    <div class="flex items-center gap-1">
      <Button
        variant="outline"
        size="sm"
        :class="[
          'gap-1.5',
          isTracking ? 'border-primary text-primary' : ''
        ]"
        @click="toggleTracking"
        :title="isTracking ? 'Stop change tracking' : 'Start change tracking'"
      >
        <Activity class="h-3.5 w-3.5" :class="{ 'animate-pulse': isPolling }" />
        <span class="text-xs">{{ isTracking ? 'Tracking' : 'Track' }}</span>
      </Button>

      <!-- Change count badge (clickable to open dropdown) -->
      <button
        v-if="isTracking && changeCount > 0"
        class="relative"
        @click.stop="toggleDropdown"
      >
        <Badge
          :class="'cursor-pointer' + (changeCount > 0 ? ' animate-pulse' : '')"
        >
          {{ changeCount }}
        </Badge>
      </button>
    </div>

    <!-- Error tooltip -->
    <div
      v-if="error"
      class="absolute right-0 top-full mt-1 z-50 w-64 rounded-md border bg-destructive/10 p-2 text-xs text-destructive shadow-md"
    >
      {{ error }}
    </div>

    <!-- Dropdown panel -->
    <div
      v-if="isDropdownOpen && changeCount > 0"
      class="absolute right-0 top-full mt-1 z-50 w-80 rounded-md border bg-popover text-popover-foreground shadow-lg"
    >
      <!-- Header -->
      <div class="flex items-center justify-between border-b px-3 py-2">
        <span class="text-sm font-medium">Changes ({{ changeCount }})</span>
        <div class="flex gap-1">
          <Button variant="ghost" size="sm" class="h-7 text-xs" @click="handleClear">
            Clear
          </Button>
          <Button variant="default" size="sm" class="h-7 text-xs" @click="handleApply">
            Apply &amp; Refresh
          </Button>
        </div>
      </div>

      <!-- Change list -->
      <div class="max-h-64 overflow-y-auto">
        <div
          v-for="(change, idx) in changes"
          :key="`${change.id}-${idx}`"
          class="flex items-center gap-2 border-b px-3 py-2 last:border-b-0 hover:bg-muted/50"
        >
          <Badge
            :variant="typeBadgeVariant(change.type)"
            :class="'shrink-0 text-[10px] px-1.5 py-0 ' + typeBadgeClass(change.type)"
          >
            {{ change.type }}
          </Badge>
          <span class="flex-1 truncate text-xs font-mono">
            {{ change.id }}
          </span>
          <span class="shrink-0 text-[10px] text-muted-foreground">
            {{ formatTimestamp(change.timestamp) }}
          </span>
        </div>
      </div>

      <!-- Empty state (shouldn't normally show when dropdown is open, but defensive) -->
      <div v-if="changeCount === 0" class="px-3 py-4 text-center text-xs text-muted-foreground">
        No changes detected
      </div>
    </div>
  </div>
</template>
