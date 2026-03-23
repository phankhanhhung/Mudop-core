<script setup lang="ts">
import { useRouter } from 'vue-router'
import { GripVertical } from 'lucide-vue-next'

interface CardData {
  id: string | number
  data: Record<string, unknown>
}

interface Props {
  card: CardData
  titleField: string
  subtitleField?: string
  module: string
  entity: string
  keyField: string
}

const props = defineProps<Props>()

const router = useRouter()

function navigateToDetail() {
  router.push(`/odata/${props.module}/${props.entity}/${props.card.id}`)
}
</script>

<template>
  <div
    class="kanban-card bg-white dark:bg-gray-800 rounded-lg shadow p-3 mb-2 cursor-pointer hover:shadow-md transition-shadow select-none"
    :data-id="card.id"
    @click="navigateToDetail"
  >
    <div class="flex items-start gap-2">
      <span
        class="kanban-card-handle flex-shrink-0 mt-0.5 text-gray-400 dark:text-gray-500 cursor-grab active:cursor-grabbing"
        @click.stop
      >
        <GripVertical class="w-4 h-4" />
      </span>
      <div class="min-w-0 flex-1">
        <p class="text-sm font-medium text-gray-900 dark:text-gray-100 truncate">
          {{ card.data[titleField] ?? '(no title)' }}
        </p>
        <p
          v-if="subtitleField && card.data[subtitleField] != null"
          class="text-xs text-gray-500 dark:text-gray-400 mt-0.5 truncate"
        >
          {{ card.data[subtitleField] }}
        </p>
      </div>
    </div>
  </div>
</template>
