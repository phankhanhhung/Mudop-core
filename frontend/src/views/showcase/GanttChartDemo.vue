<script setup lang="ts">
import { ref } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import GanttChart from '@/components/smart/GanttChart.vue'
import type { GanttTask } from '@/composables/useGanttChart'
import { ArrowLeft, GanttChartSquare } from 'lucide-vue-next'
import { useRouter } from 'vue-router'

const router = useRouter()

// ── Helper: create dates relative to today ──

function daysFromNow(offset: number): Date {
  const d = new Date()
  d.setDate(d.getDate() + offset)
  d.setHours(0, 0, 0, 0)
  return d
}

// ── Sample project data: Website Redesign ──

const projectTasks: GanttTask[] = [
  {
    id: 'planning',
    name: 'Planning & Requirements',
    start: daysFromNow(-21),
    end: daysFromNow(-7),
    progress: 100,
    group: 'Planning',
    color: 'bg-blue-500',
  },
  {
    id: 'design',
    name: 'UI/UX Design',
    start: daysFromNow(-14),
    end: daysFromNow(7),
    progress: 60,
    group: 'Design',
    dependencies: ['planning'],
    color: 'bg-violet-500',
  },
  {
    id: 'wireframes',
    name: 'Wireframes & Prototypes',
    start: daysFromNow(-10),
    end: daysFromNow(0),
    progress: 80,
    group: 'Design',
    dependencies: ['planning'],
    color: 'bg-violet-400',
  },
  {
    id: 'frontend',
    name: 'Frontend Development',
    start: daysFromNow(0),
    end: daysFromNow(28),
    progress: 15,
    group: 'Development',
    dependencies: ['design'],
    color: 'bg-emerald-500',
  },
  {
    id: 'backend',
    name: 'Backend Development',
    start: daysFromNow(-7),
    end: daysFromNow(21),
    progress: 30,
    group: 'Development',
    dependencies: ['planning'],
    color: 'bg-emerald-600',
  },
  {
    id: 'api',
    name: 'API Integration',
    start: daysFromNow(7),
    end: daysFromNow(28),
    progress: 0,
    group: 'Development',
    dependencies: ['backend'],
    color: 'bg-emerald-400',
  },
  {
    id: 'testing',
    name: 'QA & Testing',
    start: daysFromNow(28),
    end: daysFromNow(42),
    progress: 0,
    group: 'Testing',
    dependencies: ['frontend', 'api'],
    color: 'bg-amber-500',
  },
  {
    id: 'perf',
    name: 'Performance Optimization',
    start: daysFromNow(35),
    end: daysFromNow(42),
    progress: 0,
    group: 'Testing',
    dependencies: ['testing'],
    color: 'bg-amber-400',
  },
  {
    id: 'launch',
    name: 'Go Live',
    start: daysFromNow(45),
    end: daysFromNow(45),
    milestone: true,
    group: 'Launch',
    dependencies: ['testing', 'perf'],
  },
  {
    id: 'review',
    name: 'Post-Launch Review',
    start: daysFromNow(46),
    end: daysFromNow(52),
    progress: 0,
    group: 'Launch',
    dependencies: ['launch'],
    color: 'bg-rose-500',
  },
]

// ── Demo state ──

const selectedTask = ref<GanttTask | null>(null)
const currentMode = ref<'day' | 'week' | 'month'>('week')

function handleTaskClick(task: GanttTask) {
  selectedTask.value = task
}

function handleModeChange(mode: 'day' | 'week' | 'month') {
  currentMode.value = mode
}

function formatDate(d: Date): string {
  return d.toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  })
}

function taskDuration(task: GanttTask): number {
  return Math.round(
    (task.end.getTime() - task.start.getTime()) / (1000 * 60 * 60 * 24)
  )
}
</script>

<template>
  <DefaultLayout>
    <div class="space-y-8">
      <!-- Header -->
      <div class="flex items-center gap-4">
        <Button variant="ghost" size="icon" @click="router.push('/showcase')">
          <ArrowLeft class="h-5 w-5" />
        </Button>
        <div>
          <h1 class="text-2xl font-bold tracking-tight flex items-center gap-2">
            <GanttChartSquare class="h-6 w-6" />
            Gantt Chart
          </h1>
          <p class="text-muted-foreground mt-1">
            Project timeline visualization with tasks, dependencies, and milestones.
          </p>
        </div>
      </div>

      <!-- Main demo -->
      <Card>
        <CardHeader>
          <CardTitle>Website Redesign Project</CardTitle>
        </CardHeader>
        <CardContent class="space-y-4">
          <p class="text-sm text-muted-foreground">
            A sample project with 10 tasks across 4 phases. Tasks show progress bars,
            dependency arrows, a milestone marker, and a "Today" indicator line.
            Switch between Day, Week, and Month views using the toolbar.
          </p>
          <GanttChart
            :tasks="projectTasks"
            :mode="currentMode"
            :show-dependencies="true"
            :show-progress="true"
            :row-height="40"
            @task-click="handleTaskClick"
            @mode-change="handleModeChange"
          />
        </CardContent>
      </Card>

      <!-- Selected task info -->
      <Card v-if="selectedTask">
        <CardHeader>
          <CardTitle class="flex items-center gap-2">
            Selected Task
            <Badge variant="secondary">{{ selectedTask.group }}</Badge>
          </CardTitle>
        </CardHeader>
        <CardContent>
          <div class="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm">
            <div>
              <span class="text-muted-foreground block mb-1">Name</span>
              <span class="font-medium">{{ selectedTask.name }}</span>
            </div>
            <div>
              <span class="text-muted-foreground block mb-1">Start</span>
              <span class="font-medium">{{ formatDate(selectedTask.start) }}</span>
            </div>
            <div>
              <span class="text-muted-foreground block mb-1">End</span>
              <span class="font-medium">{{ formatDate(selectedTask.end) }}</span>
            </div>
            <div>
              <span class="text-muted-foreground block mb-1">Duration</span>
              <span class="font-medium">
                {{ selectedTask.milestone ? 'Milestone' : taskDuration(selectedTask) + ' days' }}
              </span>
            </div>
            <div v-if="selectedTask.progress != null">
              <span class="text-muted-foreground block mb-1">Progress</span>
              <span class="font-medium">{{ selectedTask.progress }}%</span>
            </div>
            <div v-if="selectedTask.dependencies?.length">
              <span class="text-muted-foreground block mb-1">Depends On</span>
              <span class="font-medium">
                {{ selectedTask.dependencies.join(', ') }}
              </span>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Feature list -->
      <Card>
        <CardHeader>
          <CardTitle>Features</CardTitle>
        </CardHeader>
        <CardContent>
          <div class="grid grid-cols-1 md:grid-cols-2 gap-3 text-sm">
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5">View</Badge>
              <span>Day / Week / Month view modes with automatic column scaling</span>
            </div>
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5">Tasks</Badge>
              <span>Horizontal bars with color coding and inline task name labels</span>
            </div>
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5">Progress</Badge>
              <span>Progress fill overlay on each task bar (0-100%)</span>
            </div>
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5">Deps</Badge>
              <span>SVG dependency arrows with right-angle routing and arrowheads</span>
            </div>
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5">Milestones</Badge>
              <span>Diamond-shaped milestone markers for key deliverables</span>
            </div>
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5">Today</Badge>
              <span>Red "Today" marker line with scroll-to-today button</span>
            </div>
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5">Groups</Badge>
              <span>Task grouping by category / project phase</span>
            </div>
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5">Click</Badge>
              <span>Task click events for detail panel integration</span>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
