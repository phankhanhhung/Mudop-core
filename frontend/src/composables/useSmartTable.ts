import { ref, computed, watch, type ComputedRef, toValue, type MaybeRefOrGetter } from 'vue'
import type { EntityMetadata, FieldMetadata, FieldType, EnumValue } from '@/types/metadata'

export interface SmartColumnConfig {
  field: string
  label: string
  type: FieldType
  visible: boolean
  sortable: boolean
  filterable: boolean
  width?: number
  minWidth?: number
  align?: 'left' | 'center' | 'right'
  format?: (value: unknown) => string
  importance: number // 1=key, 2=required, 3=regular, 4=computed
  isKey: boolean
  isEnum: boolean
  enumValues?: Array<{ name: string; value: string | number; displayName?: string }>
  unit?: string
  cellRenderer?: import('vue').Component  // Optional plugin-provided cell renderer
  order: number
}

export interface UseSmartTableReturn {
  columns: ComputedRef<SmartColumnConfig[]>
  visibleColumns: ComputedRef<SmartColumnConfig[]>
  toggleColumn: (field: string) => void
  setColumnWidth: (field: string, width: number) => void
  reorderColumns: (fromIndex: number, toIndex: number) => void
  resetColumns: () => void
  saveLayout: (key: string) => void
  loadLayout: (key: string) => boolean
}

// ── Storage helpers ──

const STORAGE_PREFIX = 'bmmdl-smart-table-'

interface StoredLayout {
  columns: Array<{
    field: string
    visible: boolean
    width?: number
    order: number
  }>
}

function loadLayoutFromStorage(key: string): StoredLayout | null {
  try {
    const raw = localStorage.getItem(STORAGE_PREFIX + key)
    if (!raw) return null
    return JSON.parse(raw) as StoredLayout
  } catch {
    return null
  }
}

function saveLayoutToStorage(key: string, layout: StoredLayout): void {
  try {
    localStorage.setItem(STORAGE_PREFIX + key, JSON.stringify(layout))
  } catch {
    // localStorage full or unavailable — silently ignore
  }
}

// ── Column width heuristics ──

function inferWidth(field: FieldMetadata): number {
  switch (field.type) {
    case 'UUID':
      return 120
    case 'Boolean':
      return 80
    case 'Date':
      return 130
    case 'Time':
      return 110
    case 'DateTime':
    case 'Timestamp':
      return 180
    case 'Integer':
      return 100
    case 'Decimal':
      return 120
    case 'Enum':
      return 130
    case 'String': {
      if (field.maxLength && field.maxLength <= 20) return 120
      if (field.maxLength && field.maxLength <= 50) return 160
      if (field.maxLength && field.maxLength <= 100) return 200
      return 180
    }
    default:
      return 150
  }
}

// ── Column alignment ──

function inferAlign(field: FieldMetadata): 'left' | 'center' | 'right' {
  switch (field.type) {
    case 'Integer':
    case 'Decimal':
      return 'right'
    case 'Boolean':
      return 'center'
    default:
      return 'left'
  }
}

// ── Importance ──

function inferImportance(field: FieldMetadata, isKey: boolean): number {
  if (isKey) return 1
  if (field.isRequired) return 2
  if (field.isComputed) return 4
  return 3
}

// ── Should skip field? ──

function shouldSkipField(field: FieldMetadata): boolean {
  if (field.name.startsWith('_') || field.name.startsWith('@')) return true
  const annotations = field.annotations ?? {}
  if (annotations['@UI.Hidden'] || annotations['UI.Hidden']) return true
  // Skip computed non-key fields
  if (field.isComputed) return true
  return false
}

// ── Check capability annotations ──

function isNonSortable(field: FieldMetadata, metadata: EntityMetadata): boolean {
  const annotations = metadata.annotations ?? {}
  const sortRestrictions = annotations['Org.OData.Capabilities.V1.SortRestrictions'] as
    | { NonSortableProperties?: string[] }
    | undefined
  if (sortRestrictions?.NonSortableProperties) {
    return sortRestrictions.NonSortableProperties.includes(field.name)
  }
  return false
}

function isNonFilterable(field: FieldMetadata, metadata: EntityMetadata): boolean {
  const annotations = metadata.annotations ?? {}
  const filterRestrictions = annotations['Org.OData.Capabilities.V1.FilterRestrictions'] as
    | { NonFilterableProperties?: string[] }
    | undefined
  if (filterRestrictions?.NonFilterableProperties) {
    return filterRestrictions.NonFilterableProperties.includes(field.name)
  }
  return false
}

// ── Build columns from metadata ──

function buildColumnsFromMetadata(
  metadata: EntityMetadata,
  overrides?: Partial<SmartColumnConfig>[]
): SmartColumnConfig[] {
  const keySet = new Set(metadata.keys ?? [])
  const overrideMap = new Map<string, Partial<SmartColumnConfig>>()
  if (overrides) {
    for (const o of overrides) {
      if (o.field) {
        overrideMap.set(o.field, o)
      }
    }
  }

  const columns: SmartColumnConfig[] = []
  let order = 0

  for (const field of metadata.fields) {
    if (shouldSkipField(field) && !keySet.has(field.name)) continue

    const isKey = keySet.has(field.name)
    const override = overrideMap.get(field.name)
    const enumValues: SmartColumnConfig['enumValues'] = field.enumValues?.map((ev: EnumValue) => ({
      name: ev.name,
      value: ev.value as string | number,
      displayName: ev.displayName
    }))

    const col: SmartColumnConfig = {
      field: field.name,
      label: override?.label ?? field.displayName ?? field.name,
      type: override?.type ?? field.type,
      visible: override?.visible ?? true,
      sortable: override?.sortable ?? !isNonSortable(field, metadata),
      filterable: override?.filterable ?? !isNonFilterable(field, metadata),
      width: override?.width ?? inferWidth(field),
      minWidth: override?.minWidth ?? 60,
      align: override?.align ?? inferAlign(field),
      format: override?.format,
      importance: override?.importance ?? inferImportance(field, isKey),
      isKey,
      isEnum: field.type === 'Enum',
      enumValues,
      order: override?.order ?? order
    }

    columns.push(col)
    order++
  }

  // Sort: keys first, then by importance, then by original order
  columns.sort((a, b) => {
    if (a.isKey !== b.isKey) return a.isKey ? -1 : 1
    if (a.importance !== b.importance) return a.importance - b.importance
    return a.order - b.order
  })

  // Reassign order after sorting
  columns.forEach((col, idx) => {
    col.order = idx
  })

  return columns
}

// ── Main composable ──

export function useSmartTable(
  metadata: MaybeRefOrGetter<EntityMetadata>,
  overrides?: MaybeRefOrGetter<Partial<SmartColumnConfig>[] | undefined>
): UseSmartTableReturn {
  const internalColumns = ref<SmartColumnConfig[]>([])

  function rebuild(): void {
    const meta = toValue(metadata)
    const ov = toValue(overrides)
    internalColumns.value = buildColumnsFromMetadata(meta, ov)
  }

  // Initialize
  rebuild()

  // Rebuild when metadata changes
  watch(
    () => toValue(metadata),
    () => rebuild(),
    { deep: true }
  )

  const columns = computed<SmartColumnConfig[]>(() => {
    return [...internalColumns.value].sort((a, b) => a.order - b.order)
  })

  const visibleColumns = computed<SmartColumnConfig[]>(() => {
    return columns.value.filter((c) => c.visible)
  })

  function toggleColumn(field: string): void {
    const col = internalColumns.value.find((c) => c.field === field)
    if (col) {
      col.visible = !col.visible
    }
  }

  function setColumnWidth(field: string, width: number): void {
    const col = internalColumns.value.find((c) => c.field === field)
    if (col) {
      col.width = Math.max(width, col.minWidth ?? 60)
    }
  }

  function reorderColumns(fromIndex: number, toIndex: number): void {
    if (fromIndex === toIndex) return
    const sorted = [...internalColumns.value].sort((a, b) => a.order - b.order)
    if (fromIndex < 0 || toIndex < 0 || fromIndex >= sorted.length || toIndex >= sorted.length) return

    const [moved] = sorted.splice(fromIndex, 1)
    sorted.splice(toIndex, 0, moved)

    sorted.forEach((col, idx) => {
      const original = internalColumns.value.find((c) => c.field === col.field)
      if (original) {
        original.order = idx
      }
    })
  }

  function resetColumns(): void {
    rebuild()
  }

  function saveLayout(key: string): void {
    const layout: StoredLayout = {
      columns: internalColumns.value.map((c) => ({
        field: c.field,
        visible: c.visible,
        width: c.width,
        order: c.order
      }))
    }
    saveLayoutToStorage(key, layout)
  }

  function loadLayout(key: string): boolean {
    const layout = loadLayoutFromStorage(key)
    if (!layout) return false

    const fieldMap = new Map(layout.columns.map((c) => [c.field, c]))
    for (const col of internalColumns.value) {
      const saved = fieldMap.get(col.field)
      if (saved) {
        col.visible = saved.visible
        if (saved.width !== undefined) col.width = saved.width
        col.order = saved.order
      }
    }
    return true
  }

  return {
    columns,
    visibleColumns,
    toggleColumn,
    setColumnWidth,
    reorderColumns,
    resetColumns,
    saveLayout,
    loadLayout
  }
}
