<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { PanelLeftClose, PanelLeftOpen, PanelRightClose, PanelRightOpen } from 'lucide-vue-next'

interface Props {
  sideContentPosition?: 'begin' | 'end'
  equalSplit?: boolean
  sideContentVisible?: boolean
  sideContentFallDown?: 'below' | 'hidden'
  containerQuery?: boolean
  breakpoint?: number
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  sideContentPosition: 'end',
  equalSplit: false,
  sideContentVisible: true,
  sideContentFallDown: 'below',
  containerQuery: true,
  breakpoint: 720,
})

const emit = defineEmits<{
  'breakpoint-change': [payload: { isNarrow: boolean }]
  'update:sideContentVisible': [visible: boolean]
}>()

// ── Responsive state ────────────────────────────────────────────────────

const containerRef = ref<HTMLElement | null>(null)
const isNarrow = ref(false)
let resizeObserver: ResizeObserver | null = null

function onResize(entries: ResizeObserverEntry[]) {
  const entry = entries[0]
  if (!entry) return
  const width = entry.contentRect.width
  const narrow = width < props.breakpoint
  if (narrow !== isNarrow.value) {
    isNarrow.value = narrow
    emit('breakpoint-change', { isNarrow: narrow })
  }
}

onMounted(() => {
  if (containerRef.value) {
    resizeObserver = new ResizeObserver(onResize)
    resizeObserver.observe(containerRef.value)
  }
})

onBeforeUnmount(() => {
  resizeObserver?.disconnect()
})

// ── Visibility ──────────────────────────────────────────────────────────

const sideVisible = computed(() => props.sideContentVisible)

function toggleSide() {
  emit('update:sideContentVisible', !sideVisible.value)
}

// ── Computed layout classes ─────────────────────────────────────────────

const isStacked = computed(() => isNarrow.value)

const showSide = computed(() => {
  if (!sideVisible.value) return false
  if (isStacked.value && props.sideContentFallDown === 'hidden') return false
  return true
})

const mainWidthClass = computed(() => {
  if (isStacked.value || !showSide.value) return 'w-full'
  return props.equalSplit ? 'w-1/2' : 'w-2/3'
})

const sideWidthClass = computed(() => {
  if (isStacked.value) return 'w-full'
  return props.equalSplit ? 'w-1/2' : 'w-1/3'
})

// ── Toggle icon ─────────────────────────────────────────────────────────

const ToggleIcon = computed(() => {
  if (sideVisible.value) {
    return props.sideContentPosition === 'begin' ? PanelLeftClose : PanelRightClose
  }
  return props.sideContentPosition === 'begin' ? PanelLeftOpen : PanelRightOpen
})

const toggleLabel = computed(() =>
  sideVisible.value ? 'Hide side content' : 'Show side content'
)

// ── Watch for external breakpoint changes ───────────────────────────────

watch(() => props.breakpoint, () => {
  if (containerRef.value) {
    const width = containerRef.value.getBoundingClientRect().width
    const narrow = width < props.breakpoint
    if (narrow !== isNarrow.value) {
      isNarrow.value = narrow
      emit('breakpoint-change', { isNarrow: narrow })
    }
  }
})
</script>

<template>
  <div
    ref="containerRef"
    :class="cn('dynamic-side-content relative', props.class)"
  >
    <!-- Toggle button -->
    <div class="flex justify-end mb-2">
      <Button
        variant="ghost"
        size="sm"
        :aria-label="toggleLabel"
        @click="toggleSide"
      >
        <component :is="ToggleIcon" class="h-4 w-4 mr-1" />
        <span class="text-xs">{{ sideVisible ? 'Hide' : 'Show' }} Panel</span>
      </Button>
    </div>

    <!-- Layout container -->
    <div
      :class="[
        'flex transition-all duration-300 ease-in-out',
        isStacked ? 'flex-col gap-4' : 'flex-row gap-0',
      ]"
    >
      <!-- Side content (begin position) -->
      <aside
        v-if="showSide && sideContentPosition === 'begin'"
        role="complementary"
        aria-label="Side content"
        :class="[
          sideWidthClass,
          'transition-all duration-300 ease-in-out',
          !isStacked ? 'border-r border-border pr-4' : 'border-b border-border pb-4',
        ]"
      >
        <slot name="side" />
      </aside>

      <!-- Main content -->
      <main
        role="main"
        aria-label="Main content"
        :class="[
          mainWidthClass,
          'transition-all duration-300 ease-in-out',
          !isStacked && showSide ? (sideContentPosition === 'begin' ? 'pl-4' : 'pr-4') : '',
        ]"
      >
        <slot />
      </main>

      <!-- Side content (end position) -->
      <aside
        v-if="showSide && sideContentPosition === 'end'"
        role="complementary"
        aria-label="Side content"
        :class="[
          sideWidthClass,
          'transition-all duration-300 ease-in-out',
          !isStacked ? 'border-l border-border pl-4' : 'border-t border-border pt-4',
        ]"
      >
        <slot name="side" />
      </aside>
    </div>
  </div>
</template>
