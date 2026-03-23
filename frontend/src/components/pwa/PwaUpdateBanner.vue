<script setup lang="ts">
import { ref } from 'vue'
import { usePwa } from '@/composables/usePwa'
import { Button } from '@/components/ui/button'
import { RefreshCw, X } from 'lucide-vue-next'

const { needsUpdate, updateApp } = usePwa()

const dismissed = ref(false)

function dismiss() {
  dismissed.value = true
}
</script>

<template>
  <Transition name="slide-down">
    <div
      v-if="needsUpdate && !dismissed"
      class="relative z-50 flex items-center justify-between gap-4 bg-primary px-4 py-2.5 text-primary-foreground"
      role="status"
      aria-live="polite"
    >
      <div class="flex items-center gap-2 text-sm">
        <RefreshCw class="h-4 w-4 shrink-0" aria-hidden="true" />
        <span>A new version is available.</span>
      </div>
      <div class="flex items-center gap-2 shrink-0">
        <Button
          variant="secondary"
          size="sm"
          class="h-7 text-xs"
          @click="updateApp"
        >
          Update now
        </Button>
        <button
          class="rounded p-0.5 hover:bg-primary-foreground/20 transition-colors"
          aria-label="Dismiss update notification"
          @click="dismiss"
        >
          <X class="h-4 w-4" aria-hidden="true" />
        </button>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.slide-down-enter-active,
.slide-down-leave-active {
  transition: all 0.2s ease;
}
.slide-down-enter-from,
.slide-down-leave-to {
  opacity: 0;
  transform: translateY(-100%);
}
</style>
