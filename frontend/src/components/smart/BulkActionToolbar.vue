<script setup lang="ts">
import { ref } from 'vue'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Trash2, Download, ChevronDown, CheckSquare, XSquare } from 'lucide-vue-next'

interface Props {
  selectedCount: number
  totalCount: number
  enableExport?: boolean
}

withDefaults(defineProps<Props>(), {
  enableExport: true,
})

const emit = defineEmits<{
  'select-all': []
  'deselect-all': []
  'delete-selected': []
  'export-csv': []
  'export-xlsx': []
}>()

const showExportMenu = ref(false)

function toggleExportMenu() {
  showExportMenu.value = !showExportMenu.value
}

function handleExportCsv() {
  showExportMenu.value = false
  emit('export-csv')
}

function handleExportXlsx() {
  showExportMenu.value = false
  emit('export-xlsx')
}
</script>

<template>
  <Transition
    enter-active-class="transition-all duration-200 ease-out"
    leave-active-class="transition-all duration-150 ease-in"
    enter-from-class="translate-y-full opacity-0"
    enter-to-class="translate-y-0 opacity-100"
    leave-from-class="translate-y-0 opacity-100"
    leave-to-class="translate-y-full opacity-0"
  >
    <div
      v-if="selectedCount > 0"
      class="sticky bottom-0 z-20 flex items-center justify-between gap-4 rounded-md border-t bg-background px-4 py-2 shadow-[0_-2px_10px_rgba(0,0,0,0.1)]"
    >
      <!-- Left: selection info -->
      <div class="flex items-center gap-3">
        <Badge variant="secondary" class="font-medium">
          {{ selectedCount }} selected
        </Badge>
        <Button
          variant="ghost"
          size="sm"
          class="text-xs"
          @click="emit('select-all')"
        >
          <CheckSquare class="h-3.5 w-3.5 mr-1" />
          Select All ({{ totalCount }})
        </Button>
        <Button
          variant="ghost"
          size="sm"
          class="text-xs"
          @click="emit('deselect-all')"
        >
          <XSquare class="h-3.5 w-3.5 mr-1" />
          Deselect All
        </Button>
      </div>

      <!-- Right: actions -->
      <div class="flex items-center gap-2">
        <!-- Export dropdown -->
        <div v-if="enableExport" class="relative">
          <Button
            variant="outline"
            size="sm"
            @click="toggleExportMenu"
          >
            <Download class="h-3.5 w-3.5 mr-1" />
            Export
            <ChevronDown class="h-3 w-3 ml-1" />
          </Button>
          <div
            v-if="showExportMenu"
            class="absolute bottom-full right-0 mb-1 w-36 rounded-md border bg-popover py-1 shadow-md"
          >
            <button
              class="flex w-full items-center px-3 py-1.5 text-sm hover:bg-accent"
              @click="handleExportCsv"
            >
              Export as CSV
            </button>
            <button
              class="flex w-full items-center px-3 py-1.5 text-sm hover:bg-accent"
              @click="handleExportXlsx"
            >
              Export as Excel
            </button>
          </div>
        </div>

        <!-- Delete -->
        <Button
          variant="destructive"
          size="sm"
          @click="emit('delete-selected')"
        >
          <Trash2 class="h-3.5 w-3.5 mr-1" />
          Delete
        </Button>
      </div>
    </div>
  </Transition>
</template>
