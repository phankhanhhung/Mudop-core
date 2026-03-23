import { ref, computed, watch, type Ref, type ComputedRef } from 'vue'
import type { FieldMetadata } from '@/types/metadata'

export interface ColumnConfig {
  field: string
  visible: boolean
  width: number // px
  order: number
}

export interface UseColumnConfigReturn {
  columns: Ref<ColumnConfig[]>
  visibleColumns: ComputedRef<ColumnConfig[]>
  toggleColumn: (field: string) => void
  setColumnWidth: (field: string, width: number) => void
  reorderColumns: (fromIndex: number, toIndex: number) => void
  resetToDefaults: () => void
  showAll: () => void
  hideAll: () => void
}

const DEFAULT_WIDTH = 150
const MIN_WIDTH = 80

interface StoredColumnEntry {
  visible: boolean
  width: number
  order: number
}

type StoredColumnConfig = Record<string, StoredColumnEntry>

function getStorageKey(entityKey: string): string {
  return `bmmdl-columns-${entityKey}`
}

function loadFromStorage(entityKey: string): StoredColumnConfig | null {
  try {
    const raw = localStorage.getItem(getStorageKey(entityKey))
    if (!raw) return null
    return JSON.parse(raw) as StoredColumnConfig
  } catch {
    return null
  }
}

function saveToStorage(entityKey: string, columns: ColumnConfig[]): void {
  const stored: StoredColumnConfig = {}
  for (const col of columns) {
    stored[col.field] = {
      visible: col.visible,
      width: col.width,
      order: col.order
    }
  }
  try {
    localStorage.setItem(getStorageKey(entityKey), JSON.stringify(stored))
  } catch {
    // localStorage full or unavailable — silently ignore
  }
}

function buildColumnsFromFields(
  fields: FieldMetadata[],
  saved: StoredColumnConfig | null
): ColumnConfig[] {
  return fields
    .map((f, index) => {
      const entry = saved?.[f.name]
      return {
        field: f.name,
        visible: entry != null ? entry.visible : true,
        width: entry != null ? Math.max(entry.width, MIN_WIDTH) : DEFAULT_WIDTH,
        order: entry != null ? entry.order : index
      }
    })
    .sort((a, b) => a.order - b.order)
}

export function useColumnConfig(
  entityKey: string,
  allFields: Ref<FieldMetadata[]>
): UseColumnConfigReturn {
  const columns = ref<ColumnConfig[]>([])

  // Initialize from current fields
  function initialize(): void {
    const fields = allFields.value
    if (fields.length === 0) {
      columns.value = []
      return
    }
    const saved = loadFromStorage(entityKey)
    columns.value = buildColumnsFromFields(fields, saved)
  }

  // Compute visible columns sorted by order
  const visibleColumns = computed<ColumnConfig[]>(() => {
    return columns.value
      .filter((c) => c.visible)
      .sort((a, b) => a.order - b.order)
  })

  // Persist on every change
  function persist(): void {
    if (columns.value.length > 0) {
      saveToStorage(entityKey, columns.value)
    }
  }

  function toggleColumn(field: string): void {
    const col = columns.value.find((c) => c.field === field)
    if (col) {
      col.visible = !col.visible
      persist()
    }
  }

  function setColumnWidth(field: string, width: number): void {
    const col = columns.value.find((c) => c.field === field)
    if (col) {
      col.width = Math.max(width, MIN_WIDTH)
      persist()
    }
  }

  function reorderColumns(fromIndex: number, toIndex: number): void {
    if (fromIndex === toIndex) return
    if (fromIndex < 0 || toIndex < 0) return
    if (fromIndex >= columns.value.length || toIndex >= columns.value.length) return

    // Work with sorted-by-order array to get stable reordering
    const sorted = [...columns.value].sort((a, b) => a.order - b.order)
    const [moved] = sorted.splice(fromIndex, 1)
    sorted.splice(toIndex, 0, moved)

    // Reassign order values
    sorted.forEach((col, idx) => {
      const original = columns.value.find((c) => c.field === col.field)
      if (original) {
        original.order = idx
      }
    })

    persist()
  }

  function resetToDefaults(): void {
    const fields = allFields.value
    columns.value = fields.map((f, index) => ({
      field: f.name,
      visible: true,
      width: DEFAULT_WIDTH,
      order: index
    }))
    // Remove persisted config so next load uses defaults
    try {
      localStorage.removeItem(getStorageKey(entityKey))
    } catch {
      // ignore
    }
  }

  function showAll(): void {
    for (const col of columns.value) {
      col.visible = true
    }
    persist()
  }

  function hideAll(): void {
    for (const col of columns.value) {
      col.visible = false
    }
    persist()
  }

  // Initialize on creation
  initialize()

  // Re-initialize when allFields changes (entity switch)
  watch(
    allFields,
    () => {
      initialize()
    },
    { deep: true }
  )

  return {
    columns,
    visibleColumns,
    toggleColumn,
    setColumnWidth,
    reorderColumns,
    resetToDefaults,
    showAll,
    hideAll
  }
}
