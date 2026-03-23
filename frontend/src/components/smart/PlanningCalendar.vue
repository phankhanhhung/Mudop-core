<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { ChevronLeft, ChevronRight, Calendar, Clock } from 'lucide-vue-next'

export interface CalendarResource {
  id: string
  name: string
  role?: string
  avatar?: string
  color?: string
}

export interface CalendarAppointment {
  id: string
  resourceId: string
  title: string
  start: string
  end: string
  type?: 'default' | 'info' | 'success' | 'warning' | 'error'
  description?: string
}

interface Props {
  resources: CalendarResource[]
  appointments: CalendarAppointment[]
  startDate?: string
  viewType?: 'day' | 'week' | 'month'
  hourStart?: number
  hourEnd?: number
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  startDate: '',
  viewType: 'week',
  hourStart: 8,
  hourEnd: 18,
  class: '',
})

const emit = defineEmits<{
  'appointment-click': [appointment: CalendarAppointment]
  'date-change': [date: string]
  'view-change': [viewType: 'day' | 'week' | 'month']
}>()

const currentView = ref<'day' | 'week' | 'month'>(props.viewType)
const currentDate = ref<Date>(props.startDate ? new Date(props.startDate) : new Date())
const nowMinute = ref(Date.now())

let timerHandle: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  timerHandle = setInterval(() => {
    nowMinute.value = Date.now()
  }, 60_000)
})

onUnmounted(() => {
  if (timerHandle) clearInterval(timerHandle)
})

// ── Date helpers ──

function startOfDay(d: Date): Date {
  const r = new Date(d)
  r.setHours(0, 0, 0, 0)
  return r
}

function startOfWeek(d: Date): Date {
  const r = startOfDay(d)
  r.setDate(r.getDate() - r.getDay() + 1) // Monday
  return r
}

function startOfMonth(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), 1)
}

function addDays(d: Date, n: number): Date {
  const r = new Date(d)
  r.setDate(r.getDate() + n)
  return r
}

function isSameDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate()
}

function formatShortDate(d: Date): string {
  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']
  return `${months[d.getMonth()]} ${d.getDate()}`
}

function formatDayName(d: Date): string {
  const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']
  return days[d.getDay()]
}

// ── View range ──

const viewStart = computed<Date>(() => {
  if (currentView.value === 'day') return startOfDay(currentDate.value)
  if (currentView.value === 'week') return startOfWeek(currentDate.value)
  return startOfMonth(currentDate.value)
})

const viewEnd = computed<Date>(() => {
  if (currentView.value === 'day') return addDays(viewStart.value, 1)
  if (currentView.value === 'week') return addDays(viewStart.value, 7)
  const s = viewStart.value
  return new Date(s.getFullYear(), s.getMonth() + 1, 0, 23, 59, 59, 999)
})

const columns = computed<Date[]>(() => {
  if (currentView.value === 'day') {
    const hrs: Date[] = []
    for (let h = props.hourStart; h < props.hourEnd; h++) {
      const d = new Date(viewStart.value)
      d.setHours(h, 0, 0, 0)
      hrs.push(d)
    }
    return hrs
  }
  const days: Date[] = []
  const end = viewEnd.value
  let d = new Date(viewStart.value)
  while (d <= end) {
    days.push(new Date(d))
    d = addDays(d, 1)
  }
  return days
})

const headerLabel = computed(() => {
  if (currentView.value === 'day') {
    const d = viewStart.value
    return d.toLocaleDateString('en-US', { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' })
  }
  if (currentView.value === 'week') {
    const s = viewStart.value
    const e = addDays(s, 6)
    const sYear = s.getFullYear()
    const eYear = e.getFullYear()
    if (sYear === eYear) {
      return `${formatShortDate(s)} - ${formatShortDate(e)}, ${sYear}`
    }
    return `${formatShortDate(s)}, ${sYear} - ${formatShortDate(e)}, ${eYear}`
  }
  return viewStart.value.toLocaleDateString('en-US', { month: 'long', year: 'numeric' })
})

// ── Column header labels ──

function columnLabel(col: Date): string {
  if (currentView.value === 'day') {
    const h = col.getHours()
    const suffix = h >= 12 ? 'PM' : 'AM'
    const h12 = h % 12 || 12
    return `${h12}${suffix}`
  }
  if (currentView.value === 'month') {
    return `${col.getDate()}`
  }
  return `${formatDayName(col)} ${col.getDate()}`
}

// ── Appointment positioning ──

function getTimeRangeMs(): { rangeStart: number; rangeEnd: number } {
  if (currentView.value === 'day') {
    const base = startOfDay(viewStart.value)
    const rangeStart = new Date(base).setHours(props.hourStart, 0, 0, 0)
    const rangeEnd = new Date(base).setHours(props.hourEnd, 0, 0, 0)
    return { rangeStart, rangeEnd }
  }
  return { rangeStart: viewStart.value.getTime(), rangeEnd: viewEnd.value.getTime() + 1 }
}

interface AppointmentPosition {
  appointment: CalendarAppointment
  leftPct: number
  widthPct: number
}

function getAppointmentsForResource(resourceId: string): AppointmentPosition[] {
  const { rangeStart, rangeEnd } = getTimeRangeMs()
  const totalMs = rangeEnd - rangeStart
  if (totalMs <= 0) return []

  return props.appointments
    .filter((a) => a.resourceId === resourceId)
    .map((a) => {
      const aStart = new Date(a.start).getTime()
      const aEnd = new Date(a.end).getTime()
      if (aEnd <= rangeStart || aStart >= rangeEnd) return null
      const clampedStart = Math.max(aStart, rangeStart)
      const clampedEnd = Math.min(aEnd, rangeEnd)
      const leftPct = ((clampedStart - rangeStart) / totalMs) * 100
      const widthPct = ((clampedEnd - clampedStart) / totalMs) * 100
      return { appointment: a, leftPct, widthPct }
    })
    .filter((x): x is AppointmentPosition => x !== null && x.widthPct > 0)
}

// ── Appointment colors ──

const typeColors: Record<string, string> = {
  default: 'bg-primary/80 text-primary-foreground',
  info: 'bg-blue-500/80 text-white',
  success: 'bg-green-500/80 text-white',
  warning: 'bg-amber-500/80 text-white',
  error: 'bg-red-500/80 text-white',
}

function appointmentClasses(type?: string): string {
  return typeColors[type || 'default'] || typeColors.default
}

// ── Current time indicator ──

const nowIndicatorPct = computed(() => {
  void nowMinute.value
  const now = new Date()
  const { rangeStart, rangeEnd } = getTimeRangeMs()
  const nowMs = now.getTime()
  if (nowMs < rangeStart || nowMs > rangeEnd) return -1
  return ((nowMs - rangeStart) / (rangeEnd - rangeStart)) * 100
})

// ── Today column highlight ──

function isToday(col: Date): boolean {
  if (currentView.value === 'day') return false
  return isSameDay(col, new Date())
}

function isWeekend(col: Date): boolean {
  if (currentView.value === 'day') return false
  const day = col.getDay()
  return day === 0 || day === 6
}

// ── Resource avatar ──

function getInitials(resource: CalendarResource): string {
  if (resource.avatar) return resource.avatar
  return resource.name
    .split(' ')
    .map((w) => w[0])
    .join('')
    .toUpperCase()
    .slice(0, 2)
}

const avatarColors = [
  'bg-blue-500 text-white',
  'bg-emerald-500 text-white',
  'bg-violet-500 text-white',
  'bg-amber-500 text-white',
  'bg-rose-500 text-white',
  'bg-cyan-500 text-white',
  'bg-pink-500 text-white',
  'bg-indigo-500 text-white',
]

function avatarColor(resource: CalendarResource, index: number): string {
  if (resource.color) return resource.color
  return avatarColors[index % avatarColors.length]
}

// ── Navigation ──

function navigate(direction: -1 | 1) {
  const d = new Date(currentDate.value)
  if (currentView.value === 'day') d.setDate(d.getDate() + direction)
  else if (currentView.value === 'week') d.setDate(d.getDate() + direction * 7)
  else d.setMonth(d.getMonth() + direction)
  currentDate.value = d
  emit('date-change', d.toISOString().split('T')[0])
}

function goToday() {
  currentDate.value = new Date()
  emit('date-change', new Date().toISOString().split('T')[0])
}

function setView(v: 'day' | 'week' | 'month') {
  currentView.value = v
  emit('view-change', v)
}

// ── Grid column width ──

const colMinWidth = computed(() => {
  if (currentView.value === 'day') return '80px'
  if (currentView.value === 'month') return '36px'
  return '100px'
})
</script>

<template>
  <div :class="cn('border rounded-lg bg-background overflow-hidden', props.class)">
    <!-- Toolbar -->
    <div class="flex items-center justify-between px-4 py-3 border-b bg-muted/30">
      <div class="flex items-center gap-2">
        <Button variant="outline" size="sm" @click="navigate(-1)">
          <ChevronLeft class="h-4 w-4" />
        </Button>
        <Button variant="outline" size="sm" @click="goToday">
          <Calendar class="h-4 w-4 mr-1" />
          Today
        </Button>
        <Button variant="outline" size="sm" @click="navigate(1)">
          <ChevronRight class="h-4 w-4" />
        </Button>
        <span class="ml-2 text-sm font-semibold">{{ headerLabel }}</span>
      </div>
      <div class="flex items-center gap-1">
        <Button
          v-for="v in (['day', 'week', 'month'] as const)"
          :key="v"
          size="sm"
          :variant="currentView === v ? 'default' : 'outline'"
          @click="setView(v)"
        >
          {{ v.charAt(0).toUpperCase() + v.slice(1) }}
        </Button>
      </div>
    </div>

    <!-- Calendar grid -->
    <div class="overflow-x-auto">
      <div
        class="grid"
        :style="{
          gridTemplateColumns: `180px repeat(${columns.length}, minmax(${colMinWidth}, 1fr))`,
        }"
      >
        <!-- Column headers -->
        <div class="sticky left-0 z-20 bg-muted/50 border-b border-r px-3 py-2 flex items-center">
          <span class="text-xs font-medium text-muted-foreground uppercase tracking-wider">Resources</span>
        </div>
        <div
          v-for="(col, ci) in columns"
          :key="ci"
          :class="cn(
            'border-b px-2 py-2 text-center text-xs font-medium',
            isToday(col) ? 'bg-primary/10 text-primary font-semibold' : 'text-muted-foreground',
            isWeekend(col) ? 'bg-muted/40' : '',
          )"
        >
          {{ columnLabel(col) }}
        </div>

        <!-- Resource rows -->
        <template v-for="(resource, ri) in resources" :key="resource.id">
          <!-- Resource info cell -->
          <div
            :class="cn(
              'sticky left-0 z-10 bg-background border-b border-r px-3 py-3 flex items-center gap-2',
              ri % 2 === 1 ? 'bg-muted/20' : '',
            )"
          >
            <div
              :class="cn(
                'flex-shrink-0 w-8 h-8 rounded-full flex items-center justify-center text-xs font-medium',
                avatarColor(resource, ri),
              )"
            >
              {{ getInitials(resource) }}
            </div>
            <div class="min-w-0">
              <div class="text-sm font-medium truncate">{{ resource.name }}</div>
              <div v-if="resource.role" class="text-xs text-muted-foreground truncate">{{ resource.role }}</div>
            </div>
          </div>

          <!-- Time cells for this resource -->
          <div
            :class="cn(
              'relative border-b col-span-full',
              ri % 2 === 1 ? 'bg-muted/10' : '',
            )"
            :style="{ gridColumn: `2 / span ${columns.length}` }"
          >
            <!-- Grid lines -->
            <div class="absolute inset-0 flex">
              <div
                v-for="(col, ci) in columns"
                :key="ci"
                :class="cn(
                  'h-full border-r border-dashed border-muted-foreground/10',
                  isToday(col) ? 'bg-primary/5' : '',
                  isWeekend(col) ? 'bg-muted/20' : '',
                )"
                :style="{ width: `${100 / columns.length}%` }"
              />
            </div>

            <!-- Now indicator -->
            <div
              v-if="nowIndicatorPct >= 0 && (currentView === 'day' || currentView === 'week')"
              class="absolute top-0 bottom-0 w-0.5 bg-red-500 z-10"
              :style="{ left: `${nowIndicatorPct}%` }"
            />

            <!-- Appointments -->
            <div class="relative h-14 py-1">
              <div
                v-for="pos in getAppointmentsForResource(resource.id)"
                :key="pos.appointment.id"
                :class="cn(
                  'absolute top-1 bottom-1 rounded px-1.5 flex items-center cursor-pointer',
                  'text-xs font-medium truncate shadow-sm hover:shadow-md transition-shadow',
                  appointmentClasses(pos.appointment.type),
                )"
                :style="{
                  left: `${pos.leftPct}%`,
                  width: `${pos.widthPct}%`,
                  minWidth: '2px',
                }"
                :title="`${pos.appointment.title}${pos.appointment.description ? ' — ' + pos.appointment.description : ''}`"
                @click="emit('appointment-click', pos.appointment)"
              >
                <span v-if="pos.widthPct > 5" class="truncate">{{ pos.appointment.title }}</span>
              </div>
            </div>
          </div>
        </template>
      </div>
    </div>

    <!-- Footer legend -->
    <div class="flex items-center gap-4 px-4 py-2 border-t bg-muted/30 text-xs text-muted-foreground">
      <div class="flex items-center gap-1">
        <Clock class="h-3 w-3" />
        <span>{{ resources.length }} resources, {{ appointments.length }} appointments</span>
      </div>
      <div class="flex items-center gap-3 ml-auto">
        <div class="flex items-center gap-1">
          <span class="w-3 h-3 rounded-sm bg-primary/80" />
          <span>Default</span>
        </div>
        <div class="flex items-center gap-1">
          <span class="w-3 h-3 rounded-sm bg-blue-500/80" />
          <span>Info</span>
        </div>
        <div class="flex items-center gap-1">
          <span class="w-3 h-3 rounded-sm bg-green-500/80" />
          <span>Success</span>
        </div>
        <div class="flex items-center gap-1">
          <span class="w-3 h-3 rounded-sm bg-amber-500/80" />
          <span>Warning</span>
        </div>
        <div class="flex items-center gap-1">
          <span class="w-3 h-3 rounded-sm bg-red-500/80" />
          <span>Error</span>
        </div>
      </div>
    </div>
  </div>
</template>
