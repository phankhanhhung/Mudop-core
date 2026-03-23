<script setup lang="ts">
import { ref, computed } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import IconTabBar from '@/components/smart/IconTabBar.vue'
import type { IconTab } from '@/composables/useIconTabBar'
import { ArrowLeft, Layers } from 'lucide-vue-next'
import { useRouter } from 'vue-router'

const router = useRouter()

// ─── Demo 1: Navigation mode ─────────────────────────────────────────────

const navActiveTab = ref('orders')

const navigationTabs: IconTab[] = [
  { key: 'orders', label: 'Orders', icon: 'ShoppingCart', count: 24 },
  { key: 'products', label: 'Products', icon: 'Package', count: 156 },
  { key: 'customers', label: 'Customers', icon: 'Users', count: 89 },
  { key: 'analytics', label: 'Analytics', icon: 'BarChart3' },
]

// ─── Demo 2: Filter mode ─────────────────────────────────────────────────

const filterActiveTab = ref('__all__')

const filterTabs: IconTab[] = [
  { key: 'active', label: 'Active', icon: 'CheckCircle', count: 42, semanticColor: 'positive' },
  { key: 'pending', label: 'Pending', icon: 'Clock', count: 8, semanticColor: 'critical' },
  { key: 'failed', label: 'Failed', icon: 'AlertCircle', count: 3, semanticColor: 'negative' },
  { key: 'draft', label: 'Draft', icon: 'FileText', count: 15, semanticColor: 'neutral' },
]

// ─── Demo 3: Semantic colors ─────────────────────────────────────────────

const semanticActiveTab = ref('success')

const semanticTabs: IconTab[] = [
  { key: 'success', label: 'Success', icon: 'CheckCircle', count: 120, semanticColor: 'positive' },
  { key: 'warning', label: 'Warning', icon: 'AlertCircle', count: 15, semanticColor: 'critical' },
  { key: 'error', label: 'Error', icon: 'Shield', count: 4, semanticColor: 'negative' },
  { key: 'info', label: 'Info', icon: 'Bell', count: 38, semanticColor: 'neutral' },
]

// ─── Demo 4: Overflow ────────────────────────────────────────────────────

const overflowActiveTab = ref('tab-1')

const overflowTabs: IconTab[] = [
  { key: 'tab-1', label: 'Home', icon: 'Home' },
  { key: 'tab-2', label: 'Products', icon: 'Package', count: 45 },
  { key: 'tab-3', label: 'Orders', icon: 'ShoppingCart', count: 12 },
  { key: 'tab-4', label: 'Customers', icon: 'Users', count: 89 },
  { key: 'tab-5', label: 'Shipping', icon: 'Truck', count: 7 },
  { key: 'tab-6', label: 'Payments', icon: 'CreditCard', count: 34 },
  { key: 'tab-7', label: 'Reports', icon: 'BarChart3' },
  { key: 'tab-8', label: 'Settings', icon: 'Settings' },
  { key: 'tab-9', label: 'Security', icon: 'Shield' },
  { key: 'tab-10', label: 'Database', icon: 'Database' },
  { key: 'tab-11', label: 'Mail', icon: 'Mail', count: 5 },
  { key: 'tab-12', label: 'Global', icon: 'Globe' },
]

// ─── Demo 5: Dense mode ──────────────────────────────────────────────────

const denseToggle = ref(false)
const denseActiveTab = ref('inbox')

const denseTabs: IconTab[] = [
  { key: 'inbox', label: 'Inbox', icon: 'Mail', count: 23 },
  { key: 'starred', label: 'Starred', icon: 'Star', count: 5 },
  { key: 'favorites', label: 'Favorites', icon: 'Heart', count: 12 },
  { key: 'alerts', label: 'Alerts', icon: 'Bell', count: 3, semanticColor: 'critical' },
]

// ─── Controls ─────────────────────────────────────────────────────────────

const controlMode = ref<'navigation' | 'filter'>('navigation')
const controlOverflow = ref(true)
const controlActiveTab = ref('tab-a')

const controlTabs = computed<IconTab[]>(() => [
  { key: 'tab-a', label: 'First', icon: 'Home', count: 10 },
  { key: 'tab-b', label: 'Second', icon: 'Package', count: 25 },
  { key: 'tab-c', label: 'Third', icon: 'Users', count: 8 },
  { key: 'tab-d', label: 'Fourth', icon: 'Settings' },
])
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
            IconTabBar
          </h1>
          <p class="text-muted-foreground mt-1">
            Tabbed navigation with icons, counts, semantic colors, and responsive overflow.
          </p>
        </div>
      </div>

      <!-- Demo 1: Navigation mode -->
      <Card>
        <CardHeader>
          <CardTitle>Navigation Mode</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Standard tab navigation with icons and optional counts. Each tab shows its own content panel.
          </p>
          <IconTabBar
            v-model="navActiveTab"
            :tabs="navigationTabs"
            mode="navigation"
            :enable-overflow="false"
          >
            <template #tab-orders>
              <Card>
                <CardContent class="pt-6">
                  <p class="text-sm">
                    Displaying <strong>24 orders</strong> across all statuses.
                    Use this panel to manage and track customer orders.
                  </p>
                </CardContent>
              </Card>
            </template>
            <template #tab-products>
              <Card>
                <CardContent class="pt-6">
                  <p class="text-sm">
                    Product catalog with <strong>156 items</strong>.
                    Browse inventory, update pricing, and manage stock levels.
                  </p>
                </CardContent>
              </Card>
            </template>
            <template #tab-customers>
              <Card>
                <CardContent class="pt-6">
                  <p class="text-sm">
                    Customer directory containing <strong>89 records</strong>.
                    View contact details, purchase history, and account status.
                  </p>
                </CardContent>
              </Card>
            </template>
            <template #tab-analytics>
              <Card>
                <CardContent class="pt-6">
                  <p class="text-sm">
                    Business analytics dashboard. View revenue trends, conversion rates,
                    and performance metrics.
                  </p>
                </CardContent>
              </Card>
            </template>
          </IconTabBar>
        </CardContent>
      </Card>

      <!-- Demo 2: Filter mode -->
      <Card>
        <CardHeader>
          <CardTitle>Filter Mode</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Filter mode prepends an "All" tab with a total count. Selecting "All" shows content from every tab.
            Semantic colors indicate status severity.
          </p>
          <IconTabBar
            v-model="filterActiveTab"
            :tabs="filterTabs"
            mode="filter"
            :enable-overflow="false"
          >
            <template #tab-active>
              <div class="rounded-lg border border-emerald-200 bg-emerald-50 dark:border-emerald-800 dark:bg-emerald-950 p-4 mb-3">
                <p class="text-sm font-medium text-emerald-800 dark:text-emerald-200">
                  42 active items are currently being processed.
                </p>
              </div>
            </template>
            <template #tab-pending>
              <div class="rounded-lg border border-amber-200 bg-amber-50 dark:border-amber-800 dark:bg-amber-950 p-4 mb-3">
                <p class="text-sm font-medium text-amber-800 dark:text-amber-200">
                  8 items are pending review and require attention.
                </p>
              </div>
            </template>
            <template #tab-failed>
              <div class="rounded-lg border border-red-200 bg-red-50 dark:border-red-800 dark:bg-red-950 p-4 mb-3">
                <p class="text-sm font-medium text-red-800 dark:text-red-200">
                  3 items have failed processing and need investigation.
                </p>
              </div>
            </template>
            <template #tab-draft>
              <div class="rounded-lg border bg-muted/50 p-4 mb-3">
                <p class="text-sm font-medium text-muted-foreground">
                  15 draft items are saved but not yet submitted.
                </p>
              </div>
            </template>
          </IconTabBar>
        </CardContent>
      </Card>

      <!-- Demo 3: Semantic colors -->
      <Card>
        <CardHeader>
          <CardTitle>Semantic Colors</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Each tab can carry a semantic color indicator: positive (green), critical (amber),
            negative (red), or neutral (gray). The color appears as a bar below the tab label.
          </p>
          <IconTabBar
            v-model="semanticActiveTab"
            :tabs="semanticTabs"
            mode="navigation"
            :enable-overflow="false"
          >
            <template #tab-success>
              <div class="flex items-center gap-2 py-2">
                <span class="inline-block h-3 w-3 rounded-full bg-emerald-500" />
                <span class="text-sm"><strong>Positive</strong> indicates healthy or successful states.</span>
              </div>
            </template>
            <template #tab-warning>
              <div class="flex items-center gap-2 py-2">
                <span class="inline-block h-3 w-3 rounded-full bg-amber-500" />
                <span class="text-sm"><strong>Critical</strong> indicates items that require attention.</span>
              </div>
            </template>
            <template #tab-error>
              <div class="flex items-center gap-2 py-2">
                <span class="inline-block h-3 w-3 rounded-full bg-destructive" />
                <span class="text-sm"><strong>Negative</strong> indicates errors or failures requiring immediate action.</span>
              </div>
            </template>
            <template #tab-info>
              <div class="flex items-center gap-2 py-2">
                <span class="inline-block h-3 w-3 rounded-full bg-muted-foreground" />
                <span class="text-sm"><strong>Neutral</strong> indicates informational or default states.</span>
              </div>
            </template>
          </IconTabBar>
        </CardContent>
      </Card>

      <!-- Demo 4: Overflow -->
      <Card>
        <CardHeader>
          <CardTitle>Responsive Overflow</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            When tabs exceed the container width, extra tabs collapse into a "More" dropdown.
            The container below is constrained to 500px to demonstrate overflow behavior.
          </p>
          <div class="max-w-[500px] border rounded-lg">
            <IconTabBar
              v-model="overflowActiveTab"
              :tabs="overflowTabs"
              mode="navigation"
              :enable-overflow="true"
            >
              <template v-for="tab in overflowTabs" :key="tab.key" #[`tab-${tab.key}`]>
                <div class="p-2 text-sm text-muted-foreground">
                  Content for <strong>{{ tab.label }}</strong> tab.
                </div>
              </template>
            </IconTabBar>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 5: Dense mode -->
      <Card>
        <CardHeader>
          <CardTitle>Dense Mode</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Dense mode reduces padding for compact layouts. Toggle between normal and dense sizing.
          </p>
          <div class="flex items-center gap-2 mb-4">
            <Button
              :variant="denseToggle ? 'default' : 'outline'"
              size="sm"
              @click="denseToggle = !denseToggle"
            >
              {{ denseToggle ? 'Dense: ON' : 'Dense: OFF' }}
            </Button>
          </div>
          <IconTabBar
            v-model="denseActiveTab"
            :tabs="denseTabs"
            mode="navigation"
            :enable-overflow="false"
            :dense="denseToggle"
          >
            <template #tab-inbox>
              <p class="text-sm">23 unread messages in your inbox.</p>
            </template>
            <template #tab-starred>
              <p class="text-sm">5 starred conversations for quick access.</p>
            </template>
            <template #tab-favorites>
              <p class="text-sm">12 favorite contacts and threads.</p>
            </template>
            <template #tab-alerts>
              <p class="text-sm">3 alerts requiring your attention.</p>
            </template>
          </IconTabBar>
        </CardContent>
      </Card>

      <!-- Interactive controls -->
      <Card>
        <CardHeader>
          <CardTitle>Interactive Controls</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Toggle properties interactively to observe how the component responds.
          </p>
          <div class="flex flex-wrap items-center gap-3 mb-6">
            <Button
              :variant="controlMode === 'navigation' ? 'default' : 'outline'"
              size="sm"
              @click="controlMode = 'navigation'"
            >
              Navigation
            </Button>
            <Button
              :variant="controlMode === 'filter' ? 'default' : 'outline'"
              size="sm"
              @click="controlMode = 'filter'"
            >
              Filter
            </Button>
            <span class="text-muted-foreground">|</span>
            <Button
              :variant="controlOverflow ? 'default' : 'outline'"
              size="sm"
              @click="controlOverflow = !controlOverflow"
            >
              Overflow: {{ controlOverflow ? 'ON' : 'OFF' }}
            </Button>
          </div>

          <div class="flex items-center gap-2 mb-4">
            <span class="text-sm text-muted-foreground">Active tab:</span>
            <Badge variant="secondary">{{ controlActiveTab }}</Badge>
          </div>

          <IconTabBar
            v-model="controlActiveTab"
            :tabs="controlTabs"
            :mode="controlMode"
            :enable-overflow="controlOverflow"
          >
            <template #tab-tab-a>
              <Card>
                <CardContent class="pt-6">
                  <p class="text-sm">Content panel for the First tab.</p>
                </CardContent>
              </Card>
            </template>
            <template #tab-tab-b>
              <Card>
                <CardContent class="pt-6">
                  <p class="text-sm">Content panel for the Second tab.</p>
                </CardContent>
              </Card>
            </template>
            <template #tab-tab-c>
              <Card>
                <CardContent class="pt-6">
                  <p class="text-sm">Content panel for the Third tab.</p>
                </CardContent>
              </Card>
            </template>
            <template #tab-tab-d>
              <Card>
                <CardContent class="pt-6">
                  <p class="text-sm">Content panel for the Fourth tab.</p>
                </CardContent>
              </Card>
            </template>
          </IconTabBar>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
