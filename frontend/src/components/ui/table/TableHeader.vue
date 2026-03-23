<script setup lang="ts">
import { computed } from 'vue'
import { cn } from '@/lib/utils'

interface Props {
  class?: string
}

const props = defineProps<Props>()

const classes = computed(() => cn('[&_tr]:border-b', props.class))
</script>

<template>
  <thead :class="classes">
    <slot />
  </thead>
</template>
