<script setup lang="ts">
import { ref, computed } from 'vue'
import { cn } from '@/lib/utils'
import { ChevronRight } from 'lucide-vue-next'
import {
  useResponsiveTable,
  type ResponsiveColumnConfig,
  type Breakpoint,
  DEFAULT_BREAKPOINTS,
} from '@/composables/useResponsiveTable'

interface Props {
  columns: ResponsiveColumnConfig[]
  data: Record<string, unknown>[]
  rowKey?: string
  breakpoints?: Breakpoint[]
  class?: string
  striped?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  rowKey: '',
  breakpoints: () => DEFAULT_BREAKPOINTS,
  class: '',
  striped: false,
})

const emit = defineEmits<{
  'row-click': [row: Record<string, unknown>]
  'row-expand': [rowKey: string | number]
}>()

const containerRef = ref<HTMLElement | null>(null)

const {
  visibleColumns,
  hiddenColumns,
  hasHiddenColumns,
  expandedRows,
  toggleRowExpand,
  isRowExpanded,
  expandAll,
  collapseAll,
  currentBreakpoint,
  containerWidth,
} = useResponsiveTable({
  columns: computed(() => props.columns),
  containerRef,
  breakpoints: props.breakpoints,
})

function getRowKey(row: Record<string, unknown>, index: number): string | number {
  if (props.rowKey && row[props.rowKey] != null) {
    return row[props.rowKey] as string | number
  }
  // Auto-detect common key fields
  if (row['Id'] != null) return row['Id'] as string | number
  if (row['id'] != null) return row['id'] as string | number
  if (row['ID'] != null) return row['ID'] as string | number
  return index
}

function getAllRowKeys(): (string | number)[] {
  return props.data.map((row, idx) => getRowKey(row, idx))
}

function handleRowClick(row: Record<string, unknown>, index: number): void {
  if (hasHiddenColumns.value) {
    const key = getRowKey(row, index)
    toggleRowExpand(key)
    emit('row-expand', key)
  }
  emit('row-click', row)
}

function formatValue(value: unknown): string {
  if (value == null) return '\u2014'
  if (typeof value === 'boolean') return value ? 'Yes' : 'No'
  if (value instanceof Date) return value.toLocaleDateString()
  return String(value)
}

defineExpose({
  expandAll: () => expandAll(getAllRowKeys()),
  collapseAll,
  currentBreakpoint,
  containerWidth,
  hasHiddenColumns,
  visibleColumns,
  hiddenColumns,
  expandedRows,
})
</script>

<template>
  <div
    ref="containerRef"
    :class="cn('responsive-table-container overflow-x-auto border rounded-lg', props.class)"
  >
    <table class="w-full">
      <thead>
        <tr class="bg-muted/50 border-b">
          <th
            v-if="hasHiddenColumns"
            class="w-10 px-2 py-3"
          />
          <th
            v-for="col in visibleColumns"
            :key="col.key"
            class="px-4 py-3 text-left text-sm font-medium text-muted-foreground"
          >
            {{ col.label }}
          </th>
        </tr>
      </thead>
      <tbody>
        <template v-for="(row, idx) in data" :key="getRowKey(row, idx)">
          <!-- Main data row -->
          <tr
            :class="cn(
              'border-b hover:bg-muted/20 transition-colors',
              hasHiddenColumns && 'cursor-pointer',
              striped && idx % 2 === 0 && 'bg-muted/10',
            )"
            @click="handleRowClick(row, idx)"
          >
            <td
              v-if="hasHiddenColumns"
              class="w-10 px-2 text-center"
            >
              <button
                class="inline-flex items-center justify-center rounded p-1 hover:bg-muted/40 transition-colors"
                :aria-label="isRowExpanded(getRowKey(row, idx)) ? 'Collapse row details' : 'Expand row details'"
                :aria-expanded="isRowExpanded(getRowKey(row, idx))"
                @click.stop="() => { const k = getRowKey(row, idx); toggleRowExpand(k); emit('row-expand', k) }"
              >
                <ChevronRight
                  :class="cn(
                    'h-4 w-4 transition-transform duration-200',
                    isRowExpanded(getRowKey(row, idx)) && 'rotate-90',
                  )"
                />
              </button>
            </td>
            <td
              v-for="col in visibleColumns"
              :key="col.key"
              class="px-4 py-3 text-sm"
            >
              <slot :name="'cell-' + col.key" :row="row" :value="row[col.key]">
                {{ formatValue(row[col.key]) }}
              </slot>
            </td>
          </tr>

          <!-- Popin detail row (expanded hidden columns) -->
          <tr
            v-if="hasHiddenColumns && isRowExpanded(getRowKey(row, idx))"
            class="bg-muted/30 border-b"
          >
            <td :colspan="visibleColumns.length + 1" class="px-4 py-3">
              <div
                class="grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-2 animate-in slide-in-from-top-1 duration-200"
              >
                <div
                  v-for="col in hiddenColumns"
                  :key="col.key"
                  class="flex gap-2 items-baseline"
                >
                  <span class="text-muted-foreground font-medium text-sm shrink-0">{{ col.label }}:</span>
                  <span class="text-sm">
                    <slot :name="'cell-' + col.key" :row="row" :value="row[col.key]">
                      {{ formatValue(row[col.key]) }}
                    </slot>
                  </span>
                </div>
              </div>
            </td>
          </tr>
        </template>
      </tbody>
    </table>

    <!-- Empty state -->
    <div
      v-if="data.length === 0"
      class="text-center py-8 text-muted-foreground"
    >
      No data available
    </div>
  </div>
</template>
