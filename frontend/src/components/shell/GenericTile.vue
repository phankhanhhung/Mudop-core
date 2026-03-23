<script setup lang="ts">
import { useRouter } from 'vue-router'
import { cn } from '@/lib/utils'
import { Spinner } from '@/components/ui/spinner'
import { RefreshCw, AlertTriangle } from 'lucide-vue-next'

type FrameType = 'OneByOne' | 'TwoByOne'
type TileState = 'Loaded' | 'Loading' | 'Failed'

const props = withDefaults(
  defineProps<{
    title: string
    subtitle?: string
    to?: string
    frameType?: FrameType
    state?: TileState
    headerImage?: string
  }>(),
  {
    frameType: 'OneByOne',
    state: 'Loaded',
  }
)

const emit = defineEmits<{
  click: [event: Event]
}>()

const router = useRouter()

function handleClick(event: Event): void {
  emit('click', event)
  if (props.to) {
    router.push(props.to)
  }
}
</script>

<template>
  <button
    :class="cn(
      'group relative flex flex-col items-start rounded-xl border bg-card p-5',
      'text-left transition-all duration-200',
      'hover:shadow-lg hover:-translate-y-0.5 hover:border-primary/30',
      'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2',
      'dark:hover:shadow-primary/5',
      frameType === 'TwoByOne' ? 'col-span-2' : '',
      state === 'Failed' ? 'border-destructive/30' : ''
    )"
    @click="handleClick"
  >
    <!-- Header image -->
    <div
      v-if="headerImage && state === 'Loaded'"
      class="absolute inset-x-0 top-0 h-16 rounded-t-xl bg-cover bg-center opacity-20"
      :style="{ backgroundImage: `url(${headerImage})` }"
    />

    <!-- Loading state -->
    <div
      v-if="state === 'Loading'"
      class="flex flex-1 w-full items-center justify-center py-4"
    >
      <div class="space-y-3 w-full">
        <div class="h-4 w-3/4 rounded bg-muted animate-pulse" />
        <div class="h-8 w-1/2 rounded bg-muted animate-pulse" />
        <div class="h-3 w-2/3 rounded bg-muted animate-pulse" />
      </div>
    </div>

    <!-- Failed state -->
    <div
      v-else-if="state === 'Failed'"
      class="flex flex-1 w-full flex-col items-center justify-center py-4 gap-2"
    >
      <AlertTriangle class="h-8 w-8 text-destructive/60" />
      <span class="text-sm text-destructive/80">Failed to load</span>
      <span class="text-xs text-muted-foreground flex items-center gap-1">
        <RefreshCw class="h-3 w-3" />
        Click to retry
      </span>
    </div>

    <!-- Loaded state -->
    <template v-else>
      <!-- Title area -->
      <div class="min-w-0 w-full shrink-0">
        <div class="text-sm font-semibold leading-tight text-foreground truncate">
          {{ title }}
        </div>
        <div v-if="subtitle" class="mt-0.5 text-xs text-muted-foreground truncate">
          {{ subtitle }}
        </div>
      </div>

      <!-- Content area (default slot) -->
      <div class="flex-1 w-full mt-3">
        <slot />
      </div>

      <!-- Footer slot -->
      <div v-if="$slots.footer" class="w-full mt-2 shrink-0">
        <slot name="footer" />
      </div>
    </template>
  </button>
</template>
