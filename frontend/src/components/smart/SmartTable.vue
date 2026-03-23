<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount, nextTick } from 'vue'
import type { EntityMetadata } from '@/types/metadata'
import type { FilterCondition } from '@/types/odata'
import { useSmartTable, type SmartColumnConfig } from '@/composables/useSmartTable'
import { useColumnResize } from '@/composables/useColumnResize'
import { useColumnDragReorder } from '@/composables/useColumnDragReorder'
import { useVirtualScroll } from '@/composables/useVirtualScroll'
import { useRowEdit } from '@/composables/useRowEdit'
import SmartCellDisplay from './SmartCellDisplay.vue'
import ResponsiveTable from './ResponsiveTable.vue'
import type { ResponsiveColumnConfig } from '@/composables/useResponsiveTable'
import ObjectIdentifier from './ObjectIdentifier.vue'
import SmartTableEditRow from './SmartTableEditRow.vue'
import BulkActionToolbar from './BulkActionToolbar.vue'
import ViewSettingsDialog from './ViewSettingsDialog.vue'
import SmartTablePagination from './SmartTablePagination.vue'
import { Table, TableHeader, TableBody, TableRow, TableHead, TableCell } from '@/components/ui/table'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Spinner } from '@/components/ui/spinner'
import {
  ArrowUp,
  ArrowDown,
  Filter,
  Download,
  RefreshCw,
  Pencil,
  Trash2,
  Settings2,
  GripVertical,
  Check,
  ChevronDown,
  SlidersHorizontal
} from 'lucide-vue-next'

// ── Props ──

interface Props {
  // Data source
  module: string
  entitySet: string
  metadata: EntityMetadata
  data: Record<string, unknown>[]

  // State
  totalCount?: number
  currentPage?: number
  pageSize?: number
  isLoading?: boolean
  error?: string | null

  // Configuration
  title?: string
  selectionMode?: 'none' | 'single' | 'multi'
  enableInlineEdit?: boolean
  enableColumnChooser?: boolean
  enableExport?: boolean
  enableGrouping?: boolean
  compactMode?: boolean
  maxHeight?: string

  // Phase C: Data Grid Pro
  enableVirtualScroll?: boolean
  virtualScrollThreshold?: number
  rowHeight?: number
  enableBulkActions?: boolean
  enableRowEdit?: boolean

  // Column overrides
  columnOverrides?: Partial<SmartColumnConfig>[]

  // Sort/filter state
  sortField?: string
  sortDirection?: 'asc' | 'desc'
  activeFilters?: FilterCondition[]
}

const props = withDefaults(defineProps<Props>(), {
  totalCount: 0,
  currentPage: 1,
  pageSize: 20,
  isLoading: false,
  error: null,
  title: undefined,
  selectionMode: 'none',
  enableInlineEdit: false,
  enableColumnChooser: true,
  enableExport: false,
  enableGrouping: false,
  compactMode: false,
  maxHeight: '600px',
  enableVirtualScroll: false,
  virtualScrollThreshold: 100,
  rowHeight: 40,
  enableBulkActions: false,
  enableRowEdit: false,
  columnOverrides: undefined,
  sortField: undefined,
  sortDirection: 'asc',
  activeFilters: () => []
})

// ── Emits ──

const emit = defineEmits<{
  'row-click': [row: Record<string, unknown>, id: string]
  'row-dblclick': [row: Record<string, unknown>, id: string]
  'sort': [field: string, direction: 'asc' | 'desc']
  'filter': [filters: FilterCondition[]]
  'page': [page: number]
  'page-size': [size: number]
  'selection-change': [selectedIds: string[]]
  'inline-save': [rowId: string, field: string, value: unknown]
  'delete': [id: string]
  'edit': [id: string]
  'export': [format: 'csv' | 'json' | 'xlsx']
  'refresh': []
  // Phase C
  'bulk-delete': [ids: string[]]
  'bulk-export': [format: 'csv' | 'xlsx', ids: string[]]
  'row-save': [rowId: string, changes: Record<string, unknown>]
  'row-cancel': [rowId: string]
}>()

// ── Smart table composable ──

const { columns, visibleColumns, toggleColumn, setColumnWidth, reorderColumns, resetColumns, saveLayout, loadLayout } = useSmartTable(
  () => props.metadata,
  () => props.columnOverrides
)

// ── Layout persistence ──

const layoutKey = computed(() => `${props.module}-${props.entitySet}`)

onMounted(() => {
  loadLayout(layoutKey.value)
})

watch(layoutKey, (newKey) => {
  loadLayout(newKey)
})

// Save layout when columns change (debounced)
let saveTimer: ReturnType<typeof setTimeout> | null = null
watch(
  columns,
  () => {
    if (saveTimer) clearTimeout(saveTimer)
    saveTimer = setTimeout(() => {
      saveLayout(layoutKey.value)
    }, 500)
  },
  { deep: true }
)

onBeforeUnmount(() => {
  if (saveTimer) clearTimeout(saveTimer)
  if (resizeObserver) resizeObserver.disconnect()
})

// ── Mobile detection via ResizeObserver ──

const isMobile = ref(false)
const smartTableRootRef = ref<HTMLElement | null>(null)
let resizeObserver: ResizeObserver | null = null

onMounted(() => {
  isMobile.value = window.innerWidth < 768

  resizeObserver = new ResizeObserver(() => {
    isMobile.value = window.innerWidth < 768
  })
  resizeObserver.observe(document.body)
})

// Columns for ResponsiveTable
const responsiveColumns = computed<ResponsiveColumnConfig[]>(() =>
  visibleColumns.value.map((col, idx) => ({
    key: col.field,
    label: col.label,
    importance: idx < 2 ? 'high' as const : idx < 5 ? 'medium' as const : 'low' as const,
    minWidth: col.minWidth,
  }))
)

// ── Row key extraction ──

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

const isAllSelected = computed(() => {
  if (props.data.length === 0) return false
  return props.data.every((row) => selectedIds.value.has(getRowKey(row)))
})

const isIndeterminate = computed(() => {
  if (props.data.length === 0) return false
  const count = props.data.filter((row) => selectedIds.value.has(getRowKey(row))).length
  return count > 0 && count < props.data.length
})

function toggleAll(): void {
  if (isAllSelected.value) {
    selectedIds.value = new Set()
  } else {
    selectedIds.value = new Set(props.data.map(getRowKey))
  }
  emit('selection-change', Array.from(selectedIds.value))
}

function selectAll(): void {
  selectedIds.value = new Set(props.data.map(getRowKey))
  emit('selection-change', Array.from(selectedIds.value))
}

function deselectAll(): void {
  selectedIds.value = new Set()
  emit('selection-change', [])
}

// ── Sorting ──

function handleSort(col: SmartColumnConfig): void {
  if (!col.sortable || justDraggedColumn.value) return
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

function sortState(col: SmartColumnConfig): 'ascending' | 'descending' | 'none' {
  if (!col.sortable) return 'none'
  if (props.sortField === col.field) {
    return props.sortDirection === 'asc' ? 'ascending' : 'descending'
  }
  return 'none'
}

// ── Column filtering ──

const filterPopoverField = ref<string | null>(null)
const filterPopoverAnchor = ref<HTMLElement | null>(null)
const filterInputValue = ref('')

function hasFilter(col: SmartColumnConfig): boolean {
  return props.activeFilters.some((f) => f.field === col.field)
}

function openColumnFilter(e: MouseEvent, col: SmartColumnConfig): void {
  if (filterPopoverField.value === col.field) {
    filterPopoverField.value = null
    return
  }
  filterPopoverField.value = col.field
  filterPopoverAnchor.value = e.currentTarget as HTMLElement

  // Pre-fill with current filter value if exists
  const existing = props.activeFilters.find((f) => f.field === col.field)
  filterInputValue.value = existing ? String(existing.value ?? '') : ''

  nextTick(() => {
    const input = document.querySelector('.smart-table-filter-input') as HTMLInputElement
    if (input) input.focus()
  })
}

function applyColumnFilter(field: string): void {
  if (!filterInputValue.value.trim()) {
    // Remove filter
    const updated = props.activeFilters.filter((f) => f.field !== field)
    emit('filter', updated)
  } else {
    const existing = props.activeFilters.filter((f) => f.field !== field)
    const newFilter: FilterCondition = {
      field,
      operator: 'contains',
      value: filterInputValue.value.trim()
    }
    emit('filter', [...existing, newFilter])
  }
  filterPopoverField.value = null
}

function clearColumnFilter(field: string): void {
  const updated = props.activeFilters.filter((f) => f.field !== field)
  emit('filter', updated)
  filterPopoverField.value = null
}

function handleFilterKeydown(e: KeyboardEvent, field: string): void {
  if (e.key === 'Enter') {
    applyColumnFilter(field)
  } else if (e.key === 'Escape') {
    filterPopoverField.value = null
  }
}

// Close filter popover on outside click
function handleDocumentClick(e: MouseEvent): void {
  if (!filterPopoverField.value) return
  const target = e.target as HTMLElement
  if (target.closest('.smart-table-filter-popover') || target.closest('.smart-table-filter-trigger')) return
  filterPopoverField.value = null
}

onMounted(() => {
  document.addEventListener('click', handleDocumentClick)
})
onBeforeUnmount(() => {
  document.removeEventListener('click', handleDocumentClick)
})

// ── Inline editing (cell-level) ──

const editingCell = ref<{ rowId: string; field: string } | null>(null)
const editingValue = ref<unknown>(null)

function startInlineEdit(row: Record<string, unknown>, col: SmartColumnConfig): void {
  if (!props.enableInlineEdit) return
  const id = getRowKey(row)
  editingCell.value = { rowId: id, field: col.field }
  editingValue.value = row[col.field]
}

function commitInlineEdit(): void {
  if (!editingCell.value) return
  emit('inline-save', editingCell.value.rowId, editingCell.value.field, editingValue.value)
  editingCell.value = null
}

function cancelInlineEdit(): void {
  editingCell.value = null
}

function isEditingCellAt(rowId: string, field: string): boolean {
  return editingCell.value?.rowId === rowId && editingCell.value?.field === field
}

function handleCellKeydown(e: KeyboardEvent): void {
  if (e.key === 'Enter') {
    commitInlineEdit()
  } else if (e.key === 'Escape') {
    cancelInlineEdit()
  }
}

// ── Row Edit Mode (Phase C) ──

const rowEdit = useRowEdit()

function startRowEdit(row: Record<string, unknown>): void {
  const id = getRowKey(row)
  rowEdit.startEdit(id, row)
}

function handleRowEditSave(rowId: string, changes: Record<string, unknown>): void {
  emit('row-save', rowId, changes)
  rowEdit.cancelEdit()
}

function handleRowEditCancel(rowId: string): void {
  rowEdit.cancelEdit()
  emit('row-cancel', rowId)
}

// ── Column Resize (Phase C) ──

const tableContainerRef = ref<HTMLElement | null>(null)

const { resizingField, startResize, autoFitColumn } = useColumnResize({
  onResize: (field, width) => {
    setColumnWidth(field, width)
  }
})

function handleResizeMouseDown(col: SmartColumnConfig, event: MouseEvent): void {
  startResize(col.field, col.width ?? 150, event)
}

function handleResizeDblClick(col: SmartColumnConfig): void {
  if (tableContainerRef.value) {
    autoFitColumn(col.field, tableContainerRef.value)
  }
}

// ── Column Drag Reorder (Phase C) ──

const {
  isDragging: isColumnDragging,
  justDragged: justDraggedColumn,
  draggingField,
  dropIndicator,
  handleDragStart: columnDragStart,
  handleDragOver: columnDragOver,
  handleDragLeave: columnDragLeave,
  handleDrop: columnDrop,
  handleDragEnd: columnDragEnd
} = useColumnDragReorder({
  onReorder: (fromVisibleIdx, toVisibleIdx) => {
    // Map visible column indices to full (all columns) indices
    const fromField = visibleColumns.value[fromVisibleIdx]?.field
    const toField = visibleColumns.value[toVisibleIdx]?.field
    if (!fromField || !toField) return
    const allCols = columns.value
    const realFrom = allCols.findIndex((c) => c.field === fromField)
    const realTo = allCols.findIndex((c) => c.field === toField)
    if (realFrom >= 0 && realTo >= 0) {
      reorderColumns(realFrom, realTo)
    }
  }
})

// ── Virtual Scrolling (Phase C) ──

const scrollContainerRef = ref<HTMLElement | null>(null)

const shouldVirtualize = computed(() => {
  return props.enableVirtualScroll && props.data.length > props.virtualScrollThreshold
})

const dataCount = computed(() => props.data.length)

const { virtualRows, topPadding, bottomPadding } = useVirtualScroll({
  containerRef: scrollContainerRef,
  count: dataCount,
  rowHeight: props.rowHeight,
  overscan: 10,
})

// ── Row events ──

function handleRowClick(row: Record<string, unknown>): void {
  emit('row-click', row, getRowKey(row))
}

function handleRowDblClick(row: Record<string, unknown>): void {
  emit('row-dblclick', row, getRowKey(row))
}

// ── View Settings Dialog ──

const showViewSettings = ref(false)

const viewSettingsColumns = computed(() =>
  visibleColumns.value.map((col) => ({
    field: col.field,
    label: col.label,
    sortable: col.sortable,
    filterable: col.filterable,
    groupable: true,
  }))
)

function handleViewSettingsApply(settings: {
  sort?: { field: string; direction: 'asc' | 'desc' }
  filters: { field: string; operator: string; value: string }[]
  groupBy?: string
}): void {
  if (settings.sort) {
    emit('sort', settings.sort.field, settings.sort.direction)
  }
  if (settings.filters.length > 0) {
    const filters: FilterCondition[] = settings.filters.map((f) => ({
      field: f.field,
      operator: f.operator as FilterCondition['operator'],
      value: f.value,
    }))
    emit('filter', filters)
  }
}

// ── Column chooser ──

const showColumnChooser = ref(false)
const columnChooserAnchor = ref<HTMLElement | null>(null)

function toggleColumnChooser(e: MouseEvent): void {
  showColumnChooser.value = !showColumnChooser.value
  columnChooserAnchor.value = e.currentTarget as HTMLElement
}

function handleColumnChooserOutsideClick(e: MouseEvent): void {
  if (!showColumnChooser.value) return
  const target = e.target as HTMLElement
  if (target.closest('.smart-table-column-chooser')) return
  showColumnChooser.value = false
}

onMounted(() => {
  document.addEventListener('click', handleColumnChooserOutsideClick)
})
onBeforeUnmount(() => {
  document.removeEventListener('click', handleColumnChooserOutsideClick)
})

// ── Column chooser popover positioning ──

const chooserPopoverStyle = computed(() => {
  if (!columnChooserAnchor.value) return {}
  const rect = columnChooserAnchor.value.getBoundingClientRect()
  return {
    top: (rect.bottom + 4) + 'px',
    right: (document.documentElement.clientWidth - rect.right) + 'px'
  }
})

// ── Column drag for reorder in chooser ──

let dragFromIndex = -1

function onChooserDragStart(e: DragEvent, index: number): void {
  dragFromIndex = index
  if (e.dataTransfer) {
    e.dataTransfer.effectAllowed = 'move'
    e.dataTransfer.setData('text/plain', String(index))
  }
}

function onChooserDrop(_e: DragEvent, toIndex: number): void {
  if (dragFromIndex >= 0 && dragFromIndex !== toIndex) {
    reorderColumns(dragFromIndex, toIndex)
  }
  dragFromIndex = -1
}

// ── Export dropdown (Phase C) ──

const showExportMenu = ref(false)

function toggleExportMenu(): void {
  showExportMenu.value = !showExportMenu.value
}

function handleExportCsv(): void {
  showExportMenu.value = false
  emit('export', 'csv')
}

function handleExportXlsx(): void {
  showExportMenu.value = false
  emit('export', 'xlsx')
}

// ── Bulk Actions (Phase C) ──

function handleBulkDelete(): void {
  emit('bulk-delete', Array.from(selectedIds.value))
}

function handleBulkExportCsv(): void {
  emit('bulk-export', 'csv', Array.from(selectedIds.value))
}

function handleBulkExportXlsx(): void {
  emit('bulk-export', 'xlsx', Array.from(selectedIds.value))
}

// ── Class helpers for typed UI components ──

function columnHeadClass(col: SmartColumnConfig): string {
  const parts: string[] = ['select-none', 'group', 'relative']
  if (col.sortable) parts.push('cursor-pointer')
  if (isColumnDragging.value && draggingField.value === col.field) parts.push('opacity-50')
  return parts.join(' ')
}

function rowClass(row: Record<string, unknown>): string {
  const parts: string[] = ['cursor-pointer', 'hover:bg-muted/50']
  if (isSelected(row)) parts.push('bg-muted/30')
  return parts.join(' ')
}

function cellClass(): string {
  if (props.compactMode) return 'py-1 px-2 text-xs'
  return ''
}

// ── Colspan calculation ──

const totalColspan = computed(() => {
  let count = visibleColumns.value.length + 1 // +1 for actions
  if (props.selectionMode !== 'none') count++
  return count
})

// ── Fixed column widths ──

const SELECTION_COL_WIDTH = 40
const ACTIONS_COL_WIDTH = 80

// ── ObjectIdentifier: first non-key, non-UUID column becomes clickable identifier ──
const identifierField = computed(() => {
  const col = visibleColumns.value.find(c => !c.isKey && c.type !== 'UUID' && c.type === 'String')
  return col?.field ?? null
})

function getIdentifierSubtitle(row: Record<string, unknown>): string {
  const id = getRowKey(row)
  return id ? id.substring(0, 8) + '...' : ''
}
</script>

<template>
  <div class="space-y-3">
    <!-- Toolbar -->
    <div class="flex items-center justify-between">
      <div class="flex items-center gap-2">
        <h3 v-if="title" class="text-lg font-semibold text-foreground">{{ title }}</h3>
        <Badge variant="secondary">{{ totalCount }} records</Badge>
        <slot name="toolbar-start" />
      </div>
      <div class="flex items-center gap-2">
        <slot name="toolbar-end" />
        <!-- Export (dropdown with CSV / Excel) -->
        <div v-if="enableExport" class="relative">
          <Button
            variant="outline"
            size="sm"
            @click="toggleExportMenu"
          >
            <Download class="h-4 w-4 mr-1" />
            Export
            <ChevronDown class="h-3 w-3 ml-1" />
          </Button>
          <div
            v-if="showExportMenu"
            class="absolute right-0 top-full mt-1 w-40 rounded-md border bg-popover py-1 shadow-md z-50"
          >
            <button
              class="flex w-full items-center px-3 py-1.5 text-sm hover:bg-accent"
              @click="handleExportCsv"
            >
              Export as CSV
            </button>
            <button
              class="flex w-full items-center px-3 py-1.5 text-sm hover:bg-accent"
              @click="handleExportXlsx"
            >
              Export as Excel
            </button>
          </div>
        </div>

        <!-- View Settings -->
        <Button
          variant="outline"
          size="sm"
          title="View Settings"
          @click="showViewSettings = true"
        >
          <SlidersHorizontal class="h-4 w-4" />
        </Button>

        <!-- Column chooser -->
        <div v-if="enableColumnChooser" class="relative smart-table-column-chooser">
          <Button
            variant="outline"
            size="sm"
            :title="'Configure columns'"
            @click.stop="toggleColumnChooser"
          >
            <Settings2 class="h-4 w-4" />
          </Button>

          <!-- Column chooser popover -->
          <Teleport to="body">
            <div
              v-if="showColumnChooser && columnChooserAnchor"
              class="smart-table-column-chooser fixed z-50 w-64 rounded-md border bg-popover p-3 shadow-md"
              :style="chooserPopoverStyle"
            >
              <div class="flex items-center justify-between mb-2">
                <span class="text-sm font-medium">Columns</span>
                <button
                  class="text-xs text-muted-foreground hover:text-foreground"
                  @click="resetColumns(); showColumnChooser = false"
                >
                  Reset
                </button>
              </div>
              <div class="max-h-64 overflow-y-auto space-y-1">
                <div
                  v-for="(col, idx) in columns"
                  :key="col.field"
                  class="flex items-center gap-2 py-1 px-1 rounded hover:bg-accent text-sm cursor-move"
                  :draggable="true"
                  @dragstart="onChooserDragStart($event, idx)"
                  @dragover.prevent
                  @drop.prevent="onChooserDrop($event, idx)"
                >
                  <GripVertical class="h-3.5 w-3.5 text-muted-foreground flex-shrink-0" />
                  <label class="flex items-center gap-2 flex-1 cursor-pointer select-none">
                    <input
                      type="checkbox"
                      :checked="col.visible"
                      class="h-3.5 w-3.5 rounded border-input accent-primary"
                      @change="toggleColumn(col.field)"
                    />
                    <span class="truncate">{{ col.label }}</span>
                  </label>
                  <Badge v-if="col.isKey" variant="outline" class="text-[10px] px-1 py-0">Key</Badge>
                </div>
              </div>
            </div>
          </Teleport>
        </div>

        <!-- Refresh -->
        <Button
          variant="outline"
          size="sm"
          :title="'Refresh data'"
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

    <!-- Mobile: ResponsiveTable -->
    <ResponsiveTable
      v-if="isMobile && data.length > 0"
      :columns="responsiveColumns"
      :data="data"
      @row-click="(row: Record<string, unknown>) => handleRowClick(row)"
    />

    <!-- Desktop: Table container -->
    <div
      v-else
      ref="tableContainerRef"
      class="border rounded-md relative overflow-hidden"
      :style="{ maxHeight: maxHeight }"
    >
      <!-- Loading overlay -->
      <div
        v-if="isLoading"
        class="absolute inset-0 bg-background/50 z-10 flex items-center justify-center"
        aria-live="polite"
      >
        <Spinner size="lg" role="status" aria-label="Loading data" />
      </div>

      <div
        ref="scrollContainerRef"
        class="overflow-x-auto overflow-y-auto"
        :style="{ maxHeight: maxHeight }"
      >
        <Table class="table-fixed" role="grid" :aria-rowcount="totalCount">
          <!-- Colgroup for consistent column widths -->
          <colgroup>
            <col v-if="selectionMode !== 'none'" :style="{ width: SELECTION_COL_WIDTH + 'px' }" />
            <col
              v-for="col in visibleColumns"
              :key="'cg-' + col.field"
              :style="{ width: (col.width ?? 150) + 'px' }"
            />
            <col :style="{ width: ACTIONS_COL_WIDTH + 'px' }" />
          </colgroup>

          <TableHeader class="sticky top-0 bg-background z-[5]">
            <TableRow role="row">
              <!-- Selection column -->
              <TableHead
                v-if="selectionMode !== 'none'"
                role="columnheader"
                scope="col"
              >
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

              <!-- Data columns -->
              <TableHead
                v-for="(col, colIdx) in visibleColumns"
                :key="col.field"
                :data-header-field="col.field"
                :style="{ textAlign: col.align }"
                :class="columnHeadClass(col)"
                :draggable="!col.isKey"
                role="columnheader"
                scope="col"
                :aria-sort="col.sortable ? sortState(col) : undefined"
                @click="handleSort(col)"
                @dragstart="!col.isKey && columnDragStart(col.field, colIdx, $event)"
                @dragover="!col.isKey && columnDragOver(colIdx, $event)"
                @dragleave="columnDragLeave"
                @drop="!col.isKey && columnDrop(colIdx, $event)"
                @dragend="columnDragEnd"
              >
                <!-- Drop indicator (left) -->
                <div
                  v-if="dropIndicator && dropIndicator.index === colIdx && dropIndicator.side === 'left'"
                  class="absolute left-0 top-0 bottom-0 w-0.5 bg-primary z-10"
                />
                <!-- Drop indicator (right) -->
                <div
                  v-if="dropIndicator && dropIndicator.index === colIdx && dropIndicator.side === 'right'"
                  class="absolute right-0 top-0 bottom-0 w-0.5 bg-primary z-10"
                />

                <div class="flex items-center gap-1">
                  <!-- Custom header slot -->
                  <slot
                    v-if="$slots[`header-${col.field}`]"
                    :name="`header-${col.field}`"
                    :column="col"
                  />
                  <!-- Default header -->
                  <template v-else>
                    <span class="truncate">{{ col.label }}</span>
                    <ArrowUp
                      v-if="isSortedAsc(col)"
                      class="h-3.5 w-3.5 flex-shrink-0"
                    />
                    <ArrowDown
                      v-else-if="isSortedDesc(col)"
                      class="h-3.5 w-3.5 flex-shrink-0"
                    />
                  </template>
                  <!-- Filter trigger -->
                  <button
                    v-if="col.filterable"
                    class="smart-table-filter-trigger inline-flex items-center justify-center h-6 w-6 rounded-sm hover:bg-accent transition-colors flex-shrink-0"
                    :class="hasFilter(col) ? 'text-primary' : 'text-muted-foreground opacity-0 group-hover:opacity-100'"
                    :aria-label="'Filter ' + col.label"
                    @click.stop="openColumnFilter($event, col)"
                  >
                    <Filter class="h-3.5 w-3.5" />
                  </button>
                </div>

                <!-- Resize handle -->
                <div
                  class="absolute right-0 top-0 bottom-0 w-1 cursor-col-resize hover:bg-primary/30 z-10"
                  :class="resizingField === col.field ? 'bg-primary/50' : ''"
                  @mousedown.stop="handleResizeMouseDown(col, $event)"
                  @dblclick.stop="handleResizeDblClick(col)"
                />

                <!-- Filter popover (inline teleport) -->
                <Teleport to="body">
                  <div
                    v-if="filterPopoverField === col.field && filterPopoverAnchor"
                    class="smart-table-filter-popover fixed z-50 w-56 rounded-md border bg-popover p-3 shadow-md"
                    :style="{
                      top: (filterPopoverAnchor.getBoundingClientRect().bottom + 4) + 'px',
                      left: filterPopoverAnchor.getBoundingClientRect().left + 'px'
                    }"
                    @click.stop
                  >
                    <div class="space-y-2">
                      <label class="text-xs font-medium text-muted-foreground">
                        Filter "{{ col.label }}"
                      </label>
                      <input
                        v-model="filterInputValue"
                        type="text"
                        class="smart-table-filter-input flex h-8 w-full rounded-md border border-input bg-background px-2 py-1 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
                        placeholder="Filter value..."
                        @keydown="handleFilterKeydown($event, col.field)"
                      />
                      <div class="flex items-center justify-end gap-1">
                        <Button
                          variant="ghost"
                          size="sm"
                          class="h-7 px-2 text-xs"
                          @click="clearColumnFilter(col.field)"
                        >
                          Clear
                        </Button>
                        <Button
                          size="sm"
                          class="h-7 px-2 text-xs"
                          @click="applyColumnFilter(col.field)"
                        >
                          <Check class="h-3 w-3 mr-1" />
                          Apply
                        </Button>
                      </div>
                    </div>
                  </div>
                </Teleport>
              </TableHead>

              <!-- Actions column -->
              <TableHead class="text-right" role="columnheader" scope="col">Actions</TableHead>
            </TableRow>
          </TableHeader>

          <TableBody>
            <!-- Virtual scroll path -->
            <template v-if="shouldVirtualize">
              <!-- Top spacer -->
              <tr v-if="topPadding > 0">
                <td :colspan="totalColspan" :style="{ height: topPadding + 'px', padding: 0, border: 'none' }" />
              </tr>

              <template v-for="vRow in virtualRows" :key="vRow.key">
                <!-- Row edit mode -->
                <SmartTableEditRow
                  v-if="enableRowEdit && rowEdit.editingRowId.value === getRowKey(data[vRow.index])"
                  :columns="visibleColumns"
                  :rowData="data[vRow.index]"
                  :rowId="getRowKey(data[vRow.index])"
                  :metadata="metadata"
                  :module="module"
                  :entitySet="entitySet"
                  :selectionMode="selectionMode"
                  @save="handleRowEditSave"
                  @cancel="handleRowEditCancel"
                />

                <!-- Normal display row -->
                <TableRow
                  v-else
                  role="row"
                  :class="rowClass(data[vRow.index])"
                  @click="handleRowClick(data[vRow.index])"
                  @dblclick="handleRowDblClick(data[vRow.index])"
                >
                  <!-- Selection -->
                  <TableCell
                    v-if="selectionMode !== 'none'"
                    role="gridcell"
                    @click.stop
                  >
                    <input
                      :type="selectionMode === 'multi' ? 'checkbox' : 'radio'"
                      :checked="isSelected(data[vRow.index])"
                      :name="selectionMode === 'single' ? 'smart-table-selection' : undefined"
                      aria-label="Select row"
                      class="h-4 w-4 rounded border-input accent-primary cursor-pointer"
                      @change="toggleSelection(data[vRow.index])"
                    />
                  </TableCell>

                  <!-- Data cells -->
                  <TableCell
                    v-for="col in visibleColumns"
                    :key="col.field"
                    :data-field="col.field"
                    :style="{ textAlign: col.align }"
                    :class="cellClass()"
                    role="gridcell"
                  >
                    <slot
                      v-if="$slots[`cell-${col.field}`]"
                      :name="`cell-${col.field}`"
                      :value="data[vRow.index][col.field]"
                      :row="data[vRow.index]"
                      :column="col"
                      :rowId="getRowKey(data[vRow.index])"
                    />
                    <ObjectIdentifier
                      v-else-if="col.field === identifierField"
                      :title="String(data[vRow.index][col.field] ?? '-')"
                      :text="getIdentifierSubtitle(data[vRow.index])"
                      :titleActive="true"
                      :emphasized="true"
                      @title-click="handleRowClick(data[vRow.index])"
                    />
                    <SmartCellDisplay
                      v-else
                      :column="col"
                      :value="data[vRow.index][col.field]"
                      :row="data[vRow.index]"
                    />
                  </TableCell>

                  <!-- Actions -->
                  <TableCell class="text-right" role="gridcell">
                    <div class="flex justify-end gap-1" @click.stop>
                      <slot
                        v-if="$slots['row-actions']"
                        name="row-actions"
                        :row="data[vRow.index]"
                        :rowId="getRowKey(data[vRow.index])"
                      />
                      <template v-else>
                        <Button
                          v-if="enableRowEdit"
                          variant="ghost"
                          size="icon"
                          class="h-7 w-7"
                          title="Edit row"
                          @click="startRowEdit(data[vRow.index])"
                        >
                          <Pencil class="h-3.5 w-3.5" />
                        </Button>
                        <Button
                          v-else
                          variant="ghost"
                          size="icon"
                          class="h-7 w-7"
                          title="Edit"
                          @click="emit('edit', getRowKey(data[vRow.index]))"
                        >
                          <Pencil class="h-3.5 w-3.5" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon"
                          class="h-7 w-7 text-destructive hover:text-destructive"
                          title="Delete"
                          @click="emit('delete', getRowKey(data[vRow.index]))"
                        >
                          <Trash2 class="h-3.5 w-3.5" />
                        </Button>
                      </template>
                    </div>
                  </TableCell>
                </TableRow>
              </template>

              <!-- Bottom spacer -->
              <tr v-if="bottomPadding > 0">
                <td :colspan="totalColspan" :style="{ height: bottomPadding + 'px', padding: 0, border: 'none' }" />
              </tr>
            </template>

            <!-- Normal (non-virtual) path -->
            <template v-else>
              <template v-for="row in data" :key="getRowKey(row)">
                <!-- Row edit mode -->
                <SmartTableEditRow
                  v-if="enableRowEdit && rowEdit.editingRowId.value === getRowKey(row)"
                  :columns="visibleColumns"
                  :rowData="row"
                  :rowId="getRowKey(row)"
                  :metadata="metadata"
                  :module="module"
                  :entitySet="entitySet"
                  :selectionMode="selectionMode"
                  @save="handleRowEditSave"
                  @cancel="handleRowEditCancel"
                />

                <!-- Normal display row -->
                <TableRow
                  v-else
                  role="row"
                  :class="rowClass(row)"
                  @click="handleRowClick(row)"
                  @dblclick="handleRowDblClick(row)"
                >
                  <!-- Selection -->
                  <TableCell
                    v-if="selectionMode !== 'none'"
                    role="gridcell"
                    @click.stop
                  >
                    <input
                      :type="selectionMode === 'multi' ? 'checkbox' : 'radio'"
                      :checked="isSelected(row)"
                      :name="selectionMode === 'single' ? 'smart-table-selection' : undefined"
                      aria-label="Select row"
                      class="h-4 w-4 rounded border-input accent-primary cursor-pointer"
                      @change="toggleSelection(row)"
                    />
                  </TableCell>

                  <!-- Data cells -->
                  <TableCell
                    v-for="col in visibleColumns"
                    :key="col.field"
                    :data-field="col.field"
                    :style="{ textAlign: col.align }"
                    :class="cellClass()"
                    role="gridcell"
                  >
                    <!-- Custom cell slot -->
                    <slot
                      v-if="$slots[`cell-${col.field}`]"
                      :name="`cell-${col.field}`"
                      :value="row[col.field]"
                      :row="row"
                      :column="col"
                      :rowId="getRowKey(row)"
                    />

                    <!-- Inline edit mode -->
                    <template v-else-if="enableInlineEdit && isEditingCellAt(getRowKey(row), col.field)">
                      <input
                        v-model="editingValue"
                        type="text"
                        class="flex h-7 w-full rounded-md border border-input bg-background px-2 py-1 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                        @keydown="handleCellKeydown"
                        @blur="commitInlineEdit"
                      />
                    </template>

                    <!-- Default display mode -->
                    <template v-else>
                      <div
                        :class="enableInlineEdit ? 'cursor-text' : ''"
                        @dblclick.stop="enableInlineEdit ? startInlineEdit(row, col) : undefined"
                      >
                        <!-- ObjectIdentifier for the first string column -->
                        <ObjectIdentifier
                          v-if="col.field === identifierField"
                          :title="String(row[col.field] ?? '-')"
                          :text="getIdentifierSubtitle(row)"
                          :titleActive="true"
                          :emphasized="true"
                          @title-click="handleRowClick(row)"
                        />
                        <SmartCellDisplay
                          v-else
                          :column="col"
                          :value="row[col.field]"
                          :row="row"
                        />
                      </div>
                    </template>
                  </TableCell>

                  <!-- Actions -->
                  <TableCell class="text-right" role="gridcell">
                    <div class="flex justify-end gap-1" @click.stop>
                      <slot
                        v-if="$slots['row-actions']"
                        name="row-actions"
                        :row="row"
                        :rowId="getRowKey(row)"
                      />
                      <template v-else>
                        <Button
                          v-if="enableRowEdit"
                          variant="ghost"
                          size="icon"
                          class="h-7 w-7"
                          title="Edit row"
                          @click="startRowEdit(row)"
                        >
                          <Pencil class="h-3.5 w-3.5" />
                        </Button>
                        <Button
                          v-else
                          variant="ghost"
                          size="icon"
                          class="h-7 w-7"
                          title="Edit"
                          @click="emit('edit', getRowKey(row))"
                        >
                          <Pencil class="h-3.5 w-3.5" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon"
                          class="h-7 w-7 text-destructive hover:text-destructive"
                          title="Delete"
                          @click="emit('delete', getRowKey(row))"
                        >
                          <Trash2 class="h-3.5 w-3.5" />
                        </Button>
                      </template>
                    </div>
                  </TableCell>
                </TableRow>
              </template>
            </template>

            <!-- Empty state -->
            <TableRow v-if="data.length === 0 && !isLoading">
              <TableCell
                :colspan="totalColspan"
                class="text-center py-12 text-muted-foreground"
              >
                <slot name="empty">
                  No records found
                </slot>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
    </div>

    <!-- Bulk Action Toolbar (Phase C) -->
    <BulkActionToolbar
      v-if="enableBulkActions"
      :selectedCount="selectedIds.size"
      :totalCount="totalCount"
      :enableExport="enableExport"
      @select-all="selectAll"
      @deselect-all="deselectAll"
      @delete-selected="handleBulkDelete"
      @export-csv="handleBulkExportCsv"
      @export-xlsx="handleBulkExportXlsx"
    />

    <!-- Pagination -->
    <SmartTablePagination
      :currentPage="currentPage"
      :pageSize="pageSize"
      :totalCount="totalCount"
      @page-change="emit('page', $event)"
      @page-size-change="emit('page-size', $event)"
    />

    <!-- View Settings Dialog -->
    <ViewSettingsDialog
      :open="showViewSettings"
      :columns="viewSettingsColumns"
      :currentSort="sortField ? { field: sortField, direction: sortDirection } : undefined"
      :currentFilters="activeFilters.map((f) => ({ field: f.field, operator: f.operator, value: String(f.value ?? '') }))"
      @update:open="showViewSettings = $event"
      @apply="handleViewSettingsApply"
    />
  </div>
</template>
