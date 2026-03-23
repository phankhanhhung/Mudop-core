<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'

interface Props {
  class?: HTMLAttributes['class']
}

const props = defineProps<Props>()
</script>

<template>
  <div :class="cn('grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-4', props.class)">
    <slot />
  </div>
</template>
