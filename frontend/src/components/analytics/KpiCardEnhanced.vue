<script setup lang="ts">
import { type Component, computed } from 'vue'
import { Card, CardContent } from '@/components/ui/card'
import { TrendingUp, TrendingDown, Minus } from 'lucide-vue-next'
import SparklineChart from './SparklineChart.vue'

interface Props {
  title: string
  value: string | number
  description?: string
  icon?: Component
  color?: 'primary' | 'emerald' | 'violet' | 'amber' | 'rose' | 'cyan'
  trend?: 'up' | 'down' | 'neutral'
  trendValue?: string
  sparklineData?: number[]
  target?: number
  unit?: string
  loading?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  description: undefined,
  icon: undefined,
  color: 'primary',
  trend: undefined,
  trendValue: undefined,
  sparklineData: undefined,
  target: undefined,
  unit: undefined,
  loading: false,
})

const trendIcon: Record<string, Component> = {
  up: TrendingUp,
  down: TrendingDown,
  neutral: Minus,
}

const trendColor: Record<string, string> = {
  up: 'text-emerald-600 dark:text-emerald-400',
  down: 'text-rose-600 dark:text-rose-400',
  neutral: 'text-muted-foreground',
}

const colorMap: Record<string, { bg: string; text: string; value: string; sparkline: 'primary' | 'emerald' | 'violet' | 'amber' | 'rose' | 'cyan' }> = {
  primary: { bg: 'bg-primary/10', text: 'text-primary', value: '', sparkline: 'primary' },
  emerald: { bg: 'bg-emerald-500/10', text: 'text-emerald-500', value: 'text-emerald-600 dark:text-emerald-400', sparkline: 'emerald' },
  violet: { bg: 'bg-violet-500/10', text: 'text-violet-500', value: 'text-violet-600 dark:text-violet-400', sparkline: 'violet' },
  amber: { bg: 'bg-amber-500/10', text: 'text-amber-500', value: 'text-amber-600 dark:text-amber-400', sparkline: 'amber' },
  rose: { bg: 'bg-rose-500/10', text: 'text-rose-500', value: 'text-rose-600 dark:text-rose-400', sparkline: 'rose' },
  cyan: { bg: 'bg-cyan-500/10', text: 'text-cyan-500', value: 'text-cyan-600 dark:text-cyan-400', sparkline: 'cyan' },
}

const targetProgress = computed(() => {
  if (props.target === undefined || props.target === 0) return null
  const numVal = typeof props.value === 'number' ? props.value : parseFloat(String(props.value))
  if (isNaN(numVal)) return null
  return Math.min(Math.round((numVal / props.target) * 100), 100)
})

const displayValue = computed(() => {
  if (props.unit) return `${props.value}${props.unit}`
  return String(props.value)
})
</script>

<template>
  <Card class="transition-all hover:shadow-md overflow-hidden">
    <CardContent class="p-4">
      <!-- Loading skeleton -->
      <div v-if="loading" class="animate-pulse">
        <div class="flex items-center justify-between">
          <div class="space-y-2 flex-1">
            <div class="h-3 w-20 bg-muted rounded" />
            <div class="h-7 w-24 bg-muted rounded" />
            <div class="h-3 w-16 bg-muted rounded" />
          </div>
          <div class="h-10 w-10 bg-muted rounded-full" />
        </div>
        <div class="mt-3 h-10 w-full bg-muted rounded" />
      </div>

      <!-- Loaded content -->
      <div v-else>
        <div class="flex items-center justify-between">
          <div class="min-w-0 flex-1">
            <p class="text-sm font-medium text-muted-foreground truncate">{{ title }}</p>
            <p class="text-2xl font-bold mt-1" :class="colorMap[color].value">
              {{ displayValue }}
            </p>
            <div class="flex items-center gap-1.5 mt-1" v-if="description || trend">
              <div
                v-if="trend"
                class="flex items-center gap-0.5"
                :class="trendColor[trend]"
              >
                <component :is="trendIcon[trend]" class="h-3 w-3" />
                <span v-if="trendValue" class="text-xs font-medium">{{ trendValue }}</span>
              </div>
              <p v-if="description" class="text-xs text-muted-foreground truncate">
                {{ description }}
              </p>
            </div>
          </div>
          <div
            v-if="icon"
            class="h-10 w-10 rounded-full flex items-center justify-center shrink-0"
            :class="colorMap[color].bg"
          >
            <component :is="icon" class="h-5 w-5" :class="colorMap[color].text" />
          </div>
        </div>

        <!-- Sparkline -->
        <div v-if="sparklineData && sparklineData.length >= 2" class="mt-3">
          <SparklineChart
            :data="sparklineData"
            :width="200"
            :height="36"
            :color="colorMap[color].sparkline"
            :show-area="true"
            :animate="true"
          />
        </div>

        <!-- Target progress bar -->
        <div v-if="targetProgress !== null" class="mt-3">
          <div class="flex items-center justify-between text-xs text-muted-foreground mb-1">
            <span>{{ targetProgress }}%</span>
            <span>{{ target?.toLocaleString() }}</span>
          </div>
          <div class="h-1.5 w-full bg-muted rounded-full overflow-hidden">
            <div
              class="h-full rounded-full transition-all duration-700 ease-out"
              :class="[
                targetProgress >= 100
                  ? 'bg-emerald-500'
                  : targetProgress >= 70
                    ? colorMap[color].text.replace('text-', 'bg-')
                    : 'bg-amber-500',
              ]"
              :style="{ width: `${targetProgress}%` }"
            />
          </div>
        </div>
      </div>
    </CardContent>
  </Card>
</template>
