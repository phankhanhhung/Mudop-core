<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ArrowLeft, Loader2, AlertCircle } from 'lucide-vue-next'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import ChatQueryInput from '@/components/entity/ChatQueryInput.vue'
import { useMetadata } from '@/composables/useMetadata'
import { useSavedViews } from '@/composables/useSavedViews'
import { useUiStore } from '@/stores/ui'
import { odataService } from '@/services/odataService'
import { nlQueryService } from '@/services/nlQueryService'
import type { NlQueryMessage, NlQueryResult } from '@/services/nlQueryService'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const uiStore = useUiStore()

const module = computed(() => route.params.module as string)
const entity = computed(() => route.params.entity as string)

const { metadata, fields, isLoading: metadataLoading } = useMetadata({
  module: module.value,
  entity: entity.value,
  autoLoad: true,
})

const entityKey = computed(() => `${module.value}/${entity.value}`)
const { saveView } = useSavedViews({ entityKey })

// Chat state
const messages = ref<NlQueryMessage[]>([])
const chatLoading = ref(false)

// Query results state
const queryLoading = ref(false)
const queryError = ref<string | null>(null)
const queryResults = ref<Record<string, unknown>[]>([])
const activeQuery = ref<NlQueryResult | null>(null)
const totalCount = ref<number | null>(null)

// Pin dialog state
const showPinDialog = ref(false)
const pinName = ref('')
const pinPendingQuery = ref<NlQueryResult | null>(null)

// Column headers from metadata fields (limit to reasonable columns)
const tableColumns = computed(() => {
  if (!fields.value.length) return []
  // Show first 8 non-system fields
  return fields.value
    .filter((f) => !['TenantId', 'CreatedAt', 'ModifiedAt', 'SystemStart', 'SystemEnd'].includes(f.name))
    .slice(0, 8)
})

async function handleSend(text: string) {
  if (!metadata.value) return

  const userMessage: NlQueryMessage = {
    id: crypto.randomUUID(),
    role: 'user',
    content: text,
    timestamp: Date.now(),
  }
  messages.value = [...messages.value, userMessage]
  chatLoading.value = true

  try {
    const result = await nlQueryService.translate(text, metadata.value, module.value, messages.value)

    const assistantMessage: NlQueryMessage = {
      id: crypto.randomUUID(),
      role: 'assistant',
      content: result.description,
      query: result,
      timestamp: Date.now(),
    }
    messages.value = [...messages.value, assistantMessage]

    // Auto-run if there's a filter or expand clause
    if (result.filter || result.expand) {
      await runQuery(result)
    }
  } catch {
    const errorMessage: NlQueryMessage = {
      id: crypto.randomUUID(),
      role: 'assistant',
      content: t('nlq.translateError'),
      timestamp: Date.now(),
    }
    messages.value = [...messages.value, errorMessage]
  } finally {
    chatLoading.value = false
  }
}

async function runQuery(result: NlQueryResult) {
  activeQuery.value = result
  queryLoading.value = true
  queryError.value = null

  try {
    const response = await odataService.query<Record<string, unknown>>(
      module.value,
      entity.value,
      {
        $filter: result.filter,
        $expand: result.expand,
        $select: result.select,
        $orderby: result.orderby,
        $top: 50,
        $count: true,
      },
      { skipCache: true },
    )
    queryResults.value = response.value
    totalCount.value = (response as unknown as Record<string, unknown>)['@odata.count'] as number ?? null
  } catch {
    queryError.value = t('nlq.queryError')
    queryResults.value = []
  } finally {
    queryLoading.value = false
  }
}

function openPinDialog(result: NlQueryResult) {
  pinPendingQuery.value = result
  pinName.value = result.description.slice(0, 60)
  showPinDialog.value = true
}

async function confirmPin() {
  if (!pinPendingQuery.value || !pinName.value.trim()) return
  try {
    await saveView(pinName.value.trim(), {
      filters: [],
      sort: [],
      pageSize: 50,
      search: pinPendingQuery.value.filter ?? '',
    })
    uiStore.success(t('nlq.pinnedSuccess'))
  } catch {
    uiStore.error(t('nlq.pinnedError'))
  } finally {
    showPinDialog.value = false
    pinPendingQuery.value = null
    pinName.value = ''
  }
}

function cancelPin() {
  showPinDialog.value = false
  pinPendingQuery.value = null
  pinName.value = ''
}

function goBack() {
  router.push({ name: 'entity-list', params: { module: module.value, entity: entity.value } })
}

function getCellValue(row: Record<string, unknown>, fieldName: string): string {
  const val = row[fieldName]
  if (val === null || val === undefined) return '—'
  if (typeof val === 'object') return JSON.stringify(val)
  return String(val)
}
</script>

<template>
  <DefaultLayout>
    <div class="flex flex-col h-full">
      <!-- Header -->
      <div class="flex items-center gap-3 px-6 py-4 border-b border-gray-200 dark:border-gray-700 flex-shrink-0">
        <button
          class="p-1.5 hover:bg-gray-100 dark:hover:bg-gray-800 rounded-lg transition-colors"
          @click="goBack"
        >
          <ArrowLeft class="h-4 w-4 text-gray-500" />
        </button>
        <div>
          <h1 class="text-lg font-semibold text-gray-900 dark:text-gray-100">
            {{ t('nlq.title') }} — {{ entity }}
          </h1>
          <p class="text-xs text-gray-500">{{ t('nlq.subtitle') }}</p>
        </div>
      </div>

      <!-- Main content: chat + results split -->
      <div class="flex flex-1 min-h-0">
        <!-- Chat panel -->
        <div class="w-96 flex-shrink-0 border-r border-gray-200 dark:border-gray-700 flex flex-col">
          <div v-if="metadataLoading" class="flex items-center justify-center p-8 text-gray-400">
            <Loader2 class="h-5 w-5 animate-spin mr-2" />
            {{ t('common.loading') }}
          </div>
          <ChatQueryInput
            v-else
            :messages="messages"
            :loading="chatLoading"
            :entity-type="entity"
            class="flex-1"
            @send="handleSend"
            @run-query="runQuery"
            @pin-query="openPinDialog"
          />
        </div>

        <!-- Results panel -->
        <div class="flex-1 flex flex-col min-w-0 overflow-hidden">
          <!-- Active query banner -->
          <div
            v-if="activeQuery"
            class="px-4 py-2 bg-blue-50 dark:bg-blue-900/20 border-b border-blue-100 dark:border-blue-800 text-xs text-blue-700 dark:text-blue-300 flex-shrink-0"
          >
            <span class="font-medium">{{ t('nlq.activeQuery') }}:</span>
            <span v-if="activeQuery.filter" class="ml-2 font-mono">$filter={{ activeQuery.filter }}</span>
            <span v-if="activeQuery.expand" class="ml-2 font-mono">$expand={{ activeQuery.expand }}</span>
            <span v-if="activeQuery.orderby" class="ml-2 font-mono">$orderby={{ activeQuery.orderby }}</span>
            <span v-if="totalCount !== null" class="ml-3 text-blue-600 dark:text-blue-400">
              ({{ t('nlq.resultCount', { count: totalCount }) }})
            </span>
          </div>

          <!-- Results content -->
          <div class="flex-1 overflow-auto p-4">
            <!-- Loading -->
            <div v-if="queryLoading" class="flex items-center justify-center py-12 text-gray-400">
              <Loader2 class="h-6 w-6 animate-spin mr-2" />
              {{ t('nlq.runningQuery') }}
            </div>

            <!-- Error -->
            <div
              v-else-if="queryError"
              class="flex items-center gap-2 p-4 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-xl text-red-700 dark:text-red-400 text-sm"
            >
              <AlertCircle class="h-4 w-4 flex-shrink-0" />
              {{ queryError }}
            </div>

            <!-- Empty state (before any query) -->
            <div v-else-if="!activeQuery" class="flex flex-col items-center justify-center py-24 text-gray-400">
              <div class="text-5xl mb-4">🔍</div>
              <p class="text-sm">{{ t('nlq.noQueryYet') }}</p>
            </div>

            <!-- Empty results -->
            <div
              v-else-if="queryResults.length === 0"
              class="flex flex-col items-center justify-center py-12 text-gray-400"
            >
              <p class="text-sm">{{ t('common.noData') }}</p>
            </div>

            <!-- Results table -->
            <div v-else class="overflow-x-auto">
              <table class="min-w-full text-sm border-collapse">
                <thead>
                  <tr class="border-b border-gray-200 dark:border-gray-700">
                    <th
                      v-for="col in tableColumns"
                      :key="col.name"
                      class="text-left px-3 py-2 font-medium text-gray-600 dark:text-gray-400 whitespace-nowrap"
                    >
                      {{ col.displayName ?? col.name }}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="(row, i) in queryResults"
                    :key="i"
                    class="border-b border-gray-100 dark:border-gray-800 hover:bg-gray-50 dark:hover:bg-gray-800/50"
                  >
                    <td
                      v-for="col in tableColumns"
                      :key="col.name"
                      class="px-3 py-2 text-gray-800 dark:text-gray-200 truncate max-w-[200px]"
                      :title="getCellValue(row, col.name)"
                    >
                      {{ getCellValue(row, col.name) }}
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Pin dialog -->
    <Teleport to="body">
      <div
        v-if="showPinDialog"
        class="fixed inset-0 bg-black/50 flex items-center justify-center z-50"
        @click.self="cancelPin"
      >
        <div class="bg-white dark:bg-gray-900 rounded-2xl shadow-xl p-6 w-96 max-w-[90vw]">
          <h3 class="text-base font-semibold text-gray-900 dark:text-gray-100 mb-4">
            {{ t('nlq.pinDialogTitle') }}
          </h3>
          <input
            v-model="pinName"
            type="text"
            :placeholder="t('nlq.pinNamePlaceholder')"
            class="w-full border border-gray-300 dark:border-gray-600 rounded-xl px-3 py-2 text-sm bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500 mb-4"
            @keydown.enter="confirmPin"
          />
          <div class="flex justify-end gap-2">
            <button
              class="px-4 py-2 text-sm text-gray-600 dark:text-gray-400 hover:bg-gray-100 dark:hover:bg-gray-800 rounded-xl transition-colors"
              @click="cancelPin"
            >
              {{ t('common.cancel') }}
            </button>
            <button
              :disabled="!pinName.trim()"
              class="px-4 py-2 text-sm bg-blue-600 hover:bg-blue-700 disabled:bg-gray-300 disabled:cursor-not-allowed text-white rounded-xl transition-colors"
              @click="confirmPin"
            >
              {{ t('nlq.pin') }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>
  </DefaultLayout>
</template>
