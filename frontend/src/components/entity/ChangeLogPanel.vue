<script setup lang="ts">
import { computed } from 'vue'
import { Table, TableHeader, TableBody, TableRow, TableHead, TableCell } from '@/components/ui/table'
import { Badge } from '@/components/ui/badge'

export interface ChangeLogEntry {
  id: string
  type: 'added' | 'modified' | 'removed'
  data: Record<string, unknown>
  timestamp: Date
}

const props = defineProps<{
  changes: ChangeLogEntry[]
}>()

const emit = defineEmits<{
  navigate: [id: string]
}>()

/**
 * Format a timestamp as a relative time string (e.g. "2m ago").
 */
function formatRelativeTime(date: Date): string {
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffSec = Math.floor(diffMs / 1000)

  if (diffSec < 60) return `${diffSec}s ago`
  const diffMin = Math.floor(diffSec / 60)
  if (diffMin < 60) return `${diffMin}m ago`
  const diffHr = Math.floor(diffMin / 60)
  if (diffHr < 24) return `${diffHr}h ago`
  const diffDays = Math.floor(diffHr / 24)
  return `${diffDays}d ago`
}

/**
 * For modified items, extract the field names that appear in the data
 * (excluding OData metadata annotations).
 */
function getChangedFields(entry: ChangeLogEntry): string[] {
  if (entry.type !== 'modified') return []
  return Object.keys(entry.data).filter(
    (key) => !key.startsWith('@') && key !== 'Id' && key !== 'ID' && key !== 'id'
  )
}

function typeBadgeVariant(type: 'added' | 'modified' | 'removed'): 'default' | 'secondary' | 'destructive' {
  if (type === 'removed') return 'destructive'
  return 'default'
}

function typeBadgeClass(type: 'added' | 'modified' | 'removed'): string {
  if (type === 'added') return 'bg-green-600 text-white border-green-600'
  if (type === 'modified') return 'bg-yellow-500 text-white border-yellow-500'
  return '' // destructive variant handles red
}

const sortedChanges = computed(() => {
  return [...props.changes].sort(
    (a, b) => b.timestamp.getTime() - a.timestamp.getTime()
  )
})

function handleRowClick(entry: ChangeLogEntry) {
  if (entry.type !== 'removed' && entry.id) {
    emit('navigate', entry.id)
  }
}
</script>

<template>
  <div class="w-full">
    <!-- Empty state -->
    <div
      v-if="changes.length === 0"
      class="flex flex-col items-center justify-center py-8 text-muted-foreground"
    >
      <p class="text-sm">No changes tracked</p>
      <p class="text-xs mt-1">Start change tracking to see delta updates here.</p>
    </div>

    <!-- Change log table -->
    <div v-else class="max-h-96 overflow-y-auto rounded-md border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead class="w-20">Time</TableHead>
            <TableHead class="w-48">Entity ID</TableHead>
            <TableHead class="w-24">Type</TableHead>
            <TableHead>Changed Fields</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow
            v-for="(entry, idx) in sortedChanges"
            :key="`${entry.id}-${idx}`"
            :class="'cursor-pointer hover:bg-muted/50' + (entry.type === 'removed' ? ' opacity-60' : '')"
            @click="handleRowClick(entry)"
          >
            <TableCell class="text-xs text-muted-foreground whitespace-nowrap">
              {{ formatRelativeTime(entry.timestamp) }}
            </TableCell>
            <TableCell class="font-mono text-xs truncate max-w-[12rem]">
              {{ entry.id }}
            </TableCell>
            <TableCell>
              <Badge
                :variant="typeBadgeVariant(entry.type)"
                :class="'text-[10px] px-1.5 py-0 ' + typeBadgeClass(entry.type)"
              >
                {{ entry.type }}
              </Badge>
            </TableCell>
            <TableCell class="text-xs text-muted-foreground">
              <template v-if="entry.type === 'modified'">
                <span
                  v-for="(field, fIdx) in getChangedFields(entry)"
                  :key="field"
                >
                  {{ field }}<span v-if="fIdx < getChangedFields(entry).length - 1">, </span>
                </span>
                <span v-if="getChangedFields(entry).length === 0" class="italic">--</span>
              </template>
              <template v-else-if="entry.type === 'added'">
                <span class="italic">new entity</span>
              </template>
              <template v-else>
                <span class="italic">deleted</span>
              </template>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>
  </div>
</template>
