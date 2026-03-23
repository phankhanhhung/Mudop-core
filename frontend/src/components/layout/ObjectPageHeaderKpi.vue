<script setup lang="ts">
import { computed, type Component } from 'vue'
import { ArrowUp, ArrowDown, Minus } from 'lucide-vue-next'
import { cn } from '@/lib/utils'

interface Props {
  /** KPI label */
  label: string
  /** KPI value (main display) */
  value: string | number
  /** Unit text (e.g. "USD", "items", "%") */
  unit?: string
  /** Trend direction */
  trend?: 'up' | 'down' | 'neutral'
  /** Trend color override */
  trendColor?: 'positive' | 'negative' | 'neutral'
  /** Optional icon */
  icon?: Component
}

const props = withDefaults(defineProps<Props>(), {
  unit: undefined,
  trend: undefined,
  trendColor: undefined,
  icon: undefined,
})

const trendIcon = computed(() => {
  if (!props.trend) return null
  switch (props.trend) {
    case 'up': return ArrowUp
    case 'down': return ArrowDown
    case 'neutral': return Minus
  }
})

const resolvedTrendColor = computed(() => {
  if (props.trendColor) return props.trendColor
  if (!props.trend) return 'neutral'
  switch (props.trend) {
    case 'up': return 'positive'
    case 'down': return 'negative'
    case 'neutral': return 'neutral'
  }
})

const trendColorClass = computed(() => {
  switch (resolvedTrendColor.value) {
    case 'positive': return 'text-emerald-600'
    case 'negative': return 'text-red-600'
    case 'neutral': return 'text-muted-foreground'
    default: return 'text-muted-foreground'
  }
})
</script>

<template>
  <div
    class="flex items-start gap-3 px-4 py-3 rounded-lg bg-muted/30 border border-border/50"
    role="group"
    :aria-label="`${label}: ${value}${unit ? ' ' + unit : ''}`"
  >
    <component
      v-if="icon"
      :is="icon"
      class="h-5 w-5 text-muted-foreground mt-0.5 shrink-0"
      aria-hidden="true"
    />
    <div class="min-w-0">
      <p class="text-xs text-muted-foreground uppercase tracking-wider truncate">
        {{ label }}
      </p>
      <p class="text-xl font-bold text-foreground mt-0.5 truncate">
        {{ value }}
      </p>
      <div v-if="unit || trend" class="flex items-center gap-1.5 mt-0.5">
        <span v-if="unit" class="text-xs text-muted-foreground">
          {{ unit }}
        </span>
        <component
          v-if="trendIcon"
          :is="trendIcon"
          :class="cn('h-3.5 w-3.5', trendColorClass)"
          :aria-label="`Trend: ${trend}`"
        />
      </div>
    </div>
  </div>
</template>
