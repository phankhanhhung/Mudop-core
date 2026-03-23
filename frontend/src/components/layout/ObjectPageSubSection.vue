<script setup lang="ts">
import { cn } from '@/lib/utils'

interface Props {
  /** SubSection title */
  title: string
  /** Layout mode for content */
  mode?: 'responsive' | 'columns-2' | 'columns-3'
}

const props = withDefaults(defineProps<Props>(), {
  mode: 'responsive',
})

const modeClass: Record<string, string> = {
  'responsive': 'grid grid-cols-1',
  'columns-2': 'grid grid-cols-1 md:grid-cols-2 gap-4',
  'columns-3': 'grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4',
}
</script>

<template>
  <div class="object-page-subsection py-3 first:pt-0 last:pb-0">
    <h4 class="text-sm font-medium text-foreground mb-3">
      {{ props.title }}
    </h4>
    <div :class="cn(modeClass[props.mode])">
      <slot />
    </div>
  </div>
</template>
