<script setup lang="ts">
import { ref, computed } from 'vue'
import Timeline, { type TimelineItem } from '@/components/smart/Timeline.vue'
import type { AuditLogEntry } from '@/types/audit'
import { Spinner } from '@/components/ui/spinner'
import { PlusCircle, Pencil, Trash2, Activity } from 'lucide-vue-next'

const props = defineProps<{
  entries: AuditLogEntry[]
  isLoading: boolean
}>()

const expandedIds = ref<Set<string>>(new Set())

function togglePayload(id: string) {
  if (expandedIds.value.has(id)) {
    expandedIds.value.delete(id)
  } else {
    expandedIds.value.add(id)
  }
}

function getEventIcon(eventName: string) {
  if (eventName.includes('Created')) return PlusCircle
  if (eventName.includes('Updated')) return Pencil
  if (eventName.includes('Deleted')) return Trash2
  return Activity
}

function getEventType(eventName: string): TimelineItem['type'] {
  if (eventName.includes('Created')) return 'success'
  if (eventName.includes('Updated')) return 'info'
  if (eventName.includes('Deleted')) return 'error'
  return 'neutral'
}

function formatEventName(eventName: string): string {
  const parts = eventName.split('.')
  return parts[parts.length - 1]
}

// Map AuditLogEntry[] to TimelineItem[]
const timelineItems = computed<TimelineItem[]>(() =>
  props.entries.map((entry) => ({
    id: entry.id,
    title: `${formatEventName(entry.eventName)} - ${entry.entityName}`,
    content: entry.entityId ? `ID: ${entry.entityId}` : undefined,
    datetime: entry.createdAt,
    icon: getEventIcon(entry.eventName),
    type: getEventType(entry.eventName),
    author: entry.correlationId ? `corr:${entry.correlationId.substring(0, 8)}` : undefined,
  }))
)

function handleItemClick(item: TimelineItem) {
  togglePayload(item.id)
}

function formatPayload(payload: Record<string, unknown>): string {
  return JSON.stringify(payload, null, 2)
}

function getEntry(id: string): AuditLogEntry | undefined {
  return props.entries.find((e) => e.id === id)
}
</script>

<template>
  <div>
    <div v-if="isLoading" class="flex items-center justify-center py-12">
      <Spinner class="h-6 w-6" />
    </div>

    <div v-else-if="entries.length === 0" class="py-12 text-center text-muted-foreground">
      No audit log entries found.
    </div>

    <template v-else>
      <Timeline
        :items="timelineItems"
        sortOrder="desc"
        :groupByDate="true"
        :showConnector="true"
        :maxItems="20"
        @item-click="handleItemClick"
      />

      <!-- Expanded payloads (shown below timeline for expanded items) -->
      <div
        v-for="entry in entries.filter(e => expandedIds.has(e.id) && e.payload && Object.keys(e.payload).length > 0)"
        :key="'payload-' + entry.id"
        class="ml-10 mt-2 mb-4"
      >
        <div class="flex items-center gap-2 mb-1">
          <span class="text-xs font-medium text-muted-foreground">
            Payload for {{ formatEventName(entry.eventName) }} - {{ entry.entityName }}
          </span>
          <button
            class="text-xs text-muted-foreground underline-offset-2 hover:underline"
            @click="togglePayload(entry.id)"
          >
            Hide
          </button>
        </div>
        <pre class="max-h-64 overflow-auto rounded-md bg-muted p-3 text-xs">{{ formatPayload(entry.payload) }}</pre>
      </div>
    </template>
  </div>
</template>
