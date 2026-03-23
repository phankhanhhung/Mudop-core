<script setup lang="ts">
import KanbanColumn from './KanbanColumn.vue'

interface KanbanCardItem {
  id: string | number
  data: Record<string, unknown>
}

interface KanbanColumnData {
  value: string | number
  label: string
  cards: KanbanCardItem[]
  isLoading: boolean
}

interface Props {
  columns: KanbanColumnData[]
  titleField: string
  subtitleField?: string
  module: string
  entity: string
  keyField: string
  isLoading?: boolean
}

withDefaults(defineProps<Props>(), {
  isLoading: false,
})

const emit = defineEmits<{
  move: [cardId: string | number, fromColValue: string | number, toColValue: string | number]
}>()

function onMove(cardId: string | number, fromColValue: string | number, toColValue: string | number) {
  emit('move', cardId, fromColValue, toColValue)
}
</script>

<template>
  <div class="relative overflow-x-auto pb-4">
    <!-- Global loading overlay -->
    <div
      v-if="isLoading && columns.length === 0"
      class="flex items-center justify-center py-16"
    >
      <div class="flex flex-col items-center gap-3 text-gray-500 dark:text-gray-400">
        <svg
          class="animate-spin w-8 h-8 text-blue-500"
          xmlns="http://www.w3.org/2000/svg"
          fill="none"
          viewBox="0 0 24 24"
          aria-hidden="true"
        >
          <circle
            class="opacity-25"
            cx="12"
            cy="12"
            r="10"
            stroke="currentColor"
            stroke-width="4"
          />
          <path
            class="opacity-75"
            fill="currentColor"
            d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"
          />
        </svg>
        <span class="text-sm">Loading board...</span>
      </div>
    </div>

    <!-- Board columns -->
    <div v-else class="flex gap-4 min-w-max">
      <KanbanColumn
        v-for="col in columns"
        :key="col.value"
        :column="col"
        :title-field="titleField"
        :subtitle-field="subtitleField"
        :module="module"
        :entity="entity"
        :key-field="keyField"
        @move="onMove"
      />

      <div
        v-if="columns.length === 0"
        class="flex items-center justify-center w-full py-16 text-sm text-gray-400 dark:text-gray-500 italic"
      >
        No columns configured for this board view.
      </div>
    </div>
  </div>
</template>
