import { ref, computed, watch, onScopeDispose, toValue, type Ref, type ComputedRef } from 'vue'

export interface UseCarouselOptions {
  itemCount: Ref<number> | number
  autoPlay?: boolean
  autoPlayInterval?: number // ms, default 5000
  loop?: boolean // wrap around (default true)
  startIndex?: number
}

export interface UseCarouselReturn {
  currentIndex: Ref<number>
  isFirst: ComputedRef<boolean>
  isLast: ComputedRef<boolean>
  isPlaying: Ref<boolean>
  next: () => void
  previous: () => void
  goTo: (index: number) => void
  play: () => void
  pause: () => void
  toggle: () => void
}

export function useCarousel(options: UseCarouselOptions): UseCarouselReturn {
  const {
    itemCount,
    autoPlay = false,
    autoPlayInterval = 5000,
    loop = true,
    startIndex = 0,
  } = options

  const currentIndex = ref(startIndex)
  const isPlaying = ref(autoPlay)

  let timer: ReturnType<typeof setInterval> | null = null

  // ── Boundary checks ──────────────────────────────────────────────────────

  const count = computed(() => toValue(itemCount))

  const isFirst = computed(() => currentIndex.value === 0)
  const isLast = computed(() => currentIndex.value >= count.value - 1)

  // ── Navigation ───────────────────────────────────────────────────────────

  function next() {
    if (count.value === 0) return

    if (isLast.value) {
      if (loop) {
        currentIndex.value = 0
      }
      // Without loop, next on last item is a no-op
    } else {
      currentIndex.value++
    }
  }

  function previous() {
    if (count.value === 0) return

    if (isFirst.value) {
      if (loop) {
        currentIndex.value = count.value - 1
      }
      // Without loop, previous on first item is a no-op
    } else {
      currentIndex.value--
    }
  }

  function goTo(index: number) {
    if (index < 0 || index >= count.value) return
    currentIndex.value = index
  }

  // ── Auto-play timer ──────────────────────────────────────────────────────

  function startTimer() {
    stopTimer()
    timer = setInterval(() => {
      next()
    }, autoPlayInterval)
  }

  function stopTimer() {
    if (timer !== null) {
      clearInterval(timer)
      timer = null
    }
  }

  function play() {
    isPlaying.value = true
  }

  function pause() {
    isPlaying.value = false
  }

  function toggle() {
    isPlaying.value = !isPlaying.value
  }

  // Sync timer with isPlaying state
  watch(isPlaying, (playing) => {
    if (playing) {
      startTimer()
    } else {
      stopTimer()
    }
  }, { immediate: true })

  // Clamp index when item count changes
  watch(count, (newCount) => {
    if (newCount === 0) {
      currentIndex.value = 0
    } else if (currentIndex.value >= newCount) {
      currentIndex.value = newCount - 1
    }
  })

  // ── Cleanup ──────────────────────────────────────────────────────────────

  onScopeDispose(() => {
    stopTimer()
  })

  return {
    currentIndex,
    isFirst,
    isLast,
    isPlaying,
    next,
    previous,
    goTo,
    play,
    pause,
    toggle,
  }
}
