import { ref, computed, watch, onMounted, onUnmounted, toValue, type Ref, type ComputedRef, type MaybeRefOrGetter } from 'vue'

export type ColumnImportance = 'high' | 'medium' | 'low'

export interface ResponsiveColumnConfig {
  key: string
  label: string
  importance: ColumnImportance
  minWidth?: number
}

export interface Breakpoint {
  name: string
  minWidth: number
  visibleImportance: ColumnImportance[]
}

export const DEFAULT_BREAKPOINTS: Breakpoint[] = [
  { name: 'mobile', minWidth: 0, visibleImportance: ['high'] },
  { name: 'tablet', minWidth: 768, visibleImportance: ['high', 'medium'] },
  { name: 'desktop', minWidth: 1024, visibleImportance: ['high', 'medium', 'low'] },
]

export interface UseResponsiveTableOptions {
  columns: MaybeRefOrGetter<ResponsiveColumnConfig[]>
  containerRef: Ref<HTMLElement | null>
  breakpoints?: Breakpoint[]
}

export interface UseResponsiveTableReturn {
  visibleColumns: ComputedRef<ResponsiveColumnConfig[]>
  hiddenColumns: ComputedRef<ResponsiveColumnConfig[]>
  currentBreakpoint: Ref<string>
  containerWidth: Ref<number>
  hasHiddenColumns: ComputedRef<boolean>
  expandedRows: Ref<Set<string | number>>
  toggleRowExpand: (rowKey: string | number) => void
  isRowExpanded: (rowKey: string | number) => boolean
  expandAll: (rowKeys: (string | number)[]) => void
  collapseAll: () => void
}

function resolveBreakpoint(width: number, breakpoints: Breakpoint[]): Breakpoint {
  const sorted = [...breakpoints].sort((a, b) => b.minWidth - a.minWidth)
  for (const bp of sorted) {
    if (width >= bp.minWidth) {
      return bp
    }
  }
  return sorted[sorted.length - 1]
}

export function useResponsiveTable(options: UseResponsiveTableOptions): UseResponsiveTableReturn {
  const { containerRef, breakpoints = DEFAULT_BREAKPOINTS } = options

  const containerWidth = ref(0)
  const currentBreakpoint = ref(breakpoints[0].name)
  const expandedRows = ref<Set<string | number>>(new Set())

  let resizeObserver: ResizeObserver | null = null

  function updateWidth(width: number): void {
    containerWidth.value = width
    const bp = resolveBreakpoint(width, breakpoints)
    currentBreakpoint.value = bp.name
  }

  function startObserving(el: HTMLElement | null): void {
    stopObserving()
    if (!el) return

    resizeObserver = new ResizeObserver((entries) => {
      for (const entry of entries) {
        const width = entry.contentBoxSize?.[0]?.inlineSize ?? entry.contentRect.width
        updateWidth(width)
      }
    })
    resizeObserver.observe(el)

    // Initial measurement
    updateWidth(el.clientWidth)
  }

  function stopObserving(): void {
    if (resizeObserver) {
      resizeObserver.disconnect()
      resizeObserver = null
    }
  }

  // Determine which columns are visible at the current breakpoint
  const visibleColumns = computed<ResponsiveColumnConfig[]>(() => {
    const cols = toValue(options.columns)
    const bp = resolveBreakpoint(containerWidth.value, breakpoints)

    return cols.filter((col) => {
      // If column has an explicit minWidth override, use that
      if (col.minWidth != null) {
        return containerWidth.value >= col.minWidth
      }
      // Otherwise use importance-based breakpoint logic
      return bp.visibleImportance.includes(col.importance)
    })
  })

  const hiddenColumns = computed<ResponsiveColumnConfig[]>(() => {
    const cols = toValue(options.columns)
    const visibleKeys = new Set(visibleColumns.value.map((c) => c.key))
    return cols.filter((col) => !visibleKeys.has(col.key))
  })

  const hasHiddenColumns = computed(() => hiddenColumns.value.length > 0)

  function toggleRowExpand(rowKey: string | number): void {
    const next = new Set(expandedRows.value)
    if (next.has(rowKey)) {
      next.delete(rowKey)
    } else {
      next.add(rowKey)
    }
    expandedRows.value = next
  }

  function isRowExpanded(rowKey: string | number): boolean {
    return expandedRows.value.has(rowKey)
  }

  function expandAll(rowKeys: (string | number)[]): void {
    expandedRows.value = new Set(rowKeys)
  }

  function collapseAll(): void {
    expandedRows.value = new Set()
  }

  // Watch for containerRef changes (e.g. v-if toggling)
  watch(containerRef, (el) => {
    startObserving(el)
  })

  onMounted(() => {
    startObserving(containerRef.value)
  })

  onUnmounted(() => {
    stopObserving()
  })

  return {
    visibleColumns,
    hiddenColumns,
    currentBreakpoint,
    containerWidth,
    hasHiddenColumns,
    expandedRows,
    toggleRowExpand,
    isRowExpanded,
    expandAll,
    collapseAll,
  }
}
