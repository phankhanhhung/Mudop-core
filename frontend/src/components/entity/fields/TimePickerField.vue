<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import { PopoverRoot, PopoverTrigger, PopoverContent, PopoverPortal } from 'radix-vue'
import { Label } from '@/components/ui/label'
import { Clock } from 'lucide-vue-next'
import type { FieldMetadata } from '@/types/metadata'

interface Props {
  field: FieldMetadata
  modelValue?: string | null
  readonly?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  readonly: false
})

const emit = defineEmits<{
  'update:modelValue': [value: string | null]
}>()

const isOpen = ref(false)

// Detect 12h vs 24h from user locale
const is12h = Intl.DateTimeFormat('default', { hour: 'numeric' }).resolvedOptions().hour12 ?? false

// Internal state
const hours = ref(0)
const minutes = ref(0)
const seconds = ref(0)
const period = ref<'AM' | 'PM'>('AM')

// Column refs for keyboard navigation
const hourCol = ref<HTMLDivElement | null>(null)
const minuteCol = ref<HTMLDivElement | null>(null)
const secondCol = ref<HTMLDivElement | null>(null)

// Generate hour options
const hourOptions = computed(() => {
  if (is12h) {
    return Array.from({ length: 12 }, (_, i) => i === 0 ? 12 : i)
  }
  return Array.from({ length: 24 }, (_, i) => i)
})

// Generate minute options (5-min intervals)
const minuteOptions = Array.from({ length: 12 }, (_, i) => i * 5)

// Generate second options (5-sec intervals)
const secondOptions = Array.from({ length: 12 }, (_, i) => i * 5)

// Parse modelValue into internal state
function parseModelValue(val: string | null | undefined): void {
  if (!val) {
    hours.value = 0
    minutes.value = 0
    seconds.value = 0
    period.value = 'AM'
    return
  }

  const parts = val.split(':')
  const h = parseInt(parts[0], 10) || 0
  const m = parseInt(parts[1], 10) || 0
  const s = parseInt(parts[2], 10) || 0

  if (is12h) {
    if (h === 0) {
      hours.value = 12
      period.value = 'AM'
    } else if (h === 12) {
      hours.value = 12
      period.value = 'PM'
    } else if (h > 12) {
      hours.value = h - 12
      period.value = 'PM'
    } else {
      hours.value = h
      period.value = 'AM'
    }
  } else {
    hours.value = h
  }

  // Snap to nearest 5-minute interval
  minutes.value = Math.round(m / 5) * 5
  if (minutes.value === 60) minutes.value = 55
  seconds.value = Math.round(s / 5) * 5
  if (seconds.value === 60) seconds.value = 55
}

// Convert internal state to ISO time string
function toIsoTime(): string {
  let h = hours.value
  if (is12h) {
    if (period.value === 'AM' && h === 12) h = 0
    else if (period.value === 'PM' && h !== 12) h += 12
  }
  const hh = String(h).padStart(2, '0')
  const mm = String(minutes.value).padStart(2, '0')
  const ss = String(seconds.value).padStart(2, '0')
  return `${hh}:${mm}:${ss}`
}

// Format for display
const displayValue = computed(() => {
  if (!props.modelValue) return ''

  const parts = props.modelValue.split(':')
  const h = parseInt(parts[0], 10) || 0
  const m = parseInt(parts[1], 10) || 0

  if (is12h) {
    const displayHour = h === 0 ? 12 : h > 12 ? h - 12 : h
    const ampm = h >= 12 ? 'PM' : 'AM'
    return `${displayHour}:${String(m).padStart(2, '0')} ${ampm}`
  }
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}`
})

function emitValue(): void {
  emit('update:modelValue', toIsoTime())
}

function selectHour(h: number): void {
  hours.value = h
  emitValue()
}

function selectMinute(m: number): void {
  minutes.value = m
  emitValue()
}

function selectSecond(s: number): void {
  seconds.value = s
  emitValue()
}

function clearValue(): void {
  emit('update:modelValue', null)
  isOpen.value = false
}

function confirmAndClose(): void {
  emitValue()
  isOpen.value = false
}

// Scroll the active item into view when popover opens
watch(isOpen, (open) => {
  if (open) {
    parseModelValue(props.modelValue)
    nextTick(() => {
      scrollActiveIntoView(hourCol.value)
      scrollActiveIntoView(minuteCol.value)
      scrollActiveIntoView(secondCol.value)
    })
  }
})

function scrollActiveIntoView(container: HTMLDivElement | null): void {
  if (!container) return
  const active = container.querySelector('[data-active="true"]')
  if (active) {
    active.scrollIntoView({ block: 'center', behavior: 'instant' })
  }
}

// Keyboard handlers for columns
function handleHourKey(event: KeyboardEvent): void {
  if (event.key === 'ArrowUp') {
    event.preventDefault()
    const idx = hourOptions.value.indexOf(hours.value)
    const prev = idx > 0 ? idx - 1 : hourOptions.value.length - 1
    selectHour(hourOptions.value[prev])
    nextTick(() => scrollActiveIntoView(hourCol.value))
  } else if (event.key === 'ArrowDown') {
    event.preventDefault()
    const idx = hourOptions.value.indexOf(hours.value)
    const next = idx < hourOptions.value.length - 1 ? idx + 1 : 0
    selectHour(hourOptions.value[next])
    nextTick(() => scrollActiveIntoView(hourCol.value))
  } else if (event.key === 'Enter') {
    confirmAndClose()
  } else if (event.key === 'Escape') {
    isOpen.value = false
  }
}

function handleMinuteKey(event: KeyboardEvent): void {
  if (event.key === 'ArrowUp') {
    event.preventDefault()
    const idx = minuteOptions.indexOf(minutes.value)
    const prev = idx > 0 ? idx - 1 : minuteOptions.length - 1
    selectMinute(minuteOptions[prev])
    nextTick(() => scrollActiveIntoView(minuteCol.value))
  } else if (event.key === 'ArrowDown') {
    event.preventDefault()
    const idx = minuteOptions.indexOf(minutes.value)
    const next = idx < minuteOptions.length - 1 ? idx + 1 : 0
    selectMinute(minuteOptions[next])
    nextTick(() => scrollActiveIntoView(minuteCol.value))
  } else if (event.key === 'Enter') {
    confirmAndClose()
  } else if (event.key === 'Escape') {
    isOpen.value = false
  }
}

function handleSecondKey(event: KeyboardEvent): void {
  if (event.key === 'ArrowUp') {
    event.preventDefault()
    const idx = secondOptions.indexOf(seconds.value)
    const prev = idx > 0 ? idx - 1 : secondOptions.length - 1
    selectSecond(secondOptions[prev])
    nextTick(() => scrollActiveIntoView(secondCol.value))
  } else if (event.key === 'ArrowDown') {
    event.preventDefault()
    const idx = secondOptions.indexOf(seconds.value)
    const next = idx < secondOptions.length - 1 ? idx + 1 : 0
    selectSecond(secondOptions[next])
    nextTick(() => scrollActiveIntoView(secondCol.value))
  } else if (event.key === 'Enter') {
    confirmAndClose()
  } else if (event.key === 'Escape') {
    isOpen.value = false
  }
}
</script>

<template>
  <div class="space-y-2">
    <Label :for="field.name">
      {{ field.displayName || field.name }}
      <span v-if="field.isRequired" class="text-destructive">*</span>
    </Label>

    <!-- Readonly display -->
    <p v-if="readonly || field.isReadOnly" class="text-sm py-2">
      {{ displayValue || '-' }}
    </p>

    <!-- Popover time picker -->
    <PopoverRoot v-else v-model:open="isOpen">
      <PopoverTrigger as-child>
        <button
          :id="field.name"
          type="button"
          class="flex h-10 w-full items-center justify-between rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
        >
          <span :class="displayValue ? 'text-foreground' : 'text-muted-foreground'">
            {{ displayValue || 'Select time...' }}
          </span>
          <Clock class="h-4 w-4 text-muted-foreground shrink-0" />
        </button>
      </PopoverTrigger>
      <PopoverPortal>
        <PopoverContent
          :side-offset="4"
          align="start"
          class="z-50 rounded-md border bg-background p-0 shadow-md outline-none data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95"
        >
          <div class="flex items-stretch divide-x" :class="is12h ? 'w-[280px]' : 'w-[230px]'">
            <!-- Hour column -->
            <div class="flex flex-col items-center">
              <span class="px-3 py-1.5 text-xs font-medium text-muted-foreground border-b w-full text-center">Hr</span>
              <div
                ref="hourCol"
                class="h-48 w-16 overflow-y-auto scrollbar-thin"
                tabindex="0"
                @keydown="handleHourKey"
              >
                <button
                  v-for="h in hourOptions"
                  :key="h"
                  type="button"
                  :data-active="h === hours"
                  class="flex w-full items-center justify-center py-1.5 text-sm transition-colors hover:bg-accent"
                  :class="h === hours ? 'bg-primary text-primary-foreground font-medium' : ''"
                  @click="selectHour(h)"
                >
                  {{ is12h ? h : String(h).padStart(2, '0') }}
                </button>
              </div>
            </div>

            <!-- Minute column -->
            <div class="flex flex-col items-center">
              <span class="px-3 py-1.5 text-xs font-medium text-muted-foreground border-b w-full text-center">Min</span>
              <div
                ref="minuteCol"
                class="h-48 w-16 overflow-y-auto scrollbar-thin"
                tabindex="0"
                @keydown="handleMinuteKey"
              >
                <button
                  v-for="m in minuteOptions"
                  :key="m"
                  type="button"
                  :data-active="m === minutes"
                  class="flex w-full items-center justify-center py-1.5 text-sm transition-colors hover:bg-accent"
                  :class="m === minutes ? 'bg-primary text-primary-foreground font-medium' : ''"
                  @click="selectMinute(m)"
                >
                  {{ String(m).padStart(2, '0') }}
                </button>
              </div>
            </div>

            <!-- Second column -->
            <div class="flex flex-col items-center">
              <span class="px-3 py-1.5 text-xs font-medium text-muted-foreground border-b w-full text-center">Sec</span>
              <div
                ref="secondCol"
                class="h-48 w-16 overflow-y-auto scrollbar-thin"
                tabindex="0"
                @keydown="handleSecondKey"
              >
                <button
                  v-for="s in secondOptions"
                  :key="s"
                  type="button"
                  :data-active="s === seconds"
                  class="flex w-full items-center justify-center py-1.5 text-sm transition-colors hover:bg-accent"
                  :class="s === seconds ? 'bg-primary text-primary-foreground font-medium' : ''"
                  @click="selectSecond(s)"
                >
                  {{ String(s).padStart(2, '0') }}
                </button>
              </div>
            </div>

            <!-- AM/PM toggle (12h mode only) -->
            <div v-if="is12h" class="flex flex-col items-center">
              <span class="px-3 py-1.5 text-xs font-medium text-muted-foreground border-b w-full text-center">&nbsp;</span>
              <div class="flex flex-col items-center justify-center gap-1 h-48 px-2">
                <button
                  type="button"
                  class="rounded-md px-3 py-2 text-sm font-medium transition-colors"
                  :class="period === 'AM' ? 'bg-primary text-primary-foreground' : 'hover:bg-accent'"
                  @click="period = 'AM'; emitValue()"
                >
                  AM
                </button>
                <button
                  type="button"
                  class="rounded-md px-3 py-2 text-sm font-medium transition-colors"
                  :class="period === 'PM' ? 'bg-primary text-primary-foreground' : 'hover:bg-accent'"
                  @click="period = 'PM'; emitValue()"
                >
                  PM
                </button>
              </div>
            </div>
          </div>

          <!-- Footer -->
          <div class="flex items-center justify-between border-t px-3 py-2">
            <button
              type="button"
              class="text-xs text-muted-foreground hover:text-foreground transition-colors"
              @click="clearValue"
            >
              Clear
            </button>
            <button
              type="button"
              class="rounded-md bg-primary px-3 py-1 text-xs font-medium text-primary-foreground hover:bg-primary/90 transition-colors"
              @click="confirmAndClose"
            >
              OK
            </button>
          </div>
        </PopoverContent>
      </PopoverPortal>
    </PopoverRoot>

    <p v-if="field.description" class="text-xs text-muted-foreground">
      {{ field.description }}
    </p>
  </div>
</template>
