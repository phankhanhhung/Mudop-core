<script setup lang="ts">
import { ref, computed } from 'vue'
import { cn } from '@/lib/utils'
import { Label } from '@/components/ui/label'
import { Star } from 'lucide-vue-next'

interface Props {
  modelValue?: number
  maxValue?: number
  readonly?: boolean
  disabled?: boolean
  size?: 'sm' | 'md' | 'lg'
  color?: string
  showValue?: boolean
  label?: string
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  modelValue: 0,
  maxValue: 5,
  readonly: false,
  disabled: false,
  size: 'md',
  color: 'text-amber-400',
  showValue: false,
})

const emit = defineEmits<{
  'update:modelValue': [value: number]
  'change': [value: number]
}>()

// ── Hover state ────────────────────────────────────────────────────────

const hoverValue = ref<number | null>(null)

// ── Computed helpers ───────────────────────────────────────────────────

const activeValue = computed(() => hoverValue.value ?? props.modelValue ?? 0)

const displayValue = computed(() => {
  const v = props.modelValue ?? 0
  return Number.isInteger(v) ? String(v) : v.toFixed(1)
})

const sizeClass = computed(() => {
  const sizes: Record<string, string> = {
    sm: 'h-4 w-4',
    md: 'h-5 w-5',
    lg: 'h-7 w-7',
  }
  return sizes[props.size]
})

// ── Star type resolution ─────────────────────────────────────────────

function getStarType(index: number): 'full' | 'half' | 'empty' {
  const value = activeValue.value

  if (index <= Math.floor(value)) return 'full'

  // Half star: only show in readonly/disabled mode for fractional values
  if (
    (props.readonly || props.disabled) &&
    hoverValue.value === null &&
    index === Math.ceil(value) &&
    value % 1 >= 0.25
  ) {
    return 'half'
  }

  return 'empty'
}

// ── Rating selection ─────────────────────────────────────────────────

function selectRating(value: number) {
  if (props.readonly || props.disabled) return

  // Click same value again to clear
  const newValue = props.modelValue === value ? 0 : value
  emit('update:modelValue', newValue)
  emit('change', newValue)
}

// ── Keyboard handling ────────────────────────────────────────────────

function onKeydown(event: KeyboardEvent, _currentIndex: number) {
  if (props.readonly || props.disabled) return

  let newValue: number | null = null

  switch (event.key) {
    case 'ArrowRight':
    case 'ArrowUp':
      event.preventDefault()
      newValue = Math.min((props.modelValue ?? 0) + 1, props.maxValue)
      break
    case 'ArrowLeft':
    case 'ArrowDown':
      event.preventDefault()
      newValue = Math.max((props.modelValue ?? 0) - 1, 0)
      break
    case 'Home':
      event.preventDefault()
      newValue = 1
      break
    case 'End':
      event.preventDefault()
      newValue = props.maxValue
      break
  }

  if (newValue !== null) {
    emit('update:modelValue', newValue)
    emit('change', newValue)
  }
}
</script>

<template>
  <div :class="cn('space-y-1.5', props.class)">
    <Label v-if="label" class="mb-1.5">{{ label }}</Label>

    <div class="flex items-center gap-1">
      <div
        class="flex items-center"
        @mouseleave="hoverValue = null"
        role="radiogroup"
        :aria-label="label || 'Rating'"
      >
        <button
          v-for="i in maxValue"
          :key="i"
          type="button"
          role="radio"
          :aria-checked="i === Math.ceil(modelValue || 0)"
          :aria-label="`${i} star${i > 1 ? 's' : ''}`"
          class="relative cursor-pointer p-0.5 transition-transform hover:scale-110 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded"
          :class="{
            'cursor-default hover:scale-100': readonly || disabled,
            'opacity-50': disabled,
          }"
          :disabled="disabled"
          @mouseenter="!readonly && !disabled && (hoverValue = i)"
          @click="selectRating(i)"
          @keydown="onKeydown($event, i)"
        >
          <!-- Full star -->
          <Star
            v-if="getStarType(i) === 'full'"
            class="fill-current"
            :class="[color, sizeClass]"
          />

          <!-- Half star (clip path) -->
          <div v-else-if="getStarType(i) === 'half'" class="relative">
            <Star class="text-muted-foreground/30" :class="sizeClass" />
            <div class="absolute inset-0 overflow-hidden" style="width: 50%">
              <Star class="fill-current" :class="[color, sizeClass]" />
            </div>
          </div>

          <!-- Empty star -->
          <Star v-else class="text-muted-foreground/30" :class="sizeClass" />
        </button>
      </div>

      <!-- Numeric value -->
      <span v-if="showValue" class="text-sm text-muted-foreground ml-2">
        {{ displayValue }} / {{ maxValue }}
      </span>
    </div>
  </div>
</template>
