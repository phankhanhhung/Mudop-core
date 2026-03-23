<script setup lang="ts">
import { useUiStore } from '@/stores/ui'
import { Spinner } from '@/components/ui/spinner'

const uiStore = useUiStore()
</script>

<template>
  <Teleport to="body">
    <Transition name="fade">
      <div
        v-if="uiStore.globalLoading"
        class="fixed inset-0 z-[100] flex items-center justify-center bg-background/80 backdrop-blur-sm"
        role="status"
        aria-live="assertive"
        aria-busy="true"
      >
        <div class="flex flex-col items-center gap-4">
          <Spinner size="lg" class="text-primary" aria-hidden="true" />
          <p class="text-sm text-muted-foreground">
            {{ uiStore.globalLoadingMessage || $t('accessibility.loading') }}
          </p>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
