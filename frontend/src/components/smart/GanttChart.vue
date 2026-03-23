<script setup lang="ts">
import { ref, computed, nextTick } from 'vue'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { Diamond } from 'lucide-vue-next'
import { useGanttChart, type GanttTask } from '@/composables/useGanttChart'

interface Props {
  tasks: GanttTask[]
  mode?: 'day' | 'week' | 'month'
  showDependencies?: boolean
  showProgress?: boolean
  rowHeight?: number
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  mode: 'week',
  showDependencies: true,
  showProgress: true,
  rowHeight: 40,
  class: '',
})

const emit = defineEmits<{
  'task-click': [task: GanttTask]
  'mode-change': [mode: 'day' | 'week' | 'month']
}>()

const timelineRef = ref<HTMLElement | null>(null)

const {
  tasks: sortedTasks,
  viewConfig,
  timeSlots,
  setMode: composableSetMode,
  getTaskPosition,
  scrollToToday: composableScrollToToday,
  totalDays,
} = useGanttChart({
  tasks: computed(() => props.tasks),
  mode: props.mode,
})

const modes = ['day', 'week', 'month'] as const

// Column widths per mode
const columnWidthMap: Record<string, number> = {
  day: 40,
  week: 80,
  month: 120,
}

const columnWidth = computed(() => columnWidthMap[viewConfig.value.mode])

const totalWidth = computed(() => timeSlots.value.length * columnWidth.value)

// Today marker position in pixels
const todayPosition = computed(() => {
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  const start = viewConfig.value.startDate
  const total = totalDays.value
  const daysSinceStart = Math.round(
    (today.getTime() - start.getTime()) / (1000 * 60 * 60 * 24)
  )
  if (daysSinceStart < 0 || daysSinceStart > total) return -1
  return (daysSinceStart / total) * totalWidth.value
})

// Format a time slot label
function formatSlot(date: Date): string {
  const mode = viewConfig.value.mode
  if (mode === 'day') {
    const dayNames = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']
    return `${dayNames[date.getDay()]} ${date.getDate()}`
  } else if (mode === 'week') {
    const monthNames = [
      'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
      'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec',
    ]
    return `${monthNames[date.getMonth()]} ${date.getDate()}`
  } else {
    const monthNames = [
      'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
      'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec',
    ]
    return `${monthNames[date.getMonth()]} ${date.getFullYear()}`
  }
}

// Task position in pixels
function getTaskPx(task: GanttTask): { left: number; width: number } {
  const pct = getTaskPosition(task)
  return {
    left: (pct.left / 100) * totalWidth.value,
    width: (pct.width / 100) * totalWidth.value,
  }
}

// Build task lookup by id for dependency arrows
const taskIndex = computed(() => {
  const map = new Map<string, number>()
  sortedTasks.value.forEach((t, i) => map.set(t.id, i))
  return map
})

// Dependency arrow paths (SVG)
interface DependencyPath {
  key: string
  d: string
}

const dependencyPaths = computed<DependencyPath[]>(() => {
  if (!props.showDependencies) return []
  const paths: DependencyPath[] = []
  const rh = props.rowHeight

  for (const task of sortedTasks.value) {
    if (!task.dependencies?.length) continue
    const targetIdx = taskIndex.value.get(task.id)
    if (targetIdx === undefined) continue
    const targetPos = getTaskPx(task)
    const targetY = targetIdx * rh + rh / 2

    for (const depId of task.dependencies) {
      const sourceIdx = taskIndex.value.get(depId)
      if (sourceIdx === undefined) continue
      const sourceTasks = sortedTasks.value[sourceIdx]
      const sourcePos = getTaskPx(sourceTasks)
      const sourceY = sourceIdx * rh + rh / 2

      const startX = sourcePos.left + sourcePos.width
      const endX = targetPos.left
      const midX = startX + (endX - startX) / 2

      // Right-angle path: horizontal from source end, then vertical, then horizontal to target start
      const d = `M ${startX} ${sourceY} H ${midX} V ${targetY} H ${endX}`
      paths.push({ key: `${depId}-${task.id}`, d })
    }
  }
  return paths
})

const chartHeight = computed(() => sortedTasks.value.length * props.rowHeight)

function handleSetMode(mode: 'day' | 'week' | 'month') {
  composableSetMode(mode)
  emit('mode-change', mode)
}

function handleScrollToToday() {
  composableScrollToToday()
  nextTick(() => {
    if (timelineRef.value && todayPosition.value >= 0) {
      timelineRef.value.scrollLeft = Math.max(0, todayPosition.value - timelineRef.value.clientWidth / 2)
    }
  })
}

// Check if a day column is today (for highlighting)
function isToday(date: Date): boolean {
  const now = new Date()
  return (
    date.getFullYear() === now.getFullYear() &&
    date.getMonth() === now.getMonth() &&
    date.getDate() === now.getDate()
  )
}

// Check if a day is a weekend
function isWeekend(date: Date): boolean {
  const day = date.getDay()
  return day === 0 || day === 6
}
</script>

<template>
  <div :class="cn('border rounded-lg overflow-hidden bg-background', props.class)">
    <!-- Toolbar -->
    <div class="flex items-center justify-between p-3 border-b bg-muted/30">
      <span class="font-medium text-sm">Gantt Chart</span>
      <div class="flex gap-1">
        <Button
          v-for="m in modes"
          :key="m"
          size="sm"
          :variant="viewConfig.mode === m ? 'default' : 'outline'"
          @click="handleSetMode(m)"
        >
          {{ m.charAt(0).toUpperCase() + m.slice(1) }}
        </Button>
        <Button size="sm" variant="ghost" @click="handleScrollToToday">
          Today
        </Button>
      </div>
    </div>

    <!-- Chart area -->
    <div class="flex">
      <!-- Left: Task labels (fixed width) -->
      <div class="w-52 flex-shrink-0 border-r bg-background z-10">
        <div
          class="h-10 border-b bg-muted/50 flex items-center px-3 text-sm font-medium text-muted-foreground"
        >
          Task
        </div>
        <div
          v-for="task in sortedTasks"
          :key="task.id"
          :style="{ height: rowHeight + 'px' }"
          class="flex items-center px-3 border-b text-sm truncate cursor-pointer hover:bg-muted/20 transition-colors"
          @click="emit('task-click', task)"
        >
          <Diamond v-if="task.milestone" class="h-3 w-3 mr-2 text-amber-500 flex-shrink-0" />
          <span class="truncate">{{ task.name }}</span>
        </div>
      </div>

      <!-- Right: Timeline (scrollable) -->
      <div ref="timelineRef" class="flex-1 overflow-x-auto">
        <!-- Time headers -->
        <div class="flex h-10 border-b bg-muted/50" :style="{ width: totalWidth + 'px' }">
          <div
            v-for="slot in timeSlots"
            :key="slot.toISOString()"
            class="flex-shrink-0 border-r flex items-center justify-center text-xs text-muted-foreground"
            :class="{
              'bg-primary/5 font-medium text-primary': isToday(slot),
              'bg-muted/30': isWeekend(slot) && viewConfig.mode === 'day' && !isToday(slot),
            }"
            :style="{ width: columnWidth + 'px' }"
          >
            {{ formatSlot(slot) }}
          </div>
        </div>

        <!-- Task bars area -->
        <div class="relative" :style="{ width: totalWidth + 'px', height: chartHeight + 'px' }">
          <!-- Grid lines (vertical, one per time slot) -->
          <div
            v-for="(slot, idx) in timeSlots"
            :key="'grid-' + slot.toISOString()"
            class="absolute top-0 bottom-0 border-r border-border/30"
            :class="{
              'bg-muted/20': isWeekend(slot) && viewConfig.mode === 'day',
            }"
            :style="{ left: idx * columnWidth + 'px', width: columnWidth + 'px' }"
          />

          <!-- Today line -->
          <div
            v-if="todayPosition >= 0"
            class="absolute top-0 bottom-0 w-px bg-red-500 z-20"
            :style="{ left: todayPosition + 'px' }"
          >
            <div
              class="absolute -top-0 -translate-x-1/2 bg-red-500 text-white text-[10px] px-1 rounded-b"
            >
              Today
            </div>
          </div>

          <!-- Horizontal row dividers -->
          <div
            v-for="(task, idx) in sortedTasks"
            :key="'row-' + task.id"
            class="absolute w-full border-b border-border/20"
            :style="{ top: (idx + 1) * rowHeight + 'px' }"
          />

          <!-- Task bars -->
          <div
            v-for="(task, idx) in sortedTasks"
            :key="'bar-' + task.id"
            class="absolute"
            :style="{
              top: idx * rowHeight + 'px',
              height: rowHeight + 'px',
              left: 0,
              right: 0,
            }"
          >
            <!-- Milestone diamond -->
            <template v-if="task.milestone">
              <div
                class="absolute top-1/2 -translate-y-1/2 -translate-x-1/2 cursor-pointer z-10"
                :style="{ left: getTaskPx(task).left + 'px' }"
                @click="emit('task-click', task)"
              >
                <div class="w-4 h-4 rotate-45 bg-amber-500 hover:bg-amber-400 transition-colors" />
              </div>
            </template>

            <!-- Task bar -->
            <template v-else>
              <div
                class="absolute top-1/2 -translate-y-1/2 h-6 rounded cursor-pointer hover:opacity-80 transition-opacity shadow-sm z-10"
                :class="task.color || 'bg-primary'"
                :style="{
                  left: getTaskPx(task).left + 'px',
                  width: Math.max(4, getTaskPx(task).width) + 'px',
                }"
                @click="emit('task-click', task)"
              >
                <!-- Progress fill -->
                <div
                  v-if="showProgress && task.progress != null && task.progress > 0"
                  class="h-full rounded-l bg-black/15"
                  :class="{ 'rounded-r': task.progress >= 100 }"
                  :style="{ width: Math.min(100, task.progress) + '%' }"
                />
                <!-- Task name on bar (if wide enough) -->
                <span
                  v-if="getTaskPx(task).width > 60"
                  class="absolute inset-0 flex items-center px-2 text-[11px] font-medium text-white truncate pointer-events-none"
                >
                  {{ task.name }}
                </span>
              </div>
            </template>
          </div>

          <!-- SVG dependency arrows -->
          <svg
            v-if="showDependencies && dependencyPaths.length > 0"
            class="absolute inset-0 pointer-events-none z-10"
            :width="totalWidth"
            :height="chartHeight"
          >
            <defs>
              <marker
                id="gantt-arrow"
                markerWidth="8"
                markerHeight="6"
                refX="8"
                refY="3"
                orient="auto"
              >
                <path d="M 0 0 L 8 3 L 0 6 Z" class="fill-muted-foreground" />
              </marker>
            </defs>
            <path
              v-for="dep in dependencyPaths"
              :key="dep.key"
              :d="dep.d"
              stroke="currentColor"
              fill="none"
              class="text-muted-foreground/60"
              stroke-width="1.5"
              stroke-dasharray="4 2"
              marker-end="url(#gantt-arrow)"
            />
          </svg>
        </div>
      </div>
    </div>
  </div>
</template>
