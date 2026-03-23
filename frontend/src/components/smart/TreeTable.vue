<script setup lang="ts">
import { ref, computed, toRef } from 'vue'
import type { EntityMetadata } from '@/types/metadata'
import { useSmartTable, type SmartColumnConfig } from '@/composables/useSmartTable'
import { useVirtualScroll } from '@/composables/useVirtualScroll'
import { useTreeTable, type TreeNode } from '@/composables/useTreeTable'
import SmartCellDisplay from './SmartCellDisplay.vue'
import { Table, TableHeader, TableBody, TableRow, TableHead, TableCell } from '@/components/ui/table'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Spinner } from '@/components/ui/spinner'
import {
  ChevronRight,
  ChevronDown,
  ChevronsDownUp,
  ChevronsUpDown,
  ArrowUp,
  ArrowDown,
  RefreshCw,
} from 'lucide-vue-next'

// ── Props ──

interface Props {
  treeData: TreeNode[]
  toggleField: string
  metadata: EntityMetadata
  indentSize?: number
  initialExpanded?: boolean
  title?: string
  selectionMode?: 'none' | 'single' | 'multi'
  maxHeight?: string
  enableVirtualScroll?: boolean
  rowHeight?: number
  isLoading?: boolean
  error?: string | null
  columnOverrides?: Partial<SmartColumnConfig>[]
  sortField?: string
  sortDirection?: 'asc' | 'desc'
}

const props = withDefaults(defineProps<Props>(), {
  indentSize: 24,
  initialExpanded: false,
  title: undefined,
  selectionMode: 'none',
  maxHeight: '600px',
  enableVirtualScroll: false,
  rowHeight: 40,
  isLoading: false,
  error: null,
  columnOverrides: undefined,
  sortField: undefined,
  sortDirection: 'asc',
})

// ── Emits ──

const emit = defineEmits<{
  'row-click': [row: Record<string, unknown>, key: string]
  'row-dblclick': [row: Record<string, unknown>, key: string]
  'sort': [field: string, direction: 'asc' | 'desc']
  'selection-change': [selectedKeys: string[]]
  'refresh': []
  'expand-change': [expandedKeys: string[]]
}>()

// ── Smart table columns ──

const { visibleColumns } = useSmartTable(
  () => props.metadata,
  () => props.columnOverrides,
)

// ── Tree table composable ──

const treeDataRef = toRef(props, 'treeData')

function getRowKey(row: Record<string, unknown>): string {
  return String(row['Id'] ?? row['ID'] ?? row['id'] ?? row['Name'] ?? '')
}

const { flatRows, toggleExpand, expandAll, collapseAll } = useTreeTable({
  treeData: treeDataRef,
  getRowKey,
  initialExpanded: props.initialExpanded,
})

function handleToggle(key: string) {
  toggleExpand(key)
  emit('expand-change', Array.from(flatRows.value.filter((r) => r.isExpanded).map((r) => r.key)))
}

function handleExpandAll() {
  expandAll()
  emit('expand-change', Array.from(flatRows.value.filter((r) => r.isExpanded).map((r) => r.key)))
}

function handleCollapseAll() {
  collapseAll()
  emit('expand-change', [])
}

// ── Selection ──

const selectedKeys = ref<Set<string>>(new Set())

function isSelected(key: string): boolean {
  return selectedKeys.value.has(key)
}

function toggleSelection(key: string) {
  if (props.selectionMode === 'single') {
    selectedKeys.value = new Set([key])
  } else {
    const next = new Set(selectedKeys.value)
    if (next.has(key)) {
      next.delete(key)
    } else {
      next.add(key)
    }
    selectedKeys.value = next
  }
  emit('selection-change', Array.from(selectedKeys.value))
}

const isAllSelected = computed(() => {
  if (flatRows.value.length === 0) return false
  return flatRows.value.every((r) => selectedKeys.value.has(r.key))
})

const isIndeterminate = computed(() => {
  if (flatRows.value.length === 0) return false
  const count = flatRows.value.filter((r) => selectedKeys.value.has(r.key)).length
  return count > 0 && count < flatRows.value.length
})

function toggleAll() {
  if (isAllSelected.value) {
    selectedKeys.value = new Set()
  } else {
    selectedKeys.value = new Set(flatRows.value.map((r) => r.key))
  }
  emit('selection-change', Array.from(selectedKeys.value))
}

// ── Sorting ──

function handleSort(col: SmartColumnConfig) {
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

// ── Virtual Scrolling ──

const scrollContainerRef = ref<HTMLElement | null>(null)
const flatRowCount = computed(() => flatRows.value.length)

const { virtualRows, topPadding, bottomPadding } = useVirtualScroll({
  containerRef: scrollContainerRef,
  count: flatRowCount,
  rowHeight: props.rowHeight,
  overscan: 10,
})

// ── Row events ──

function handleRowClick(row: Record<string, unknown>, key: string) {
  emit('row-click', row, key)
}

function handleRowDblClick(row: Record<string, unknown>, key: string) {
  emit('row-dblclick', row, key)
}

// ── Helpers ──

function isTreeColumn(_col: SmartColumnConfig, colIdx: number): boolean {
  return colIdx === 0
}

const totalColspan = computed(() => {
  let count = visibleColumns.value.length
  if (props.selectionMode !== 'none') count++
  return count
})

const SELECTION_COL_WIDTH = 40
</script>

<template>
  <div class="space-y-3">
    <!-- Toolbar -->
    <div class="flex items-center justify-between">
      <div class="flex items-center gap-2">
        <h3 v-if="title" class="text-lg font-semibold text-foreground">{{ title }}</h3>
        <Badge variant="secondary">{{ flatRows.length }} rows</Badge>
      </div>
      <div class="flex items-center gap-2">
        <Button
          variant="outline"
          size="sm"
          title="Expand all"
          @click="handleExpandAll"
        >
          <ChevronsUpDown class="h-4 w-4 mr-1" />
          Expand All
        </Button>
        <Button
          variant="outline"
          size="sm"
          title="Collapse all"
          @click="handleCollapseAll"
        >
          <ChevronsDownUp class="h-4 w-4 mr-1" />
          Collapse All
        </Button>
        <Button
          variant="outline"
          size="sm"
          title="Refresh"
          @click="emit('refresh')"
        >
          <RefreshCw class="h-4 w-4" />
        </Button>
      </div>
    </div>

    <!-- Error alert -->
    <div
      v-if="error"
      class="rounded-md border border-destructive/50 bg-destructive/10 px-4 py-3 text-sm text-destructive"
    >
      {{ error }}
    </div>

    <!-- Table container -->
    <div
      class="border rounded-md relative overflow-hidden"
      :style="{ maxHeight }"
    >
      <!-- Loading overlay -->
      <div
        v-if="isLoading"
        class="absolute inset-0 bg-background/50 z-10 flex items-center justify-center"
      >
        <Spinner size="lg" />
      </div>

      <div
        ref="scrollContainerRef"
        class="overflow-x-auto overflow-y-auto"
        :style="{ maxHeight }"
      >
        <Table class="table-fixed">
          <!-- Colgroup -->
          <colgroup>
            <col v-if="selectionMode !== 'none'" :style="{ width: SELECTION_COL_WIDTH + 'px' }" />
            <col
              v-for="col in visibleColumns"
              :key="'cg-' + col.field"
              :style="{ width: (col.width ?? 150) + 'px' }"
            />
          </colgroup>

          <TableHeader class="sticky top-0 bg-background z-[5]">
            <TableRow>
              <!-- Selection header -->
              <TableHead v-if="selectionMode !== 'none'">
                <input
                  v-if="selectionMode === 'multi'"
                  type="checkbox"
                  :checked="isAllSelected"
                  :indeterminate.prop="isIndeterminate"
                  aria-label="Select all rows"
                  class="h-4 w-4 rounded border-input accent-primary cursor-pointer"
                  @change="toggleAll"
                />
              </TableHead>

              <!-- Data column headers -->
              <TableHead
                v-for="col in visibleColumns"
                :key="col.field"
                :style="{ textAlign: col.align }"
                :class="'select-none' + (col.sortable ? ' cursor-pointer' : '')"
                @click="handleSort(col)"
              >
                <div class="flex items-center gap-1">
                  <span class="truncate">{{ col.label }}</span>
                  <ArrowUp
                    v-if="isSortedAsc(col)"
                    class="h-3.5 w-3.5 flex-shrink-0"
                  />
                  <ArrowDown
                    v-else-if="isSortedDesc(col)"
                    class="h-3.5 w-3.5 flex-shrink-0"
                  />
                </div>
              </TableHead>
            </TableRow>
          </TableHeader>

          <TableBody>
            <!-- Virtual scroll path -->
            <template v-if="enableVirtualScroll">
              <!-- Top spacer -->
              <tr v-if="topPadding > 0">
                <td :colspan="totalColspan" :style="{ height: topPadding + 'px', padding: 0, border: 'none' }" />
              </tr>

              <TableRow
                v-for="vRow in virtualRows"
                :key="flatRows[vRow.index]?.key ?? vRow.index"
                :class="'cursor-pointer hover:bg-muted/50' + (flatRows[vRow.index] && isSelected(flatRows[vRow.index].key) ? ' bg-muted/30' : '')"
                @click="flatRows[vRow.index] && handleRowClick(flatRows[vRow.index].data, flatRows[vRow.index].key)"
                @dblclick="flatRows[vRow.index] && handleRowDblClick(flatRows[vRow.index].data, flatRows[vRow.index].key)"
              >
                <!-- Selection -->
                <TableCell
                  v-if="selectionMode !== 'none'"
                  @click.stop
                >
                  <input
                    v-if="flatRows[vRow.index]"
                    :type="selectionMode === 'multi' ? 'checkbox' : 'radio'"
                    :checked="isSelected(flatRows[vRow.index].key)"
                    :name="selectionMode === 'single' ? 'tree-table-selection' : undefined"
                    aria-label="Select row"
                    class="h-4 w-4 rounded border-input accent-primary cursor-pointer"
                    @change="toggleSelection(flatRows[vRow.index].key)"
                  />
                </TableCell>

                <!-- Data cells -->
                <TableCell
                  v-for="(col, colIdx) in visibleColumns"
                  :key="col.field"
                  :style="{ textAlign: col.align }"
                >
                  <template v-if="flatRows[vRow.index]">
                    <!-- Tree column (first visible column) -->
                    <div
                      v-if="isTreeColumn(col, colIdx)"
                      class="flex items-center"
                      :style="{ paddingLeft: flatRows[vRow.index].depth * indentSize + 'px' }"
                    >
                      <button
                        v-if="flatRows[vRow.index].hasChildren"
                        class="inline-flex items-center justify-center h-5 w-5 rounded-sm hover:bg-accent transition-colors shrink-0"
                        @click.stop="handleToggle(flatRows[vRow.index].key)"
                      >
                        <ChevronDown v-if="flatRows[vRow.index].isExpanded" class="h-4 w-4" />
                        <ChevronRight v-else class="h-4 w-4" />
                      </button>
                      <span v-else class="w-5 h-5 shrink-0" />
                      <span class="ml-1 truncate">
                        <SmartCellDisplay :column="col" :value="flatRows[vRow.index].data[col.field]" :row="flatRows[vRow.index].data" />
                      </span>
                    </div>

                    <!-- Normal cell -->
                    <SmartCellDisplay
                      v-else
                      :column="col"
                      :value="flatRows[vRow.index].data[col.field]"
                      :row="flatRows[vRow.index].data"
                    />
                  </template>
                </TableCell>
              </TableRow>

              <!-- Bottom spacer -->
              <tr v-if="bottomPadding > 0">
                <td :colspan="totalColspan" :style="{ height: bottomPadding + 'px', padding: 0, border: 'none' }" />
              </tr>
            </template>

            <!-- Normal (non-virtual) path -->
            <template v-else>
              <TableRow
                v-for="row in flatRows"
                :key="row.key"
                :class="'cursor-pointer hover:bg-muted/50' + (isSelected(row.key) ? ' bg-muted/30' : '')"
                @click="handleRowClick(row.data, row.key)"
                @dblclick="handleRowDblClick(row.data, row.key)"
              >
                <!-- Selection -->
                <TableCell
                  v-if="selectionMode !== 'none'"
                  @click.stop
                >
                  <input
                    :type="selectionMode === 'multi' ? 'checkbox' : 'radio'"
                    :checked="isSelected(row.key)"
                    :name="selectionMode === 'single' ? 'tree-table-selection' : undefined"
                    aria-label="Select row"
                    class="h-4 w-4 rounded border-input accent-primary cursor-pointer"
                    @change="toggleSelection(row.key)"
                  />
                </TableCell>

                <!-- Data cells -->
                <TableCell
                  v-for="(col, colIdx) in visibleColumns"
                  :key="col.field"
                  :style="{ textAlign: col.align }"
                >
                  <!-- Tree column (first visible column) -->
                  <div
                    v-if="isTreeColumn(col, colIdx)"
                    class="flex items-center"
                    :style="{ paddingLeft: row.depth * indentSize + 'px' }"
                  >
                    <button
                      v-if="row.hasChildren"
                      class="inline-flex items-center justify-center h-5 w-5 rounded-sm hover:bg-accent transition-colors shrink-0"
                      @click.stop="handleToggle(row.key)"
                    >
                      <ChevronDown v-if="row.isExpanded" class="h-4 w-4" />
                      <ChevronRight v-else class="h-4 w-4" />
                    </button>
                    <span v-else class="w-5 h-5 shrink-0" />
                    <span class="ml-1 truncate">
                      <SmartCellDisplay :column="col" :value="row.data[col.field]" :row="row.data" />
                    </span>
                  </div>

                  <!-- Normal cell -->
                  <SmartCellDisplay
                    v-else
                    :column="col"
                    :value="row.data[col.field]"
                    :row="row.data"
                  />
                </TableCell>
              </TableRow>
            </template>

            <!-- Empty state -->
            <TableRow v-if="flatRows.length === 0 && !isLoading">
              <TableCell
                :colspan="totalColspan"
                class="text-center py-12 text-muted-foreground"
              >
                No data available
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
    </div>
  </div>
</template>
