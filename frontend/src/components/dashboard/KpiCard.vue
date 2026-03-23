<script setup lang="ts">
import { type Component } from 'vue'
import { Card, CardContent } from '@/components/ui/card'
import { TrendingUp, TrendingDown, Minus } from 'lucide-vue-next'

interface Props {
  title: string
  value: string | number
  description?: string
  icon?: Component
  trend?: 'up' | 'down' | 'neutral'
  trendValue?: string
  color?: 'primary' | 'emerald' | 'violet' | 'amber' | 'rose' | 'cyan'
}

withDefaults(defineProps<Props>(), {
  color: 'primary'
})

const trendIcon: Record<string, Component> = {
  up: TrendingUp,
  down: TrendingDown,
  neutral: Minus
}

const trendColor: Record<string, string> = {
  up: 'text-emerald-600 dark:text-emerald-400',
  down: 'text-rose-600 dark:text-rose-400',
  neutral: 'text-muted-foreground'
}

const colorMap: Record<string, { bg: string; text: string; value: string }> = {
  primary: { bg: 'bg-primary/10', text: 'text-primary', value: '' },
  emerald: { bg: 'bg-emerald-500/10', text: 'text-emerald-500', value: 'text-emerald-600 dark:text-emerald-400' },
  violet: { bg: 'bg-violet-500/10', text: 'text-violet-500', value: 'text-violet-600 dark:text-violet-400' },
  amber: { bg: 'bg-amber-500/10', text: 'text-amber-500', value: 'text-amber-600 dark:text-amber-400' },
  rose: { bg: 'bg-rose-500/10', text: 'text-rose-500', value: 'text-rose-600 dark:text-rose-400' },
  cyan: { bg: 'bg-cyan-500/10', text: 'text-cyan-500', value: 'text-cyan-600 dark:text-cyan-400' }
}
</script>

<template>
  <Card class="transition-all hover:shadow-md">
    <CardContent class="p-4">
      <div class="flex items-center justify-between">
        <div class="min-w-0">
          <p class="text-sm font-medium text-muted-foreground truncate">{{ title }}</p>
          <p class="text-2xl font-bold mt-1" :class="colorMap[color].value">{{ value }}</p>
          <div class="flex items-center gap-1.5 mt-1" v-if="description || trend">
            <div v-if="trend" class="flex items-center gap-0.5" :class="trendColor[trend]">
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
    </CardContent>
  </Card>
</template>
