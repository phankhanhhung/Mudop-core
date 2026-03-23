<script setup lang="ts">
import { ref } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import Timeline, { type TimelineItem } from '@/components/smart/Timeline.vue'
import { ArrowLeft, Clock, UserPlus, FileText, AlertTriangle, CheckCircle, XCircle, ShieldAlert, Settings } from 'lucide-vue-next'
import { useRouter } from 'vue-router'

const router = useRouter()

// ── Shared sample data ───────────────────────────────────────────────

const now = new Date()

function hoursAgo(h: number): string {
  return new Date(now.getTime() - h * 60 * 60 * 1000).toISOString()
}

function daysAgo(d: number): string {
  return new Date(now.getTime() - d * 24 * 60 * 60 * 1000).toISOString()
}

// ─── Demo 1: Activity Feed ──────────────────────────────────────────

const activityItems: TimelineItem[] = [
  {
    id: '1',
    title: 'User account created',
    content: 'New user John Smith registered via SSO.',
    datetime: hoursAgo(1),
    icon: UserPlus,
    type: 'success',
    author: 'System',
  },
  {
    id: '2',
    title: 'Invoice #4021 submitted for approval',
    content: 'Amount: $12,450.00 — awaiting manager review.',
    datetime: hoursAgo(3),
    icon: FileText,
    type: 'info',
    author: 'Jane Doe',
  },
  {
    id: '3',
    title: 'Payment gateway timeout',
    content: 'Stripe webhook delivery failed after 3 retries.',
    datetime: hoursAgo(5),
    icon: AlertTriangle,
    type: 'warning',
    author: 'Monitoring',
  },
  {
    id: '4',
    title: 'Deployment to production completed',
    content: 'Version 2.4.1 rolled out successfully.',
    datetime: daysAgo(1),
    icon: CheckCircle,
    type: 'success',
    author: 'CI/CD Pipeline',
  },
  {
    id: '5',
    title: 'Database migration failed',
    content: 'Migration 20260208_add_index failed on table "orders".',
    datetime: daysAgo(1),
    icon: XCircle,
    type: 'error',
    author: 'DBA Bot',
  },
  {
    id: '6',
    title: 'Security scan completed',
    content: 'No critical vulnerabilities found. 2 low-severity issues flagged.',
    datetime: daysAgo(2),
    icon: ShieldAlert,
    type: 'info',
    author: 'Security Scanner',
  },
  {
    id: '7',
    title: 'System configuration updated',
    content: 'Max upload size changed from 5MB to 25MB.',
    datetime: daysAgo(3),
    icon: Settings,
    type: 'neutral',
    author: 'Admin',
  },
]

// ─── Demo 5: Error Log ──────────────────────────────────────────────

const errorItems: TimelineItem[] = [
  {
    id: 'e1',
    title: 'NullReferenceException in OrderService',
    content: 'at OrderService.ProcessPayment() line 142',
    datetime: hoursAgo(0.5),
    type: 'error',
    author: 'Runtime',
  },
  {
    id: 'e2',
    title: 'Connection pool exhausted',
    content: 'PostgreSQL max_connections limit reached (100/100).',
    datetime: hoursAgo(2),
    type: 'error',
    author: 'Database Monitor',
  },
  {
    id: 'e3',
    title: 'Authentication service unreachable',
    content: 'OAuth2 token endpoint returned 503 for 45 seconds.',
    datetime: hoursAgo(6),
    type: 'error',
    author: 'Health Check',
  },
  {
    id: 'e4',
    title: 'Disk space critical on worker-03',
    content: '/var/log at 98% capacity. Log rotation triggered.',
    datetime: daysAgo(1),
    type: 'error',
    author: 'Infrastructure',
  },
]

// ─── Interaction tracking ────────────────────────────────────────────

const lastClicked = ref<string | null>(null)

function onItemClick(item: TimelineItem) {
  lastClicked.value = item.title
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
            <Clock class="h-6 w-6" />
            Timeline
          </h1>
          <p class="text-muted-foreground mt-1">
            Vertical activity feed with date grouping, type coloring, and relative timestamps.
          </p>
        </div>
      </div>

      <!-- Click feedback -->
      <div v-if="lastClicked" class="text-sm text-muted-foreground">
        Last clicked: <Badge variant="secondary">{{ lastClicked }}</Badge>
      </div>

      <!-- Demo 1: Activity Feed -->
      <Card>
        <CardHeader>
          <CardTitle>Activity Feed</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Mixed event types with icons, authors, and date grouping. Entries are sorted newest first by default.
          </p>
          <Timeline :items="activityItems" @item-click="onItemClick" />
        </CardContent>
      </Card>

      <!-- Demo 2: Ascending Order -->
      <Card>
        <CardHeader>
          <CardTitle>Ascending Order</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Same data sorted chronologically (oldest first) using <code class="text-xs bg-muted px-1 py-0.5 rounded">sort-order="asc"</code>.
          </p>
          <Timeline :items="activityItems" sort-order="asc" @item-click="onItemClick" />
        </CardContent>
      </Card>

      <!-- Demo 3: Without Grouping -->
      <Card>
        <CardHeader>
          <CardTitle>Without Grouping</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Flat list without date headers. Set <code class="text-xs bg-muted px-1 py-0.5 rounded">:group-by-date="false"</code>.
          </p>
          <Timeline :items="activityItems" :group-by-date="false" @item-click="onItemClick" />
        </CardContent>
      </Card>

      <!-- Demo 4: Compact (Max Items) -->
      <Card>
        <CardHeader>
          <CardTitle>Compact (Max Items)</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Limited to 3 visible entries with a "Show more" button to reveal the rest.
          </p>
          <Timeline :items="activityItems" :max-items="3" @item-click="onItemClick" />
        </CardContent>
      </Card>

      <!-- Demo 5: Error Log -->
      <Card>
        <CardHeader>
          <CardTitle>Error Log</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            All entries are error type, creating a focused red-themed error log view.
          </p>
          <Timeline :items="errorItems" @item-click="onItemClick" />
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
