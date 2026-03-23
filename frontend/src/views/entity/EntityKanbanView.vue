<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useMetadata } from '@/composables/useMetadata'
import { useKanban } from '@/composables/useKanban'
import KanbanBoard from '@/components/entity/KanbanBoard.vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { useI18n } from 'vue-i18n'
import { List } from 'lucide-vue-next'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()

const module = computed(() => route.params.module as string)
const entity = computed(() => route.params.entity as string)

const {
  metadata,
  isLoading: metadataLoading,
  fields,
} = useMetadata({
  module: module.value,
  entity: entity.value,
  autoLoad: true,
})

// Enum field detection
const enumFields = computed(() =>
  fields.value.filter((f) => f.type === 'Enum' && (f.enumValues?.length ?? 0) > 0),
)

const selectedStatusFieldName = ref('')
const statusField = computed(
  () =>
    enumFields.value.find((f) => f.name === selectedStatusFieldName.value) ??
    enumFields.value[0] ??
    null,
)

const keyField = computed(() => metadata.value?.keys?.[0] ?? 'ID')

const titleField = ref('')
const subtitleField = ref('')
const searchQuery = ref('')

// Auto-select first enum field as status field
watch(
  enumFields,
  (newFields) => {
    if (newFields.length && !selectedStatusFieldName.value) {
      selectedStatusFieldName.value = newFields[0].name
    }
  },
  { immediate: true },
)

// Auto-select first non-key string field as title field
watch(
  fields,
  (newFields) => {
    if (!titleField.value && newFields.length) {
      const keyNames = metadata.value?.keys ?? ['ID']
      const candidate = newFields.find(
        (f) => !keyNames.includes(f.name) && ['String', 'Text'].includes(f.type),
      )
      titleField.value = candidate?.name ?? newFields[1]?.name ?? newFields[0]?.name ?? ''
    }
  },
  { immediate: true },
)

const { filteredColumns, isLoading: kanbanLoading, error: kanbanError, loadCards, moveCard } =
  useKanban({
    module,
    entity,
    statusField,
    titleField,
    subtitleField,
    searchQuery,
    keyField,
  })

// Reload cards when status field changes
watch(
  statusField,
  (field) => {
    if (field) loadCards()
  },
  { immediate: true },
)

// String fields for title selector
const stringFields = computed(() =>
  fields.value.filter((f) => ['String', 'Text'].includes(f.type)),
)

function goToList() {
  router.push(`/odata/${module.value}/${entity.value}`)
}
</script>

<template>
  <DefaultLayout>
    <div class="h-full flex flex-col">
      <!-- Header toolbar -->
      <div class="flex items-center gap-3 px-4 py-3 border-b border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-900 flex-wrap">
        <h1 class="text-lg font-semibold text-gray-900 dark:text-gray-100">
          {{ entity }} &ndash; {{ t('kanban.title') }}
        </h1>
        <div class="flex-1" />

        <!-- Search input -->
        <input
          v-model="searchQuery"
          type="search"
          :placeholder="t('kanban.search')"
          class="px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500 w-48"
        />

        <!-- Status field selector (only when >1 enum field) -->
        <template v-if="enumFields.length > 1">
          <label class="text-sm text-gray-600 dark:text-gray-400">{{ t('kanban.statusField') }}:</label>
          <select
            v-model="selectedStatusFieldName"
            class="px-2 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500"
          >
            <option v-for="f in enumFields" :key="f.name" :value="f.name">
              {{ f.displayName ?? f.name }}
            </option>
          </select>
        </template>

        <!-- Title field selector -->
        <template v-if="stringFields.length > 0">
          <label class="text-sm text-gray-600 dark:text-gray-400">{{ t('kanban.titleField') }}:</label>
          <select
            v-model="titleField"
            class="px-2 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500"
          >
            <option v-for="f in stringFields" :key="f.name" :value="f.name">
              {{ f.displayName ?? f.name }}
            </option>
          </select>
        </template>

        <!-- List view button -->
        <button
          @click="goToList"
          class="inline-flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-200 hover:bg-gray-50 dark:hover:bg-gray-700 focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          <List class="w-4 h-4" />
          {{ t('kanban.listView') }}
        </button>
      </div>

      <!-- Loading state -->
      <div
        v-if="metadataLoading || (kanbanLoading && filteredColumns.length === 0)"
        class="flex-1 flex items-center justify-center"
      >
        <div class="flex flex-col items-center gap-3 text-gray-500 dark:text-gray-400">
          <svg
            class="animate-spin w-8 h-8 text-blue-500"
            xmlns="http://www.w3.org/2000/svg"
            fill="none"
            viewBox="0 0 24 24"
          >
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
          </svg>
          <span class="text-sm">{{ t('common.loading') }}</span>
        </div>
      </div>

      <!-- No enum fields message -->
      <div
        v-else-if="enumFields.length === 0"
        class="flex-1 flex items-center justify-center"
      >
        <div class="text-center max-w-sm">
          <p class="text-gray-500 dark:text-gray-400 text-sm">{{ t('kanban.noEnumFields') }}</p>
        </div>
      </div>

      <!-- Error state -->
      <div
        v-else-if="kanbanError"
        class="flex-1 flex items-center justify-center"
      >
        <div class="text-center max-w-sm">
          <p class="text-red-500 text-sm">{{ t('kanban.loadError') }}: {{ kanbanError }}</p>
        </div>
      </div>

      <!-- Kanban board -->
      <div v-else class="flex-1 overflow-hidden p-4">
        <KanbanBoard
          :columns="filteredColumns"
          :title-field="titleField"
          :subtitle-field="subtitleField"
          :module="module"
          :entity="entity"
          :key-field="keyField"
          :is-loading="kanbanLoading"
          @move="moveCard"
        />
      </div>
    </div>
  </DefaultLayout>
</template>
