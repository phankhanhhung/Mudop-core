<script setup lang="ts">
import { computed, type Component } from 'vue'
import { cn } from '@/lib/utils'

interface Props {
  text: string
  colorScheme?: 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10
  icon?: Component
  displayOnly?: boolean
  renderMode?: 'filled' | 'outlined'
  size?: 'sm' | 'md' | 'lg'
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  colorScheme: 1,
  displayOnly: false,
  renderMode: 'filled',
  size: 'md',
})

const emit = defineEmits<{
  click: [event: Event]
}>()

const filledSchemes: Record<number, string> = {
  1: 'bg-blue-600 text-white dark:bg-blue-500',
  2: 'bg-teal-600 text-white dark:bg-teal-500',
  3: 'bg-green-600 text-white dark:bg-green-500',
  4: 'bg-amber-500 text-white dark:bg-amber-400 dark:text-amber-950',
  5: 'bg-red-600 text-white dark:bg-red-500',
  6: 'bg-indigo-600 text-white dark:bg-indigo-500',
  7: 'bg-pink-600 text-white dark:bg-pink-500',
  8: 'bg-cyan-600 text-white dark:bg-cyan-500',
  9: 'bg-orange-600 text-white dark:bg-orange-500',
  10: 'bg-violet-600 text-white dark:bg-violet-500',
}

const outlinedSchemes: Record<number, string> = {
  1: 'border-blue-500 text-blue-700 dark:border-blue-400 dark:text-blue-300',
  2: 'border-teal-500 text-teal-700 dark:border-teal-400 dark:text-teal-300',
  3: 'border-green-500 text-green-700 dark:border-green-400 dark:text-green-300',
  4: 'border-amber-500 text-amber-700 dark:border-amber-400 dark:text-amber-300',
  5: 'border-red-500 text-red-700 dark:border-red-400 dark:text-red-300',
  6: 'border-indigo-500 text-indigo-700 dark:border-indigo-400 dark:text-indigo-300',
  7: 'border-pink-500 text-pink-700 dark:border-pink-400 dark:text-pink-300',
  8: 'border-cyan-500 text-cyan-700 dark:border-cyan-400 dark:text-cyan-300',
  9: 'border-orange-500 text-orange-700 dark:border-orange-400 dark:text-orange-300',
  10: 'border-violet-500 text-violet-700 dark:border-violet-400 dark:text-violet-300',
}

const sizeClasses: Record<string, string> = {
  sm: 'text-xs px-1.5 py-0.5',
  md: 'text-sm px-2 py-0.5',
  lg: 'text-sm px-2.5 py-1',
}

const iconSizeClasses: Record<string, string> = {
  sm: 'h-3 w-3',
  md: 'h-3.5 w-3.5',
  lg: 'h-4 w-4',
}

const colorClasses = computed(() => {
  if (props.renderMode === 'outlined') {
    return outlinedSchemes[props.colorScheme] ?? outlinedSchemes[1]
  }
  return filledSchemes[props.colorScheme] ?? filledSchemes[1]
})

const classes = computed(() =>
  cn(
    'inline-flex items-center gap-1 rounded-full font-medium leading-none whitespace-nowrap',
    'border',
    sizeClasses[props.size],
    props.renderMode === 'filled' ? 'border-transparent' : 'bg-transparent',
    colorClasses.value,
    !props.displayOnly && [
      'cursor-pointer',
      'transition-opacity',
      'hover:opacity-80',
      'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2',
    ],
    props.displayOnly && 'cursor-default',
    props.class,
  ),
)

function handleClick(event: Event) {
  if (!props.displayOnly) {
    emit('click', event)
  }
}
</script>

<template>
  <span
    :class="classes"
    :role="displayOnly ? 'status' : 'button'"
    :tabindex="displayOnly ? undefined : 0"
    @click="handleClick"
    @keydown.enter="handleClick"
    @keydown.space.prevent="handleClick"
  >
    <component
      :is="icon"
      v-if="icon"
      :class="iconSizeClasses[size]"
      aria-hidden="true"
    />
    {{ text }}
  </span>
</template>
