<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { RefreshCw, Trash2, Loader2, Inbox } from 'lucide-vue-next'
import type { OutboxEntry } from '@/services/integrationService'

interface Props {
  entries: OutboxEntry[]
  loading?: boolean
}

const props = defineProps<Props>()

const emit = defineEmits<{
  retry: [id: string]
  dismiss: [id: string]
  refresh: []
}>()

const { t } = useI18n()

type StatusKey = OutboxEntry['status']

const statusBadgeClass: Record<StatusKey, string> = {
  pending: 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900/30 dark:text-yellow-300',
  delivered: 'bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-300',
  dead_letter: 'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300'
}

function statusLabel(status: StatusKey): string {
  return t(`integration.outbox.status.${status}`, status)
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString()
}
</script>

<template>
  <div class="relative">
    <!-- Loading overlay -->
    <div
      v-if="props.loading"
      class="absolute inset-0 z-10 flex items-center justify-center bg-background/60 rounded-md"
    >
      <Loader2 class="h-6 w-6 animate-spin text-muted-foreground" />
    </div>

    <!-- Empty state -->
    <div
      v-if="!props.loading && props.entries.length === 0"
      class="flex flex-col items-center justify-center py-16 text-center"
    >
      <div class="h-14 w-14 rounded-full bg-muted flex items-center justify-center mb-4">
        <Inbox class="h-7 w-7 text-muted-foreground" />
      </div>
      <p class="text-sm font-medium text-foreground">{{ $t('integration.outbox.empty') }}</p>
      <p class="text-xs text-muted-foreground mt-1">{{ $t('integration.outbox.emptyDescription') }}</p>
      <button
        type="button"
        class="mt-4 inline-flex items-center gap-1.5 text-xs text-primary hover:underline focus:outline-none"
        @click="emit('refresh')"
      >
        <RefreshCw class="h-3 w-3" />
        {{ $t('common.refresh') }}
      </button>
    </div>

    <!-- Table -->
    <div v-else class="overflow-x-auto">
      <table class="min-w-full text-sm border-collapse">
        <thead>
          <tr class="border-b border-gray-200 dark:border-gray-700">
            <th class="text-left px-3 py-2 font-medium text-gray-600 dark:text-gray-400 whitespace-nowrap">
              {{ $t('integration.outbox.col.event') }}
            </th>
            <th class="text-left px-3 py-2 font-medium text-gray-600 dark:text-gray-400 whitespace-nowrap">
              {{ $t('integration.outbox.col.entity') }}
            </th>
            <th class="text-left px-3 py-2 font-medium text-gray-600 dark:text-gray-400 whitespace-nowrap">
              {{ $t('integration.outbox.col.status') }}
            </th>
            <th class="text-left px-3 py-2 font-medium text-gray-600 dark:text-gray-400 whitespace-nowrap">
              {{ $t('integration.outbox.col.retries') }}
            </th>
            <th class="text-left px-3 py-2 font-medium text-gray-600 dark:text-gray-400 whitespace-nowrap">
              {{ $t('integration.outbox.col.created') }}
            </th>
            <th class="text-left px-3 py-2 font-medium text-gray-600 dark:text-gray-400 whitespace-nowrap">
              {{ $t('integration.outbox.col.error') }}
            </th>
            <th class="text-left px-3 py-2 font-medium text-gray-600 dark:text-gray-400 whitespace-nowrap">
              {{ $t('integration.outbox.col.actions') }}
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="entry in props.entries"
            :key="entry.id"
            class="border-b border-gray-100 dark:border-gray-800 hover:bg-muted/40 transition-colors"
          >
            <!-- Event Name -->
            <td class="px-3 py-2 font-mono text-xs text-foreground whitespace-nowrap">
              {{ entry.eventName }}
            </td>

            <!-- Entity -->
            <td class="px-3 py-2 text-foreground whitespace-nowrap">
              <span>{{ entry.entityName }}</span>
              <span v-if="entry.entityId" class="ml-1 text-xs text-muted-foreground font-mono">
                ({{ entry.entityId }})
              </span>
            </td>

            <!-- Status badge -->
            <td class="px-3 py-2 whitespace-nowrap">
              <span
                class="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium"
                :class="statusBadgeClass[entry.status]"
              >
                {{ statusLabel(entry.status) }}
              </span>
            </td>

            <!-- Retries -->
            <td class="px-3 py-2 text-muted-foreground whitespace-nowrap tabular-nums">
              {{ entry.retryCount }} / {{ entry.maxRetries }}
            </td>

            <!-- Created At -->
            <td class="px-3 py-2 text-muted-foreground whitespace-nowrap tabular-nums text-xs">
              {{ formatDate(entry.createdAt) }}
            </td>

            <!-- Error -->
            <td class="px-3 py-2 max-w-[200px]">
              <span
                v-if="entry.errorMessage"
                class="block truncate text-xs text-red-600 dark:text-red-400 cursor-default"
                :title="entry.errorMessage"
              >
                {{ entry.errorMessage }}
              </span>
              <span v-else class="text-muted-foreground text-xs">—</span>
            </td>

            <!-- Actions -->
            <td class="px-3 py-2 whitespace-nowrap">
              <div v-if="entry.status === 'dead_letter'" class="flex items-center gap-1">
                <button
                  type="button"
                  class="inline-flex items-center gap-1 rounded px-2 py-1 text-xs font-medium text-blue-700 dark:text-blue-300 bg-blue-50 dark:bg-blue-900/30 hover:bg-blue-100 dark:hover:bg-blue-900/50 transition-colors focus:outline-none focus:ring-2 focus:ring-blue-500"
                  :title="$t('integration.outbox.retry')"
                  @click="emit('retry', entry.id)"
                >
                  <RefreshCw class="h-3 w-3" />
                  {{ $t('integration.outbox.retry') }}
                </button>
                <button
                  type="button"
                  class="inline-flex items-center gap-1 rounded px-2 py-1 text-xs font-medium text-red-700 dark:text-red-300 bg-red-50 dark:bg-red-900/30 hover:bg-red-100 dark:hover:bg-red-900/50 transition-colors focus:outline-none focus:ring-2 focus:ring-red-500"
                  :title="$t('integration.outbox.dismiss')"
                  @click="emit('dismiss', entry.id)"
                >
                  <Trash2 class="h-3 w-3" />
                  {{ $t('integration.outbox.dismiss') }}
                </button>
              </div>
              <span v-else class="text-muted-foreground text-xs">—</span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
