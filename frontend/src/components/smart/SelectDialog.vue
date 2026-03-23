<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import {
  DialogRoot,
  DialogPortal,
  DialogOverlay,
  DialogContent,
  DialogTitle,
  DialogDescription,
} from 'radix-vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { cn } from '@/lib/utils'
import { X, Search } from 'lucide-vue-next'

interface SelectItem {
  key: string
  label: string
  description?: string
}

interface Props {
  open: boolean
  title?: string
  items: SelectItem[]
  multiSelect?: boolean
  selectedKeys?: string[]
}

const props = withDefaults(defineProps<Props>(), {
  title: 'Select',
  multiSelect: false,
  selectedKeys: () => [],
})

const emit = defineEmits<{
  'update:open': [value: boolean]
  select: [keys: string[]]
}>()

const searchQuery = ref('')
const draftSelection = ref<Set<string>>(new Set())

// Initialize selection when dialog opens
watch(
  () => props.open,
  (isOpen) => {
    if (isOpen) {
      searchQuery.value = ''
      draftSelection.value = new Set(props.selectedKeys)
    }
  }
)

// Filtered items
const filteredItems = computed(() => {
  const query = searchQuery.value.toLowerCase().trim()
  if (!query) return props.items
  return props.items.filter(
    (item) =>
      item.label.toLowerCase().includes(query) ||
      item.description?.toLowerCase().includes(query)
  )
})

function isSelected(key: string): boolean {
  return draftSelection.value.has(key)
}

function toggleItem(key: string): void {
  if (props.multiSelect) {
    const next = new Set(draftSelection.value)
    if (next.has(key)) {
      next.delete(key)
    } else {
      next.add(key)
    }
    draftSelection.value = next
  } else {
    draftSelection.value = new Set([key])
  }
}

function handleConfirm(): void {
  emit('select', Array.from(draftSelection.value))
  emit('update:open', false)
}

function handleCancel(): void {
  emit('update:open', false)
}

function onOpenChange(value: boolean): void {
  if (!value) {
    emit('update:open', false)
  }
}

const hasSelection = computed(() => draftSelection.value.size > 0)
</script>

<template>
  <DialogRoot :open="open" @update:open="onOpenChange">
    <DialogPortal>
      <DialogOverlay
        class="fixed inset-0 z-50 bg-black/50 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0"
      />
      <DialogContent
        class="fixed left-1/2 top-1/2 z-50 w-full max-w-md -translate-x-1/2 -translate-y-1/2 rounded-lg border bg-background shadow-lg duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[state=closed]:slide-out-to-left-1/2 data-[state=closed]:slide-out-to-top-[48%] data-[state=open]:slide-in-from-left-1/2 data-[state=open]:slide-in-from-top-[48%] flex flex-col"
        style="max-height: 70vh"
      >
        <!-- Header -->
        <div class="flex items-center justify-between px-6 pt-6 pb-4 border-b shrink-0">
          <div>
            <DialogTitle class="text-lg font-semibold text-foreground">
              {{ title }}
            </DialogTitle>
            <DialogDescription class="text-sm text-muted-foreground mt-0.5">
              {{ multiSelect ? 'Select one or more items' : 'Select an item' }}
            </DialogDescription>
          </div>
          <button
            class="rounded-sm opacity-70 ring-offset-background transition-opacity hover:opacity-100 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
            @click="handleCancel"
          >
            <X class="h-4 w-4" />
            <span class="sr-only">Close</span>
          </button>
        </div>

        <!-- Search -->
        <div class="px-6 pt-4 shrink-0">
          <div class="relative">
            <Search class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
            <Input
              v-model="searchQuery"
              placeholder="Search..."
              class="pl-9"
            />
          </div>
        </div>

        <!-- Item List -->
        <div class="flex-1 overflow-y-auto px-6 py-3">
          <div v-if="filteredItems.length === 0" class="text-sm text-muted-foreground text-center py-8">
            No items match your search.
          </div>

          <div class="space-y-1">
            <div
              v-for="item in filteredItems"
              :key="item.key"
              :class="cn(
                'flex items-center gap-3 rounded-md px-3 py-2.5 cursor-pointer transition-colors',
                isSelected(item.key)
                  ? 'bg-primary/5 border border-primary/20'
                  : 'hover:bg-muted/50 border border-transparent'
              )"
              @click="toggleItem(item.key)"
            >
              <input
                v-if="multiSelect"
                type="checkbox"
                :checked="isSelected(item.key)"
                class="h-4 w-4 rounded border-input text-primary focus:ring-primary shrink-0"
                @click.stop
                @change="toggleItem(item.key)"
              />
              <input
                v-else
                type="radio"
                :checked="isSelected(item.key)"
                name="select-dialog-radio"
                class="h-4 w-4 border-input text-primary focus:ring-primary shrink-0"
                @click.stop
                @change="toggleItem(item.key)"
              />
              <div class="min-w-0 flex-1">
                <div class="text-sm font-medium text-foreground truncate">
                  {{ item.label }}
                </div>
                <div
                  v-if="item.description"
                  class="text-xs text-muted-foreground truncate mt-0.5"
                >
                  {{ item.description }}
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- Footer -->
        <div class="flex items-center justify-between px-6 py-4 border-t shrink-0">
          <span class="text-sm text-muted-foreground">
            {{ draftSelection.size }} selected
          </span>
          <div class="flex gap-2">
            <Button variant="outline" @click="handleCancel">
              Cancel
            </Button>
            <Button :disabled="!hasSelection" @click="handleConfirm">
              Confirm
            </Button>
          </div>
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
