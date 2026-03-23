<script setup lang="ts">
import { ref } from 'vue'
import { usePwa } from '@/composables/usePwa'
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
      class="fixed top-0 inset-x-0 z-50 bg-blue-600 text-white px-4 py-3 shadow-lg"
    >
      <div class="max-w-7xl mx-auto flex items-center justify-between gap-4">
        <div class="flex items-center gap-2 text-sm">
          <RefreshCw class="h-4 w-4" />
          <span>A new version is available.</span>
        </div>
        <div class="flex items-center gap-2">
          <button
            class="rounded bg-white text-blue-600 px-3 py-1 text-sm font-medium hover:bg-blue-50 transition-colors"
            @click="updateApp"
          >
            Update now
          </button>
          <button
            class="rounded p-1 hover:bg-blue-500 transition-colors"
            aria-label="Dismiss"
            @click="dismiss"
          >
            <X class="h-4 w-4" />
          </button>
        </div>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.slide-down-enter-active,
.slide-down-leave-active {
  transition: transform 0.3s ease, opacity 0.3s ease;
}
.slide-down-enter-from,
.slide-down-leave-to {
  transform: translateY(-100%);
  opacity: 0;
}
</style>
