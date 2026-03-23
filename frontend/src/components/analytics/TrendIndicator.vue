<script setup lang="ts">
import { computed } from 'vue'
import { TrendingUp, TrendingDown, Minus } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'

interface Props {
  value: number
  previousValue: number
  format?: 'percent' | 'absolute' | 'compact'
  invertColors?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  format: 'percent',
  invertColors: false,
})

const { t } = useI18n()

const diff = computed(() => props.value - props.previousValue)

const direction = computed<'up' | 'down' | 'neutral'>(() => {
  if (diff.value > 0) return 'up'
  if (diff.value < 0) return 'down'
  return 'neutral'
})

const formattedChange = computed(() => {
  const absDiff = Math.abs(diff.value)
  switch (props.format) {
    case 'percent': {
      if (props.previousValue === 0) {
        return diff.value > 0 ? '+100%' : diff.value < 0 ? '-100%' : '0%'
      }
      const pct = (diff.value / props.previousValue) * 100
      const sign = pct > 0 ? '+' : ''
      return `${sign}${pct.toFixed(1)}%`
    }
    case 'absolute': {
      const sign = diff.value > 0 ? '+' : ''
      return `${sign}${diff.value.toLocaleString()}`
    }
    case 'compact': {
      const sign = diff.value > 0 ? '+' : diff.value < 0 ? '-' : ''
      if (absDiff >= 1_000_000) return `${sign}${(absDiff / 1_000_000).toFixed(1)}M`
      if (absDiff >= 1_000) return `${sign}${(absDiff / 1_000).toFixed(1)}K`
      return `${sign}${absDiff}`
    }
    default:
      return String(diff.value)
  }
})

const colorClass = computed(() => {
  if (direction.value === 'neutral') return 'text-muted-foreground bg-muted'
  const isPositive = direction.value === 'up'
  const isGood = props.invertColors ? !isPositive : isPositive
  return isGood
    ? 'text-emerald-700 bg-emerald-500/10 dark:text-emerald-400 dark:bg-emerald-500/15'
    : 'text-rose-700 bg-rose-500/10 dark:text-rose-400 dark:bg-rose-500/15'
})

const tooltipText = computed(() => {
  return `${props.previousValue.toLocaleString()} \u2192 ${props.value.toLocaleString()}`
})

const iconComponent = computed(() => {
  switch (direction.value) {
    case 'up':
      return TrendingUp
    case 'down':
      return TrendingDown
    default:
      return Minus
  }
})
</script>

<template>
  <span
    class="inline-flex items-center gap-1 px-1.5 py-0.5 rounded-md text-xs font-medium"
    :class="colorClass"
    :title="tooltipText"
    role="status"
    :aria-label="t('common.analytics', 'Trend') + ': ' + formattedChange"
  >
    <component :is="iconComponent" class="h-3 w-3" />
    <span>{{ formattedChange }}</span>
  </span>
</template>
