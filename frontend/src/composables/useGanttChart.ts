import { ref, computed, toValue, type Ref, type ComputedRef, type MaybeRefOrGetter } from 'vue'

export interface GanttTask {
  id: string
  name: string
  start: Date
  end: Date
  progress?: number // 0-100
  color?: string // tailwind color class
  dependencies?: string[] // ids of tasks this depends on
  group?: string // group/category label
  milestone?: boolean // render as diamond instead of bar
}

export interface GanttViewConfig {
  mode: 'day' | 'week' | 'month'
  startDate: Date
  endDate: Date
}

export interface UseGanttChartOptions {
  tasks: MaybeRefOrGetter<GanttTask[]>
  mode?: 'day' | 'week' | 'month'
}

export interface UseGanttChartReturn {
  tasks: ComputedRef<GanttTask[]>
  viewConfig: Ref<GanttViewConfig>
  timeSlots: ComputedRef<Date[]>
  groups: ComputedRef<string[]>
  setMode: (mode: 'day' | 'week' | 'month') => void
  getTaskPosition: (task: GanttTask) => { left: number; width: number }
  scrollToToday: () => void
  totalDays: ComputedRef<number>
}

// ── Date helpers ──

function startOfDay(d: Date): Date {
  const r = new Date(d)
  r.setHours(0, 0, 0, 0)
  return r
}

function addDays(d: Date, n: number): Date {
  const r = new Date(d)
  r.setDate(r.getDate() + n)
  return r
}

function startOfWeek(d: Date): Date {
  const r = startOfDay(d)
  const day = r.getDay()
  // Start on Monday
  const diff = day === 0 ? -6 : 1 - day
  r.setDate(r.getDate() + diff)
  return r
}

function startOfMonth(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), 1)
}

function addMonths(d: Date, n: number): Date {
  const r = new Date(d)
  r.setMonth(r.getMonth() + n)
  return r
}

function diffDays(a: Date, b: Date): number {
  return Math.round((b.getTime() - a.getTime()) / (1000 * 60 * 60 * 24))
}

// ── Composable ──

export function useGanttChart(options: UseGanttChartOptions): UseGanttChartReturn {
  const tasks = computed(() => toValue(options.tasks))

  // Auto-detect date range from tasks with padding
  function computeRange(): { start: Date; end: Date } {
    const items = tasks.value
    if (items.length === 0) {
      const today = startOfDay(new Date())
      return { start: today, end: addDays(today, 30) }
    }
    let minDate = items[0].start
    let maxDate = items[0].end
    for (const t of items) {
      if (t.start < minDate) minDate = t.start
      if (t.end > maxDate) maxDate = t.end
    }
    // Add padding: 3 days before, 7 days after
    return {
      start: addDays(startOfDay(minDate), -3),
      end: addDays(startOfDay(maxDate), 7),
    }
  }

  const range = computeRange()

  const viewConfig = ref<GanttViewConfig>({
    mode: options.mode ?? 'week',
    startDate: range.start,
    endDate: range.end,
  })

  const totalDays = computed(() =>
    Math.max(1, diffDays(viewConfig.value.startDate, viewConfig.value.endDate))
  )

  const timeSlots = computed<Date[]>(() => {
    const slots: Date[] = []
    const { mode, startDate, endDate } = viewConfig.value

    if (mode === 'day') {
      let cursor = startOfDay(startDate)
      while (cursor <= endDate) {
        slots.push(new Date(cursor))
        cursor = addDays(cursor, 1)
      }
    } else if (mode === 'week') {
      let cursor = startOfWeek(startDate)
      while (cursor <= endDate) {
        slots.push(new Date(cursor))
        cursor = addDays(cursor, 7)
      }
    } else {
      // month
      let cursor = startOfMonth(startDate)
      while (cursor <= endDate) {
        slots.push(new Date(cursor))
        cursor = addMonths(cursor, 1)
      }
    }
    return slots
  })

  const groups = computed(() => {
    const seen = new Set<string>()
    for (const t of tasks.value) {
      if (t.group) seen.add(t.group)
    }
    return Array.from(seen)
  })

  function setMode(mode: 'day' | 'week' | 'month') {
    viewConfig.value.mode = mode
    // Re-compute range when switching modes
    const r = computeRange()
    viewConfig.value.startDate = r.start
    viewConfig.value.endDate = r.end
  }

  function getTaskPosition(task: GanttTask): { left: number; width: number } {
    const start = viewConfig.value.startDate
    const total = totalDays.value

    const taskStartDays = diffDays(start, startOfDay(task.start))
    const taskDuration = Math.max(1, diffDays(startOfDay(task.start), startOfDay(task.end)))

    const left = (taskStartDays / total) * 100
    const width = (taskDuration / total) * 100

    return {
      left: Math.max(0, left),
      width: Math.max(0.5, width), // minimum visibility
    }
  }

  function scrollToToday() {
    // This is a hook for the component to call — the component handles DOM scrolling.
    // We recenter the view around today if today is outside visible range.
    const today = startOfDay(new Date())
    const r = computeRange()
    if (today < r.start || today > r.end) {
      // Center on today with some context
      viewConfig.value.startDate = addDays(today, -14)
      viewConfig.value.endDate = addDays(today, 30)
    }
  }

  return {
    tasks,
    viewConfig,
    timeSlots,
    groups,
    setMode,
    getTaskPosition,
    scrollToToday,
    totalDays,
  }
}
