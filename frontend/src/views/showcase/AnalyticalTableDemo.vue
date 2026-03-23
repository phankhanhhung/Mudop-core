<script setup lang="ts">
import { ref, computed } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import AnalyticalTable from '@/components/smart/AnalyticalTable.vue'
import type { EntityMetadata, FieldMetadata } from '@/types/metadata'
import type { GroupConfig, AggregateConfig } from '@/composables/useAnalyticalTable'
import { ArrowLeft, BarChart3 } from 'lucide-vue-next'

// ── Metadata ──

const salesFields: FieldMetadata[] = [
  { name: 'Id', type: 'UUID', displayName: 'ID', isRequired: true, isReadOnly: true, isComputed: false, annotations: {} },
  { name: 'Region', type: 'String', displayName: 'Region', isRequired: true, isReadOnly: false, isComputed: false, maxLength: 50, annotations: {} },
  { name: 'Product', type: 'String', displayName: 'Product', isRequired: true, isReadOnly: false, isComputed: false, maxLength: 100, annotations: {} },
  { name: 'Quarter', type: 'String', displayName: 'Quarter', isRequired: true, isReadOnly: false, isComputed: false, maxLength: 10, annotations: {} },
  { name: 'Revenue', type: 'Decimal', displayName: 'Revenue', isRequired: true, isReadOnly: false, isComputed: false, precision: 18, scale: 2, annotations: {} },
  { name: 'Units', type: 'Integer', displayName: 'Units Sold', isRequired: true, isReadOnly: false, isComputed: false, annotations: {} },
  { name: 'Discount', type: 'Decimal', displayName: 'Discount %', isRequired: false, isReadOnly: false, isComputed: false, precision: 5, scale: 2, annotations: {} },
]

const salesMetadata: EntityMetadata = {
  name: 'SalesRecord',
  namespace: 'demo',
  displayName: 'Sales Record',
  fields: salesFields,
  keys: ['Id'],
  associations: [],
  annotations: {},
}

// ── Sample data ──

const regions = ['North America', 'Europe', 'Asia Pacific', 'Latin America']
const products = ['Cloud Platform', 'Analytics Suite', 'Security Shield', 'Data Engine', 'Mobile SDK']
const quarters = ['Q1 2025', 'Q2 2025', 'Q3 2025', 'Q4 2025']

function generateSalesData(): Record<string, unknown>[] {
  const rows: Record<string, unknown>[] = []
  let idx = 0
  for (const region of regions) {
    for (const product of products) {
      for (const quarter of quarters) {
        // Skip some combinations to get ~40 rows instead of 80
        if (idx % 2 === 0 || (region === 'Latin America' && product === 'Mobile SDK')) {
          const revenue = Math.round((5000 + Math.random() * 95000) * 100) / 100
          const units = Math.floor(10 + Math.random() * 490)
          const discount = Math.round(Math.random() * 25 * 100) / 100
          const uuid = `${String(idx).padStart(8, '0')}-sale-0000-0000-${String(idx).padStart(12, '0')}`
          rows.push({
            Id: uuid,
            Region: region,
            Product: product,
            Quarter: quarter,
            Revenue: revenue,
            Units: units,
            Discount: discount,
          })
        }
        idx++
      }
    }
  }
  return rows
}

const salesData = ref(generateSalesData())

// ── Config state ──

type GroupByOption = 'Region' | 'Quarter' | 'Product' | 'none'

const groupByOption = ref<GroupByOption>('Region')
const showSubtotals = ref(true)
const showGrandTotal = ref(true)
const showDataBars = ref(true)
const selectionMode = ref<'none' | 'single' | 'multi'>('none')

const sortField = ref<string | undefined>(undefined)
const sortDir = ref<'asc' | 'desc'>('asc')

const groupBy = computed<GroupConfig[]>(() => {
  if (groupByOption.value === 'none') return []
  return [{ field: groupByOption.value, label: groupByOption.value }]
})

const aggregatesConfig = computed<AggregateConfig[]>(() => [
  { field: 'Revenue', fn: 'sum', label: 'Total Revenue' },
  { field: 'Units', fn: 'sum', label: 'Total Units' },
  { field: 'Discount', fn: 'avg', label: 'Avg Discount' },
])

// ── Sorting ──

const sortedData = computed(() => {
  if (!sortField.value) return salesData.value
  const field = sortField.value
  const dir = sortDir.value
  return [...salesData.value].sort((a, b) => {
    const va = a[field]
    const vb = b[field]
    if (va == null && vb == null) return 0
    if (va == null) return 1
    if (vb == null) return -1
    const cmp = va < vb ? -1 : va > vb ? 1 : 0
    return dir === 'asc' ? cmp : -cmp
  })
})

function handleSort(field: string, direction: 'asc' | 'desc') {
  sortField.value = field
  sortDir.value = direction
}

// ── Events ──

const eventLog = ref<string[]>([])

function log(msg: string) {
  eventLog.value.unshift(`[${new Date().toLocaleTimeString()}] ${msg}`)
  if (eventLog.value.length > 15) eventLog.value.pop()
}

function handleSelectionChange(ids: string[]) {
  log(`Selection: ${ids.length} rows`)
}

function handleRefresh() {
  log('Refresh requested')
  salesData.value = generateSalesData()
}
</script>

<template>
  <DefaultLayout>
    <div class="max-w-7xl mx-auto space-y-6">
      <!-- Header -->
      <div class="flex items-center gap-4">
        <RouterLink to="/showcase" class="text-muted-foreground hover:text-foreground">
          <ArrowLeft class="h-5 w-5" />
        </RouterLink>
        <div>
          <h1 class="text-2xl font-bold text-foreground flex items-center gap-2">
            <BarChart3 class="h-6 w-6" />
            Analytical Table
          </h1>
          <p class="text-sm text-muted-foreground">
            Phase D: Grouped data grid with aggregations, subtotals, and data bars
          </p>
        </div>
      </div>

      <!-- Feature badges -->
      <div class="flex flex-wrap gap-2">
        <Badge variant="outline" class="gap-1">Group By</Badge>
        <Badge variant="outline" class="gap-1">Subtotals</Badge>
        <Badge variant="outline" class="gap-1">Grand Total</Badge>
        <Badge variant="outline" class="gap-1">Data Bars</Badge>
        <Badge variant="outline" class="gap-1">Expand / Collapse</Badge>
        <Badge variant="outline" class="gap-1">Selection</Badge>
        <Badge variant="outline" class="gap-1">Sorting</Badge>
      </div>

      <!-- Controls -->
      <Card>
        <CardHeader>
          <CardTitle class="text-base">Configuration</CardTitle>
        </CardHeader>
        <CardContent class="space-y-4">
          <!-- Group By -->
          <div class="flex items-center gap-3">
            <span class="text-sm font-medium w-28">Group By:</span>
            <div class="flex gap-2">
              <Button
                v-for="opt in (['Region', 'Quarter', 'Product', 'none'] as const)"
                :key="opt"
                :variant="groupByOption === opt ? 'default' : 'outline'"
                size="sm"
                @click="groupByOption = opt"
              >
                {{ opt === 'none' ? 'None' : opt }}
              </Button>
            </div>
          </div>

          <!-- Toggles -->
          <div class="flex items-center gap-6">
            <label class="flex items-center gap-2 text-sm">
              <input type="checkbox" v-model="showSubtotals" class="rounded border-input" />
              Subtotals
            </label>
            <label class="flex items-center gap-2 text-sm">
              <input type="checkbox" v-model="showGrandTotal" class="rounded border-input" />
              Grand Total
            </label>
            <label class="flex items-center gap-2 text-sm">
              <input type="checkbox" v-model="showDataBars" class="rounded border-input" />
              Data Bars (Revenue)
            </label>
          </div>

          <!-- Selection Mode -->
          <div class="flex items-center gap-3">
            <span class="text-sm font-medium w-28">Selection:</span>
            <div class="flex gap-2">
              <Button
                v-for="mode in (['none', 'single', 'multi'] as const)"
                :key="mode"
                :variant="selectionMode === mode ? 'default' : 'outline'"
                size="sm"
                @click="selectionMode = mode"
              >
                {{ mode === 'none' ? 'None' : mode === 'single' ? 'Single' : 'Multi' }}
              </Button>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Analytical Table -->
      <AnalyticalTable
        :data="sortedData"
        :metadata="salesMetadata"
        :group-by="groupBy"
        :aggregates="aggregatesConfig"
        :show-subtotals="showSubtotals"
        :show-grand-total="showGrandTotal"
        :show-data-bars="showDataBars"
        data-bar-field="Revenue"
        title="Sales Performance"
        :selection-mode="selectionMode"
        max-height="500px"
        :sort-field="sortField"
        :sort-direction="sortDir"
        @sort="handleSort"
        @selection-change="handleSelectionChange"
        @refresh="handleRefresh"
      />

      <!-- Event Log -->
      <Card v-if="eventLog.length > 0">
        <CardHeader>
          <CardTitle class="text-base">Event Log</CardTitle>
        </CardHeader>
        <CardContent>
          <div class="space-y-1 font-mono text-xs max-h-40 overflow-auto">
            <p
              v-for="(entry, i) in eventLog"
              :key="i"
              class="text-muted-foreground"
            >
              {{ entry }}
            </p>
          </div>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
