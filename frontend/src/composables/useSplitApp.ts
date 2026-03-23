import { ref, computed, watch, onMounted, onUnmounted, type Ref, type ComputedRef } from 'vue'

export type SplitAppMode = 'master' | 'detail' | 'both'

export interface UseSplitAppOptions {
  /** Width in pixels below which to show only one pane (default 768) */
  breakpoint?: number
  /** CSS width for the master pane in desktop mode (default '360px') */
  masterWidth?: string
  /** Which pane to show on mobile initially (default 'master') */
  initialMode?: 'master' | 'detail'
}

export interface UseSplitAppReturn {
  containerRef: Ref<HTMLElement | null>
  containerWidth: Ref<number>
  isMobile: ComputedRef<boolean>
  currentMode: Ref<SplitAppMode>
  showMaster: () => void
  showDetail: () => void
  hasDetail: Ref<boolean>
  setHasDetail: (val: boolean) => void
}

export function useSplitApp(options: UseSplitAppOptions = {}): UseSplitAppReturn {
  const {
    breakpoint = 768,
    initialMode = 'master',
  } = options

  const containerRef = ref<HTMLElement | null>(null)
  const containerWidth = ref(0)
  const hasDetail = ref(false)
  const currentMode = ref<SplitAppMode>('both')

  const isMobile = computed(() => containerWidth.value > 0 && containerWidth.value < breakpoint)

  let resizeObserver: ResizeObserver | null = null

  function updateMode(mobile: boolean): void {
    if (mobile) {
      currentMode.value = hasDetail.value ? 'detail' : 'master'
    } else {
      currentMode.value = 'both'
    }
  }

  // Watch for responsive transitions
  watch(isMobile, (mobile, wasMobile) => {
    if (wasMobile === undefined) return
    if (mobile !== wasMobile) {
      updateMode(mobile)
    }
  })

  function showMaster(): void {
    if (isMobile.value) {
      currentMode.value = 'master'
    }
  }

  function showDetail(): void {
    if (isMobile.value) {
      currentMode.value = 'detail'
    }
  }

  function setHasDetail(val: boolean): void {
    hasDetail.value = val
  }

  onMounted(() => {
    if (!containerRef.value) return

    resizeObserver = new ResizeObserver((entries) => {
      for (const entry of entries) {
        containerWidth.value = entry.contentRect.width
      }
    })
    resizeObserver.observe(containerRef.value)

    // Initial measurement
    containerWidth.value = containerRef.value.offsetWidth

    // Set initial mode based on container width
    if (containerWidth.value < breakpoint) {
      currentMode.value = initialMode
    } else {
      currentMode.value = 'both'
    }
  })

  onUnmounted(() => {
    if (resizeObserver) {
      resizeObserver.disconnect()
      resizeObserver = null
    }
  })

  return {
    containerRef,
    containerWidth,
    isMobile,
    currentMode,
    showMaster,
    showDetail,
    hasDetail,
    setHasDetail,
  }
}
