<script setup lang="ts">
import { ref, computed } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import RatingIndicator from '@/components/smart/RatingIndicator.vue'
import Carousel from '@/components/smart/Carousel.vue'
import CarouselSlide from '@/components/smart/CarouselSlide.vue'
import RangeSlider from '@/components/smart/RangeSlider.vue'
import ColorPicker from '@/components/smart/ColorPicker.vue'
import ProcessFlow from '@/components/smart/ProcessFlow.vue'
import GanttChart from '@/components/smart/GanttChart.vue'
import SplitApp from '@/components/layout/SplitApp.vue'
import {
  ArrowLeft,
  Layers,
  Star,
  Image,
  SlidersHorizontal,
  Palette,
  GitBranch,
  GanttChartSquare,
  PanelLeftClose,
  ArrowRight,
  Mail,
} from 'lucide-vue-next'
import { useRouter } from 'vue-router'
import type { ProcessNode } from '@/composables/useProcessFlow'
import type { GanttTask } from '@/composables/useGanttChart'

const router = useRouter()

// ── RatingIndicator state ──────────────────────────────────────────────────
const ratingValue = ref(3)

// ── RangeSlider state ──────────────────────────────────────────────────────
const singleSlider = ref(40)
const rangeSlider = ref<[number, number]>([20, 70])

// ── ColorPicker state ──────────────────────────────────────────────────────
const pickedColor = ref('#3b82f6')

// ── ProcessFlow data ───────────────────────────────────────────────────────
const processNodes: ProcessNode[] = [
  { id: '1', title: 'Request', subtitle: 'Submitted', status: 'positive' },
  { id: '2', title: 'Review', subtitle: 'Approved', status: 'positive' },
  { id: '3', title: 'Processing', subtitle: 'In progress', status: 'critical' },
  { id: '4', title: 'Shipping', subtitle: 'Pending', status: 'neutral' },
  { id: '5', title: 'Delivered', subtitle: 'Planned', status: 'planned' },
]

// ── GanttChart data ────────────────────────────────────────────────────────
const today = new Date()
const ganttTasks = computed<GanttTask[]>(() => {
  const d = (offset: number) => {
    const date = new Date(today)
    date.setDate(date.getDate() + offset)
    return date
  }
  return [
    { id: 't1', name: 'Research', start: d(-10), end: d(-3), progress: 100, color: 'bg-emerald-500' },
    { id: 't2', name: 'Design', start: d(-4), end: d(3), progress: 70, color: 'bg-blue-500', dependencies: ['t1'] },
    { id: 't3', name: 'Development', start: d(2), end: d(14), progress: 20, color: 'bg-violet-500', dependencies: ['t2'] },
    { id: 't4', name: 'Launch', start: d(14), end: d(14), milestone: true, dependencies: ['t3'] },
  ]
})

// ── SplitApp state ─────────────────────────────────────────────────────────
interface ListItem {
  id: number
  title: string
  description: string
  badge: string
}

const splitItems: ListItem[] = [
  { id: 1, title: 'Inbox', description: '12 new messages', badge: '12' },
  { id: 2, title: 'Drafts', description: '3 unsent drafts', badge: '3' },
  { id: 3, title: 'Sent', description: 'Last sent 2h ago', badge: '' },
  { id: 4, title: 'Archive', description: '847 archived items', badge: '' },
]

const selectedItem = ref<ListItem | null>(null)
const splitRef = ref<InstanceType<typeof SplitApp> | null>(null)

function selectItem(item: ListItem) {
  selectedItem.value = item
  splitRef.value?.setHasDetail(true)
  splitRef.value?.showDetail()
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
            <Layers class="h-6 w-6" />
            Phase F — Interactive & Layout Components
          </h1>
          <p class="text-muted-foreground mt-1">
            Seven components for ratings, sliders, color selection, process visualization, scheduling, and responsive layouts.
          </p>
        </div>
      </div>

      <!-- 1. RatingIndicator -->
      <Card>
        <CardHeader>
          <div class="flex items-center justify-between">
            <CardTitle class="flex items-center gap-2">
              <Star class="h-5 w-5" />
              RatingIndicator
            </CardTitle>
            <Button variant="outline" size="sm" @click="router.push('/showcase/rating-indicator')">
              View Full Demo
              <ArrowRight class="h-4 w-4 ml-1" />
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Star rating input with hover preview, half-star display, and keyboard navigation.
          </p>
          <div class="flex items-end gap-6">
            <RatingIndicator v-model="ratingValue" show-value />
            <div class="text-sm text-muted-foreground pb-1">
              Selected: <Badge variant="secondary">{{ ratingValue }}</Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- 2. Carousel -->
      <Card>
        <CardHeader>
          <div class="flex items-center justify-between">
            <CardTitle class="flex items-center gap-2">
              <Image class="h-5 w-5" />
              Carousel
            </CardTitle>
            <Button variant="outline" size="sm" @click="router.push('/showcase/carousel')">
              View Full Demo
              <ArrowRight class="h-4 w-4 ml-1" />
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Content slider with arrow navigation, dot indicators, touch/swipe, and optional auto-play.
          </p>
          <Carousel :loop="true" aspect-ratio="16/9" class="max-w-xl">
            <CarouselSlide>
              <div class="h-full flex items-center justify-center bg-gradient-to-br from-blue-500 to-indigo-600 text-white rounded-lg">
                <div class="text-center">
                  <h3 class="text-2xl font-bold">Slide 1</h3>
                  <p class="mt-1 text-blue-100">First content panel</p>
                </div>
              </div>
            </CarouselSlide>
            <CarouselSlide>
              <div class="h-full flex items-center justify-center bg-gradient-to-br from-emerald-500 to-teal-600 text-white rounded-lg">
                <div class="text-center">
                  <h3 class="text-2xl font-bold">Slide 2</h3>
                  <p class="mt-1 text-emerald-100">Second content panel</p>
                </div>
              </div>
            </CarouselSlide>
            <CarouselSlide>
              <div class="h-full flex items-center justify-center bg-gradient-to-br from-amber-500 to-orange-600 text-white rounded-lg">
                <div class="text-center">
                  <h3 class="text-2xl font-bold">Slide 3</h3>
                  <p class="mt-1 text-amber-100">Third content panel</p>
                </div>
              </div>
            </CarouselSlide>
          </Carousel>
        </CardContent>
      </Card>

      <!-- 3. RangeSlider -->
      <Card>
        <CardHeader>
          <div class="flex items-center justify-between">
            <CardTitle class="flex items-center gap-2">
              <SlidersHorizontal class="h-5 w-5" />
              RangeSlider
            </CardTitle>
            <Button variant="outline" size="sm" @click="router.push('/showcase/range-slider')">
              View Full Demo
              <ArrowRight class="h-4 w-4 ml-1" />
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Draggable slider supporting single-thumb and dual-thumb (range) modes with step snapping.
          </p>
          <div class="space-y-6 max-w-md">
            <RangeSlider
              v-model="singleSlider"
              label="Single Thumb"
              :min="0"
              :max="100"
              :step="1"
              show-value
              show-min-max
            />
            <RangeSlider
              v-model="rangeSlider"
              label="Dual Thumb (Range)"
              :min="0"
              :max="100"
              :step="5"
              show-value
              show-min-max
            />
          </div>
        </CardContent>
      </Card>

      <!-- 4. ColorPicker -->
      <Card>
        <CardHeader>
          <div class="flex items-center justify-between">
            <CardTitle class="flex items-center gap-2">
              <Palette class="h-5 w-5" />
              ColorPicker
            </CardTitle>
            <Button variant="outline" size="sm" @click="router.push('/showcase/color-picker')">
              View Full Demo
              <ArrowRight class="h-4 w-4 ml-1" />
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Popover-based color picker with saturation-value area, hue slider, hex input, and preset swatches.
          </p>
          <div class="flex items-center gap-4">
            <ColorPicker v-model="pickedColor" label="Pick a color" />
            <div class="flex items-center gap-2">
              <div class="w-8 h-8 rounded border" :style="{ backgroundColor: pickedColor }" />
              <span class="text-sm font-mono text-muted-foreground">{{ pickedColor }}</span>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- 5. ProcessFlow -->
      <Card>
        <CardHeader>
          <div class="flex items-center justify-between">
            <CardTitle class="flex items-center gap-2">
              <GitBranch class="h-5 w-5" />
              ProcessFlow
            </CardTitle>
            <Button variant="outline" size="sm" @click="router.push('/showcase/process-flow')">
              View Full Demo
              <ArrowRight class="h-4 w-4 ml-1" />
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Horizontal workflow visualization with status-colored nodes, connectors, and click interaction.
          </p>
          <ProcessFlow :nodes="processNodes" />
        </CardContent>
      </Card>

      <!-- 6. GanttChart -->
      <Card>
        <CardHeader>
          <div class="flex items-center justify-between">
            <CardTitle class="flex items-center gap-2">
              <GanttChartSquare class="h-5 w-5" />
              GanttChart
            </CardTitle>
            <Button variant="outline" size="sm" @click="router.push('/showcase/gantt-chart')">
              View Full Demo
              <ArrowRight class="h-4 w-4 ml-1" />
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Project timeline with task bars, progress indicators, dependency arrows, milestones, and day/week/month views.
          </p>
          <GanttChart :tasks="ganttTasks" mode="week" :show-progress="true" :show-dependencies="true" />
        </CardContent>
      </Card>

      <!-- 7. SplitApp -->
      <Card>
        <CardHeader>
          <div class="flex items-center justify-between">
            <CardTitle class="flex items-center gap-2">
              <PanelLeftClose class="h-5 w-5" />
              SplitApp
            </CardTitle>
            <Button variant="outline" size="sm" @click="router.push('/showcase/split-app')">
              View Full Demo
              <ArrowRight class="h-4 w-4 ml-1" />
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Responsive master-detail layout that shows both panes on desktop and navigates between panes on narrow widths.
          </p>
          <div class="border rounded-lg overflow-hidden h-[320px]">
            <SplitApp ref="splitRef" master-width="220px">
              <template #master-header>
                <div class="flex items-center gap-2">
                  <Mail class="h-4 w-4" />
                  <span class="font-medium text-sm">Mailbox</span>
                </div>
              </template>
              <template #master="{ showDetail: _sd }">
                <div class="divide-y">
                  <button
                    v-for="item in splitItems"
                    :key="item.id"
                    class="w-full text-left px-4 py-3 hover:bg-muted/50 transition-colors"
                    :class="{ 'bg-muted': selectedItem?.id === item.id }"
                    @click="selectItem(item)"
                  >
                    <div class="flex items-center justify-between">
                      <span class="text-sm font-medium">{{ item.title }}</span>
                      <Badge v-if="item.badge" variant="secondary" class="text-xs">{{ item.badge }}</Badge>
                    </div>
                    <p class="text-xs text-muted-foreground mt-0.5">{{ item.description }}</p>
                  </button>
                </div>
              </template>
              <template #detail-header>
                <span class="font-medium text-sm">{{ selectedItem?.title || 'Details' }}</span>
              </template>
              <template #detail>
                <div v-if="selectedItem" class="p-4 space-y-2">
                  <h3 class="font-semibold">{{ selectedItem.title }}</h3>
                  <p class="text-sm text-muted-foreground">{{ selectedItem.description }}</p>
                  <p class="text-sm mt-4">
                    This is the detail view for the selected item. In a real application, this pane would show
                    full content such as email bodies, document previews, or record details.
                  </p>
                </div>
              </template>
            </SplitApp>
          </div>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
