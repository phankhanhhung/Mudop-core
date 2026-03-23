import { computed, type Ref, type ComputedRef } from 'vue'
import { useVirtualizer } from '@tanstack/vue-virtual'

export interface UseVirtualScrollOptions {
  /** Ref to the scrollable container element */
  containerRef: Ref<HTMLElement | null>
  /** Total number of items */
  count: Ref<number> | ComputedRef<number>
  /** Estimated row height in pixels */
  rowHeight?: number
  /** Number of rows to render outside the visible area */
  overscan?: number
  /** Whether virtual scrolling is enabled */
  enabled?: Ref<boolean> | ComputedRef<boolean> | boolean
}

export interface UseVirtualScrollReturn {
  /** Virtual items to render */
  virtualRows: ComputedRef<Array<{ index: number; start: number; size: number; key: number }>>
  /** Total height of all rows */
  totalSize: ComputedRef<number>
  /** Padding for the top spacer row */
  topPadding: ComputedRef<number>
  /** Padding for the bottom spacer row */
  bottomPadding: ComputedRef<number>
  /** Scroll to a specific index */
  scrollToIndex: (index: number) => void
}

export function useVirtualScroll(options: UseVirtualScrollOptions): UseVirtualScrollReturn {
  const {
    containerRef,
    count,
    rowHeight = 40,
    overscan = 10,
  } = options

  const itemCount = computed(() => count.value)

  const virtualizer = useVirtualizer({
    get count() {
      return itemCount.value
    },
    getScrollElement: () => containerRef.value,
    estimateSize: () => rowHeight,
    overscan,
  })

  const virtualRows = computed(() => {
    return virtualizer.value.getVirtualItems().map((item) => ({
      index: item.index,
      start: item.start,
      size: item.size,
      key: item.key as number,
    }))
  })

  const totalSize = computed(() => virtualizer.value.getTotalSize())

  const topPadding = computed(() => {
    const items = virtualizer.value.getVirtualItems()
    return items.length > 0 ? items[0].start : 0
  })

  const bottomPadding = computed(() => {
    const items = virtualizer.value.getVirtualItems()
    if (items.length === 0) return 0
    const lastItem = items[items.length - 1]
    return totalSize.value - (lastItem.start + lastItem.size)
  })

  function scrollToIndex(index: number) {
    virtualizer.value.scrollToIndex(index, { align: 'start' })
  }

  return {
    virtualRows,
    totalSize,
    topPadding,
    bottomPadding,
    scrollToIndex,
  }
}
