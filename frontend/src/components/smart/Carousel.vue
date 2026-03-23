<script setup lang="ts">
import { ref, computed, useSlots, watch } from 'vue'
import { cn } from '@/lib/utils'
import { useCarousel } from '@/composables/useCarousel'
import { ChevronLeft, ChevronRight, Play, Pause } from 'lucide-vue-next'

interface Props {
  autoPlay?: boolean
  autoPlayInterval?: number
  loop?: boolean
  showArrows?: boolean
  showDots?: boolean
  showPlayButton?: boolean
  aspectRatio?: string // e.g. '16/9', 'auto'
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  autoPlay: false,
  autoPlayInterval: 5000,
  loop: true,
  showArrows: true,
  showDots: true,
  showPlayButton: false,
})

const emit = defineEmits<{
  'slide-change': [index: number]
}>()

const slots = useSlots()

// ── Item count from default slot ────────────────────────────────────────

const itemCount = computed(() => {
  const children = slots.default?.()
  if (!children) return 0
  // Count top-level VNodes (each CarouselSlide is one)
  return children.reduce((count, vnode) => {
    // Handle Fragment (v-for produces fragments)
    if (Array.isArray(vnode.children)) {
      return count + vnode.children.length
    }
    return count + 1
  }, 0)
})

// ── Composable ──────────────────────────────────────────────────────────

const {
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
} = useCarousel({
  itemCount,
  autoPlay: props.autoPlay,
  autoPlayInterval: props.autoPlayInterval,
  loop: props.loop,
})

// Emit slide-change when index changes
watch(currentIndex, (index) => {
  emit('slide-change', index)
})

// ── Pause on hover ──────────────────────────────────────────────────────

const wasPlayingBeforeHover = ref(false)

function onMouseEnter() {
  if (isPlaying.value) {
    wasPlayingBeforeHover.value = true
    pause()
  }
}

function onMouseLeave() {
  if (wasPlayingBeforeHover.value) {
    wasPlayingBeforeHover.value = false
    play()
  }
}

// ── Touch / swipe support ───────────────────────────────────────────────

let touchStartX = 0
let touchStartY = 0

function onTouchStart(event: TouchEvent) {
  touchStartX = event.touches[0].clientX
  touchStartY = event.touches[0].clientY
}

function onTouchEnd(event: TouchEvent) {
  const touchEndX = event.changedTouches[0].clientX
  const touchEndY = event.changedTouches[0].clientY
  const deltaX = touchEndX - touchStartX
  const deltaY = touchEndY - touchStartY

  // Only navigate if horizontal swipe is dominant and exceeds threshold
  if (Math.abs(deltaX) > 50 && Math.abs(deltaX) > Math.abs(deltaY)) {
    if (deltaX < 0) {
      next()
    } else {
      previous()
    }
  }
}

// ── Keyboard navigation ────────────────────────────────────────────────

const rootRef = ref<HTMLElement | null>(null)

function onKeydown(event: KeyboardEvent) {
  switch (event.key) {
    case 'ArrowLeft':
      event.preventDefault()
      previous()
      break
    case 'ArrowRight':
      event.preventDefault()
      next()
      break
    case 'Home':
      event.preventDefault()
      goTo(0)
      break
    case 'End':
      event.preventDefault()
      if (itemCount.value > 0) {
        goTo(itemCount.value - 1)
      }
      break
    case ' ':
      event.preventDefault()
      toggle()
      break
  }
}
</script>

<template>
  <div
    ref="rootRef"
    :class="cn('relative group', props.class)"
    role="region"
    aria-roledescription="carousel"
    aria-label="Content slider"
    tabindex="0"
    @mouseenter="onMouseEnter"
    @mouseleave="onMouseLeave"
    @keydown="onKeydown"
  >
    <!-- Slides container -->
    <div
      class="overflow-hidden rounded-lg"
      :style="aspectRatio ? { aspectRatio } : {}"
    >
      <div
        class="flex transition-transform duration-500 ease-in-out h-full"
        :style="{ transform: `translateX(-${currentIndex * 100}%)` }"
        @touchstart.passive="onTouchStart"
        @touchend="onTouchEnd"
      >
        <slot />
      </div>
    </div>

    <!-- Previous arrow -->
    <template v-if="showArrows">
      <button
        v-if="!isFirst || loop"
        class="absolute left-2 top-1/2 -translate-y-1/2 w-10 h-10 rounded-full bg-background/80 backdrop-blur-sm shadow-md flex items-center justify-center opacity-0 group-hover:opacity-100 transition-opacity hover:bg-background focus-visible:opacity-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        aria-label="Previous slide"
        @click="previous"
      >
        <ChevronLeft class="h-5 w-5" />
      </button>

      <!-- Next arrow -->
      <button
        v-if="!isLast || loop"
        class="absolute right-2 top-1/2 -translate-y-1/2 w-10 h-10 rounded-full bg-background/80 backdrop-blur-sm shadow-md flex items-center justify-center opacity-0 group-hover:opacity-100 transition-opacity hover:bg-background focus-visible:opacity-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        aria-label="Next slide"
        @click="next"
      >
        <ChevronRight class="h-5 w-5" />
      </button>
    </template>

    <!-- Dot indicators -->
    <div
      v-if="showDots && itemCount > 1"
      class="absolute bottom-3 left-1/2 -translate-x-1/2 flex gap-1.5"
      role="tablist"
      aria-label="Slide indicators"
    >
      <button
        v-for="i in itemCount"
        :key="i"
        role="tab"
        :aria-selected="i - 1 === currentIndex"
        :aria-label="`Go to slide ${i}`"
        class="h-2 rounded-full transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1"
        :class="i - 1 === currentIndex ? 'bg-primary w-6' : 'bg-primary/40 hover:bg-primary/60 w-2'"
        @click="goTo(i - 1)"
      />
    </div>

    <!-- Play/Pause button -->
    <button
      v-if="showPlayButton"
      class="absolute top-3 right-3 w-8 h-8 rounded-full bg-background/80 backdrop-blur-sm shadow flex items-center justify-center hover:bg-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      :aria-label="isPlaying ? 'Pause auto-play' : 'Start auto-play'"
      @click="toggle"
    >
      <Play v-if="!isPlaying" class="h-4 w-4" />
      <Pause v-else class="h-4 w-4" />
    </button>
  </div>
</template>
