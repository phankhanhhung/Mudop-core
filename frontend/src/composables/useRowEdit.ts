import { ref, computed, readonly, type Ref, type ComputedRef } from 'vue'

export interface UseRowEditReturn {
  /** ID of the currently editing row (null if none) */
  editingRowId: Readonly<Ref<string | null>>
  /** Current edit values */
  editValues: Ref<Record<string, unknown>>
  /** Whether any row is being edited */
  isEditing: ComputedRef<boolean>
  /** Set of dirty field names */
  dirtyFields: ComputedRef<Set<string>>
  /** Start editing a row - snapshots original data */
  startEdit: (rowId: string, rowData: Record<string, unknown>) => void
  /** Update a single field value */
  updateField: (field: string, value: unknown) => void
  /** Check if a specific field (or any field) is dirty */
  isDirty: (field?: string) => boolean
  /** Get only the changed fields (diff against snapshot) */
  getChanges: () => Record<string, unknown>
  /** Cancel editing */
  cancelEdit: () => void
}

export function useRowEdit(): UseRowEditReturn {
  const editingRowId = ref<string | null>(null)
  const editValues = ref<Record<string, unknown>>({})
  const originalSnapshot = ref<Record<string, unknown>>({})

  const isEditing = computed(() => editingRowId.value !== null)

  const dirtyFields = computed(() => {
    const dirty = new Set<string>()
    if (!isEditing.value) return dirty

    for (const [key, value] of Object.entries(editValues.value)) {
      const original = originalSnapshot.value[key]
      if (!deepEqual(value, original)) {
        dirty.add(key)
      }
    }
    return dirty
  })

  function startEdit(rowId: string, rowData: Record<string, unknown>) {
    editingRowId.value = rowId
    // Deep clone to avoid reference sharing
    originalSnapshot.value = JSON.parse(JSON.stringify(rowData))
    editValues.value = JSON.parse(JSON.stringify(rowData))
  }

  function updateField(field: string, value: unknown) {
    editValues.value = { ...editValues.value, [field]: value }
  }

  function isDirty(field?: string): boolean {
    if (!isEditing.value) return false
    if (field) {
      return dirtyFields.value.has(field)
    }
    return dirtyFields.value.size > 0
  }

  function getChanges(): Record<string, unknown> {
    const changes: Record<string, unknown> = {}
    for (const field of dirtyFields.value) {
      changes[field] = editValues.value[field]
    }
    return changes
  }

  function cancelEdit() {
    editingRowId.value = null
    editValues.value = {}
    originalSnapshot.value = {}
  }

  return {
    editingRowId: readonly(editingRowId),
    editValues,
    isEditing,
    dirtyFields,
    startEdit,
    updateField,
    isDirty,
    getChanges,
    cancelEdit,
  }
}

/** Simple deep equality check for primitives, arrays, and plain objects */
function deepEqual(a: unknown, b: unknown): boolean {
  if (a === b) return true
  if (a === null || b === null) return false
  if (a === undefined || b === undefined) return false
  if (typeof a !== typeof b) return false

  if (Array.isArray(a) && Array.isArray(b)) {
    if (a.length !== b.length) return false
    return a.every((val, idx) => deepEqual(val, b[idx]))
  }

  if (typeof a === 'object' && typeof b === 'object') {
    const aObj = a as Record<string, unknown>
    const bObj = b as Record<string, unknown>
    const aKeys = Object.keys(aObj)
    const bKeys = Object.keys(bObj)
    if (aKeys.length !== bKeys.length) return false
    return aKeys.every((key) => deepEqual(aObj[key], bObj[key]))
  }

  return false
}
