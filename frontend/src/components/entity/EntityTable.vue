<script setup lang="ts">
import { ref, computed, watch, onBeforeUnmount } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { usePreferences } from '@/utils/preferences'
import type { FieldMetadata, AssociationMetadata } from '@/types/metadata'
import type { SortOption, FilterCondition } from '@/types/odata'
import type { ColumnConfig } from '@/composables/useColumnConfig'
import type { CellEditState } from '@/composables/useInlineEdit'
import {
  Table,
  TableHeader,
  TableRow,
  TableHead
} from '@/components/ui/table'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import EditableCell from './EditableCell.vue'
import FilterPopover from './FilterPopover.vue'
import { ArrowUp, ArrowDown, Pencil, Trash2, Filter } from 'lucide-vue-next'
import { formatValue } from '@/utils/formValidator'
import { findAssociationForField, getExpandedDisplayValue } from '@/utils/associationDisplay'

interface Props {
  data: Record<string, unknown>[]
  fields: FieldMetadata[]
  keyField: string
  isLoading?: boolean
  sortOptions?: SortOption[]
  selectedId?: string | null
  associations?: AssociationMetadata[]
  // Phase 2 props
  columns?: ColumnConfig[]
  // Row selection
  selectionEnabled?: boolean
  selectedRowIds?: Set<string>
  isAllSelected?: boolean
  isIndeterminate?: boolean
  // Inline edit
  editingCell?: CellEditState | null
  isCellSaving?: boolean
  cellSaveError?: string | null
  // Phase 3: Active filters
  activeFilters?: FilterCondition[]
}

const props = withDefaults(defineProps<Props>(), {
  isLoading: false,
  sortOptions: () => [],
  selectedId: null,
  associations: () => [],
  columns: undefined,
  selectionEnabled: false,
  selectedRowIds: () => new Set<string>(),
  isAllSelected: false,
  isIndeterminate: false,
  editingCell: null,
  isCellSaving: false,
  cellSaveError: null,
  activeFilters: () => []
})

const emit = defineEmits<{
  // Existing
  sort: [field: string]
  view: [id: string]
  edit: [id: string]
  delete: [id: string]
  // Phase 2: Row selection
  'toggle-row': [id: string]
  'toggle-all': []
  // Phase 2: Inline editing
  'start-cell-edit': [rowId: string, field: string, value: unknown]
  'update-cell-value': [value: unknown]
  'commit-cell-edit': []
  'cancel-cell-edit': []
  // Phase 2: Column config
  'resize-column': [field: string, width: number]
  'reorder-column': [fromIndex: number, toIndex: number]
  // Phase 3: Filter events
  'apply-filter': [filter: FilterCondition]
  'apply-between-filter': [field: string, min: unknown, max: unknown, type: 'number' | 'date']
  'clear-filter': [field: string]
}>()

// ── Delayed loading overlay (flicker fix) ──
const showLoadingOverlay = ref(false)
let loadingTimer: ReturnType<typeof setTimeout> | null = null

watch(() => props.isLoading, (loading) => {
  if (loading) {
    loadingTimer = setTimeout(() => {
      showLoadingOverlay.value = true
    }, 300)
  } else {
    if (loadingTimer) {
      clearTimeout(loadingTimer)
      loadingTimer = null
    }
    showLoadingOverlay.value = false
  }
})

// ── Phase 3: Filter helpers ──
function getFilterForField(fieldName: string): FilterCondition | null {
  return props.activeFilters.find(f => f.field === fieldName) ?? null
}

function hasActiveFilter(fieldName: string): boolean {
  return props.activeFilters.some(f => f.field === fieldName)
}

// ── Effective columns (from props or fallback) ──
const effectiveColumns = computed<ColumnConfig[]>(() => {
  if (props.columns) {
    return props.columns
      .filter((c) => c.visible)
      .sort((a, b) => a.order - b.order)
  }
  // Fallback: first 6 non-computed, non-key fields with default widths
  return props.fields
    .filter((f) => !f.isComputed && f.name !== props.keyField)
    .slice(0, 6)
    .map((f, index) => ({
      field: f.name,
      visible: true,
      width: 150,
      order: index
    }))
})

// ── Preferences: list view mode ──
const { preferences } = usePreferences()
const isCompact = computed(() => preferences.value.listViewMode === 'compact')

// ── Virtual scrolling ──
const ROW_HEIGHT = computed(() => isCompact.value ? 32 : 40)
const parentRef = ref<HTMLElement | null>(null)

const virtualizer = useVirtualizer(computed(() => ({
  count: props.data.length,
  getScrollElement: () => parentRef.value,
  estimateSize: () => ROW_HEIGHT.value,
  overscan: 5
})))

const virtualRows = computed(() => virtualizer.value.getVirtualItems())
const totalSize = computed(() => virtualizer.value.getTotalSize())

// ── Sorting ──
function getSortDirection(fieldName: string): 'asc' | 'desc' | null {
  const sort = props.sortOptions.find((s) => s.field === fieldName)
  return sort?.direction ?? null
}

function handleSort(fieldName: string) {
  emit('sort', fieldName)
}

// ── Cell value helpers ──
function getEntityId(row: Record<string, unknown>): string {
  return String(row[props.keyField])
}

function getFieldDisplayName(fieldName: string): string {
  const field = props.fields.find((f) => f.name === fieldName)
  return field?.displayName || fieldName
}

function getFieldMeta(fieldName: string): FieldMetadata | undefined {
  return props.fields.find((f) => f.name === fieldName)
}

function getCellDisplayValue(row: Record<string, unknown>, fieldName: string): string {
  const field = getFieldMeta(fieldName)
  if (!field) return String(row[fieldName] ?? '')

  const assoc = findAssociationForField(field.name, props.associations)
  if (assoc) {
    const expanded = row[assoc.name]
    const display = getExpandedDisplayValue(expanded)
    if (display) return display
  }
  const value = row[field.name]
  return formatValue(value, field.type, field.enumValues)
}

// ── Row selection helpers ──
function isRowChecked(index: number): boolean {
  const row = props.data[index]
  if (!row) return false
  return props.selectedRowIds.has(getEntityId(row))
}

function rowIsSelected(index: number): boolean {
  if (!props.selectedId) return false
  const row = props.data[index]
  if (!row) return false
  return props.selectedId === getEntityId(row)
}

// ── Inline edit helpers ──
function isEditingCell(rowId: string, fieldName: string): boolean {
  if (!props.editingCell) return false
  return props.editingCell.rowId === rowId && props.editingCell.field === fieldName
}

// ── Column resize (mouse events) ──
let resizingField = ''
let startX = 0
let startWidth = 0

function startResize(e: MouseEvent, col: ColumnConfig) {
  resizingField = col.field
  startX = e.clientX
  startWidth = col.width
  document.addEventListener('mousemove', onResizeMove)
  document.addEventListener('mouseup', onResizeEnd)
}

function onResizeMove(e: MouseEvent) {
  const diff = e.clientX - startX
  emit('resize-column', resizingField, Math.max(80, startWidth + diff))
}

function onResizeEnd() {
  document.removeEventListener('mousemove', onResizeMove)
  document.removeEventListener('mouseup', onResizeEnd)
}

// ── Column reorder (drag & drop) ──
let dragField = ''

function onDragStart(e: DragEvent, col: ColumnConfig) {
  dragField = col.field
  e.dataTransfer?.setData('text/plain', col.field)
  if (e.dataTransfer) {
    e.dataTransfer.effectAllowed = 'move'
  }
}

function onDrop(_e: DragEvent, targetCol: ColumnConfig) {
  const fromIdx = effectiveColumns.value.findIndex((c) => c.field === dragField)
  const toIdx = effectiveColumns.value.findIndex((c) => c.field === targetCol.field)
  if (fromIdx !== -1 && toIdx !== -1 && fromIdx !== toIdx) {
    emit('reorder-column', fromIdx, toIdx)
  }
}

// Cleanup on unmount
onBeforeUnmount(() => {
  document.removeEventListener('mousemove', onResizeMove)
  document.removeEventListener('mouseup', onResizeEnd)
  if (loadingTimer) {
    clearTimeout(loadingTimer)
    loadingTimer = null
  }
})
</script>

<template>
  <div class="border rounded-md relative overflow-x-auto -mx-4 sm:mx-0">
    <!-- Loading overlay (delayed to prevent flicker) -->
    <div
      v-if="showLoadingOverlay"
      class="absolute inset-0 bg-background/50 flex items-center justify-center z-30"
    >
      <Spinner size="lg" />
    </div>

    <!-- Header (sticky table header) -->
    <div class="min-w-[640px]">
      <Table>
        <TableHeader>
          <TableRow>
            <!-- Checkbox column header -->
            <TableHead v-if="selectionEnabled" class="w-10" scope="col">
              <input
                type="checkbox"
                :checked="isAllSelected"
                :indeterminate.prop="isIndeterminate"
                aria-label="Select all rows"
                class="h-4 w-4 rounded border-input accent-primary cursor-pointer"
                @change="emit('toggle-all')"
              />
            </TableHead>
            <!-- Data columns with sort, drag, resize, and filter -->
            <TableHead
              v-for="col in effectiveColumns"
              :key="col.field"
              :style="{ width: col.width + 'px', minWidth: col.width + 'px' }"
              class="cursor-pointer select-none relative group"
              :draggable="true"
              @click="handleSort(col.field)"
              @dragstart="onDragStart($event, col)"
              @dragover.prevent
              @drop.prevent="onDrop($event, col)"
            >
              <div class="flex items-center gap-1">
                <span class="truncate">{{ getFieldDisplayName(col.field) }}</span>
                <ArrowUp
                  v-if="getSortDirection(col.field) === 'asc'"
                  class="h-4 w-4 flex-shrink-0"
                />
                <ArrowDown
                  v-else-if="getSortDirection(col.field) === 'desc'"
                  class="h-4 w-4 flex-shrink-0"
                />
                <!-- Filter popover trigger -->
                <span @click.stop class="flex-shrink-0">
                  <FilterPopover
                    :fieldName="col.field"
                    :fieldType="getFieldMeta(col.field)?.type ?? 'String'"
                    :enumValues="getFieldMeta(col.field)?.enumValues"
                    :currentFilter="getFilterForField(col.field)"
                    @apply="emit('apply-filter', $event)"
                    @apply-between="(field: string, min: unknown, max: unknown, betweenType: 'number' | 'date') => emit('apply-between-filter', field, min, max, betweenType)"
                    @clear="emit('clear-filter', col.field)"
                  >
                    <button
                      class="inline-flex items-center justify-center h-6 w-6 rounded-sm hover:bg-accent transition-colors"
                      :class="hasActiveFilter(col.field) ? 'text-primary' : 'text-muted-foreground opacity-0 group-hover:opacity-100'"
                      :aria-label="`Filter ${getFieldDisplayName(col.field)}`"
                    >
                      <Filter class="h-3.5 w-3.5" aria-hidden="true" />
                    </button>
                  </FilterPopover>
                </span>
              </div>
              <!-- Resize handle -->
              <div
                class="absolute right-0 top-0 bottom-0 w-1 cursor-col-resize hover:bg-primary/50 opacity-0 group-hover:opacity-100 transition-opacity"
                @mousedown.stop.prevent="startResize($event, col)"
                @click.stop
              />
            </TableHead>
            <TableHead class="w-[80px]" scope="col">Actions</TableHead>
          </TableRow>
        </TableHeader>
      </Table>
    </div>

    <!-- Virtual scrolled body -->
    <div
      ref="parentRef"
      class="max-h-[600px] overflow-auto min-w-[640px]"
      style="contain: layout paint;"
    >
      <div
        :style="{ height: `${totalSize}px`, width: '100%', position: 'relative' }"
      >
        <div
          v-for="virtualRow in virtualRows"
          :key="virtualRow.index"
          :data-index="virtualRow.index"
          :style="{
            position: 'absolute',
            top: 0,
            left: 0,
            width: '100%',
            transform: `translateY(${virtualRow.start}px)`,
            height: `${ROW_HEIGHT}px`
          }"
          :class="[
            'flex items-center border-b cursor-pointer transition-colors',
            isCompact ? 'px-2' : 'px-4',
            rowIsSelected(virtualRow.index) ? 'bg-muted' : 'hover:bg-muted/50'
          ]"
          @click="emit('view', getEntityId(data[virtualRow.index]))"
        >
          <!-- Selection checkbox -->
          <div
            v-if="selectionEnabled"
            class="w-10 flex-shrink-0 flex items-center"
            @click.stop
          >
            <input
              type="checkbox"
              :checked="isRowChecked(virtualRow.index)"
              aria-label="Select row"
              class="h-4 w-4 rounded border-input accent-primary cursor-pointer"
              @change="emit('toggle-row', getEntityId(data[virtualRow.index]))"
            />
          </div>

          <!-- Data cells -->
          <div
            v-for="col in effectiveColumns"
            :key="col.field"
            :style="{ width: col.width + 'px', minWidth: col.width + 'px' }"
            :class="['flex-shrink-0 truncate', isCompact ? 'px-1 py-0.5 text-xs' : 'px-2 py-1 text-sm']"
            @click.stop
          >
            <EditableCell
              :value="data[virtualRow.index][col.field]"
              :fieldType="getFieldMeta(col.field)?.type ?? 'String'"
              :fieldName="col.field"
              :isEditing="isEditingCell(getEntityId(data[virtualRow.index]), col.field)"
              :isSaving="isCellSaving && isEditingCell(getEntityId(data[virtualRow.index]), col.field)"
              :error="isEditingCell(getEntityId(data[virtualRow.index]), col.field) ? cellSaveError : null"
              :enumValues="getFieldMeta(col.field)?.enumValues ?? []"
              :maxLength="getFieldMeta(col.field)?.maxLength"
              :isReadOnly="getFieldMeta(col.field)?.isReadOnly ?? false"
              :isComputed="getFieldMeta(col.field)?.isComputed ?? false"
              :displayValue="getCellDisplayValue(data[virtualRow.index], col.field)"
              @start-edit="emit('start-cell-edit', getEntityId(data[virtualRow.index]), col.field, data[virtualRow.index][col.field])"
              @update-value="emit('update-cell-value', $event)"
              @commit="emit('commit-cell-edit')"
              @cancel="emit('cancel-cell-edit')"
            />
          </div>

          <!-- Action buttons -->
          <div class="w-[80px] flex-shrink-0 flex items-center gap-1 px-2" @click.stop>
            <Button
              variant="ghost"
              size="icon"
              title="Edit"
              aria-label="Edit"
              @click="emit('edit', getEntityId(data[virtualRow.index]))"
            >
              <Pencil class="h-4 w-4" aria-hidden="true" />
            </Button>
            <Button
              variant="ghost"
              size="icon"
              title="Delete"
              aria-label="Delete"
              class="text-destructive hover:text-destructive"
              @click="emit('delete', getEntityId(data[virtualRow.index]))"
            >
              <Trash2 class="h-4 w-4" aria-hidden="true" />
            </Button>
          </div>
        </div>
      </div>
    </div>

    <!-- Empty state -->
    <div v-if="data.length === 0 && !isLoading" class="text-center py-8 min-w-[640px] sm:min-w-0">
      <p class="text-muted-foreground">No data found</p>
    </div>
  </div>
</template>
