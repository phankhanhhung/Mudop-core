<script setup lang="ts">
import { computed } from 'vue'
import { cn } from '@/lib/utils'
import { TrendingUp, TrendingDown } from 'lucide-vue-next'

type Indicator = 'Up' | 'Down' | 'None'
type ValueColor = 'Good' | 'Error' | 'Critical' | 'Neutral'

const props = withDefaults(
  defineProps<{
    value: string | number
    indicator?: Indicator
    valueColor?: ValueColor
    scale?: string
    withMargin?: boolean
  }>(),
  {
    indicator: 'None',
    valueColor: 'Neutral',
    withMargin: false,
  }
)

const colorClass = computed(() => {
  switch (props.valueColor) {
    case 'Good':
      return 'text-emerald-600 dark:text-emerald-400'
    case 'Error':
      return 'text-amber-600 dark:text-amber-400'
    case 'Critical':
      return 'text-red-600 dark:text-red-400'
    case 'Neutral':
    default:
      return 'text-foreground'
  }
})

const indicatorColorClass = computed(() => {
  switch (props.indicator) {
    case 'Up':
      return 'text-emerald-600 dark:text-emerald-400'
    case 'Down':
      return 'text-red-600 dark:text-red-400'
    default:
      return ''
  }
})
</script>

<template>
  <div :class="cn('flex items-baseline gap-1.5', withMargin ? 'mt-1' : '')">
    <!-- Trend indicator -->
    <TrendingUp
      v-if="indicator === 'Up'"
      :class="cn('h-4 w-4 shrink-0 self-center', indicatorColorClass)"
    />
    <TrendingDown
      v-if="indicator === 'Down'"
      :class="cn('h-4 w-4 shrink-0 self-center', indicatorColorClass)"
    />

    <!-- Value -->
    <span :class="cn('text-2xl font-bold leading-none tracking-tight', colorClass)">
      {{ value }}
    </span>

    <!-- Scale -->
    <span v-if="scale" class="text-sm font-medium text-muted-foreground">
      {{ scale }}
    </span>
  </div>
</template>
