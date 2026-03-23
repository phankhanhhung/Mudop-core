<script setup lang="ts">
import { useUiStore, type Toast } from '@/stores/ui'
import { cn } from '@/lib/utils'
import { X, CheckCircle, XCircle, AlertTriangle, Info } from 'lucide-vue-next'

const uiStore = useUiStore()

const icons = {
  success: CheckCircle,
  error: XCircle,
  warning: AlertTriangle,
  info: Info
}

const colors = {
  success: 'border-green-500 bg-green-50 dark:bg-green-950',
  error: 'border-red-500 bg-red-50 dark:bg-red-950',
  warning: 'border-yellow-500 bg-yellow-50 dark:bg-yellow-950',
  info: 'border-blue-500 bg-blue-50 dark:bg-blue-950'
}

const iconColors = {
  success: 'text-green-600 dark:text-green-400',
  error: 'text-red-600 dark:text-red-400',
  warning: 'text-yellow-600 dark:text-yellow-400',
  info: 'text-blue-600 dark:text-blue-400'
}

function getToastClass(toast: Toast) {
  return cn(
    'flex items-start gap-3 rounded-lg border-l-4 p-4 shadow-lg bg-background',
    colors[toast.type]
  )
}
</script>

<template>
  <div
    class="fixed bottom-4 right-4 z-50 flex flex-col gap-2 max-w-sm"
    role="region"
    :aria-label="$t('accessibility.toastNotifications')"
    aria-live="polite"
  >
    <TransitionGroup name="toast">
      <div
        v-for="toast in uiStore.toasts"
        :key="toast.id"
        role="alert"
        :class="getToastClass(toast)"
      >
        <component
          :is="icons[toast.type]"
          :class="cn('h-5 w-5 shrink-0 mt-0.5', iconColors[toast.type])"
        />
        <div class="flex-1 min-w-0">
          <p class="font-medium text-sm">{{ toast.title }}</p>
          <p v-if="toast.message" class="text-sm text-muted-foreground mt-1">
            {{ toast.message }}
          </p>
        </div>
        <button
          class="shrink-0 text-muted-foreground hover:text-foreground"
          :aria-label="$t('accessibility.dismissToast')"
          @click="uiStore.removeToast(toast.id)"
        >
          <X class="h-4 w-4" />
        </button>
      </div>
    </TransitionGroup>
  </div>
</template>

<style scoped>
.toast-enter-active,
.toast-leave-active {
  transition: all 0.3s ease;
}

.toast-enter-from {
  opacity: 0;
  transform: translateX(100%);
}

.toast-leave-to {
  opacity: 0;
  transform: translateX(100%);
}

.toast-move {
  transition: transform 0.3s ease;
}
</style>
