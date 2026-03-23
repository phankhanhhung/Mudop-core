<script setup lang="ts">
import { watch } from 'vue'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { ArrowLeft, PanelLeftClose } from 'lucide-vue-next'
import { useSplitApp, type SplitAppMode } from '@/composables/useSplitApp'

interface Props {
  breakpoint?: number
  masterWidth?: string
  showBackButton?: boolean
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  breakpoint: 768,
  masterWidth: '360px',
  showBackButton: true,
})

const emit = defineEmits<{
  'mode-change': [mode: SplitAppMode]
  'back': []
}>()

const {
  containerRef,
  containerWidth,
  isMobile,
  currentMode,
  showMaster,
  showDetail,
  hasDetail,
  setHasDetail,
} = useSplitApp({
  breakpoint: props.breakpoint,
  masterWidth: props.masterWidth,
})

watch(currentMode, (mode) => {
  emit('mode-change', mode)
})

function onBack(): void {
  showMaster()
  emit('back')
}

defineExpose({
  containerWidth,
  isMobile,
  currentMode,
  showMaster,
  showDetail,
  hasDetail,
  setHasDetail,
})
</script>

<template>
  <div
    ref="containerRef"
    :class="cn('split-app h-full flex overflow-hidden', props.class)"
  >
    <!-- Master pane -->
    <div
      v-show="currentMode === 'master' || currentMode === 'both'"
      class="split-master h-full flex-shrink-0 border-r border-border overflow-y-auto bg-background transition-all duration-300"
      :style="{ width: currentMode === 'both' ? props.masterWidth : '100%' }"
    >
      <div
        v-if="$slots['master-header']"
        class="sticky top-0 z-10 border-b border-border bg-background/95 backdrop-blur-sm px-4 py-3"
      >
        <slot name="master-header" />
      </div>
      <slot name="master" :show-detail="showDetail" />
    </div>

    <!-- Detail pane -->
    <div
      v-show="currentMode === 'detail' || currentMode === 'both'"
      class="split-detail h-full flex-1 overflow-y-auto bg-background transition-all duration-300"
    >
      <div class="sticky top-0 z-10 border-b border-border bg-background/95 backdrop-blur-sm">
        <div class="flex items-center gap-2 px-4 py-3">
          <!-- Back button (mobile only) -->
          <Button
            v-if="props.showBackButton && isMobile"
            variant="ghost"
            size="icon"
            @click="onBack"
          >
            <ArrowLeft class="h-4 w-4" />
          </Button>
          <slot name="detail-header" />
        </div>
      </div>

      <slot name="detail" :show-master="showMaster" />

      <!-- Empty state when no detail selected -->
      <div
        v-if="!hasDetail"
        class="flex flex-col items-center justify-center h-[calc(100%-53px)] text-muted-foreground"
      >
        <PanelLeftClose class="h-12 w-12 mb-4 opacity-30" />
        <p class="text-sm">Select an item to view details</p>
      </div>
    </div>
  </div>
</template>
