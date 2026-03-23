<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'

const isOffline = ref(!navigator.onLine)
const showBackOnline = ref(false)

let backOnlineTimer: ReturnType<typeof setTimeout> | null = null

function handleOffline() {
  isOffline.value = true
  showBackOnline.value = false
}

function handleOnline() {
  isOffline.value = false
  showBackOnline.value = true
  backOnlineTimer = setTimeout(() => {
    showBackOnline.value = false
  }, 3000)
}

onMounted(() => {
  window.addEventListener('offline', handleOffline)
  window.addEventListener('online', handleOnline)
})

onUnmounted(() => {
  window.removeEventListener('offline', handleOffline)
  window.removeEventListener('online', handleOnline)
  if (backOnlineTimer) clearTimeout(backOnlineTimer)
})
</script>

<template>
  <Transition name="slide">
    <div
      v-if="isOffline"
      class="fixed top-0 left-0 right-0 z-[200] bg-destructive px-4 py-2 text-center text-sm text-destructive-foreground"
      role="alert"
    >
      {{ $t('errors.offline') }}
    </div>
  </Transition>
  <Transition name="slide">
    <div
      v-if="showBackOnline"
      class="fixed top-0 left-0 right-0 z-[200] bg-green-600 px-4 py-2 text-center text-sm text-white"
      role="status"
    >
      {{ $t('errors.backOnline') }}
    </div>
  </Transition>
</template>

<style scoped>
.slide-enter-active,
.slide-leave-active {
  transition: transform 0.3s ease, opacity 0.3s ease;
}

.slide-enter-from,
.slide-leave-to {
  transform: translateY(-100%);
  opacity: 0;
}
</style>
