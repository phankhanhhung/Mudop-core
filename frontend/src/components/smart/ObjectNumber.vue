<script setup lang="ts">
import { computed } from 'vue'
import { cn } from '@/lib/utils'
import type { ObjectStatusState } from './ObjectStatus.vue'

interface Props {
  number: string | number
  unit?: string
  state?: ObjectStatusState
  emphasized?: boolean
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  state: 'None',
  emphasized: false,
})

const stateColorClass = computed(() => {
  switch (props.state) {
    case 'Success': return 'text-emerald-700 dark:text-emerald-400'
    case 'Warning': return 'text-amber-700 dark:text-amber-400'
    case 'Error': return 'text-red-700 dark:text-red-400'
    case 'Information': return 'text-blue-700 dark:text-blue-400'
    default: return 'text-foreground'
  }
})

const formattedNumber = computed(() => {
  if (typeof props.number === 'number') {
    return props.number.toLocaleString()
  }
  return props.number
})

const classes = computed(() =>
  cn(
    'inline-flex items-baseline gap-1',
    stateColorClass.value,
    props.class
  )
)
</script>

<template>
  <span :class="classes">
    <span :class="emphasized ? 'text-lg font-bold' : 'text-sm font-semibold tabular-nums'">
      {{ formattedNumber }}
    </span>
    <span v-if="unit" class="text-xs text-muted-foreground">{{ unit }}</span>
  </span>
</template>
