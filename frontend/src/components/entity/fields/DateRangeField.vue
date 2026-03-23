<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { PopoverRoot, PopoverTrigger, PopoverContent, PopoverPortal } from 'radix-vue'
import { Label } from '@/components/ui/label'
import { Calendar, ChevronLeft, ChevronRight } from 'lucide-vue-next'
import type { FieldMetadata } from '@/types/metadata'

interface DateRange {
  from: string
  to: string
}

interface Props {
  field: FieldMetadata
  modelValue?: DateRange | null
  readonly?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  readonly: false
})

const emit = defineEmits<{
  'update:modelValue': [value: DateRange | null]
}>()

const isOpen = ref(false)
const selectingEnd = ref(false)
const hoverDate = ref<string | null>(null)

// Calendar navigation state
const today = new Date()
const viewYear = ref(today.getFullYear())
const viewMonth = ref(today.getMonth())

// Internal from/to values
const fromDate = ref('')
const toDate = ref('')

// Preset options
type PresetKey = 'today' | 'last7' | 'thisMonth' | 'thisQuarter' | 'custom'
const presets: { key: PresetKey; label: string }[] = [
  { key: 'today', label: 'Today' },
  { key: 'last7', label: 'Last 7 Days' },
  { key: 'thisMonth', label: 'This Month' },
  { key: 'thisQuarter', label: 'This Quarter' },
  { key: 'custom', label: 'Custom' }
]
const activePreset = ref<PresetKey>('custom')

// Validation
const validationError = computed(() => {
  if (fromDate.value && toDate.value && fromDate.value > toDate.value) {
    return 'From date must be before or equal to To date'
  }
  return null
})

// Format a date string for display
function formatDisplayDate(dateStr: string): string {
  if (!dateStr) return ''
  const d = new Date(dateStr + 'T00:00:00')
  return d.toLocaleDateString('default', { month: 'short', day: 'numeric', year: 'numeric' })
}

const displayValue = computed(() => {
  const from = props.modelValue?.from
  const to = props.modelValue?.to
  if (!from && !to) return ''
  if (from && to) return `${formatDisplayDate(from)} \u2013 ${formatDisplayDate(to)}`
  if (from) return `From ${formatDisplayDate(from)}`
  return to ? `To ${formatDisplayDate(to)}` : ''
})

// Calendar grid computation
const daysInMonth = computed(() => {
  return new Date(viewYear.value, viewMonth.value + 1, 0).getDate()
})

const firstDayOfWeek = computed(() => {
  // 0 = Sunday
  return new Date(viewYear.value, viewMonth.value, 1).getDay()
})

const monthLabel = computed(() => {
  return new Date(viewYear.value, viewMonth.value, 1).toLocaleDateString('default', {
    month: 'long',
    year: 'numeric'
  })
})

interface CalendarDay {
  date: string
  day: number
  isCurrentMonth: boolean
  isToday: boolean
  isInRange: boolean
  isRangeStart: boolean
  isRangeEnd: boolean
}

const calendarDays = computed<CalendarDay[]>(() => {
  const days: CalendarDay[] = []
  const todayStr = toIsoDate(today)

  // Fill leading blanks from previous month
  const prevMonthDays = new Date(viewYear.value, viewMonth.value, 0).getDate()
  for (let i = firstDayOfWeek.value - 1; i >= 0; i--) {
    const day = prevMonthDays - i
    const m = viewMonth.value === 0 ? 11 : viewMonth.value - 1
    const y = viewMonth.value === 0 ? viewYear.value - 1 : viewYear.value
    const dateStr = `${y}-${String(m + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`
    days.push({
      date: dateStr,
      day,
      isCurrentMonth: false,
      isToday: dateStr === todayStr,
      isInRange: isDateInRange(dateStr),
      isRangeStart: dateStr === fromDate.value,
      isRangeEnd: dateStr === toDate.value
    })
  }

  // Current month days
  for (let d = 1; d <= daysInMonth.value; d++) {
    const dateStr = `${viewYear.value}-${String(viewMonth.value + 1).padStart(2, '0')}-${String(d).padStart(2, '0')}`
    days.push({
      date: dateStr,
      day: d,
      isCurrentMonth: true,
      isToday: dateStr === todayStr,
      isInRange: isDateInRange(dateStr),
      isRangeStart: dateStr === fromDate.value,
      isRangeEnd: dateStr === toDate.value
    })
  }

  // Fill trailing blanks
  const remaining = 42 - days.length
  for (let d = 1; d <= remaining; d++) {
    const m = viewMonth.value === 11 ? 0 : viewMonth.value + 1
    const y = viewMonth.value === 11 ? viewYear.value + 1 : viewYear.value
    const dateStr = `${y}-${String(m + 1).padStart(2, '0')}-${String(d).padStart(2, '0')}`
    days.push({
      date: dateStr,
      day: d,
      isCurrentMonth: false,
      isToday: dateStr === todayStr,
      isInRange: isDateInRange(dateStr),
      isRangeStart: dateStr === fromDate.value,
      isRangeEnd: dateStr === toDate.value
    })
  }

  return days
})

function isDateInRange(dateStr: string): boolean {
  const from = fromDate.value
  const to = selectingEnd.value && hoverDate.value ? hoverDate.value : toDate.value
  if (!from || !to) return false
  const effectiveFrom = from <= to ? from : to
  const effectiveTo = from <= to ? to : from
  return dateStr >= effectiveFrom && dateStr <= effectiveTo
}

function toIsoDate(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function prevMonth(): void {
  if (viewMonth.value === 0) {
    viewMonth.value = 11
    viewYear.value--
  } else {
    viewMonth.value--
  }
}

function nextMonth(): void {
  if (viewMonth.value === 11) {
    viewMonth.value = 0
    viewYear.value++
  } else {
    viewMonth.value++
  }
}

function handleDayClick(dateStr: string): void {
  if (!selectingEnd.value) {
    // First click: set from, start selecting end
    fromDate.value = dateStr
    toDate.value = ''
    selectingEnd.value = true
  } else {
    // Second click: set to, finalize
    if (dateStr < fromDate.value) {
      toDate.value = fromDate.value
      fromDate.value = dateStr
    } else {
      toDate.value = dateStr
    }
    selectingEnd.value = false
    hoverDate.value = null
    activePreset.value = 'custom'
    emitRange()
  }
}

function handleDayHover(dateStr: string): void {
  if (selectingEnd.value) {
    hoverDate.value = dateStr
  }
}

function applyPreset(key: PresetKey): void {
  activePreset.value = key
  const now = new Date()

  switch (key) {
    case 'today':
      fromDate.value = toIsoDate(now)
      toDate.value = toIsoDate(now)
      break
    case 'last7': {
      const weekAgo = new Date(now)
      weekAgo.setDate(weekAgo.getDate() - 6)
      fromDate.value = toIsoDate(weekAgo)
      toDate.value = toIsoDate(now)
      break
    }
    case 'thisMonth': {
      const monthStart = new Date(now.getFullYear(), now.getMonth(), 1)
      const monthEnd = new Date(now.getFullYear(), now.getMonth() + 1, 0)
      fromDate.value = toIsoDate(monthStart)
      toDate.value = toIsoDate(monthEnd)
      break
    }
    case 'thisQuarter': {
      const quarter = Math.floor(now.getMonth() / 3)
      const qStart = new Date(now.getFullYear(), quarter * 3, 1)
      const qEnd = new Date(now.getFullYear(), quarter * 3 + 3, 0)
      fromDate.value = toIsoDate(qStart)
      toDate.value = toIsoDate(qEnd)
      break
    }
    case 'custom':
      return
  }

  selectingEnd.value = false
  hoverDate.value = null
  emitRange()
}

function emitRange(): void {
  if (fromDate.value && toDate.value && !validationError.value) {
    emit('update:modelValue', { from: fromDate.value, to: toDate.value })
  }
}

function clearRange(): void {
  fromDate.value = ''
  toDate.value = ''
  selectingEnd.value = false
  hoverDate.value = null
  activePreset.value = 'custom'
  emit('update:modelValue', null)
  isOpen.value = false
}

// Sync internal state when popover opens
watch(isOpen, (open) => {
  if (open) {
    fromDate.value = props.modelValue?.from ?? ''
    toDate.value = props.modelValue?.to ?? ''
    selectingEnd.value = false
    hoverDate.value = null

    // Navigate calendar to the from date or today
    if (fromDate.value) {
      const d = new Date(fromDate.value + 'T00:00:00')
      viewYear.value = d.getFullYear()
      viewMonth.value = d.getMonth()
    } else {
      viewYear.value = today.getFullYear()
      viewMonth.value = today.getMonth()
    }
  }
})

const weekDays = ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa']
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

    <!-- Interactive picker -->
    <PopoverRoot v-else v-model:open="isOpen">
      <PopoverTrigger as-child>
        <button
          :id="field.name"
          type="button"
          class="flex h-10 w-full items-center justify-between rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
        >
          <span :class="displayValue ? 'text-foreground' : 'text-muted-foreground'">
            {{ displayValue || 'Select date range...' }}
          </span>
          <Calendar class="h-4 w-4 text-muted-foreground shrink-0" />
        </button>
      </PopoverTrigger>
      <PopoverPortal>
        <PopoverContent
          :side-offset="4"
          align="start"
          class="z-50 w-[320px] rounded-md border bg-background p-0 shadow-md outline-none data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95"
        >
          <!-- Presets -->
          <div class="flex items-center gap-1 border-b px-3 py-2 overflow-x-auto">
            <button
              v-for="preset in presets"
              :key="preset.key"
              type="button"
              class="shrink-0 rounded-full px-2.5 py-1 text-xs font-medium transition-colors"
              :class="activePreset === preset.key ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground hover:bg-accent'"
              @click="applyPreset(preset.key)"
            >
              {{ preset.label }}
            </button>
          </div>

          <!-- Calendar header -->
          <div class="flex items-center justify-between px-3 py-2">
            <button
              type="button"
              class="rounded-md p-1 hover:bg-accent transition-colors"
              @click="prevMonth"
            >
              <ChevronLeft class="h-4 w-4" />
            </button>
            <span class="text-sm font-medium">{{ monthLabel }}</span>
            <button
              type="button"
              class="rounded-md p-1 hover:bg-accent transition-colors"
              @click="nextMonth"
            >
              <ChevronRight class="h-4 w-4" />
            </button>
          </div>

          <!-- Calendar grid -->
          <div class="px-3 pb-2">
            <!-- Weekday headers -->
            <div class="grid grid-cols-7 text-center mb-1">
              <span
                v-for="wd in weekDays"
                :key="wd"
                class="text-xs font-medium text-muted-foreground py-1"
              >
                {{ wd }}
              </span>
            </div>

            <!-- Day cells -->
            <div class="grid grid-cols-7">
              <button
                v-for="(day, idx) in calendarDays"
                :key="idx"
                type="button"
                class="relative h-8 w-full text-sm transition-colors"
                :class="[
                  !day.isCurrentMonth ? 'text-muted-foreground/40' : '',
                  day.isToday && !day.isRangeStart && !day.isRangeEnd ? 'font-bold text-primary' : '',
                  day.isInRange && !day.isRangeStart && !day.isRangeEnd ? 'bg-primary/10' : '',
                  day.isRangeStart || day.isRangeEnd ? 'bg-primary text-primary-foreground font-medium rounded-md' : '',
                  !day.isInRange && !day.isRangeStart && !day.isRangeEnd ? 'hover:bg-accent rounded-md' : ''
                ]"
                @click="handleDayClick(day.date)"
                @mouseenter="handleDayHover(day.date)"
              >
                {{ day.day }}
              </button>
            </div>
          </div>

          <!-- Selection indicator -->
          <div v-if="selectingEnd" class="px-3 pb-2">
            <p class="text-xs text-muted-foreground text-center">
              Click to select end date
            </p>
          </div>

          <!-- Validation error -->
          <div v-if="validationError" class="px-3 pb-2">
            <p class="text-xs text-destructive">{{ validationError }}</p>
          </div>

          <!-- Footer -->
          <div class="flex items-center justify-between border-t px-3 py-2">
            <button
              type="button"
              class="text-xs text-muted-foreground hover:text-foreground transition-colors"
              @click="clearRange"
            >
              Clear
            </button>
            <div class="text-xs text-muted-foreground">
              <template v-if="fromDate && toDate">
                {{ formatDisplayDate(fromDate) }} &ndash; {{ formatDisplayDate(toDate) }}
              </template>
              <template v-else-if="fromDate">
                {{ formatDisplayDate(fromDate) }} &ndash; ...
              </template>
            </div>
          </div>
        </PopoverContent>
      </PopoverPortal>
    </PopoverRoot>

    <p v-if="field.description" class="text-xs text-muted-foreground">
      {{ field.description }}
    </p>
  </div>
</template>
