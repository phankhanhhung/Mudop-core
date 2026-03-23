<script setup lang="ts">
import { computed } from 'vue'
import { PopoverRoot, PopoverTrigger, PopoverContent, PopoverPortal } from 'radix-vue'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Checkbox } from '@/components/ui/checkbox'
import { Columns3 } from 'lucide-vue-next'
import type { ColumnConfig } from '@/composables/useColumnConfig'

interface Props {
  columns: ColumnConfig[]
  totalFields: number
}

const props = defineProps<Props>()

const emit = defineEmits<{
  toggle: [field: string]
  'show-all': []
  'hide-all': []
  reset: []
}>()

const visibleCount = computed(() => props.columns.filter((c) => c.visible).length)

function displayName(field: string): string {
  // Convert PascalCase/camelCase to spaced form for display
  return field.replace(/([A-Z])/g, ' $1').trim()
}
</script>

<template>
  <PopoverRoot>
    <PopoverTrigger as-child>
      <Button variant="outline" size="sm" class="gap-1.5" aria-label="Column visibility">
        <Columns3 class="h-4 w-4" />
        Columns
        <Badge variant="secondary" class="ml-0.5 px-1.5 py-0 text-xs">
          {{ visibleCount }}/{{ totalFields }}
        </Badge>
      </Button>
    </PopoverTrigger>
    <PopoverPortal>
      <PopoverContent
        :side-offset="4"
        align="end"
        class="z-50 w-56 rounded-md border bg-background p-0 shadow-md outline-none data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95"
      >
        <!-- Scrollable field list -->
        <div role="group" aria-label="Toggle column visibility" class="max-h-64 overflow-y-auto p-1">
          <button
            v-for="col in columns"
            :key="col.field"
            role="checkbox"
            :aria-checked="col.visible"
            class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent hover:text-accent-foreground cursor-pointer"
            @click="emit('toggle', col.field)"
          >
            <Checkbox
              :model-value="col.visible"
              class="pointer-events-none"
              aria-hidden="true"
            />
            <span class="truncate">{{ displayName(col.field) }}</span>
          </button>
        </div>

        <!-- Actions footer -->
        <div class="border-t p-1.5 flex items-center gap-1">
          <Button
            variant="ghost"
            size="sm"
            class="h-7 flex-1 text-xs"
            aria-label="Show all columns"
            @click="emit('show-all')"
          >
            Show All
          </Button>
          <Button
            variant="ghost"
            size="sm"
            class="h-7 flex-1 text-xs"
            aria-label="Hide all columns"
            @click="emit('hide-all')"
          >
            Hide All
          </Button>
          <Button
            variant="ghost"
            size="sm"
            class="h-7 flex-1 text-xs"
            aria-label="Reset column visibility to defaults"
            @click="emit('reset')"
          >
            Reset
          </Button>
        </div>
      </PopoverContent>
    </PopoverPortal>
  </PopoverRoot>
</template>
