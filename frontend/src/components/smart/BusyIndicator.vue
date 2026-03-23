<script setup lang="ts">
import { computed } from 'vue'
import { cn } from '@/lib/utils'
import { Spinner } from '@/components/ui/spinner'

interface Props {
  busy?: boolean
  size?: 'sm' | 'md' | 'lg'
  text?: string
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  busy: false,
  size: 'md',
})

const spinnerSize = computed(() => {
  switch (props.size) {
    case 'sm': return 'sm'
    case 'lg': return 'lg'
    default: return 'default'
  }
})
</script>

<template>
  <div :class="cn('relative', props.class)">
    <slot />
    <Transition
      enter-active-class="transition-opacity duration-200"
      enter-from-class="opacity-0"
      enter-to-class="opacity-100"
      leave-active-class="transition-opacity duration-150"
      leave-from-class="opacity-100"
      leave-to-class="opacity-0"
    >
      <div
        v-if="busy"
        class="absolute inset-0 z-10 flex flex-col items-center justify-center bg-background/60 backdrop-blur-[1px] rounded-[inherit]"
      >
        <Spinner :size="spinnerSize" />
        <p v-if="text" class="text-xs text-muted-foreground mt-2">{{ text }}</p>
      </div>
    </Transition>
  </div>
</template>
