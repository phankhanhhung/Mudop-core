<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'

interface Props {
  title: string
  subtitle?: string
  class?: HTMLAttributes['class']
}

const props = defineProps<Props>()
</script>

<template>
  <div :class="cn('min-w-0', props.class)">
    <h1 class="text-xl font-semibold text-foreground truncate">{{ title }}</h1>
    <p v-if="subtitle" class="text-sm text-muted-foreground mt-0.5 truncate">{{ subtitle }}</p>
  </div>
</template>
