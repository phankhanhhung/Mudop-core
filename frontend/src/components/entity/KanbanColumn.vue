<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount } from 'vue'
import Sortable from 'sortablejs'
import KanbanCard from './KanbanCard.vue'

interface KanbanCardItem {
  id: string | number
  data: Record<string, unknown>
}

interface ColumnData {
  value: string | number
  label: string
  cards: KanbanCardItem[]
  isLoading: boolean
}

interface Props {
  column: ColumnData
  titleField: string
  subtitleField?: string
  module: string
  entity: string
  keyField: string
}

const props = defineProps<Props>()

const emit = defineEmits<{
  move: [cardId: string | number, fromColValue: string | number, toColValue: string | number]
}>()

const cardsContainerRef = ref<HTMLElement | null>(null)
let sortable: Sortable | null = null

onMounted(() => {
  if (!cardsContainerRef.value) return

  sortable = Sortable.create(cardsContainerRef.value, {
    group: 'kanban-cards',
    handle: '.kanban-card-handle',
    animation: 150,
    ghostClass: 'opacity-40',
    onEnd(evt) {
      const cardId = evt.item.dataset.id
      const fromColValue = props.column.value
      const toColEl = evt.to.closest('[data-col-value]') as HTMLElement | null
      const toColValue = toColEl?.dataset.colValue ?? String(props.column.value)

      // Revert DOM change — Vue will re-render from reactive data
      if (evt.to !== evt.from) {
        evt.from.appendChild(evt.item)
      } else {
        // Revert reorder within the same column — we do not support in-column reorder
        const children = Array.from(evt.from.children)
        const [moved] = children.splice(evt.newIndex!, 1)
        children.splice(evt.oldIndex!, 0, moved)
        while (evt.from.firstChild) {
          evt.from.removeChild(evt.from.firstChild)
        }
        children.forEach((c) => evt.from.appendChild(c))
      }

      if (cardId != null && String(fromColValue) !== String(toColValue)) {
        emit('move', cardId, fromColValue, toColValue)
      }
    },
  })
})

onBeforeUnmount(() => {
  sortable?.destroy()
  sortable = null
})
</script>

<template>
  <div
    class="bg-gray-100 dark:bg-gray-700/50 rounded-xl p-3 w-72 flex-shrink-0 flex flex-col"
    :data-col-value="column.value"
  >
    <!-- Column header -->
    <div class="flex items-center justify-between mb-3">
      <span class="font-semibold text-sm text-gray-800 dark:text-gray-200 truncate mr-2">
        {{ column.label }}
      </span>
      <span
        class="inline-flex items-center justify-center rounded-full bg-gray-200 dark:bg-gray-600 text-gray-700 dark:text-gray-300 text-xs font-medium px-2 py-0.5 flex-shrink-0"
      >
        {{ column.cards.length }}
      </span>
    </div>

    <!-- Cards container (SortableJS target) -->
    <div ref="cardsContainerRef" class="flex-1 min-h-[4rem]">
      <!-- Loading shimmer -->
      <template v-if="column.isLoading">
        <div
          v-for="i in 3"
          :key="i"
          class="bg-white dark:bg-gray-800 rounded-lg shadow p-3 mb-2 animate-pulse"
        >
          <div class="h-3 bg-gray-200 dark:bg-gray-600 rounded w-3/4 mb-2"></div>
          <div class="h-2 bg-gray-200 dark:bg-gray-600 rounded w-1/2"></div>
        </div>
      </template>

      <template v-else>
        <KanbanCard
          v-for="card in column.cards"
          :key="card.id"
          :card="card"
          :title-field="titleField"
          :subtitle-field="subtitleField"
          :module="module"
          :entity="entity"
          :key-field="keyField"
        />

        <div
          v-if="column.cards.length === 0"
          class="text-xs text-gray-400 dark:text-gray-500 text-center py-4 italic"
        >
          No items
        </div>
      </template>
    </div>
  </div>
</template>
