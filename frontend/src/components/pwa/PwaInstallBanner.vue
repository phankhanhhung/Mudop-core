<script setup lang="ts">
import { ref } from 'vue'
import { usePwa } from '@/composables/usePwa'
import { Button } from '@/components/ui/button'
import { Download, X } from 'lucide-vue-next'

const { isInstallable, installApp } = usePwa()

const dismissed = ref(false)

function dismiss() {
  dismissed.value = true
}
</script>

<template>
  <Transition name="slide-up">
    <div
      v-if="isInstallable && !dismissed"
      class="fixed bottom-4 right-4 z-50 flex items-center gap-3 rounded-lg border bg-background shadow-lg px-4 py-3 max-w-sm"
      role="complementary"
      aria-label="Install application"
    >
      <Download class="h-5 w-5 text-primary shrink-0" aria-hidden="true" />
      <div class="flex-1 min-w-0">
        <p class="text-sm font-medium">Install BMMDL Platform</p>
        <p class="text-xs text-muted-foreground">Add to your home screen for quick access</p>
      </div>
      <div class="flex items-center gap-1 shrink-0">
        <Button size="sm" class="h-7 text-xs" @click="installApp">Install</Button>
        <button
          class="rounded p-0.5 hover:bg-muted transition-colors"
          aria-label="Dismiss install prompt"
          @click="dismiss"
        >
          <X class="h-3.5 w-3.5 text-muted-foreground" aria-hidden="true" />
        </button>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.slide-up-enter-active,
.slide-up-leave-active {
  transition: all 0.25s ease;
}
.slide-up-enter-from,
.slide-up-leave-to {
  opacity: 0;
  transform: translateY(1rem);
}
</style>
