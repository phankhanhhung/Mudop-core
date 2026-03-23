<script setup lang="ts">
import { ref, computed } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import P13nDialog from '@/components/smart/P13nDialog.vue'
import type { P13nState } from '@/composables/useP13nDialog'
import { ArrowLeft, SlidersHorizontal, ArrowUp, ArrowDown } from 'lucide-vue-next'

const p13nDialogRef = ref<InstanceType<typeof P13nDialog> | null>(null)

const availableColumns = [
  { key: 'id', label: 'ID' },
  { key: 'name', label: 'Name' },
  { key: 'email', label: 'Email' },
  { key: 'status', label: 'Status' },
  { key: 'amount', label: 'Amount' },
  { key: 'createdAt', label: 'Created At' },
  { key: 'country', label: 'Country' },
  { key: 'category', label: 'Category' },
]

const p13nState = ref<P13nState>({
  columns: availableColumns.map((col, index) => ({
    key: col.key,
    label: col.label,
    visible: true,
    order: index,
  })),
  sortItems: [],
  filterItems: [],
  groupItems: [],
})

function openDialog() {
  p13nDialogRef.value?.open()
}

function onApply(state: P13nState) {
  p13nState.value = state
}

// Sample data
const sampleData = [
  { id: '001', name: 'Alice Johnson', email: 'alice@example.com', status: 'Active', amount: 12500.00, createdAt: '2026-01-10', country: 'United States', category: 'Enterprise' },
  { id: '002', name: 'Bob Williams', email: 'bob@example.com', status: 'Inactive', amount: 8750.50, createdAt: '2026-01-15', country: 'Canada', category: 'Professional' },
  { id: '003', name: 'Clara Chen', email: 'clara@example.com', status: 'Active', amount: 34200.00, createdAt: '2026-01-22', country: 'Singapore', category: 'Enterprise' },
  { id: '004', name: 'David Park', email: 'david@example.com', status: 'Pending', amount: 5100.75, createdAt: '2026-02-01', country: 'South Korea', category: 'Starter' },
  { id: '005', name: 'Eva Mueller', email: 'eva@example.com', status: 'Active', amount: 19800.00, createdAt: '2026-02-05', country: 'Germany', category: 'Professional' },
  { id: '006', name: 'Frank Rossi', email: 'frank@example.com', status: 'Suspended', amount: 42000.25, createdAt: '2026-02-08', country: 'Italy', category: 'Enterprise' },
]

// Visible columns in order
const visibleColumns = computed(() =>
  p13nState.value.columns.filter(col => col.visible)
)

// Sorted data based on sort items
const sortedData = computed(() => {
  const data = [...sampleData]
  const sortItems = p13nState.value.sortItems
  if (sortItems.length === 0) return data

  data.sort((a, b) => {
    for (const item of sortItems) {
      const aVal = String((a as Record<string, unknown>)[item.key] ?? '')
      const bVal = String((b as Record<string, unknown>)[item.key] ?? '')
      const cmp = aVal.localeCompare(bVal, undefined, { numeric: true })
      if (cmp !== 0) return item.direction === 'asc' ? cmp : -cmp
    }
    return 0
  })

  return data
})

const statusVariants: Record<string, 'default' | 'secondary' | 'destructive' | 'outline'> = {
  Active: 'default',
  Inactive: 'secondary',
  Pending: 'outline',
  Suspended: 'destructive',
}

function getSortDirection(key: string): 'asc' | 'desc' | null {
  const item = p13nState.value.sortItems.find(s => s.key === key)
  return item ? item.direction : null
}

const operatorDisplayLabels: Record<string, string> = {
  eq: '=',
  ne: '!=',
  gt: '>',
  lt: '<',
  ge: '>=',
  le: '<=',
  contains: 'contains',
  startswith: 'starts with',
  endswith: 'ends with',
}
</script>

<template>
  <DefaultLayout>
    <div class="container mx-auto p-6 space-y-6">
      <!-- Page Header -->
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-2xl font-bold text-foreground">P13n Dialog</h1>
          <p class="text-muted-foreground">
            Personalization dialog with columns, sort, filter, and group configuration
          </p>
        </div>
        <RouterLink to="/showcase">
          <Button variant="outline" size="sm" class="gap-2">
            <ArrowLeft class="h-4 w-4" />
            Back to Showcase
          </Button>
        </RouterLink>
      </div>

      <!-- Controls -->
      <Card>
        <CardHeader>
          <CardTitle>Table Personalization</CardTitle>
        </CardHeader>
        <CardContent>
          <div class="flex items-center gap-3">
            <Button class="gap-2" @click="openDialog">
              <SlidersHorizontal class="h-4 w-4" />
              Personalize Table
            </Button>
            <span class="text-sm text-muted-foreground">
              {{ visibleColumns.length }} of {{ availableColumns.length }} columns visible
            </span>
          </div>
        </CardContent>
      </Card>

      <!-- Active Configuration Summary -->
      <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
        <!-- Active Sorts -->
        <Card>
          <CardHeader class="pb-3">
            <CardTitle class="text-sm font-medium">Active Sorts</CardTitle>
          </CardHeader>
          <CardContent>
            <div v-if="p13nState.sortItems.length === 0" class="text-sm text-muted-foreground">
              No sort criteria applied
            </div>
            <div v-else class="space-y-1.5">
              <div
                v-for="(item, index) in p13nState.sortItems"
                :key="index"
                class="flex items-center gap-2 text-sm"
              >
                <Badge variant="secondary" class="gap-1">
                  {{ item.label }}
                  <component
                    :is="item.direction === 'asc' ? ArrowUp : ArrowDown"
                    class="h-3 w-3"
                  />
                </Badge>
              </div>
            </div>
          </CardContent>
        </Card>

        <!-- Active Filters -->
        <Card>
          <CardHeader class="pb-3">
            <CardTitle class="text-sm font-medium">Active Filters</CardTitle>
          </CardHeader>
          <CardContent>
            <div v-if="p13nState.filterItems.length === 0" class="text-sm text-muted-foreground">
              No filter criteria applied
            </div>
            <div v-else class="space-y-1.5">
              <div
                v-for="(item, index) in p13nState.filterItems"
                :key="index"
                class="text-sm"
              >
                <Badge variant="outline" class="gap-1">
                  {{ item.label }} {{ operatorDisplayLabels[item.operator] }} "{{ item.value }}"
                </Badge>
              </div>
            </div>
          </CardContent>
        </Card>

        <!-- Active Groups -->
        <Card>
          <CardHeader class="pb-3">
            <CardTitle class="text-sm font-medium">Active Groups</CardTitle>
          </CardHeader>
          <CardContent>
            <div v-if="p13nState.groupItems.length === 0" class="text-sm text-muted-foreground">
              No grouping applied
            </div>
            <div v-else class="space-y-1.5">
              <div
                v-for="(item, index) in p13nState.groupItems"
                :key="index"
                class="text-sm"
              >
                <Badge variant="secondary">
                  {{ item.label }}
                  <span v-if="item.showSubtotals" class="ml-1 text-xs opacity-70">(subtotals)</span>
                </Badge>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      <!-- Data Table -->
      <Card>
        <CardHeader>
          <CardTitle>Sample Data</CardTitle>
        </CardHeader>
        <CardContent>
          <div class="overflow-x-auto rounded-md border">
            <table class="w-full text-sm">
              <thead>
                <tr class="border-b bg-muted/50">
                  <th
                    v-for="col in visibleColumns"
                    :key="col.key"
                    class="px-4 py-3 text-left font-medium text-muted-foreground"
                  >
                    <div class="flex items-center gap-1">
                      {{ col.label }}
                      <component
                        v-if="getSortDirection(col.key) === 'asc'"
                        :is="ArrowUp"
                        class="h-3 w-3 text-primary"
                      />
                      <component
                        v-else-if="getSortDirection(col.key) === 'desc'"
                        :is="ArrowDown"
                        class="h-3 w-3 text-primary"
                      />
                    </div>
                  </th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="row in sortedData"
                  :key="row.id"
                  class="border-b last:border-0 hover:bg-muted/30 transition-colors"
                >
                  <td
                    v-for="col in visibleColumns"
                    :key="col.key"
                    class="px-4 py-3"
                  >
                    <template v-if="col.key === 'status'">
                      <Badge :variant="statusVariants[(row as Record<string, unknown>)[col.key] as string] ?? 'secondary'">
                        {{ (row as Record<string, unknown>)[col.key] }}
                      </Badge>
                    </template>
                    <template v-else-if="col.key === 'amount'">
                      ${{ ((row as Record<string, unknown>)[col.key] as number).toLocaleString('en-US', { minimumFractionDigits: 2 }) }}
                    </template>
                    <template v-else>
                      {{ (row as Record<string, unknown>)[col.key] }}
                    </template>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>

      <!-- Raw State -->
      <Card>
        <CardHeader>
          <CardTitle class="text-sm font-medium">Current P13n State (JSON)</CardTitle>
        </CardHeader>
        <CardContent>
          <pre class="text-xs bg-muted rounded-md p-4 overflow-x-auto max-h-64 overflow-y-auto">{{ JSON.stringify(p13nState, null, 2) }}</pre>
        </CardContent>
      </Card>

      <!-- P13n Dialog -->
      <P13nDialog
        ref="p13nDialogRef"
        :available-columns="availableColumns"
        v-model="p13nState"
        @apply="onApply"
      />
    </div>
  </DefaultLayout>
</template>
