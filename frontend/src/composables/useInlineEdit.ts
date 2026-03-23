import { ref, type Ref } from 'vue'

export interface CellEditState {
  rowId: string
  field: string
  originalValue: unknown
  currentValue: unknown
  isDirty: boolean
}

export interface UseInlineEditOptions {
  onSave: (rowId: string, field: string, value: unknown) => Promise<void>
}

export interface UseInlineEditReturn {
  editingCell: Ref<CellEditState | null>
  isSaving: Ref<boolean>
  saveError: Ref<string | null>
  startEdit: (rowId: string, field: string, currentValue: unknown) => void
  updateValue: (value: unknown) => void
  commitEdit: () => Promise<void>
  cancelEdit: () => void
  isEditing: (rowId: string, field: string) => boolean
}

export function useInlineEdit(options: UseInlineEditOptions): UseInlineEditReturn {
  const editingCell = ref<CellEditState | null>(null)
  const isSaving = ref(false)
  const saveError = ref<string | null>(null)

  function startEdit(rowId: string, field: string, currentValue: unknown): void {
    // If already editing another cell, cancel it silently
    editingCell.value = {
      rowId,
      field,
      originalValue: currentValue,
      currentValue,
      isDirty: false
    }
    saveError.value = null
  }

  function updateValue(value: unknown): void {
    if (!editingCell.value) return

    editingCell.value = {
      ...editingCell.value,
      currentValue: value,
      isDirty: value !== editingCell.value.originalValue
    }
  }

  async function commitEdit(): Promise<void> {
    if (!editingCell.value) return

    // If value hasn't changed, just cancel
    if (!editingCell.value.isDirty) {
      cancelEdit()
      return
    }

    isSaving.value = true
    saveError.value = null

    try {
      await options.onSave(
        editingCell.value.rowId,
        editingCell.value.field,
        editingCell.value.currentValue
      )
      // Success — clear editing state
      editingCell.value = null
    } catch (e) {
      // Keep cell in edit mode and show error
      saveError.value = e instanceof Error ? e.message : 'Failed to save'
    } finally {
      isSaving.value = false
    }
  }

  function cancelEdit(): void {
    editingCell.value = null
    saveError.value = null
  }

  function isEditing(rowId: string, field: string): boolean {
    if (!editingCell.value) return false
    return editingCell.value.rowId === rowId && editingCell.value.field === field
  }

  return {
    editingCell,
    isSaving,
    saveError,
    startEdit,
    updateValue,
    commitEdit,
    cancelEdit,
    isEditing
  }
}
