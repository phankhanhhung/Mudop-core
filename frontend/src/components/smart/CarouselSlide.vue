<script setup lang="ts">
import { cn } from '@/lib/utils'

interface Props {
  class?: string
}

const props = defineProps<Props>()
</script>

<template>
  <div :class="cn('w-full flex-shrink-0 h-full', props.class)">
    <slot />
  </div>
</template>
