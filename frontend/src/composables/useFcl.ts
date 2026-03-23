import { ref, computed, onMounted, onUnmounted, type Ref, type ComputedRef } from 'vue'

export type FclLayout =
  | 'OneColumn'
  | 'TwoColumnsMidExpanded'
  | 'TwoColumnsBeginExpanded'
  | 'ThreeColumnsMidExpanded'
  | 'ThreeColumnsEndExpanded'
  | 'MidColumnFullScreen'
  | 'EndColumnFullScreen'

export interface FclNavigationState {
  /** Current layout */
  layout: Ref<FclLayout>
  /** Whether each column has content */
  hasBeginContent: Ref<boolean>
  hasMidContent: Ref<boolean>
  hasEndContent: Ref<boolean>
}

export interface UseFclReturn {
  // State
  layout: Ref<FclLayout>
  previousLayout: Ref<FclLayout | null>

  // Column visibility (computed from layout)
  showBegin: ComputedRef<boolean>
  showMid: ComputedRef<boolean>
  showEnd: ComputedRef<boolean>

  // Column widths (computed from layout)
  beginWidth: ComputedRef<string>
  midWidth: ComputedRef<string>
  endWidth: ComputedRef<string>

  // Navigation actions
  navigateToDetail: () => void
  navigateToSubDetail: () => void
  navigateBack: () => void
  closeDetail: () => void
  closeSubDetail: () => void
  fullscreenDetail: () => void
  fullscreenSubDetail: () => void
  exitFullscreen: () => void
  setLayout: (layout: FclLayout) => void

  // Responsive
  isMobile: Ref<boolean>
  effectiveLayout: ComputedRef<FclLayout>
}

const columnWidths: Record<FclLayout, { begin: string; mid: string; end: string }> = {
  'OneColumn':               { begin: '100%',  mid: '0%',   end: '0%' },
  'TwoColumnsMidExpanded':   { begin: '33.3%', mid: '66.7%', end: '0%' },
  'TwoColumnsBeginExpanded': { begin: '66.7%', mid: '33.3%', end: '0%' },
  'ThreeColumnsMidExpanded': { begin: '25%',   mid: '50%',  end: '25%' },
  'ThreeColumnsEndExpanded': { begin: '25%',   mid: '25%',  end: '50%' },
  'MidColumnFullScreen':     { begin: '0%',    mid: '100%', end: '0%' },
  'EndColumnFullScreen':     { begin: '0%',    mid: '0%',   end: '100%' },
}

const LG_BREAKPOINT = '(min-width: 1024px)'

/**
 * Determines which single column to show on mobile for a given layout.
 * On mobile, only one column is displayed at a time.
 */
function getMobileLayout(layout: FclLayout): FclLayout {
  switch (layout) {
    case 'OneColumn':
      return 'OneColumn'
    case 'TwoColumnsMidExpanded':
    case 'TwoColumnsBeginExpanded':
    case 'MidColumnFullScreen':
      return 'MidColumnFullScreen'
    case 'ThreeColumnsMidExpanded':
    case 'ThreeColumnsEndExpanded':
    case 'EndColumnFullScreen':
      return 'EndColumnFullScreen'
    default:
      return 'OneColumn'
  }
}

export function useFcl(initialLayout: FclLayout = 'OneColumn'): UseFclReturn {
  const layout = ref<FclLayout>(initialLayout)
  const previousLayout = ref<FclLayout | null>(null)
  const isMobile = ref(false)

  // Media query listener for responsive behavior
  let mediaQuery: MediaQueryList | null = null

  function handleMediaChange(e: MediaQueryListEvent | MediaQueryList) {
    isMobile.value = !e.matches
  }

  onMounted(() => {
    mediaQuery = window.matchMedia(LG_BREAKPOINT)
    isMobile.value = !mediaQuery.matches
    mediaQuery.addEventListener('change', handleMediaChange as (e: MediaQueryListEvent) => void)
  })

  onUnmounted(() => {
    if (mediaQuery) {
      mediaQuery.removeEventListener('change', handleMediaChange as (e: MediaQueryListEvent) => void)
    }
  })

  // Effective layout accounts for mobile collapsing
  const effectiveLayout = computed<FclLayout>(() => {
    if (isMobile.value) {
      return getMobileLayout(layout.value)
    }
    return layout.value
  })

  // Column visibility
  const showBegin = computed(() => {
    const widths = columnWidths[effectiveLayout.value]
    return widths.begin !== '0%'
  })

  const showMid = computed(() => {
    const widths = columnWidths[effectiveLayout.value]
    return widths.mid !== '0%'
  })

  const showEnd = computed(() => {
    const widths = columnWidths[effectiveLayout.value]
    return widths.end !== '0%'
  })

  // Column widths
  const beginWidth = computed(() => columnWidths[effectiveLayout.value].begin)
  const midWidth = computed(() => columnWidths[effectiveLayout.value].mid)
  const endWidth = computed(() => columnWidths[effectiveLayout.value].end)

  // Navigation actions

  /** OneColumn -> TwoColumnsMidExpanded */
  function navigateToDetail(): void {
    previousLayout.value = layout.value
    layout.value = 'TwoColumnsMidExpanded'
  }

  /** TwoColumns -> ThreeColumnsMidExpanded */
  function navigateToSubDetail(): void {
    previousLayout.value = layout.value
    layout.value = 'ThreeColumnsMidExpanded'
  }

  /** Reverse navigation: go back one level */
  function navigateBack(): void {
    if (isMobile.value) {
      // On mobile, step back through the column hierarchy
      switch (layout.value) {
        case 'EndColumnFullScreen':
        case 'ThreeColumnsMidExpanded':
        case 'ThreeColumnsEndExpanded':
          layout.value = 'TwoColumnsMidExpanded'
          break
        case 'MidColumnFullScreen':
        case 'TwoColumnsMidExpanded':
        case 'TwoColumnsBeginExpanded':
          layout.value = 'OneColumn'
          break
        default:
          break
      }
    } else if (previousLayout.value) {
      layout.value = previousLayout.value
      previousLayout.value = null
    } else {
      // Fallback: step back through the standard flow
      switch (layout.value) {
        case 'ThreeColumnsMidExpanded':
        case 'ThreeColumnsEndExpanded':
          layout.value = 'TwoColumnsMidExpanded'
          break
        case 'TwoColumnsMidExpanded':
        case 'TwoColumnsBeginExpanded':
          layout.value = 'OneColumn'
          break
        case 'MidColumnFullScreen':
          layout.value = 'TwoColumnsMidExpanded'
          break
        case 'EndColumnFullScreen':
          layout.value = 'ThreeColumnsMidExpanded'
          break
        default:
          break
      }
    }
  }

  /** Close mid column -> OneColumn */
  function closeDetail(): void {
    previousLayout.value = layout.value
    layout.value = 'OneColumn'
  }

  /** Close end column -> TwoColumnsMidExpanded */
  function closeSubDetail(): void {
    previousLayout.value = layout.value
    layout.value = 'TwoColumnsMidExpanded'
  }

  /** Fullscreen detail (mid) column */
  function fullscreenDetail(): void {
    previousLayout.value = layout.value
    layout.value = 'MidColumnFullScreen'
  }

  /** Fullscreen sub-detail (end) column */
  function fullscreenSubDetail(): void {
    previousLayout.value = layout.value
    layout.value = 'EndColumnFullScreen'
  }

  /** Exit fullscreen, return to previous layout */
  function exitFullscreen(): void {
    if (previousLayout.value) {
      layout.value = previousLayout.value
      previousLayout.value = null
    } else {
      // Fallback if no previous layout recorded
      if (layout.value === 'MidColumnFullScreen') {
        layout.value = 'TwoColumnsMidExpanded'
      } else if (layout.value === 'EndColumnFullScreen') {
        layout.value = 'ThreeColumnsMidExpanded'
      }
    }
  }

  /** Directly set a specific layout */
  function setLayout(newLayout: FclLayout): void {
    previousLayout.value = layout.value
    layout.value = newLayout
  }

  return {
    layout,
    previousLayout,
    showBegin,
    showMid,
    showEnd,
    beginWidth,
    midWidth,
    endWidth,
    navigateToDetail,
    navigateToSubDetail,
    navigateBack,
    closeDetail,
    closeSubDetail,
    fullscreenDetail,
    fullscreenSubDetail,
    exitFullscreen,
    setLayout,
    isMobile,
    effectiveLayout,
  }
}
