import { ref, computed } from 'vue'
import type { Ref, ComputedRef } from 'vue'

export interface UseRowSelectionReturn {
  selectedIds: Ref<Set<string>>
  selectedCount: ComputedRef<number>
  isAllSelected: ComputedRef<boolean>
  isIndeterminate: ComputedRef<boolean>
  isSelected: (id: string) => boolean
  toggleRow: (id: string) => void
  toggleAll: (allIds: string[]) => void
  selectAll: (allIds: string[]) => void
  deselectAll: () => void
  getSelectedIds: () => string[]
}

export function useRowSelection(): UseRowSelectionReturn {
  const selectedIds = ref<Set<string>>(new Set())

  // Track the current page IDs for isAllSelected / isIndeterminate checks
  const currentPageIds = ref<string[]>([])

  const selectedCount = computed(() => selectedIds.value.size)

  const isAllSelected = computed(() => {
    if (currentPageIds.value.length === 0) return false
    return currentPageIds.value.every((id) => selectedIds.value.has(id))
  })

  const isIndeterminate = computed(() => {
    if (currentPageIds.value.length === 0) return false
    const someSelected = currentPageIds.value.some((id) => selectedIds.value.has(id))
    return someSelected && !isAllSelected.value
  })

  function isSelected(id: string): boolean {
    return selectedIds.value.has(id)
  }

  function toggleRow(id: string): void {
    const next = new Set(selectedIds.value)
    if (next.has(id)) {
      next.delete(id)
    } else {
      next.add(id)
    }
    selectedIds.value = next
  }

  function toggleAll(allIds: string[]): void {
    currentPageIds.value = allIds
    if (isAllSelected.value) {
      deselectAll()
    } else {
      selectAll(allIds)
    }
  }

  function selectAll(allIds: string[]): void {
    currentPageIds.value = allIds
    const next = new Set(selectedIds.value)
    for (const id of allIds) {
      next.add(id)
    }
    selectedIds.value = next
  }

  function deselectAll(): void {
    selectedIds.value = new Set()
  }

  function getSelectedIds(): string[] {
    return Array.from(selectedIds.value)
  }

  return {
    selectedIds,
    selectedCount,
    isAllSelected,
    isIndeterminate,
    isSelected,
    toggleRow,
    toggleAll,
    selectAll,
    deselectAll,
    getSelectedIds
  }
}
