<script setup lang="ts">
import type { Component } from 'vue'
import { cn } from '@/lib/utils'

interface Props {
  /** Attribute label */
  label: string
  /** Attribute value */
  value?: string
  /** Make value a link */
  href?: string
  /** Optional icon before value */
  icon?: Component
  /** Is this attribute active/clickable? */
  active?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  value: undefined,
  href: undefined,
  icon: undefined,
  active: false,
})

const emit = defineEmits<{
  click: []
}>()

function handleClick(): void {
  if (props.active) {
    emit('click')
  }
}
</script>

<template>
  <div class="flex items-center gap-1.5" role="group" :aria-label="`${label}: ${value ?? ''}`">
    <span class="text-muted-foreground">{{ label }}:</span>
    <component
      v-if="icon"
      :is="icon"
      class="h-3.5 w-3.5 text-muted-foreground shrink-0"
      aria-hidden="true"
    />
    <a
      v-if="href"
      :href="href"
      :class="cn('text-primary hover:underline', active && 'cursor-pointer')"
      target="_blank"
      rel="noopener noreferrer"
    >
      {{ value }}
    </a>
    <span
      v-else
      :class="cn(
        'text-foreground',
        active && 'text-primary hover:underline cursor-pointer'
      )"
      :role="active ? 'button' : undefined"
      :tabindex="active ? 0 : undefined"
      @click="handleClick"
      @keydown.enter="handleClick"
      @keydown.space.prevent="handleClick"
    >
      {{ value }}
    </span>
  </div>
</template>
