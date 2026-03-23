<script setup lang="ts">
import { ref, computed } from 'vue'
import { Button } from '@/components/ui/button'
import { Zap, ChevronDown } from 'lucide-vue-next'
import ActionDialog from './ActionDialog.vue'
import type { ActionMetadata } from '@/types/metadata'

interface Props {
  actions: ActionMetadata[]
  functions: ActionMetadata[]
  module: string
  entitySet: string
  entityId?: string
}

const props = defineProps<Props>()

const dialogOpen = ref(false)
const selectedAction = ref<ActionMetadata | null>(null)
const selectedMode = ref<'bound' | 'unbound'>('bound')
const showOverflow = ref(false)

const allItems = computed(() => {
  const items: { action: ActionMetadata; isFunction: boolean }[] = []
  for (const a of props.actions) {
    items.push({ action: a, isFunction: false })
  }
  for (const f of props.functions) {
    items.push({ action: f, isFunction: true })
  }
  return items
})

const primaryItems = computed(() => {
  if (allItems.value.length <= 3) return allItems.value
  return allItems.value.slice(0, 2)
})

const overflowItems = computed(() => {
  if (allItems.value.length <= 3) return []
  return allItems.value.slice(2)
})

function openAction(action: ActionMetadata) {
  selectedAction.value = action
  selectedMode.value = props.entityId ? 'bound' : 'unbound'
  dialogOpen.value = true
  showOverflow.value = false
}

function handleClose() {
  dialogOpen.value = false
  selectedAction.value = null
}

function handleExecuted(_result: unknown) {
  // Keep dialog open to show result; user can close manually
}

function toggleOverflow() {
  showOverflow.value = !showOverflow.value
}

function handleOverflowBlur(event: FocusEvent) {
  const target = event.relatedTarget as HTMLElement | null
  if (!target || !target.closest('.overflow-menu')) {
    showOverflow.value = false
  }
}
</script>

<template>
  <div class="flex items-center gap-1.5 relative">
    <!-- Primary action/function buttons -->
    <Button
      v-for="item in primaryItems"
      :key="item.action.name"
      :variant="item.isFunction ? 'outline' : 'default'"
      size="sm"
      @click="openAction(item.action)"
    >
      <Zap class="mr-1.5 h-3.5 w-3.5" />
      {{ item.action.name }}
    </Button>

    <!-- Overflow dropdown -->
    <div v-if="overflowItems.length > 0" class="relative overflow-menu">
      <Button
        variant="outline"
        size="sm"
        @click="toggleOverflow"
        @blur="handleOverflowBlur"
      >
        <ChevronDown class="h-3.5 w-3.5" />
        <span class="ml-1">+{{ overflowItems.length }}</span>
      </Button>

      <div
        v-if="showOverflow"
        class="absolute right-0 top-full mt-1 z-50 min-w-[160px] rounded-md border bg-popover p-1 shadow-md overflow-menu"
      >
        <button
          v-for="item in overflowItems"
          :key="item.action.name"
          class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent hover:text-accent-foreground cursor-pointer"
          @mousedown.prevent="openAction(item.action)"
        >
          <Zap class="h-3.5 w-3.5" />
          <span>{{ item.action.name }}</span>
          <span
            v-if="item.isFunction"
            class="ml-auto text-xs text-muted-foreground"
          >fn</span>
        </button>
      </div>
    </div>

    <!-- Action dialog -->
    <ActionDialog
      :open="dialogOpen"
      :action="selectedAction"
      :module="module"
      :entitySet="entitySet"
      :entityId="entityId"
      :mode="selectedMode"
      @close="handleClose"
      @executed="handleExecuted"
    />
  </div>
</template>
