<script setup lang="ts">
import { computed, type Component } from 'vue'
import { cn } from '@/lib/utils'
import { CheckCircle, AlertCircle, AlertTriangle, Info, MinusCircle } from 'lucide-vue-next'

export type ObjectStatusState = 'Success' | 'Warning' | 'Error' | 'Information' | 'None'

interface Props {
  text: string
  state?: ObjectStatusState
  icon?: Component
  active?: boolean
  inverted?: boolean
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  state: 'None',
  active: false,
  inverted: false,
})

const emit = defineEmits<{
  click: [event: Event]
}>()

const stateConfig = computed(() => {
  const configs: Record<ObjectStatusState, { text: string; bg: string; icon: Component }> = {
    Success: {
      text: 'text-emerald-700 dark:text-emerald-400',
      bg: 'bg-emerald-100 dark:bg-emerald-900/40',
      icon: CheckCircle,
    },
    Warning: {
      text: 'text-amber-700 dark:text-amber-400',
      bg: 'bg-amber-100 dark:bg-amber-900/40',
      icon: AlertTriangle,
    },
    Error: {
      text: 'text-red-700 dark:text-red-400',
      bg: 'bg-red-100 dark:bg-red-900/40',
      icon: AlertCircle,
    },
    Information: {
      text: 'text-blue-700 dark:text-blue-400',
      bg: 'bg-blue-100 dark:bg-blue-900/40',
      icon: Info,
    },
    None: {
      text: 'text-muted-foreground',
      bg: 'bg-muted',
      icon: MinusCircle,
    },
  }
  return configs[props.state]
})

const iconComponent = computed(() => props.icon ?? stateConfig.value.icon)

const classes = computed(() =>
  cn(
    'inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-medium',
    props.inverted
      ? [stateConfig.value.bg, stateConfig.value.text]
      : stateConfig.value.text,
    props.active && 'cursor-pointer hover:opacity-80 transition-opacity',
    props.class
  )
)

function handleClick(event: Event) {
  if (props.active) {
    emit('click', event)
  }
}
</script>

<template>
  <span
    :class="classes"
    :role="active ? 'button' : 'status'"
    :tabindex="active ? 0 : undefined"
    @click="handleClick"
    @keydown.enter="handleClick"
    @keydown.space.prevent="handleClick"
  >
    <component :is="iconComponent" class="h-3 w-3 shrink-0" />
    <span>{{ text }}</span>
  </span>
</template>
