<script setup lang="ts">
import { ref } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import PlanningCalendar from '@/components/smart/PlanningCalendar.vue'
import type { CalendarResource, CalendarAppointment } from '@/components/smart/PlanningCalendar.vue'
import { ArrowLeft, Calendar } from 'lucide-vue-next'
import { useRouter } from 'vue-router'

const router = useRouter()

// ── Helpers ──

function dayOffset(offset: number, hours: number, minutes = 0): string {
  const d = new Date()
  d.setDate(d.getDate() + offset)
  d.setHours(hours, minutes, 0, 0)
  return d.toISOString()
}

// ── Resources ──

const teamResources: CalendarResource[] = [
  { id: 'alice', name: 'Alice Chen', role: 'Tech Lead', avatar: 'AC' },
  { id: 'bob', name: 'Bob Martinez', role: 'Senior Dev', avatar: 'BM' },
  { id: 'carol', name: 'Carol Davis', role: 'Full Stack Dev', avatar: 'CD' },
  { id: 'dave', name: 'Dave Wilson', role: 'DevOps Engineer', avatar: 'DW' },
]

// ── Appointments spanning the week ──

const weekAppointments: CalendarAppointment[] = [
  // Monday (offset depends on current day — use fixed offsets relative to week start)
  { id: 'a1', resourceId: 'alice', title: 'Sprint Planning', start: dayOffset(0, 9, 0), end: dayOffset(0, 10, 30), type: 'info', description: 'Q1 sprint kickoff' },
  { id: 'a2', resourceId: 'alice', title: 'Architecture Review', start: dayOffset(1, 14, 0), end: dayOffset(1, 16, 0), type: 'warning', description: 'Review microservice boundaries' },
  { id: 'a3', resourceId: 'alice', title: '1:1 with PM', start: dayOffset(3, 11, 0), end: dayOffset(3, 11, 30), type: 'default' },
  { id: 'a4', resourceId: 'alice', title: 'Release Sign-off', start: dayOffset(4, 16, 0), end: dayOffset(4, 17, 0), type: 'error', description: 'v2.4 release deadline' },

  { id: 'b1', resourceId: 'bob', title: 'Code Review', start: dayOffset(0, 10, 0), end: dayOffset(0, 12, 0), type: 'success', description: 'PR #482 — auth module' },
  { id: 'b2', resourceId: 'bob', title: 'Pair Programming', start: dayOffset(1, 9, 0), end: dayOffset(1, 12, 0), type: 'info', description: 'Payment integration with Carol' },
  { id: 'b3', resourceId: 'bob', title: 'Team Standup', start: dayOffset(2, 9, 0), end: dayOffset(2, 9, 30), type: 'default' },
  { id: 'b4', resourceId: 'bob', title: 'Bug Triage', start: dayOffset(3, 14, 0), end: dayOffset(3, 15, 30), type: 'error', description: 'Critical bugs for hotfix' },

  { id: 'c1', resourceId: 'carol', title: 'Feature Dev', start: dayOffset(0, 9, 0), end: dayOffset(0, 17, 0), type: 'success', description: 'Implement checkout flow' },
  { id: 'c2', resourceId: 'carol', title: 'Pair Programming', start: dayOffset(1, 9, 0), end: dayOffset(1, 12, 0), type: 'info', description: 'Payment integration with Bob' },
  { id: 'c3', resourceId: 'carol', title: 'Testing', start: dayOffset(2, 13, 0), end: dayOffset(2, 17, 0), type: 'warning', description: 'Integration test suite' },
  { id: 'c4', resourceId: 'carol', title: 'Demo Prep', start: dayOffset(4, 10, 0), end: dayOffset(4, 12, 0), type: 'default', description: 'Sprint demo rehearsal' },

  { id: 'd1', resourceId: 'dave', title: 'CI Pipeline Fix', start: dayOffset(0, 8, 0), end: dayOffset(0, 11, 0), type: 'error', description: 'Fix flaky e2e tests' },
  { id: 'd2', resourceId: 'dave', title: 'Infra Planning', start: dayOffset(1, 13, 0), end: dayOffset(1, 15, 0), type: 'info', description: 'K8s cluster upgrade plan' },
  { id: 'd3', resourceId: 'dave', title: 'Deployment', start: dayOffset(3, 8, 0), end: dayOffset(3, 10, 0), type: 'success', description: 'Staging environment deploy' },
  { id: 'd4', resourceId: 'dave', title: 'Monitoring Setup', start: dayOffset(4, 13, 0), end: dayOffset(4, 16, 0), type: 'warning', description: 'Grafana dashboard updates' },
]

// ── Demo state ──

const selectedAppointment = ref<CalendarAppointment | null>(null)

function handleAppointmentClick(appt: CalendarAppointment) {
  selectedAppointment.value = appt
}

function formatTime(iso: string): string {
  const d = new Date(iso)
  return d.toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' })
}

function formatDate(iso: string): string {
  const d = new Date(iso)
  return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
}

const typeBadgeVariants: Record<string, string> = {
  default: 'bg-primary/20 text-primary',
  info: 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400',
  success: 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400',
  warning: 'bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-400',
  error: 'bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400',
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
            <Calendar class="h-6 w-6" />
            Planning Calendar
          </h1>
          <p class="text-muted-foreground mt-1">
            Resource scheduling calendar with appointments shown as colored blocks across time.
          </p>
        </div>
      </div>

      <!-- Week View demo -->
      <Card>
        <CardHeader>
          <CardTitle>Week View -- Sprint Team Schedule</CardTitle>
        </CardHeader>
        <CardContent class="space-y-4">
          <p class="text-sm text-muted-foreground">
            A development team's weekly schedule. Each row represents a team member with their
            appointments displayed as colored blocks. Navigate with the arrow buttons and switch
            between Day, Week, and Month views.
          </p>
          <PlanningCalendar
            :resources="teamResources"
            :appointments="weekAppointments"
            view-type="week"
            @appointment-click="handleAppointmentClick"
          />
        </CardContent>
      </Card>

      <!-- Selected appointment detail -->
      <Card v-if="selectedAppointment">
        <CardHeader>
          <CardTitle class="flex items-center gap-2">
            Selected Appointment
            <span
              :class="[
                'inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium',
                typeBadgeVariants[selectedAppointment.type || 'default'],
              ]"
            >
              {{ selectedAppointment.type || 'default' }}
            </span>
          </CardTitle>
        </CardHeader>
        <CardContent>
          <div class="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm">
            <div>
              <span class="text-muted-foreground block mb-1">Title</span>
              <span class="font-medium">{{ selectedAppointment.title }}</span>
            </div>
            <div>
              <span class="text-muted-foreground block mb-1">Resource</span>
              <span class="font-medium">
                {{ teamResources.find(r => r.id === selectedAppointment!.resourceId)?.name }}
              </span>
            </div>
            <div>
              <span class="text-muted-foreground block mb-1">Start</span>
              <span class="font-medium">{{ formatDate(selectedAppointment.start) }} {{ formatTime(selectedAppointment.start) }}</span>
            </div>
            <div>
              <span class="text-muted-foreground block mb-1">End</span>
              <span class="font-medium">{{ formatDate(selectedAppointment.end) }} {{ formatTime(selectedAppointment.end) }}</span>
            </div>
            <div v-if="selectedAppointment.description" class="col-span-2 md:col-span-4">
              <span class="text-muted-foreground block mb-1">Description</span>
              <span class="font-medium">{{ selectedAppointment.description }}</span>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Day View demo -->
      <Card>
        <CardHeader>
          <CardTitle>Day View -- Hourly Timeline</CardTitle>
        </CardHeader>
        <CardContent class="space-y-4">
          <p class="text-sm text-muted-foreground">
            Zoomed into a single day with hourly columns. Ideal for detailed daily scheduling.
            The red line indicates the current time.
          </p>
          <PlanningCalendar
            :resources="teamResources"
            :appointments="weekAppointments"
            view-type="day"
            :hour-start="8"
            :hour-end="18"
          />
        </CardContent>
      </Card>

      <!-- Month View demo -->
      <Card>
        <CardHeader>
          <CardTitle>Month View -- Compact Overview</CardTitle>
        </CardHeader>
        <CardContent class="space-y-4">
          <p class="text-sm text-muted-foreground">
            A compact month-level overview showing appointment density across the entire month.
            Useful for high-level capacity planning.
          </p>
          <PlanningCalendar
            :resources="teamResources"
            :appointments="weekAppointments"
            view-type="month"
          />
        </CardContent>
      </Card>

      <!-- Typed appointments demo -->
      <Card>
        <CardHeader>
          <CardTitle>Appointment Types</CardTitle>
        </CardHeader>
        <CardContent>
          <div class="grid grid-cols-1 md:grid-cols-2 gap-3 text-sm">
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5">Default</Badge>
              <span>Standard appointments using the primary theme color</span>
            </div>
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5 border-blue-500 text-blue-500">Info</Badge>
              <span>Informational events like meetings and planning sessions</span>
            </div>
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5 border-green-500 text-green-500">Success</Badge>
              <span>Completed items or positive activities like code reviews</span>
            </div>
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5 border-amber-500 text-amber-500">Warning</Badge>
              <span>Items needing attention such as architecture reviews or testing</span>
            </div>
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5 border-red-500 text-red-500">Error</Badge>
              <span>Critical deadlines, bug triage, and blockers</span>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Features list -->
      <Card>
        <CardHeader>
          <CardTitle>Features</CardTitle>
        </CardHeader>
        <CardContent>
          <div class="grid grid-cols-1 md:grid-cols-2 gap-3 text-sm">
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5">Views</Badge>
              <span>Day / Week / Month view modes with appropriate column scaling</span>
            </div>
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5">Resources</Badge>
              <span>Row-per-resource layout with avatar, name, and role display</span>
            </div>
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5">Appointments</Badge>
              <span>Colored horizontal blocks spanning their exact time range</span>
            </div>
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5">Types</Badge>
              <span>Five appointment types: default, info, success, warning, error</span>
            </div>
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5">Navigation</Badge>
              <span>Previous / Next / Today buttons for temporal navigation</span>
            </div>
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5">Now</Badge>
              <span>Current time indicator (red line) in day and week views</span>
            </div>
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5">Click</Badge>
              <span>Appointment click events for detail panels and interactions</span>
            </div>
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="mt-0.5">Responsive</Badge>
              <span>Horizontal scroll when columns overflow the container width</span>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
