<script setup lang="ts">
import { ref, computed } from 'vue'
import { RouterLink } from 'vue-router'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import ResponsiveTable from '@/components/smart/ResponsiveTable.vue'
import {
  type ResponsiveColumnConfig,
  type Breakpoint,
  DEFAULT_BREAKPOINTS,
} from '@/composables/useResponsiveTable'
import {
  ArrowLeft,
  Monitor,
  Tablet,
  Smartphone,
  Expand,
  Shrink,
  Columns3,
} from 'lucide-vue-next'

// ── Column definitions ──

const columns: ResponsiveColumnConfig[] = [
  { key: 'Name', label: 'Name', importance: 'high' },
  { key: 'Email', label: 'Email', importance: 'high' },
  { key: 'Department', label: 'Department', importance: 'medium' },
  { key: 'Title', label: 'Title', importance: 'medium' },
  { key: 'Salary', label: 'Salary', importance: 'low' },
  { key: 'Location', label: 'Location', importance: 'low' },
  { key: 'HireDate', label: 'Hire Date', importance: 'low' },
  { key: 'Id', label: 'ID', importance: 'low' },
]

// ── Sample employee data ──

const sampleData: Record<string, unknown>[] = [
  { Id: 'E001', Name: 'Sarah Chen', Email: 'sarah.chen@acme.com', Department: 'Engineering', Title: 'Sr. Software Engineer', Salary: '$145,000', Location: 'San Francisco, CA', HireDate: '2021-03-15' },
  { Id: 'E002', Name: 'Marcus Rivera', Email: 'marcus.r@acme.com', Department: 'Product', Title: 'Product Manager', Salary: '$135,000', Location: 'New York, NY', HireDate: '2020-07-22' },
  { Id: 'E003', Name: 'Anika Patel', Email: 'anika.patel@acme.com', Department: 'Engineering', Title: 'Staff Engineer', Salary: '$172,000', Location: 'Seattle, WA', HireDate: '2019-01-10' },
  { Id: 'E004', Name: 'James Wilson', Email: 'j.wilson@acme.com', Department: 'Design', Title: 'UX Lead', Salary: '$128,000', Location: 'Austin, TX', HireDate: '2022-05-03' },
  { Id: 'E005', Name: 'Laura Kim', Email: 'laura.kim@acme.com', Department: 'Marketing', Title: 'Marketing Director', Salary: '$152,000', Location: 'Chicago, IL', HireDate: '2018-11-28' },
  { Id: 'E006', Name: 'David Okafor', Email: 'd.okafor@acme.com', Department: 'Engineering', Title: 'DevOps Engineer', Salary: '$138,000', Location: 'Denver, CO', HireDate: '2023-02-14' },
  { Id: 'E007', Name: 'Emily Tran', Email: 'emily.tran@acme.com', Department: 'Finance', Title: 'Financial Analyst', Salary: '$98,000', Location: 'Boston, MA', HireDate: '2022-09-01' },
  { Id: 'E008', Name: 'Carlos Mendez', Email: 'carlos.m@acme.com', Department: 'Sales', Title: 'Account Executive', Salary: '$115,000', Location: 'Miami, FL', HireDate: '2021-06-17' },
  { Id: 'E009', Name: 'Priya Sharma', Email: 'priya.s@acme.com', Department: 'Data Science', Title: 'ML Engineer', Salary: '$155,000', Location: 'San Francisco, CA', HireDate: '2020-04-08' },
  { Id: 'E010', Name: 'Thomas Mueller', Email: 't.mueller@acme.com', Department: 'Engineering', Title: 'Backend Engineer', Salary: '$132,000', Location: 'Portland, OR', HireDate: '2023-08-21' },
]

// ── Demo controls ──

const tableRef = ref<InstanceType<typeof ResponsiveTable> | null>(null)
const useCustomBreakpoints = ref(false)

const customBreakpoints: Breakpoint[] = [
  { name: 'compact', minWidth: 0, visibleImportance: ['high'] },
  { name: 'standard', minWidth: 600, visibleImportance: ['high', 'medium'] },
  { name: 'wide', minWidth: 900, visibleImportance: ['high', 'medium', 'low'] },
]

const activeBreakpoints = computed(() =>
  useCustomBreakpoints.value ? customBreakpoints : DEFAULT_BREAKPOINTS,
)

const breakpointIcon = computed(() => {
  const bp = tableRef.value?.currentBreakpoint
  if (bp === 'mobile' || bp === 'compact') return Smartphone
  if (bp === 'tablet' || bp === 'standard') return Tablet
  return Monitor
})

const breakpointLabel = computed(() => tableRef.value?.currentBreakpoint ?? 'unknown')
const widthLabel = computed(() => `${tableRef.value?.containerWidth ?? 0}px`)

function handleExpandAll() {
  tableRef.value?.expandAll()
}

function handleCollapseAll() {
  tableRef.value?.collapseAll()
}
</script>

<template>
  <DefaultLayout>
    <div class="space-y-6">
      <!-- Page header -->
      <div class="flex items-center gap-4">
        <RouterLink to="/showcase">
          <Button variant="ghost" size="sm">
            <ArrowLeft class="h-4 w-4 mr-1" />
            Back to Showcase
          </Button>
        </RouterLink>
        <div class="flex items-center gap-3">
          <div class="flex items-center justify-center h-10 w-10 rounded-lg bg-primary/10">
            <Columns3 class="h-5 w-5 text-primary" />
          </div>
          <div>
            <h1 class="text-2xl font-bold text-foreground">Responsive Table Popin</h1>
            <p class="text-sm text-muted-foreground">
              Columns collapse into expandable detail rows on smaller viewports
            </p>
          </div>
        </div>
      </div>

      <!-- Controls -->
      <Card>
        <CardHeader>
          <CardTitle class="text-base">Controls</CardTitle>
        </CardHeader>
        <CardContent>
          <div class="flex flex-wrap items-center gap-4">
            <!-- Breakpoint indicator -->
            <div class="flex items-center gap-2">
              <span class="text-sm font-medium text-muted-foreground">Breakpoint:</span>
              <Badge variant="secondary" class="gap-1.5">
                <component :is="breakpointIcon" class="h-3 w-3" />
                {{ breakpointLabel }}
              </Badge>
              <span class="text-xs text-muted-foreground">{{ widthLabel }}</span>
            </div>

            <div class="h-6 w-px bg-border" />

            <!-- Expand / Collapse -->
            <div class="flex items-center gap-2">
              <Button variant="outline" size="sm" class="gap-1.5" @click="handleExpandAll">
                <Expand class="h-3.5 w-3.5" />
                Expand All
              </Button>
              <Button variant="outline" size="sm" class="gap-1.5" @click="handleCollapseAll">
                <Shrink class="h-3.5 w-3.5" />
                Collapse All
              </Button>
            </div>

            <div class="h-6 w-px bg-border" />

            <!-- Custom breakpoints toggle -->
            <div class="flex items-center gap-2">
              <span class="text-sm font-medium text-muted-foreground">Breakpoints:</span>
              <Button
                :variant="useCustomBreakpoints ? 'outline' : 'default'"
                size="sm"
                @click="useCustomBreakpoints = false"
              >
                Default
              </Button>
              <Button
                :variant="useCustomBreakpoints ? 'default' : 'outline'"
                size="sm"
                @click="useCustomBreakpoints = true"
              >
                Custom
              </Button>
            </div>
          </div>

          <!-- Breakpoint detail -->
          <div class="mt-4 text-xs text-muted-foreground">
            <span class="font-medium">Active breakpoints: </span>
            <span v-for="(bp, i) in activeBreakpoints" :key="bp.name">
              {{ bp.name }} (&ge;{{ bp.minWidth }}px: {{ bp.visibleImportance.join(', ') }}){{ i < activeBreakpoints.length - 1 ? ' | ' : '' }}
            </span>
          </div>
        </CardContent>
      </Card>

      <!-- Instructions -->
      <div class="rounded-lg border border-dashed border-primary/40 bg-primary/5 px-4 py-3">
        <p class="text-sm text-primary/80">
          Resize the browser window to see columns collapse into detail rows.
          On narrow viewports, only <strong>high</strong>-importance columns remain visible.
          Click a row (or the chevron) to reveal hidden column data in an expandable popin row beneath it.
        </p>
      </div>

      <!-- Responsive table -->
      <Card>
        <CardHeader>
          <CardTitle class="text-base">Employee Directory</CardTitle>
        </CardHeader>
        <CardContent>
          <ResponsiveTable
            ref="tableRef"
            :columns="columns"
            :data="sampleData"
            row-key="Id"
            :breakpoints="activeBreakpoints"
            striped
          >
            <template #cell-Email="{ value }">
              <a
                :href="'mailto:' + value"
                class="text-primary hover:underline"
                @click.stop
              >
                {{ value }}
              </a>
            </template>
            <template #cell-Salary="{ value }">
              <span class="font-mono tabular-nums">{{ value }}</span>
            </template>
          </ResponsiveTable>
        </CardContent>
      </Card>

      <!-- Column importance legend -->
      <Card>
        <CardHeader>
          <CardTitle class="text-base">Column Importance Legend</CardTitle>
        </CardHeader>
        <CardContent>
          <div class="grid grid-cols-1 sm:grid-cols-3 gap-4 text-sm">
            <div class="flex items-start gap-2">
              <Badge variant="default" class="shrink-0">High</Badge>
              <span class="text-muted-foreground">Always visible. Name, Email.</span>
            </div>
            <div class="flex items-start gap-2">
              <Badge variant="secondary" class="shrink-0">Medium</Badge>
              <span class="text-muted-foreground">Hidden on mobile. Department, Title.</span>
            </div>
            <div class="flex items-start gap-2">
              <Badge variant="outline" class="shrink-0">Low</Badge>
              <span class="text-muted-foreground">Only on desktop. Salary, Location, Hire Date, ID.</span>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
