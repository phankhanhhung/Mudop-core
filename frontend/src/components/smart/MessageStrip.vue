<script setup lang="ts">
import { ref, computed } from 'vue'
import type { MessageType } from '@/odata/MessageManager'
import { AlertCircle, AlertTriangle, Info, CheckCircle2, X } from 'lucide-vue-next'

interface Props {
  type?: MessageType
  title: string
  description?: string
  closable?: boolean
  icon?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  type: 'info',
  closable: true,
  icon: true,
})

const emit = defineEmits<{
  close: []
}>()

const visible = ref(true)

const typeClasses = computed(() => {
  switch (props.type) {
    case 'error':
      return 'bg-destructive/10 border-destructive/30 text-destructive'
    case 'warning':
      return 'bg-amber-50 border-amber-200 text-amber-800 dark:bg-amber-950/30 dark:border-amber-800 dark:text-amber-200'
    case 'success':
      return 'bg-emerald-50 border-emerald-200 text-emerald-800 dark:bg-emerald-950/30 dark:border-emerald-800 dark:text-emerald-200'
    case 'info':
    default:
      return 'bg-blue-50 border-blue-200 text-blue-800 dark:bg-blue-950/30 dark:border-blue-800 dark:text-blue-200'
  }
})

const typeIcon = computed(() => {
  switch (props.type) {
    case 'error':
      return AlertCircle
    case 'warning':
      return AlertTriangle
    case 'success':
      return CheckCircle2
    case 'info':
    default:
      return Info
  }
})

function handleClose() {
  visible.value = false
  emit('close')
}
</script>

<template>
  <div
    v-if="visible"
    :class="['flex items-start gap-3 rounded-lg border p-4', typeClasses]"
    role="alert"
  >
    <component
      :is="typeIcon"
      v-if="icon"
      class="h-4 w-4 mt-0.5 flex-shrink-0"
    />
    <div class="flex-1 min-w-0">
      <p class="text-sm font-medium">{{ title }}</p>
      <p v-if="description" class="text-sm mt-1 opacity-80">
        {{ description }}
      </p>
      <slot />
    </div>
    <button
      v-if="closable"
      class="flex-shrink-0 p-1 rounded hover:bg-black/10 dark:hover:bg-white/10"
      @click="handleClose"
    >
      <X class="h-4 w-4" />
    </button>
  </div>
</template>
