<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import { Label } from '@/components/ui/label'
import { Badge } from '@/components/ui/badge'
import { X, ChevronsUpDown } from 'lucide-vue-next'
import { PopoverRoot, PopoverTrigger, PopoverContent, PopoverPortal } from 'radix-vue'
import type { FieldMetadata } from '@/types/metadata'

interface Props {
  field: FieldMetadata
  modelValue?: (string | number)[]
  readonly?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  readonly: false,
  modelValue: () => []
})

const emit = defineEmits<{
  'update:modelValue': [value: (string | number)[]]
}>()

const isOpen = ref(false)
const searchQuery = ref('')
const highlightedIndex = ref(-1)
const searchInputRef = ref<HTMLInputElement | null>(null)
const listRef = ref<HTMLDivElement | null>(null)

const options = computed(() => {
  return (props.field.enumValues ?? []).map((e) => ({
    value: e.value,
    label: e.displayName || e.name
  }))
})

const filteredOptions = computed(() => {
  const q = searchQuery.value.toLowerCase()
  if (!q) return options.value
  return options.value.filter((o) => o.label.toLowerCase().includes(q))
})

const selectedSet = computed(() => new Set(props.modelValue.map(String)))

function isSelected(value: string | number): boolean {
  return selectedSet.value.has(String(value))
}

function toggleOption(value: string | number) {
  const current = [...props.modelValue]
  const strVal = String(value)
  const idx = current.findIndex((v) => String(v) === strVal)
  if (idx >= 0) {
    current.splice(idx, 1)
  } else {
    current.push(value)
  }
  emit('update:modelValue', current)
}

function removeTag(value: string | number) {
  const current = props.modelValue.filter((v) => String(v) !== String(value))
  emit('update:modelValue', current)
}

function getLabel(value: string | number): string {
  const opt = options.value.find((o) => String(o.value) === String(value))
  return opt?.label ?? String(value)
}

function handleTriggerKeydown(e: KeyboardEvent) {
  if (props.readonly || props.field.isReadOnly) return
  if (e.key === 'ArrowDown' && !isOpen.value) {
    e.preventDefault()
    isOpen.value = true
  }
}

function handleSearchKeydown(e: KeyboardEvent) {
  const opts = filteredOptions.value
  if (e.key === 'ArrowDown') {
    e.preventDefault()
    highlightedIndex.value = Math.min(highlightedIndex.value + 1, opts.length - 1)
    scrollToHighlighted()
  } else if (e.key === 'ArrowUp') {
    e.preventDefault()
    highlightedIndex.value = Math.max(highlightedIndex.value - 1, 0)
    scrollToHighlighted()
  } else if (e.key === 'Enter' || e.key === ' ') {
    e.preventDefault()
    if (highlightedIndex.value >= 0 && highlightedIndex.value < opts.length) {
      toggleOption(opts[highlightedIndex.value].value)
    }
  } else if (e.key === 'Backspace' && searchQuery.value === '' && props.modelValue.length > 0) {
    removeTag(props.modelValue[props.modelValue.length - 1])
  } else if (e.key === 'Escape') {
    isOpen.value = false
  }
}

function scrollToHighlighted() {
  nextTick(() => {
    if (!listRef.value) return
    const items = listRef.value.querySelectorAll('[data-option]')
    const item = items[highlightedIndex.value] as HTMLElement | undefined
    item?.scrollIntoView({ block: 'nearest' })
  })
}

watch(isOpen, (open) => {
  if (open) {
    searchQuery.value = ''
    highlightedIndex.value = -1
    nextTick(() => {
      searchInputRef.value?.focus()
    })
  }
})

watch(searchQuery, () => {
  highlightedIndex.value = -1
})
</script>

<template>
  <div class="space-y-2">
    <Label :for="field.name">
      {{ field.displayName || field.name }}
      <span v-if="field.isRequired" class="text-destructive">*</span>
    </Label>

    <!-- Readonly display -->
    <div v-if="readonly || field.isReadOnly" class="flex flex-wrap gap-1 min-h-[2.5rem] items-center">
      <Badge
        v-for="val in modelValue"
        :key="String(val)"
        variant="secondary"
      >
        {{ getLabel(val) }}
      </Badge>
      <span v-if="modelValue.length === 0" class="text-sm text-muted-foreground">No values selected</span>
    </div>

    <!-- Editable combo box -->
    <PopoverRoot v-else v-model:open="isOpen">
      <PopoverTrigger as-child>
        <button
          type="button"
          :id="field.name"
          class="flex min-h-[2.5rem] w-full flex-wrap items-center gap-1 rounded-md border border-input bg-background px-3 py-1.5 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
          @keydown="handleTriggerKeydown"
        >
          <Badge
            v-for="val in modelValue"
            :key="String(val)"
            variant="secondary"
            class="gap-1 pr-1"
          >
            {{ getLabel(val) }}
            <span
              role="button"
              tabindex="-1"
              class="rounded-full hover:bg-muted-foreground/20 p-0.5 cursor-pointer"
              @click.stop="removeTag(val)"
              @mousedown.prevent
            >
              <X class="h-3 w-3" />
            </span>
          </Badge>
          <span
            v-if="modelValue.length === 0"
            class="text-muted-foreground"
          >
            Select...
          </span>
          <ChevronsUpDown class="ml-auto h-4 w-4 shrink-0 opacity-50" />
        </button>
      </PopoverTrigger>

      <PopoverPortal>
        <PopoverContent
          align="start"
          :side-offset="4"
          class="w-[--radix-popover-trigger-width] p-0 rounded-md border bg-popover shadow-md"
        >
          <!-- Search input -->
          <div class="p-2 border-b">
            <input
              ref="searchInputRef"
              v-model="searchQuery"
              type="text"
              placeholder="Search..."
              class="flex h-8 w-full rounded-md border border-input bg-background px-3 py-1 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
              @keydown="handleSearchKeydown"
            />
          </div>

          <!-- Options list -->
          <div ref="listRef" class="max-h-60 overflow-y-auto p-1">
            <div
              v-if="filteredOptions.length === 0"
              class="py-4 text-center text-sm text-muted-foreground"
            >
              No options found
            </div>

            <button
              v-for="(option, idx) in filteredOptions"
              :key="String(option.value)"
              type="button"
              data-option
              class="relative flex w-full cursor-pointer select-none items-center rounded-sm px-2 py-1.5 text-sm outline-none hover:bg-accent hover:text-accent-foreground"
              :class="{ 'bg-accent text-accent-foreground': idx === highlightedIndex }"
              @click="toggleOption(option.value)"
              @mouseenter="highlightedIndex = idx"
            >
              <!-- Simple styled checkbox -->
              <span
                class="mr-2 flex h-4 w-4 shrink-0 items-center justify-center rounded-sm border border-primary"
                :class="isSelected(option.value) ? 'bg-primary text-primary-foreground' : 'bg-background'"
              >
                <svg
                  v-if="isSelected(option.value)"
                  class="h-3 w-3"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="3"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                >
                  <polyline points="20 6 9 17 4 12" />
                </svg>
              </span>
              <span class="truncate">{{ option.label }}</span>
            </button>
          </div>
        </PopoverContent>
      </PopoverPortal>
    </PopoverRoot>

    <p v-if="field.description" class="text-xs text-muted-foreground">
      {{ field.description }}
    </p>
  </div>
</template>
