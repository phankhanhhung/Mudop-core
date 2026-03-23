<script setup lang="ts">
import { ref, computed, onBeforeUnmount } from 'vue'
import { cn } from '@/lib/utils'
import { Label } from '@/components/ui/label'

interface Props {
  modelValue?: number | [number, number]
  min?: number
  max?: number
  step?: number
  disabled?: boolean
  showValue?: boolean
  showMinMax?: boolean
  showTooltip?: boolean
  showTicks?: boolean
  tickInterval?: number
  label?: string
  formatValue?: (value: number) => string
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  min: 0,
  max: 100,
  step: 1,
  disabled: false,
  showValue: true,
  showMinMax: false,
  showTooltip: true,
  showTicks: false,
})

const emit = defineEmits<{
  'update:modelValue': [value: number | [number, number]]
  'change': [value: number | [number, number]]
}>()

// ── Range detection ──────────────────────────────────────────────────────

const isRange = computed(() => Array.isArray(props.modelValue))

// ── Snap to step ─────────────────────────────────────────────────────────

function snapToStep(value: number): number {
  const range = props.max - props.min
  if (range <= 0) return props.min
  const steps = Math.round((value - props.min) / props.step)
  const snapped = props.min + steps * props.step
  // Clamp to bounds
  return Math.min(props.max, Math.max(props.min, snapped))
}

// Round to avoid floating-point artifacts
function roundPrecision(value: number): number {
  const stepStr = String(props.step)
  const dotIndex = stepStr.indexOf('.')
  const precision = dotIndex === -1 ? 0 : stepStr.length - dotIndex - 1
  const factor = Math.pow(10, precision)
  return Math.round(value * factor) / factor
}

function snap(value: number): number {
  return roundPrecision(snapToStep(value))
}

// ── Value helpers ────────────────────────────────────────────────────────

function getLowValue(): number {
  if (Array.isArray(props.modelValue)) return props.modelValue[0] ?? props.min
  return props.modelValue ?? props.min
}

function getHighValue(): number {
  if (Array.isArray(props.modelValue)) return props.modelValue[1] ?? props.max
  return props.modelValue ?? props.min
}

// ── Thumbs ───────────────────────────────────────────────────────────────

const thumbs = computed(() => {
  const range = props.max - props.min
  if (range <= 0) {
    if (isRange.value) {
      return [
        { value: props.min, percent: 0 },
        { value: props.min, percent: 0 },
      ]
    }
    return [{ value: props.min, percent: 0 }]
  }

  if (isRange.value) {
    const low = getLowValue()
    const high = getHighValue()
    return [
      { value: low, percent: ((low - props.min) / range) * 100 },
      { value: high, percent: ((high - props.min) / range) * 100 },
    ]
  }

  const val = getLowValue()
  return [{ value: val, percent: ((val - props.min) / range) * 100 }]
})

// ── Fill position ────────────────────────────────────────────────────────

const fillLeft = computed(() => {
  if (isRange.value) return thumbs.value[0].percent
  return 0
})

const fillWidth = computed(() => {
  if (isRange.value) return thumbs.value[1].percent - thumbs.value[0].percent
  return thumbs.value[0].percent
})

// ── Tick marks ───────────────────────────────────────────────────────────

const ticks = computed(() => {
  if (!props.showTicks) return []
  const interval = props.tickInterval ?? props.step
  const result: number[] = []
  for (let v = props.min; v <= props.max; v = roundPrecision(v + interval)) {
    result.push(v)
  }
  return result
})

// ── Format ───────────────────────────────────────────────────────────────

function format(value: number): string {
  if (props.formatValue) return props.formatValue(value)
  // Show integer for whole numbers, otherwise show decimal
  return Number.isInteger(value) ? String(value) : String(roundPrecision(value))
}

// ── Track ref and position calculation ───────────────────────────────────

const trackRef = ref<HTMLElement | null>(null)

function getValueFromPosition(clientX: number): number {
  if (!trackRef.value) return props.min
  const rect = trackRef.value.getBoundingClientRect()
  const fraction = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width))
  return snap(props.min + fraction * (props.max - props.min))
}

// ── Emit helpers ─────────────────────────────────────────────────────────

function emitValue(value: number | [number, number]) {
  emit('update:modelValue', value)
  emit('change', value)
}

function setThumbValue(thumbIndex: number, newValue: number) {
  if (isRange.value) {
    const low = getLowValue()
    const high = getHighValue()
    if (thumbIndex === 0) {
      // Low thumb can't exceed high
      const clamped = Math.min(newValue, high)
      emitValue([clamped, high])
    } else {
      // High thumb can't go below low
      const clamped = Math.max(newValue, low)
      emitValue([low, clamped])
    }
  } else {
    emitValue(newValue)
  }
}

// ── Drag state ───────────────────────────────────────────────────────────

const draggingThumb = ref<number | null>(null)

function startDrag(thumbIndex: number, _event: MouseEvent | TouchEvent) {
  if (props.disabled) return
  draggingThumb.value = thumbIndex

  const onMove = (e: MouseEvent | TouchEvent) => {
    const clientX = 'touches' in e ? e.touches[0].clientX : e.clientX
    const newVal = getValueFromPosition(clientX)
    setThumbValue(thumbIndex, newVal)
  }

  const onEnd = () => {
    draggingThumb.value = null
    document.removeEventListener('mousemove', onMove)
    document.removeEventListener('mouseup', onEnd)
    document.removeEventListener('touchmove', onMove)
    document.removeEventListener('touchend', onEnd)
  }

  document.addEventListener('mousemove', onMove)
  document.addEventListener('mouseup', onEnd)
  document.addEventListener('touchmove', onMove)
  document.addEventListener('touchend', onEnd)
}

// ── Track click ──────────────────────────────────────────────────────────

function onTrackClick(event: MouseEvent) {
  if (props.disabled) return
  // Don't handle if the event originated from a thumb (thumb handlers take priority)
  if ((event.target as HTMLElement).getAttribute('role') === 'slider') return

  const newVal = getValueFromPosition(event.clientX)

  if (isRange.value) {
    const low = getLowValue()
    const high = getHighValue()
    // Move the nearest thumb
    const distToLow = Math.abs(newVal - low)
    const distToHigh = Math.abs(newVal - high)
    if (distToLow <= distToHigh) {
      setThumbValue(0, newVal)
    } else {
      setThumbValue(1, newVal)
    }
  } else {
    emitValue(newVal)
  }
}

function onTrackTouch(event: TouchEvent) {
  if (props.disabled) return
  if ((event.target as HTMLElement).getAttribute('role') === 'slider') return

  const touch = event.touches[0]
  if (!touch) return
  const newVal = getValueFromPosition(touch.clientX)

  if (isRange.value) {
    const low = getLowValue()
    const high = getHighValue()
    const distToLow = Math.abs(newVal - low)
    const distToHigh = Math.abs(newVal - high)
    if (distToLow <= distToHigh) {
      setThumbValue(0, newVal)
    } else {
      setThumbValue(1, newVal)
    }
  } else {
    emitValue(newVal)
  }
}

// ── Keyboard ─────────────────────────────────────────────────────────────

function onThumbKeydown(thumbIndex: number, event: KeyboardEvent) {
  if (props.disabled) return

  const currentVal = thumbs.value[thumbIndex].value
  const largeStep = props.step * 10
  let newVal: number | null = null

  switch (event.key) {
    case 'ArrowRight':
    case 'ArrowUp':
      event.preventDefault()
      newVal = snap(currentVal + (event.shiftKey ? largeStep : props.step))
      break
    case 'ArrowLeft':
    case 'ArrowDown':
      event.preventDefault()
      newVal = snap(currentVal - (event.shiftKey ? largeStep : props.step))
      break
    case 'Home':
      event.preventDefault()
      newVal = props.min
      break
    case 'End':
      event.preventDefault()
      newVal = props.max
      break
    default:
      return
  }

  if (newVal !== null) {
    setThumbValue(thumbIndex, newVal)
  }
}

// ── Cleanup ──────────────────────────────────────────────────────────────

onBeforeUnmount(() => {
  draggingThumb.value = null
})
</script>

<template>
  <div :class="cn('range-slider-root', props.class)">
    <Label v-if="label" class="mb-2">{{ label }}</Label>

    <div class="flex items-center gap-3">
      <!-- Min label -->
      <span v-if="showMinMax" class="text-xs text-muted-foreground w-8 text-right shrink-0">
        {{ format(min) }}
      </span>

      <!-- Slider track -->
      <div
        ref="trackRef"
        class="relative flex-1 h-2 select-none"
        :class="{ 'opacity-50 pointer-events-none': disabled }"
        @mousedown="onTrackClick"
        @touchstart.passive="onTrackTouch"
      >
        <!-- Background track -->
        <div class="absolute inset-0 rounded-full bg-muted" />

        <!-- Active range fill -->
        <div
          class="absolute h-full rounded-full bg-primary transition-[left,width] duration-75"
          :style="{ left: fillLeft + '%', width: fillWidth + '%' }"
        />

        <!-- Tick marks -->
        <template v-if="showTicks">
          <div
            v-for="tick in ticks"
            :key="tick"
            class="absolute top-3 w-px h-1.5 bg-muted-foreground/30"
            :style="{ left: ((tick - min) / (max - min)) * 100 + '%' }"
          />
        </template>

        <!-- Thumb(s) -->
        <div
          v-for="(thumb, idx) in thumbs"
          :key="idx"
          class="absolute top-1/2 -translate-y-1/2 -translate-x-1/2 w-5 h-5 rounded-full bg-primary border-2 border-background shadow cursor-grab active:cursor-grabbing hover:ring-4 hover:ring-primary/20 transition-shadow focus-visible:ring-4 focus-visible:ring-ring"
          :class="{ 'opacity-50 cursor-not-allowed': disabled }"
          :style="{ left: thumb.percent + '%' }"
          tabindex="0"
          role="slider"
          :aria-valuemin="min"
          :aria-valuemax="max"
          :aria-valuenow="thumb.value"
          :aria-label="isRange ? (idx === 0 ? 'Minimum' : 'Maximum') : label || 'Value'"
          @mousedown.prevent="startDrag(idx, $event)"
          @touchstart.passive="startDrag(idx, $event)"
          @keydown="onThumbKeydown(idx, $event)"
        >
          <!-- Tooltip -->
          <div
            v-if="showTooltip && draggingThumb === idx"
            class="absolute -top-8 left-1/2 -translate-x-1/2 px-2 py-0.5 rounded bg-popover text-popover-foreground text-xs shadow-md whitespace-nowrap border"
          >
            {{ format(thumb.value) }}
          </div>
        </div>
      </div>

      <!-- Max label -->
      <span v-if="showMinMax" class="text-xs text-muted-foreground w-8 shrink-0">
        {{ format(max) }}
      </span>
    </div>

    <!-- Value display -->
    <div v-if="showValue" class="mt-1 text-sm text-muted-foreground">
      <template v-if="isRange">
        {{ format(thumbs[0].value) }} &ndash; {{ format(thumbs[1].value) }}
      </template>
      <template v-else>
        {{ format(thumbs[0].value) }}
      </template>
    </div>
  </div>
</template>
