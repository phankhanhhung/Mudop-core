<script setup lang="ts">
import { computed } from 'vue'
import { cn } from '@/lib/utils'

interface Props {
  class?: string
}

const props = defineProps<Props>()

const classes = computed(() =>
  cn(
    'border-b transition-colors hover:bg-muted/50 data-[state=selected]:bg-muted',
    props.class
  )
)
</script>

<template>
  <tr :class="classes">
    <slot />
  </tr>
</template>
