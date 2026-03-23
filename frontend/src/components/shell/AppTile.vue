<script setup lang="ts">
import { type Component } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { cn } from '@/lib/utils'

type TileStatus = 'active' | 'warning' | 'error'
type TileSize = 'normal' | 'wide'

const props = withDefaults(
  defineProps<{
    title: string
    subtitle?: string
    icon: Component
    to: string
    color?: string
    count?: number
    status?: TileStatus
    size?: TileSize
  }>(),
  {
    color: 'primary',
    size: 'normal'
  }
)

useI18n()
const router = useRouter()

function handleClick(): void {
  router.push(props.to)
}

function getStatusColor(s: TileStatus): string {
  switch (s) {
    case 'active':
      return 'bg-green-500'
    case 'warning':
      return 'bg-amber-500'
    case 'error':
      return 'bg-red-500'
  }
}

function getIconBg(): string {
  switch (props.color) {
    case 'primary':
      return 'bg-primary/10 text-primary'
    case 'emerald':
      return 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400'
    case 'violet':
      return 'bg-violet-500/10 text-violet-600 dark:text-violet-400'
    case 'cyan':
      return 'bg-cyan-500/10 text-cyan-600 dark:text-cyan-400'
    case 'amber':
      return 'bg-amber-500/10 text-amber-600 dark:text-amber-400'
    case 'rose':
      return 'bg-rose-500/10 text-rose-600 dark:text-rose-400'
    case 'blue':
      return 'bg-blue-500/10 text-blue-600 dark:text-blue-400'
    case 'indigo':
      return 'bg-indigo-500/10 text-indigo-600 dark:text-indigo-400'
    case 'orange':
      return 'bg-orange-500/10 text-orange-600 dark:text-orange-400'
    case 'teal':
      return 'bg-teal-500/10 text-teal-600 dark:text-teal-400'
    default:
      return 'bg-primary/10 text-primary'
  }
}
</script>

<template>
  <button
    :class="cn(
      'group relative flex flex-col items-start gap-3 rounded-xl border bg-card p-5',
      'text-left transition-all duration-200',
      'hover:shadow-lg hover:-translate-y-0.5 hover:border-primary/30',
      'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2',
      'dark:hover:shadow-primary/5',
      size === 'wide' ? 'col-span-2' : ''
    )"
    @click="handleClick"
  >
    <!-- Count badge -->
    <span
      v-if="count !== undefined"
      class="absolute top-3 right-3 flex h-6 min-w-6 items-center justify-center rounded-full bg-primary px-1.5 text-xs font-bold text-primary-foreground"
    >
      {{ count > 999 ? '999+' : count }}
    </span>

    <!-- Icon -->
    <div
      :class="cn(
        'flex h-11 w-11 items-center justify-center rounded-lg transition-transform duration-200',
        'group-hover:scale-110',
        getIconBg()
      )"
    >
      <component :is="icon" class="h-5 w-5" aria-hidden="true" />
    </div>

    <!-- Text -->
    <div class="min-w-0 flex-1">
      <div class="text-sm font-semibold leading-tight text-foreground">
        {{ title }}
      </div>
      <div v-if="subtitle" class="mt-1 text-xs text-muted-foreground line-clamp-2">
        {{ subtitle }}
      </div>
    </div>

    <!-- Footer slot -->
    <slot name="footer" />

    <!-- Status indicator -->
    <span
      v-if="status"
      :class="cn(
        'absolute bottom-3 right-3 h-2.5 w-2.5 rounded-full ring-2 ring-card',
        getStatusColor(status)
      )"
      :title="status"
    />
  </button>
</template>
