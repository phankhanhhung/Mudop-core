<script setup lang="ts">
import { computed } from 'vue'
import { cn } from '@/lib/utils'

interface Props {
  class?: string
  role?: string
  'aria-rowcount'?: number
  'aria-label'?: string
  'aria-labelledby'?: string
}

const props = defineProps<Props>()

const classes = computed(() => cn('w-full caption-bottom text-sm', props.class))
</script>

<template>
  <div class="relative w-full overflow-auto">
    <table
      :class="classes"
      :role="props.role"
      :aria-rowcount="props['aria-rowcount']"
      :aria-label="props['aria-label']"
      :aria-labelledby="props['aria-labelledby']"
    >
      <slot />
    </table>
  </div>
</template>
