<script setup lang="ts">
import { ref, onErrorCaptured } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'

const { t } = useI18n()
const router = useRouter()

const hasError = ref(false)
const errorMessage = ref('')
const errorDetails = ref('')
const showDetails = ref(false)

onErrorCaptured((err: Error) => {
  hasError.value = true
  errorMessage.value = err.message || t('errors.unknown')
  errorDetails.value = import.meta.env.DEV ? (err.stack || '') : ''
  console.error('[ErrorBoundary]', err)
  return false
})

function retry() {
  hasError.value = false
  errorMessage.value = ''
  errorDetails.value = ''
  showDetails.value = false
}

function goHome() {
  hasError.value = false
  router.push('/')
}
</script>

<template>
  <slot v-if="!hasError" />
  <div v-else class="flex flex-col items-center justify-center min-h-[400px] p-8 text-center">
    <div class="rounded-lg border border-destructive/30 bg-destructive/5 p-8 max-w-lg w-full">
      <svg
        class="mx-auto mb-4 h-12 w-12 text-destructive"
        xmlns="http://www.w3.org/2000/svg"
        fill="none"
        viewBox="0 0 24 24"
        stroke-width="1.5"
        stroke="currentColor"
        aria-hidden="true"
      >
        <path
          stroke-linecap="round"
          stroke-linejoin="round"
          d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126ZM12 15.75h.007v.008H12v-.008Z"
        />
      </svg>

      <h2 class="text-lg font-semibold text-foreground mb-2">
        {{ $t('errors.boundary.title') }}
      </h2>
      <p class="text-sm text-muted-foreground mb-6">
        {{ errorMessage }}
      </p>

      <div class="flex items-center justify-center gap-3 mb-4">
        <Button variant="default" @click="retry">
          {{ $t('common.retry') }}
        </Button>
        <Button variant="outline" @click="goHome">
          {{ $t('errors.boundary.goHome') }}
        </Button>
      </div>

      <button
        v-if="errorDetails"
        class="text-xs text-muted-foreground hover:text-foreground underline transition-colors"
        @click="showDetails = !showDetails"
      >
        {{ showDetails ? $t('errors.boundary.hideDetails') : $t('errors.boundary.showDetails') }}
      </button>

      <Transition name="details">
        <pre
          v-if="showDetails"
          class="mt-3 max-h-48 overflow-auto rounded bg-muted p-3 text-left text-xs text-muted-foreground"
        >{{ errorDetails }}</pre>
      </Transition>
    </div>
  </div>
</template>

<style scoped>
.details-enter-active,
.details-leave-active {
  transition: all 0.2s ease;
}

.details-enter-from,
.details-leave-to {
  opacity: 0;
  max-height: 0;
}
</style>
