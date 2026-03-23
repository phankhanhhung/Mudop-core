<script setup lang="ts">
import { ref } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import TokenInput from '@/components/smart/TokenInput.vue'
import Timeline from '@/components/smart/Timeline.vue'
import type { TimelineItem } from '@/components/smart/Timeline.vue'
import OverflowToolbar from '@/components/smart/OverflowToolbar.vue'
import type { ToolbarItem } from '@/components/smart/OverflowToolbar.vue'
import DynamicSideContent from '@/components/smart/DynamicSideContent.vue'
import PlanningCalendar from '@/components/smart/PlanningCalendar.vue'
import type { CalendarResource, CalendarAppointment } from '@/components/smart/PlanningCalendar.vue'
import FeedList from '@/components/smart/FeedList.vue'
import type { FeedItem } from '@/components/smart/FeedList.vue'
import InfoLabel from '@/components/smart/InfoLabel.vue'
import {
  ArrowLeft,
  Layers,
  ArrowRight,
  Bold,
  Italic,
  Underline,
  AlignLeft,
  AlignCenter,
  AlignRight,
  Copy,
  Scissors,
  Clipboard,
  Undo,
  Redo,
  Save,
  Printer,
  TrendingUp,
  TrendingDown,
  AlertTriangle,
  CheckCircle,
  Info,
  Zap,
  Shield,
  DollarSign,
} from 'lucide-vue-next'
import { useRouter } from 'vue-router'

const router = useRouter()

// ── 1. TokenInput ──────────────────────────────────────────────────────

const tokens = ref(['Vue', 'TypeScript', 'Tailwind'])

// ── 2. Timeline ────────────────────────────────────────────────────────

const now = new Date()

function hoursAgo(h: number): string {
  const d = new Date(now.getTime() - h * 3600_000)
  return d.toISOString()
}

const timelineItems: TimelineItem[] = [
  {
    id: '1',
    title: 'Deployment succeeded',
    content: 'Production v2.4.0 rolled out to all regions.',
    datetime: hoursAgo(1),
    type: 'success',
    author: 'CI Pipeline',
  },
  {
    id: '2',
    title: 'High memory alert',
    content: 'Node pool memory usage exceeded 85% threshold.',
    datetime: hoursAgo(3),
    type: 'warning',
    author: 'Monitoring',
  },
  {
    id: '3',
    title: 'Pull request merged',
    content: 'PR #472 — Refactor authentication middleware.',
    datetime: hoursAgo(5),
    type: 'info',
    author: 'Sarah Chen',
  },
  {
    id: '4',
    title: 'Build failed',
    content: 'Unit tests failed on feature/calendar-view branch.',
    datetime: hoursAgo(8),
    type: 'error',
    author: 'CI Pipeline',
  },
  {
    id: '5',
    title: 'New team member added',
    content: 'Alex Kim joined the Engineering team.',
    datetime: hoursAgo(24),
    type: 'neutral',
    author: 'Admin',
  },
]

// ── 3. OverflowToolbar ─────────────────────────────────────────────────

const toolbarItems: ToolbarItem[] = [
  { id: 'bold', label: 'Bold', icon: Bold, priority: 10 },
  { id: 'italic', label: 'Italic', icon: Italic, priority: 10 },
  { id: 'underline', label: 'Underline', icon: Underline, priority: 9 },
  { id: 'sep1', label: '', separator: true },
  { id: 'align-left', label: 'Left', icon: AlignLeft, priority: 7 },
  { id: 'align-center', label: 'Center', icon: AlignCenter, priority: 6 },
  { id: 'align-right', label: 'Right', icon: AlignRight, priority: 5 },
  { id: 'sep2', label: '', separator: true },
  { id: 'copy', label: 'Copy', icon: Copy, priority: 4 },
  { id: 'cut', label: 'Cut', icon: Scissors, priority: 3 },
  { id: 'paste', label: 'Paste', icon: Clipboard, priority: 3 },
  { id: 'sep3', label: '', separator: true },
  { id: 'undo', label: 'Undo', icon: Undo, priority: 2 },
  { id: 'redo', label: 'Redo', icon: Redo, priority: 2 },
  { id: 'save', label: 'Save', icon: Save, priority: 1 },
  { id: 'print', label: 'Print', icon: Printer, priority: 0 },
]

const toolbarClicked = ref('')

// ── 4. DynamicSideContent ──────────────────────────────────────────────

const sideVisible = ref(true)

// ── 5. PlanningCalendar ────────────────────────────────────────────────

function weekday(dayOffset: number, hour: number, minute = 0): string {
  const d = new Date(now)
  const dayOfWeek = d.getDay()
  const monday = new Date(d)
  monday.setDate(d.getDate() - dayOfWeek + 1)
  monday.setHours(hour, minute, 0, 0)
  monday.setDate(monday.getDate() + dayOffset)
  return monday.toISOString()
}

const calResources: CalendarResource[] = [
  { id: 'r1', name: 'Alice Martin', role: 'Lead Engineer' },
  { id: 'r2', name: 'Bob Johnson', role: 'Designer' },
  { id: 'r3', name: 'Carol White', role: 'Product Manager' },
]

const calAppointments: CalendarAppointment[] = [
  { id: 'a1', resourceId: 'r1', title: 'Sprint Planning', start: weekday(0, 9), end: weekday(0, 11), type: 'info' },
  { id: 'a2', resourceId: 'r1', title: 'Code Review', start: weekday(2, 14), end: weekday(2, 16), type: 'default' },
  { id: 'a3', resourceId: 'r2', title: 'Design Workshop', start: weekday(1, 10), end: weekday(1, 12), type: 'success' },
  { id: 'a4', resourceId: 'r2', title: 'Prototype Review', start: weekday(3, 13), end: weekday(3, 15), type: 'warning' },
  { id: 'a5', resourceId: 'r3', title: 'Stakeholder Sync', start: weekday(0, 14), end: weekday(0, 15), type: 'error' },
  { id: 'a6', resourceId: 'r3', title: 'Roadmap Update', start: weekday(4, 9), end: weekday(4, 11), type: 'info' },
]

// ── 6. FeedList ────────────────────────────────────────────────────────

const feedItems = ref<FeedItem[]>([
  {
    id: 'f1',
    author: 'Jane Doe',
    datetime: hoursAgo(2),
    content: 'Just shipped the new dashboard layout. Feedback welcome!',
    liked: true,
    likeCount: 5,
    replyCount: 2,
  },
  {
    id: 'f2',
    author: 'Mark Lee',
    datetime: hoursAgo(6),
    content: 'Found an edge case in the date picker component when crossing timezone boundaries. Created a ticket.',
    liked: false,
    likeCount: 3,
    replyCount: 0,
  },
  {
    id: 'f3',
    author: 'You',
    datetime: hoursAgo(12),
    content: 'Updated the contribution guide with new testing standards. Please review when you get a chance.',
    liked: false,
    likeCount: 1,
    replyCount: 1,
  },
])

function handleFeedPost(payload: { content: string }) {
  feedItems.value.unshift({
    id: `f${Date.now()}`,
    author: 'You',
    datetime: new Date().toISOString(),
    content: payload.content,
    liked: false,
    likeCount: 0,
    replyCount: 0,
  })
}

function handleFeedLike(payload: { itemId: string; liked: boolean }) {
  const item = feedItems.value.find(i => i.id === payload.itemId)
  if (item) {
    item.liked = payload.liked
    item.likeCount = (item.likeCount ?? 0) + (payload.liked ? 1 : -1)
  }
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
            Phase G — Enterprise Components
          </h1>
          <p class="text-muted-foreground mt-1">
            Seven smart components for enterprise workflows: tagging, activity feeds, calendars, and more.
          </p>
        </div>
      </div>

      <!-- 1. TokenInput -->
      <Card>
        <CardHeader>
          <div class="flex items-center justify-between">
            <CardTitle>TokenInput</CardTitle>
            <Button variant="ghost" size="sm" @click="router.push('/showcase/token-input')">
              View Full Demo
              <ArrowRight class="h-4 w-4 ml-1" />
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Tag input with keyboard navigation, duplicate prevention, and remove-on-backspace.
            Type a value and press Enter to add.
          </p>
          <div class="max-w-md space-y-2">
            <TokenInput
              v-model="tokens"
              label="Skills"
              placeholder="Add a skill..."
            />
            <p class="text-xs text-muted-foreground">
              Current: <Badge v-for="t in tokens" :key="t" variant="outline" class="mr-1">{{ t }}</Badge>
            </p>
          </div>
        </CardContent>
      </Card>

      <!-- 2. Timeline -->
      <Card>
        <CardHeader>
          <div class="flex items-center justify-between">
            <CardTitle>Timeline</CardTitle>
            <Button variant="ghost" size="sm" @click="router.push('/showcase/timeline')">
              View Full Demo
              <ArrowRight class="h-4 w-4 ml-1" />
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Chronological activity feed with date grouping, type-colored icons, and relative timestamps.
          </p>
          <Timeline :items="timelineItems" :group-by-date="true" sort-order="desc" />
        </CardContent>
      </Card>

      <!-- 3. OverflowToolbar -->
      <Card>
        <CardHeader>
          <div class="flex items-center justify-between">
            <CardTitle>OverflowToolbar</CardTitle>
            <Button variant="ghost" size="sm" @click="router.push('/showcase/overflow-toolbar')">
              View Full Demo
              <ArrowRight class="h-4 w-4 ml-1" />
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Responsive toolbar that moves low-priority items into a popover menu when space is limited.
            Resize the browser to see items collapse into the overflow menu.
          </p>
          <div class="border rounded-lg p-3 max-w-lg">
            <OverflowToolbar
              :items="toolbarItems"
              @item-click="toolbarClicked = $event"
            />
          </div>
          <p v-if="toolbarClicked" class="text-xs text-muted-foreground mt-2">
            Last clicked: <Badge variant="secondary">{{ toolbarClicked }}</Badge>
          </p>
        </CardContent>
      </Card>

      <!-- 4. DynamicSideContent -->
      <Card>
        <CardHeader>
          <div class="flex items-center justify-between">
            <CardTitle>DynamicSideContent</CardTitle>
            <Button variant="ghost" size="sm" @click="router.push('/showcase/dynamic-side-content')">
              View Full Demo
              <ArrowRight class="h-4 w-4 ml-1" />
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Responsive master-detail layout with a collapsible side panel that stacks on narrow containers.
          </p>
          <div class="border rounded-lg p-4">
            <DynamicSideContent
              v-model:side-content-visible="sideVisible"
              side-content-position="end"
              :breakpoint="500"
            >
              <div class="space-y-3">
                <h3 class="font-semibold text-sm">Main Content</h3>
                <p class="text-sm text-muted-foreground">
                  This is the primary content area. It expands to full width when the side panel is hidden.
                  The layout is fully responsive and adapts to the container width.
                </p>
                <div class="flex gap-2">
                  <Badge>Status: Active</Badge>
                  <Badge variant="outline">Priority: High</Badge>
                </div>
              </div>
              <template #side>
                <div class="space-y-3">
                  <h3 class="font-semibold text-sm">Side Panel</h3>
                  <p class="text-sm text-muted-foreground">
                    Supplementary details, filters, or contextual information.
                  </p>
                  <ul class="text-sm space-y-1 text-muted-foreground">
                    <li>Created: Jan 15, 2026</li>
                    <li>Updated: Feb 08, 2026</li>
                    <li>Owner: Engineering</li>
                  </ul>
                </div>
              </template>
            </DynamicSideContent>
          </div>
        </CardContent>
      </Card>

      <!-- 5. PlanningCalendar -->
      <Card>
        <CardHeader>
          <div class="flex items-center justify-between">
            <CardTitle>PlanningCalendar</CardTitle>
            <Button variant="ghost" size="sm" @click="router.push('/showcase/planning-calendar')">
              View Full Demo
              <ArrowRight class="h-4 w-4 ml-1" />
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Resource-based calendar with day/week/month views, appointment positioning, and a current-time indicator.
          </p>
          <PlanningCalendar
            :resources="calResources"
            :appointments="calAppointments"
            view-type="week"
            :hour-start="8"
            :hour-end="18"
          />
        </CardContent>
      </Card>

      <!-- 6. FeedList -->
      <Card>
        <CardHeader>
          <div class="flex items-center justify-between">
            <CardTitle>FeedList</CardTitle>
            <Button variant="ghost" size="sm" @click="router.push('/showcase/feed-list')">
              View Full Demo
              <ArrowRight class="h-4 w-4 ml-1" />
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Social-style feed with comment input, likes, replies, and relative time formatting.
            Post a new comment using Ctrl+Enter or the Post button.
          </p>
          <div class="max-w-xl">
            <FeedList
              :items="feedItems"
              :show-input="true"
              current-user="You"
              input-placeholder="Share an update..."
              @post="handleFeedPost"
              @like="handleFeedLike"
            />
          </div>
        </CardContent>
      </Card>

      <!-- 7. InfoLabel -->
      <Card>
        <CardHeader>
          <div class="flex items-center justify-between">
            <CardTitle>InfoLabel</CardTitle>
            <Button variant="ghost" size="sm" @click="router.push('/showcase/info-label')">
              View Full Demo
              <ArrowRight class="h-4 w-4 ml-1" />
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Compact status labels with 10 color schemes, filled/outlined modes, and optional icons.
          </p>
          <div class="flex flex-wrap gap-2">
            <InfoLabel text="Revenue Up" :color-scheme="3" :icon="TrendingUp" display-only />
            <InfoLabel text="Costs Down" :color-scheme="2" :icon="TrendingDown" display-only />
            <InfoLabel text="At Risk" :color-scheme="5" :icon="AlertTriangle" display-only />
            <InfoLabel text="Approved" :color-scheme="3" render-mode="outlined" :icon="CheckCircle" display-only />
            <InfoLabel text="Pending" :color-scheme="4" render-mode="outlined" :icon="Info" display-only />
            <InfoLabel text="Performance" :color-scheme="6" :icon="Zap" display-only />
            <InfoLabel text="Secure" :color-scheme="8" :icon="Shield" display-only />
            <InfoLabel text="$12.4k" :color-scheme="9" :icon="DollarSign" display-only />
          </div>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
