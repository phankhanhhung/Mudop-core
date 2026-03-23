import { ref, watch, type Ref } from 'vue'

export interface P13nColumnItem {
  key: string
  label: string
  visible: boolean
  order: number
}

export interface P13nSortItem {
  key: string
  label: string
  direction: 'asc' | 'desc'
}

export interface P13nFilterItem {
  key: string
  label: string
  operator: 'eq' | 'ne' | 'gt' | 'lt' | 'ge' | 'le' | 'contains' | 'startswith' | 'endswith'
  value: string
}

export interface P13nGroupItem {
  key: string
  label: string
  showSubtotals: boolean
}

export interface P13nState {
  columns: P13nColumnItem[]
  sortItems: P13nSortItem[]
  filterItems: P13nFilterItem[]
  groupItems: P13nGroupItem[]
}

export interface UseP13nDialogOptions {
  availableColumns: { key: string; label: string }[]
  initialState?: Partial<P13nState>
  persistKey?: string
}

export interface UseP13nDialogReturn {
  state: Ref<P13nState>
  draftState: Ref<P13nState>
  isOpen: Ref<boolean>
  activeTab: Ref<'columns' | 'sort' | 'filter' | 'group'>
  open: () => void
  close: () => void
  apply: () => void
  reset: () => void
  moveColumn: (index: number, direction: 'up' | 'down') => void
  toggleColumnVisibility: (key: string) => void
  selectAllColumns: () => void
  deselectAllColumns: () => void
  addSortItem: () => void
  removeSortItem: (index: number) => void
  addFilterItem: () => void
  removeFilterItem: (index: number) => void
  addGroupItem: () => void
  removeGroupItem: (index: number) => void
}

function deepClone<T>(obj: T): T {
  return JSON.parse(JSON.stringify(obj))
}

function buildDefaultState(
  availableColumns: { key: string; label: string }[],
  initialState?: Partial<P13nState>
): P13nState {
  const defaultColumns: P13nColumnItem[] = availableColumns.map((col, index) => ({
    key: col.key,
    label: col.label,
    visible: true,
    order: index,
  }))

  return {
    columns: initialState?.columns ?? defaultColumns,
    sortItems: initialState?.sortItems ?? [],
    filterItems: initialState?.filterItems ?? [],
    groupItems: initialState?.groupItems ?? [],
  }
}

function loadFromStorage(persistKey: string): P13nState | null {
  try {
    const raw = localStorage.getItem(persistKey)
    if (raw) {
      return JSON.parse(raw) as P13nState
    }
  } catch {
    // Ignore invalid JSON
  }
  return null
}

function saveToStorage(persistKey: string, state: P13nState): void {
  try {
    localStorage.setItem(persistKey, JSON.stringify(state))
  } catch {
    // Ignore quota exceeded
  }
}

export function useP13nDialog(options: UseP13nDialogOptions): UseP13nDialogReturn {
  const { availableColumns, initialState, persistKey } = options

  // Load persisted state or build default
  const persisted = persistKey ? loadFromStorage(persistKey) : null
  const defaultState = buildDefaultState(availableColumns, initialState)
  const state = ref<P13nState>(persisted ?? defaultState)
  const draftState = ref<P13nState>(deepClone(state.value))
  const isOpen = ref(false)
  const activeTab = ref<'columns' | 'sort' | 'filter' | 'group'>('columns')

  // Persist on state changes
  if (persistKey) {
    watch(state, (newState) => {
      saveToStorage(persistKey, newState)
    }, { deep: true })
  }

  function open(): void {
    draftState.value = deepClone(state.value)
    activeTab.value = 'columns'
    isOpen.value = true
  }

  function close(): void {
    isOpen.value = false
  }

  function apply(): void {
    state.value = deepClone(draftState.value)
    isOpen.value = false
  }

  function reset(): void {
    draftState.value = deepClone(state.value)
  }

  // Column helpers
  function moveColumn(index: number, direction: 'up' | 'down'): void {
    const cols = draftState.value.columns
    const targetIndex = direction === 'up' ? index - 1 : index + 1
    if (targetIndex < 0 || targetIndex >= cols.length) return

    const temp = cols[index]
    cols[index] = cols[targetIndex]
    cols[targetIndex] = temp

    // Update order values
    cols.forEach((col, i) => {
      col.order = i
    })
  }

  function toggleColumnVisibility(key: string): void {
    const col = draftState.value.columns.find(c => c.key === key)
    if (col) {
      col.visible = !col.visible
    }
  }

  function selectAllColumns(): void {
    draftState.value.columns.forEach(col => {
      col.visible = true
    })
  }

  function deselectAllColumns(): void {
    draftState.value.columns.forEach(col => {
      col.visible = false
    })
  }

  // Sort helpers
  function addSortItem(): void {
    const firstAvailable = availableColumns[0]
    if (!firstAvailable) return
    draftState.value.sortItems.push({
      key: firstAvailable.key,
      label: firstAvailable.label,
      direction: 'asc',
    })
  }

  function removeSortItem(index: number): void {
    draftState.value.sortItems.splice(index, 1)
  }

  // Filter helpers
  function addFilterItem(): void {
    const firstAvailable = availableColumns[0]
    if (!firstAvailable) return
    draftState.value.filterItems.push({
      key: firstAvailable.key,
      label: firstAvailable.label,
      operator: 'eq',
      value: '',
    })
  }

  function removeFilterItem(index: number): void {
    draftState.value.filterItems.splice(index, 1)
  }

  // Group helpers
  function addGroupItem(): void {
    const firstAvailable = availableColumns[0]
    if (!firstAvailable) return
    draftState.value.groupItems.push({
      key: firstAvailable.key,
      label: firstAvailable.label,
      showSubtotals: false,
    })
  }

  function removeGroupItem(index: number): void {
    draftState.value.groupItems.splice(index, 1)
  }

  return {
    state,
    draftState,
    isOpen,
    activeTab,
    open,
    close,
    apply,
    reset,
    moveColumn,
    toggleColumnVisibility,
    selectAllColumns,
    deselectAllColumns,
    addSortItem,
    removeSortItem,
    addFilterItem,
    removeFilterItem,
    addGroupItem,
    removeGroupItem,
  }
}
