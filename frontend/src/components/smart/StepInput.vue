<script setup lang="ts">
import { ref, computed, onBeforeUnmount } from 'vue'
import { cn } from '@/lib/utils'
import { Label } from '@/components/ui/label'
import { Minus, Plus } from 'lucide-vue-next'

interface Props {
  modelValue?: number
  min?: number
  max?: number
  step?: number
  precision?: number
  disabled?: boolean
  readonly?: boolean
  size?: 'sm' | 'md' | 'lg'
  label?: string
  description?: string
  required?: boolean
  error?: string
  largeStep?: number
  editable?: boolean
  displayFormat?: (value: number) => string
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  modelValue: 0,
  step: 1,
  disabled: false,
  readonly: false,
  size: 'md',
  editable: true,
})

const emit = defineEmits<{
  'update:modelValue': [value: number]
  'change': [value: number]
}>()

// ── Derived config ────────────────────────────────────────────────────────

const effectivePrecision = computed(() => {
  if (props.precision !== undefined) return props.precision
  const stepStr = String(props.step)
  const dotIndex = stepStr.indexOf('.')
  return dotIndex === -1 ? 0 : stepStr.length - dotIndex - 1
})

const effectiveLargeStep = computed(() => props.largeStep ?? props.step * 10)

// ── Clamping / rounding helpers ───────────────────────────────────────────

function clamp(value: number): number {
  let v = value
  if (props.min !== undefined && v < props.min) v = props.min
  if (props.max !== undefined && v > props.max) v = props.max
  return v
}

function roundToPrecision(value: number): number {
  const factor = Math.pow(10, effectivePrecision.value)
  return Math.round(value * factor) / factor
}

function normalize(value: number): number {
  return roundToPrecision(clamp(value))
}

// ── Display ───────────────────────────────────────────────────────────────

const isFocused = ref(false)
const inputRef = ref<HTMLInputElement | null>(null)

const displayValue = computed(() => {
  const v = props.modelValue ?? 0
  if (props.displayFormat && !isFocused.value) {
    return props.displayFormat(v)
  }
  return v.toFixed(effectivePrecision.value)
})

// ── Boundary checks ──────────────────────────────────────────────────────

const atMin = computed(() => props.min !== undefined && (props.modelValue ?? 0) <= props.min)
const atMax = computed(() => props.max !== undefined && (props.modelValue ?? 0) >= props.max)

// ── Value mutation ────────────────────────────────────────────────────────

function setValue(value: number) {
  const normalized = normalize(value)
  emit('update:modelValue', normalized)
  emit('change', normalized)
}

function increment(amount?: number) {
  const stepSize = amount ?? props.step
  setValue((props.modelValue ?? 0) + stepSize)
}

function decrement(amount?: number) {
  const stepSize = amount ?? props.step
  setValue((props.modelValue ?? 0) - stepSize)
}

// ── Continuous stepping (press-and-hold) ──────────────────────────────────

let continuousTimer: ReturnType<typeof setTimeout> | null = null
let continuousInterval: ReturnType<typeof setInterval> | null = null
let currentDelay = 400

function startContinuousStep(direction: 'increment' | 'decrement') {
  stopContinuousStep()
  currentDelay = 400

  const step = () => {
    if (direction === 'increment') increment()
    else decrement()
  }

  const scheduleNext = () => {
    continuousTimer = setTimeout(() => {
      step()
      // Accelerate: decrease delay but never below 50ms
      currentDelay = Math.max(50, currentDelay - 30)
      scheduleNext()
    }, currentDelay)
  }

  // Initial delay before repeat starts
  continuousTimer = setTimeout(() => {
    step()
    currentDelay = Math.max(50, currentDelay - 30)
    scheduleNext()
  }, currentDelay)
}

function stopContinuousStep() {
  if (continuousTimer !== null) {
    clearTimeout(continuousTimer)
    continuousTimer = null
  }
  if (continuousInterval !== null) {
    clearInterval(continuousInterval)
    continuousInterval = null
  }
}

onBeforeUnmount(() => {
  stopContinuousStep()
})

// ── Direct input handling ─────────────────────────────────────────────────

function onDirectInput(event: Event) {
  const target = event.target as HTMLInputElement
  const raw = target.value.trim()
  const parsed = parseFloat(raw)

  if (isNaN(parsed)) {
    // Revert to current value
    target.value = displayValue.value
    return
  }

  setValue(parsed)
}

function onFocus(event: FocusEvent) {
  isFocused.value = true
  const target = event.target as HTMLInputElement
  // Select all text on focus for easy overwrite
  requestAnimationFrame(() => {
    target.select()
  })
}

function onBlur(event: FocusEvent) {
  isFocused.value = false
  onDirectInput(event)
}

// ── Keyboard handling ─────────────────────────────────────────────────────

function onKeydown(event: KeyboardEvent) {
  switch (event.key) {
    case 'ArrowUp':
      event.preventDefault()
      if (event.shiftKey) {
        increment(effectiveLargeStep.value)
      } else {
        increment()
      }
      break
    case 'ArrowDown':
      event.preventDefault()
      if (event.shiftKey) {
        decrement(effectiveLargeStep.value)
      } else {
        decrement()
      }
      break
    case 'Home':
      event.preventDefault()
      if (props.min !== undefined) setValue(props.min)
      break
    case 'End':
      event.preventDefault()
      if (props.max !== undefined) setValue(props.max)
      break
    case 'Escape':
      event.preventDefault()
      inputRef.value?.blur()
      break
    case 'Enter':
      event.preventDefault()
      onDirectInput(event)
      break
  }
}

// ── Size classes ──────────────────────────────────────────────────────────

const btnClasses = computed(() => {
  const base = 'flex items-center justify-center border bg-muted/50 hover:bg-muted transition-colors shrink-0 disabled:opacity-50 disabled:cursor-not-allowed disabled:hover:bg-muted/50 select-none'
  const sizes: Record<string, string> = {
    sm: 'h-7 w-7',
    md: 'h-9 w-9',
    lg: 'h-11 w-11',
  }
  return cn(base, sizes[props.size])
})

const inputClasses = computed(() => {
  const base = 'text-center border-y bg-background outline-none focus:ring-2 focus:ring-ring focus:ring-inset disabled:opacity-50 disabled:cursor-not-allowed'
  const sizes: Record<string, string> = {
    sm: 'h-7 w-16 text-xs',
    md: 'h-9 w-20 text-sm',
    lg: 'h-11 w-24 text-base',
  }
  const errorCls = props.error ? 'border-destructive' : ''
  return cn(base, sizes[props.size], errorCls)
})

const leftBtnClasses = computed(() =>
  cn(btnClasses.value, 'rounded-l-md border-r-0', props.error ? 'border-destructive' : '')
)

const rightBtnClasses = computed(() =>
  cn(btnClasses.value, 'rounded-r-md border-l-0', props.error ? 'border-destructive' : '')
)
</script>

<template>
  <div :class="cn('space-y-1.5', props.class)">
    <Label v-if="label">
      {{ label }}
      <span v-if="required" class="text-destructive">*</span>
    </Label>

    <div class="flex items-center">
      <!-- Decrement button -->
      <button
        type="button"
        :class="leftBtnClasses"
        :disabled="disabled || readonly || atMin"
        aria-label="Decrease value"
        @click="decrement()"
        @mousedown.prevent="startContinuousStep('decrement')"
        @mouseup="stopContinuousStep"
        @mouseleave="stopContinuousStep"
      >
        <Minus :class="size === 'sm' ? 'h-3 w-3' : size === 'lg' ? 'h-5 w-5' : 'h-4 w-4'" />
      </button>

      <!-- Input field -->
      <input
        ref="inputRef"
        type="text"
        inputmode="decimal"
        :class="inputClasses"
        :value="displayValue"
        :disabled="disabled"
        :readonly="readonly || !editable"
        @change="onDirectInput"
        @keydown="onKeydown"
        @focus="onFocus"
        @blur="onBlur"
      />

      <!-- Increment button -->
      <button
        type="button"
        :class="rightBtnClasses"
        :disabled="disabled || readonly || atMax"
        aria-label="Increase value"
        @click="increment()"
        @mousedown.prevent="startContinuousStep('increment')"
        @mouseup="stopContinuousStep"
        @mouseleave="stopContinuousStep"
      >
        <Plus :class="size === 'sm' ? 'h-3 w-3' : size === 'lg' ? 'h-5 w-5' : 'h-4 w-4'" />
      </button>
    </div>

    <p v-if="description && !error" class="text-xs text-muted-foreground">
      {{ description }}
    </p>
    <p v-if="error" class="text-xs text-destructive">
      {{ error }}
    </p>
  </div>
</template>
