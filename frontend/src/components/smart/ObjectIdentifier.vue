<script setup lang="ts">
import { computed } from 'vue'
import { cn } from '@/lib/utils'

interface Props {
  title: string
  text?: string
  titleActive?: boolean
  emphasized?: boolean
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  titleActive: false,
  emphasized: false,
})

const emit = defineEmits<{
  'title-click': [event: Event]
}>()

function handleTitleClick(event: Event) {
  if (props.titleActive) {
    emit('title-click', event)
  }
}

const titleClasses = computed(() =>
  cn(
    'text-sm truncate',
    props.emphasized ? 'font-semibold' : 'font-medium',
    props.titleActive
      ? 'text-primary hover:underline cursor-pointer'
      : 'text-foreground'
  )
)
</script>

<template>
  <div :class="cn('min-w-0', props.class)">
    <span
      :class="titleClasses"
      :role="titleActive ? 'link' : undefined"
      :tabindex="titleActive ? 0 : undefined"
      @click="handleTitleClick"
      @keydown.enter="handleTitleClick"
    >
      {{ title }}
    </span>
    <p v-if="text" class="text-xs text-muted-foreground truncate mt-0.5">
      {{ text }}
    </p>
  </div>
</template>
