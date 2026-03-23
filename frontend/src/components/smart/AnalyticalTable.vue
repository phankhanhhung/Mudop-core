<script setup lang="ts">
import { ref, computed } from 'vue'
import type { EntityMetadata } from '@/types/metadata'
import { useSmartTable, type SmartColumnConfig } from '@/composables/useSmartTable'
import {
  useAnalyticalTable,
  type GroupConfig,
  type AggregateConfig,
} from '@/composables/useAnalyticalTable'
import SmartCellDisplay from './SmartCellDisplay.vue'
import { Table, TableHeader, TableBody, TableRow, TableHead, TableCell } from '@/components/ui/table'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Spinner } from '@/components/ui/spinner'
import {
  ChevronDown,
  ChevronRight,
  ChevronsDownUp,
  ChevronsUpDown,
  ArrowUp,
  ArrowDown,
  RefreshCw,
} from 'lucide-vue-next'

// ── Props ──

interface Props {
  data: Record<string, unknown>[]
  metadata: EntityMetadata
  groupBy?: GroupConfig[]
  aggregates?: AggregateConfig[]
  showSubtotals?: boolean
  showGrandTotal?: boolean
  showDataBars?: boolean
  dataBarField?: string
  title?: string
  selectionMode?: 'none' | 'single' | 'multi'
  maxHeight?: string
  isLoading?: boolean
  error?: string | null
  columnOverrides?: Partial<SmartColumnConfig>[]
  sortField?: string
  sortDirection?: 'asc' | 'desc'
}

const props = withDefaults(defineProps<Props>(), {
  groupBy: () => [],
  aggregates: () => [],
  showSubtotals: true,
  showGrandTotal: true,
  showDataBars: false,
  dataBarField: undefined,
  title: undefined,
  selectionMode: 'none',
  maxHeight: '600px',
  isLoading: false,
  error: null,
  columnOverrides: undefined,
  sortField: undefined,
  sortDirection: 'asc',
})

// ── Emits ──

const emit = defineEmits<{
  'row-click': [row: Record<string, unknown>, id: string]
  'row-dblclick': [row: Record<string, unknown>, id: string]
  'sort': [field: string, direction: 'asc' | 'desc']
  'selection-change': [selectedIds: string[]]
  'refresh': []
}>()

// ── Smart table columns ──

const { visibleColumns } = useSmartTable(
  () => props.metadata,
  () => props.columnOverrides
)

// ── Analytical table composable ──

const dataRef = computed(() => props.data)
const groupByRef = computed(() => props.groupBy)
const aggregatesRef = computed(() => props.aggregates)

const {
  analyticalRows,
  toggleGroup,
  expandAllGroups,
  collapseAllGroups,
  dataBarMax,
} = useAnalyticalTable({
  data: dataRef,
  groupBy: groupByRef,
  aggregates: aggregatesRef,
  showSubtotals: props.showSubtotals,
  showGrandTotal: props.showGrandTotal,
})

// ── Row key ──

function getRowKey(row: Record<string, unknown>): string {
  return String(row['Id'] ?? row['ID'] ?? row['id'] ?? '')
}

// ── Selection ──

const selectedIds = ref<Set<string>>(new Set())

function isSelected(row: Record<string, unknown>): boolean {
  return selectedIds.value.has(getRowKey(row))
}

function toggleSelection(row: Record<string, unknown>): void {
  const id = getRowKey(row)
  if (props.selectionMode === 'single') {
    selectedIds.value = new Set([id])
  } else {
    const next = new Set(selectedIds.value)
    if (next.has(id)) {
      next.delete(id)
    } else {
      next.add(id)
    }
    selectedIds.value = next
  }
  emit('selection-change', Array.from(selectedIds.value))
}

const dataRows = computed(() => analyticalRows.value.filter(r => r.type === 'data'))

const isAllSelected = computed(() => {
  if (dataRows.value.length === 0) return false
  return dataRows.value.every(r => selectedIds.value.has(getRowKey(r.data)))
})

const isIndeterminate = computed(() => {
  if (dataRows.value.length === 0) return false
  const count = dataRows.value.filter(r => selectedIds.value.has(getRowKey(r.data))).length
  return count > 0 && count < dataRows.value.length
})

function toggleAll(): void {
  if (isAllSelected.value) {
    selectedIds.value = new Set()
  } else {
    selectedIds.value = new Set(dataRows.value.map(r => getRowKey(r.data)))
  }
  emit('selection-change', Array.from(selectedIds.value))
}

// ── Sorting ──

function handleSort(col: SmartColumnConfig): void {
  if (!col.sortable) return
  let direction: 'asc' | 'desc' = 'asc'
  if (props.sortField === col.field) {
    direction = props.sortDirection === 'asc' ? 'desc' : 'asc'
  }
  emit('sort', col.field, direction)
}

function isSortedAsc(col: SmartColumnConfig): boolean {
  return props.sortField === col.field && props.sortDirection === 'asc'
}

function isSortedDesc(col: SmartColumnConfig): boolean {
  return props.sortField === col.field && props.sortDirection === 'desc'
}

// ── Row events ──

function handleRowClick(row: Record<string, unknown>): void {
  emit('row-click', row, getRowKey(row))
}

function handleRowDblClick(row: Record<string, unknown>): void {
  emit('row-dblclick', row, getRowKey(row))
}

// ── Helpers ──

const totalColumnCount = computed(() => {
  let count = visibleColumns.value.length
  if (props.selectionMode !== 'none') count += 1
  return count
})

const hasGroups = computed(() => props.groupBy.length > 0)

const aggregateFieldSet = computed(() => new Set(props.aggregates.map(a => a.field)))

function isAggregateField(field: string): boolean {
  return aggregateFieldSet.value.has(field)
}

function getAggregateLabel(field: string): string {
  const agg = props.aggregates.find(a => a.field === field)
  if (!agg) return ''
  return agg.fn.toUpperCase()
}

function formatAggregate(value: number, field: string): string {
  const col = visibleColumns.value.find(c => c.field === field)
  if (col?.type === 'Decimal') {
    return value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
  }
  if (col?.type === 'Integer') {
    return value.toLocaleString(undefined, { maximumFractionDigits: 0 })
  }
  return value.toLocaleString(undefined, { maximumFractionDigits: 2 })
}

function shouldShowDataBar(field: string): boolean {
  return props.showDataBars && field === props.dataBarField
}

function dataBarWidth(value: unknown, field: string): string {
  const max = dataBarMax.value[field]
  if (!max || max === 0) return '0%'
  const pct = (Math.abs(Number(value) || 0) / max) * 100
  return pct + '%'
}
</script>

<template>
  <div class="space-y-0 border rounded-lg bg-card">
    <!-- Toolbar -->
    <div class="flex items-center justify-between px-4 py-3 border-b bg-muted/20">
      <div class="flex items-center gap-3">
        <h3 v-if="title" class="text-sm font-semibold text-foreground">{{ title }}</h3>
        <Badge variant="secondary" class="text-xs">
          {{ data.length }} records
        </Badge>
      </div>
      <div class="flex items-center gap-2">
        <template v-if="hasGroups">
          <Button variant="ghost" size="sm" @click="expandAllGroups">
            <ChevronsUpDown class="h-4 w-4 mr-1" />
            Expand All
          </Button>
          <Button variant="ghost" size="sm" @click="collapseAllGroups">
            <ChevronsDownUp class="h-4 w-4 mr-1" />
            Collapse All
          </Button>
        </template>
        <Button variant="ghost" size="sm" @click="emit('refresh')">
          <RefreshCw class="h-4 w-4" />
        </Button>
      </div>
    </div>

    <!-- Loading -->
    <div v-if="isLoading" class="flex items-center justify-center py-16">
      <Spinner class="h-6 w-6 text-muted-foreground" />
      <span class="ml-2 text-sm text-muted-foreground">Loading...</span>
    </div>

    <!-- Error -->
    <div v-else-if="error" class="flex items-center justify-center py-16">
      <p class="text-sm text-destructive">{{ error }}</p>
    </div>

    <!-- Table -->
    <div v-else class="overflow-auto" :style="{ maxHeight }">
      <Table>
        <TableHeader class="sticky top-0 z-10 bg-card">
          <TableRow>
            <!-- Selection header -->
            <TableHead
              v-if="selectionMode !== 'none'"
              class="w-10 text-center"
            >
              <input
                v-if="selectionMode === 'multi'"
                type="checkbox"
                :checked="isAllSelected"
                :indeterminate="isIndeterminate"
                class="rounded border-input"
                @change="toggleAll"
              />
            </TableHead>

            <!-- Column headers -->
            <TableHead
              v-for="col in visibleColumns"
              :key="col.field"
              :class="'select-none ' + (col.sortable ? 'cursor-pointer hover:bg-muted/50 ' : '') + (col.align === 'right' ? 'text-right' : col.align === 'center' ? 'text-center' : 'text-left')"
              :style="col.width ? { width: col.width + 'px', minWidth: (col.minWidth ?? 60) + 'px' } : { minWidth: (col.minWidth ?? 60) + 'px' }"
              @click="handleSort(col)"
            >
              <div class="flex items-center gap-1" :class="col.align === 'right' ? 'justify-end' : ''">
                <span>{{ col.label }}</span>
                <ArrowUp v-if="isSortedAsc(col)" class="h-3 w-3 text-primary" />
                <ArrowDown v-if="isSortedDesc(col)" class="h-3 w-3 text-primary" />
                <Badge
                  v-if="isAggregateField(col.field)"
                  variant="outline"
                  class="text-[10px] px-1 py-0 ml-1 font-normal"
                >
                  {{ getAggregateLabel(col.field) }}
                </Badge>
              </div>
            </TableHead>
          </TableRow>
        </TableHeader>

        <TableBody>
          <template v-if="analyticalRows.length === 0">
            <TableRow>
              <TableCell :colspan="totalColumnCount" class="text-center py-12 text-muted-foreground">
                No data available
              </TableCell>
            </TableRow>
          </template>

          <template v-for="aRow in analyticalRows" :key="aRow.key">
            <!-- Group Header Row -->
            <TableRow
              v-if="aRow.type === 'group-header'"
              class="bg-muted/30 hover:bg-muted/40"
            >
              <TableCell :colspan="totalColumnCount" class="py-2">
                <div class="flex items-center gap-2">
                  <Button
                    variant="ghost"
                    size="sm"
                    class="h-6 w-6 p-0"
                    @click="toggleGroup(aRow.groupKey!)"
                  >
                    <ChevronDown v-if="aRow.isExpanded" class="h-4 w-4" />
                    <ChevronRight v-else class="h-4 w-4" />
                  </Button>
                  <span class="font-semibold text-sm">{{ aRow.groupField }}: {{ aRow.groupValue }}</span>
                  <Badge variant="secondary" class="text-xs">
                    {{ aRow.groupCount }}
                  </Badge>
                </div>
              </TableCell>
            </TableRow>

            <!-- Data Row -->
            <TableRow
              v-else-if="aRow.type === 'data'"
              class="cursor-pointer hover:bg-muted/50"
              :class="isSelected(aRow.data) ? 'bg-muted/30' : ''"
              @click="handleRowClick(aRow.data)"
              @dblclick="handleRowDblClick(aRow.data)"
            >
              <!-- Selection cell -->
              <TableCell v-if="selectionMode !== 'none'" class="w-10 text-center" @click.stop>
                <input
                  :type="selectionMode === 'multi' ? 'checkbox' : 'radio'"
                  :checked="isSelected(aRow.data)"
                  class="rounded border-input"
                  @change="toggleSelection(aRow.data)"
                />
              </TableCell>

              <!-- Data cells -->
              <TableCell
                v-for="col in visibleColumns"
                :key="col.field"
                :class="col.align === 'right' ? 'text-right' : col.align === 'center' ? 'text-center' : ''"
              >
                <!-- Data bar cell -->
                <div v-if="shouldShowDataBar(col.field)" class="relative">
                  <div
                    class="absolute inset-y-0 left-0 bg-primary/10 rounded-sm"
                    :style="{ width: dataBarWidth(aRow.data[col.field], col.field) }"
                  />
                  <span class="relative z-10">
                    <SmartCellDisplay :column="col" :value="aRow.data[col.field]" :row="aRow.data" />
                  </span>
                </div>
                <!-- Normal cell -->
                <SmartCellDisplay v-else :column="col" :value="aRow.data[col.field]" :row="aRow.data" />
              </TableCell>
            </TableRow>

            <!-- Subtotal Row -->
            <TableRow
              v-else-if="aRow.type === 'subtotal'"
              class="bg-muted/20 hover:bg-muted/30"
            >
              <!-- Selection placeholder -->
              <TableCell v-if="selectionMode !== 'none'" class="w-10" />

              <!-- Subtotal cells -->
              <TableCell
                v-for="(col, colIdx) in visibleColumns"
                :key="col.field"
                class="font-medium text-sm"
                :class="col.align === 'right' ? 'text-right' : col.align === 'center' ? 'text-center' : ''"
              >
                <template v-if="colIdx === 0">
                  Subtotal
                </template>
                <template v-else-if="aRow.aggregates && aRow.aggregates[col.field] !== undefined">
                  {{ formatAggregate(aRow.aggregates[col.field], col.field) }}
                </template>
              </TableCell>
            </TableRow>

            <!-- Grand Total Row -->
            <TableRow
              v-else-if="aRow.type === 'grand-total'"
              class="bg-muted/50 border-t-2 sticky bottom-0"
            >
              <!-- Selection placeholder -->
              <TableCell v-if="selectionMode !== 'none'" class="w-10" />

              <!-- Grand total cells -->
              <TableCell
                v-for="(col, colIdx) in visibleColumns"
                :key="col.field"
                class="font-bold text-sm"
                :class="col.align === 'right' ? 'text-right' : col.align === 'center' ? 'text-center' : ''"
              >
                <template v-if="colIdx === 0">
                  Grand Total
                </template>
                <template v-else-if="aRow.aggregates && aRow.aggregates[col.field] !== undefined">
                  {{ formatAggregate(aRow.aggregates[col.field], col.field) }}
                </template>
              </TableCell>
            </TableRow>
          </template>
        </TableBody>
      </Table>
    </div>
  </div>
</template>
