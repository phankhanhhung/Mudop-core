<script setup lang="ts">
import { computed } from 'vue'
import { ChevronLeft, ChevronRight, X, Maximize2 } from 'lucide-vue-next'
import type { FclLayout } from '@/composables/useFcl'

interface Props {
  layout: FclLayout
  showSeparators?: boolean
  backgroundDesign?: 'solid' | 'transparent'
}

const props = withDefaults(defineProps<Props>(), {
  showSeparators: true,
  backgroundDesign: 'solid',
})

const emit = defineEmits<{
  'layout-change': [layout: FclLayout]
  'begin-column-navigate': []
  'mid-column-navigate': []
  'end-column-navigate': []
}>()

const columnWidths: Record<FclLayout, { begin: string; mid: string; end: string }> = {
  'OneColumn':               { begin: '100%',  mid: '0%',   end: '0%' },
  'TwoColumnsMidExpanded':   { begin: '33.3%', mid: '66.7%', end: '0%' },
  'TwoColumnsBeginExpanded': { begin: '66.7%', mid: '33.3%', end: '0%' },
  'ThreeColumnsMidExpanded': { begin: '25%',   mid: '50%',  end: '25%' },
  'ThreeColumnsEndExpanded': { begin: '25%',   mid: '25%',  end: '50%' },
  'MidColumnFullScreen':     { begin: '0%',    mid: '100%', end: '0%' },
  'EndColumnFullScreen':     { begin: '0%',    mid: '0%',   end: '100%' },
}

const backgroundClass = computed(() => {
  if (props.backgroundDesign === 'transparent') return 'bg-transparent'
  return 'bg-background'
})

const beginWidth = computed(() => columnWidths[props.layout].begin)
const midWidth = computed(() => columnWidths[props.layout].mid)
const endWidth = computed(() => columnWidths[props.layout].end)

const showBegin = computed(() => beginWidth.value !== '0%')
const showMid = computed(() => midWidth.value !== '0%')
const showEnd = computed(() => endWidth.value !== '0%')

const isBeginExpanded = computed(() =>
  props.layout === 'TwoColumnsBeginExpanded'
)

const isEndExpanded = computed(() =>
  props.layout === 'ThreeColumnsEndExpanded'
)

const showMidActions = computed(() =>
  showMid.value && props.layout !== 'MidColumnFullScreen'
)

const showEndActions = computed(() =>
  showEnd.value && props.layout !== 'EndColumnFullScreen'
)

const beginExpandTitle = computed(() =>
  isBeginExpanded.value ? 'Collapse list' : 'Expand list'
)

function toggleBeginExpand(): void {
  if (isBeginExpanded.value) {
    emit('layout-change', 'TwoColumnsMidExpanded')
  } else {
    emit('layout-change', 'TwoColumnsBeginExpanded')
  }
}

function toggleEndExpand(): void {
  if (isEndExpanded.value) {
    emit('layout-change', 'ThreeColumnsMidExpanded')
  } else {
    emit('layout-change', 'ThreeColumnsEndExpanded')
  }
}

function fullscreenMid(): void {
  emit('layout-change', 'MidColumnFullScreen')
}

function closeMid(): void {
  emit('layout-change', 'OneColumn')
}

function fullscreenEnd(): void {
  emit('layout-change', 'EndColumnFullScreen')
}

function closeEnd(): void {
  emit('layout-change', 'TwoColumnsMidExpanded')
}
</script>

<template>
  <div class="fcl-container flex h-full w-full overflow-hidden" :class="backgroundClass">
    <!-- Begin Column (List) -->
    <transition name="fcl-slide">
      <div
        v-if="showBegin"
        class="fcl-column flex-shrink-0 overflow-y-auto border-r border-border transition-all duration-300 ease-in-out"
        :style="{ width: beginWidth }"
      >
        <slot name="begin" />
      </div>
    </transition>

    <!-- Begin -> Mid separator -->
    <div
      v-if="showBegin && showMid && showSeparators"
      class="flex flex-col items-center justify-center w-6 flex-shrink-0 border-r border-border bg-muted/30"
    >
      <button
        class="p-1 rounded hover:bg-muted text-muted-foreground hover:text-foreground transition-colors"
        :title="beginExpandTitle"
        @click="toggleBeginExpand"
      >
        <ChevronLeft v-if="isBeginExpanded" class="h-4 w-4" />
        <ChevronRight v-else class="h-4 w-4" />
      </button>
    </div>

    <!-- Mid Column (Detail) -->
    <transition name="fcl-slide">
      <div
        v-if="showMid"
        class="fcl-column relative flex-shrink-0 overflow-y-auto border-r border-border transition-all duration-300 ease-in-out"
        :style="{ width: midWidth }"
      >
        <!-- Close / Full-screen buttons -->
        <div v-if="showMidActions" class="sticky top-0 z-10 flex justify-end gap-1 px-4 pt-3 pb-1">
          <button
            class="p-1.5 rounded-md hover:bg-muted text-muted-foreground hover:text-foreground transition-colors"
            title="Full screen"
            @click="fullscreenMid"
          >
            <Maximize2 class="h-4 w-4" />
          </button>
          <button
            class="p-1.5 rounded-md hover:bg-muted text-muted-foreground hover:text-foreground transition-colors"
            title="Close"
            @click="closeMid"
          >
            <X class="h-4 w-4" />
          </button>
        </div>
        <slot name="mid" />
      </div>
    </transition>

    <!-- Mid -> End separator -->
    <div
      v-if="showMid && showEnd && showSeparators"
      class="flex flex-col items-center justify-center w-6 flex-shrink-0 border-r border-border bg-muted/30"
    >
      <button
        class="p-1 rounded hover:bg-muted text-muted-foreground hover:text-foreground transition-colors"
        :title="isEndExpanded ? 'Collapse sub-detail' : 'Expand sub-detail'"
        @click="toggleEndExpand"
      >
        <ChevronLeft v-if="isEndExpanded" class="h-4 w-4" />
        <ChevronRight v-else class="h-4 w-4" />
      </button>
    </div>

    <!-- End Column (Sub-detail) -->
    <transition name="fcl-slide">
      <div
        v-if="showEnd"
        class="fcl-column relative flex-shrink-0 overflow-y-auto transition-all duration-300 ease-in-out"
        :style="{ width: endWidth }"
      >
        <div v-if="showEndActions" class="sticky top-0 z-10 flex justify-end gap-1 px-4 pt-3 pb-1">
          <button
            class="p-1.5 rounded-md hover:bg-muted text-muted-foreground hover:text-foreground transition-colors"
            title="Full screen"
            @click="fullscreenEnd"
          >
            <Maximize2 class="h-4 w-4" />
          </button>
          <button
            class="p-1.5 rounded-md hover:bg-muted text-muted-foreground hover:text-foreground transition-colors"
            title="Close"
            @click="closeEnd"
          >
            <X class="h-4 w-4" />
          </button>
        </div>
        <slot name="end" />
      </div>
    </transition>
  </div>
</template>

<style scoped>
.fcl-slide-enter-active,
.fcl-slide-leave-active {
  transition: width 0.3s ease, opacity 0.3s ease;
}

.fcl-slide-enter-from,
.fcl-slide-leave-to {
  width: 0 !important;
  opacity: 0;
}
</style>
