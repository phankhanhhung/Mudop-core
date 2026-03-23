<script setup lang="ts">
import { Button } from '@/components/ui/button'
import { Trash2, Download, X } from 'lucide-vue-next'

interface Props {
  selectedCount: number
}

defineProps<Props>()

defineEmits<{
  'delete-selected': []
  'export-selected': []
  'deselect-all': []
}>()
</script>

<template>
  <Transition
    enter-active-class="transition-all duration-300 ease-out"
    enter-from-class="opacity-0 -translate-y-2"
    enter-to-class="opacity-100 translate-y-0"
    leave-active-class="transition-all duration-200 ease-in"
    leave-from-class="opacity-100 translate-y-0"
    leave-to-class="opacity-0 -translate-y-2"
  >
    <div
      v-if="selectedCount > 0"
      class="sticky top-0 z-10 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-2 sm:gap-4 rounded-md border border-primary/20 bg-primary/5 px-3 sm:px-4 py-2 sm:py-2.5"
    >
      <span class="text-sm font-medium">
        {{ selectedCount }} selected
      </span>

      <div class="flex items-center gap-1.5 sm:gap-2 flex-wrap">
        <Button
          variant="destructive"
          size="sm"
          @click="$emit('delete-selected')"
        >
          <Trash2 class="mr-1 sm:mr-1.5 h-4 w-4" />
          <span class="hidden sm:inline">Delete Selected</span>
          <span class="sm:hidden">Delete</span>
        </Button>

        <Button
          variant="outline"
          size="sm"
          class="hidden sm:inline-flex"
          @click="$emit('export-selected')"
        >
          <Download class="mr-1.5 h-4 w-4" />
          Export Selected
        </Button>

        <Button
          variant="ghost"
          size="sm"
          @click="$emit('deselect-all')"
        >
          <X class="mr-1 sm:mr-1.5 h-4 w-4" />
          <span class="hidden sm:inline">Deselect All</span>
          <span class="sm:hidden">Clear</span>
        </Button>
      </div>
    </div>
  </Transition>
</template>
