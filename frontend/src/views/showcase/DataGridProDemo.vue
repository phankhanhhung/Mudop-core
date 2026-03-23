<script setup lang="ts">
import { ref, computed } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import SmartTable from '@/components/smart/SmartTable.vue'
import type { EntityMetadata, FieldMetadata } from '@/types/metadata'
import type { FilterCondition } from '@/types/odata'
import {
  ArrowLeft,
  GripHorizontal,
  Columns3,
  Rows3,
  MousePointerClick,
  Download,
  Pencil,
  Puzzle,
} from 'lucide-vue-next'

// ─── Sample metadata ──────────────────────────────────────────────────────

const productFields: FieldMetadata[] = [
  {
    name: 'Id',
    type: 'UUID',
    displayName: 'ID',
    isRequired: true,
    isReadOnly: true,
    isComputed: false,
    annotations: {},
  },
  {
    name: 'ProductCode',
    type: 'String',
    displayName: 'Product Code',
    isRequired: true,
    isReadOnly: false,
    isComputed: false,
    maxLength: 20,
    annotations: {},
  },
  {
    name: 'Name',
    type: 'String',
    displayName: 'Product Name',
    isRequired: true,
    isReadOnly: false,
    isComputed: false,
    maxLength: 100,
    annotations: {},
  },
  {
    name: 'Category',
    type: 'Enum',
    displayName: 'Category',
    isRequired: true,
    isReadOnly: false,
    isComputed: false,
    enumValues: [
      { name: 'Electronics', value: 1 },
      { name: 'Furniture', value: 2 },
      { name: 'Clothing', value: 3 },
      { name: 'Food', value: 4 },
      { name: 'Tools', value: 5 },
    ],
    annotations: {},
  },
  {
    name: 'Price',
    type: 'Decimal',
    displayName: 'Unit Price',
    isRequired: true,
    isReadOnly: false,
    isComputed: false,
    precision: 18,
    scale: 2,
    annotations: {},
  },
  {
    name: 'Stock',
    type: 'Integer',
    displayName: 'Stock Qty',
    isRequired: false,
    isReadOnly: false,
    isComputed: false,
    annotations: {},
  },
  {
    name: 'InStock',
    type: 'Boolean',
    displayName: 'In Stock',
    isRequired: false,
    isReadOnly: false,
    isComputed: false,
    annotations: {},
  },
  {
    name: 'LastUpdated',
    type: 'DateTime',
    displayName: 'Last Updated',
    isRequired: false,
    isReadOnly: true,
    isComputed: true,
    annotations: {},
  },
]

const productMetadata: EntityMetadata = {
  name: 'Product',
  namespace: 'demo',
  displayName: 'Product',
  fields: productFields,
  keys: ['Id'],
  associations: [],
  annotations: {},
}

// ─── Generate 150 sample rows (to demonstrate virtual scrolling) ──────────

const adjectives = ['Premium', 'Standard', 'Economy', 'Professional', 'Deluxe', 'Ultra', 'Basic', 'Advanced']
const nouns = ['Widget', 'Gadget', 'Component', 'Module', 'Assembly', 'Unit', 'Kit', 'Set']

function generateProducts(count: number): Record<string, unknown>[] {
  const rows: Record<string, unknown>[] = []
  for (let i = 0; i < count; i++) {
    const adj = adjectives[i % adjectives.length]
    const noun = nouns[i % nouns.length]
    const cat = (i % 5) + 1
    const price = Math.round((10 + Math.random() * 990) * 100) / 100
    const stock = Math.floor(Math.random() * 500)
    const uuid = `${String(i).padStart(8, '0')}-0000-0000-0000-${String(i).padStart(12, '0')}`
    rows.push({
      Id: uuid,
      ProductCode: `PRD-${String(i + 1).padStart(4, '0')}`,
      Name: `${adj} ${noun} ${i + 1}`,
      Category: cat,
      Price: price,
      Stock: stock,
      InStock: stock > 0,
      LastUpdated: new Date(2026, 0, 1 + (i % 28), 8 + (i % 12), i % 60).toISOString(),
    })
  }
  return rows
}

const allProducts = ref(generateProducts(150))
const sortField = ref<string | undefined>(undefined)
const sortDir = ref<'asc' | 'desc'>('asc')
const activeFilters = ref<FilterCondition[]>([])

const filteredProducts = computed(() => {
  let result = allProducts.value
  for (const f of activeFilters.value) {
    const val = String(f.value ?? '').toLowerCase()
    if (!val) continue
    result = result.filter((row) => {
      const cell = row[f.field]
      if (cell == null) return false
      return String(cell).toLowerCase().includes(val)
    })
  }
  return result
})

const sortedProducts = computed(() => {
  if (!sortField.value) return filteredProducts.value
  const field = sortField.value
  const dir = sortDir.value
  return [...filteredProducts.value].sort((a, b) => {
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

function handleFilter(filters: FilterCondition[]) {
  activeFilters.value = filters
  log(`Filter: ${filters.length === 0 ? 'cleared' : filters.map(f => `${f.field} ${f.operator} "${f.value}"`).join(', ')}`)
}

// ─── Event handlers for demo ──────────────────────────────────────────────

const eventLog = ref<string[]>([])

function log(msg: string) {
  eventLog.value.unshift(`[${new Date().toLocaleTimeString()}] ${msg}`)
  if (eventLog.value.length > 20) eventLog.value.pop()
}

function handleRowSave(rowId: string, changes: Record<string, unknown>) {
  log(`Row Edit Save: ${rowId} → ${JSON.stringify(changes)}`)
  // Apply changes to local data
  const idx = allProducts.value.findIndex((r) => r.Id === rowId)
  if (idx >= 0) {
    allProducts.value[idx] = { ...allProducts.value[idx], ...changes }
    allProducts.value = [...allProducts.value]
  }
}

function handleBulkDelete(ids: string[]) {
  log(`Bulk Delete: ${ids.length} rows`)
  allProducts.value = allProducts.value.filter((r) => !ids.includes(String(r.Id)))
}

function handleBulkExport(format: 'csv' | 'xlsx', ids: string[]) {
  log(`Bulk Export: ${ids.length} rows as ${format.toUpperCase()}`)
}

function handleExport(format: 'csv' | 'json' | 'xlsx') {
  log(`Export All: ${format.toUpperCase()}`)
}

function handleSelectionChange(ids: string[]) {
  log(`Selection: ${ids.length} rows`)
}

// ─── Custom slot demo helpers ─────────────────────────────────────────────

const categoryMap: Record<number, { label: string; color: string }> = {
  1: { label: 'Electronics', color: 'bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-200' },
  2: { label: 'Furniture', color: 'bg-amber-100 text-amber-800 dark:bg-amber-900 dark:text-amber-200' },
  3: { label: 'Clothing', color: 'bg-pink-100 text-pink-800 dark:bg-pink-900 dark:text-pink-200' },
  4: { label: 'Food', color: 'bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-200' },
  5: { label: 'Tools', color: 'bg-slate-100 text-slate-800 dark:bg-slate-900 dark:text-slate-200' },
}

function getCategoryInfo(val: unknown) {
  return categoryMap[Number(val)] ?? { label: String(val), color: 'bg-muted text-muted-foreground' }
}

function formatPrice(val: unknown): string {
  return typeof val === 'number' ? `$${val.toFixed(2)}` : '-'
}

function stockPercent(val: unknown): number {
  return Math.min(100, Math.max(0, Math.round((Number(val) / 500) * 100)))
}

const slotsEnabled = ref(true)
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
          <h1 class="text-2xl font-bold text-foreground">Data Grid Pro</h1>
          <p class="text-sm text-muted-foreground">
            Phase C features: Column Resize, Drag Reorder, Virtual Scrolling, Bulk Actions, Row Edit, Excel Export
          </p>
        </div>
      </div>

      <!-- Feature badges -->
      <div class="flex flex-wrap gap-2">
        <Badge variant="outline" class="gap-1">
          <Columns3 class="h-3 w-3" />
          Column Resize
        </Badge>
        <Badge variant="outline" class="gap-1">
          <GripHorizontal class="h-3 w-3" />
          Drag Reorder
        </Badge>
        <Badge variant="outline" class="gap-1">
          <Rows3 class="h-3 w-3" />
          Virtual Scroll (150 rows)
        </Badge>
        <Badge variant="outline" class="gap-1">
          <MousePointerClick class="h-3 w-3" />
          Bulk Actions
        </Badge>
        <Badge variant="outline" class="gap-1">
          <Pencil class="h-3 w-3" />
          Row Edit Mode
        </Badge>
        <Badge variant="outline" class="gap-1">
          <Download class="h-3 w-3" />
          CSV / Excel Export
        </Badge>
        <Badge variant="outline" class="gap-1">
          <Puzzle class="h-3 w-3" />
          Scoped Slots
        </Badge>
      </div>

      <!-- Instructions -->
      <Card>
        <CardHeader>
          <CardTitle class="text-base">How to Test</CardTitle>
        </CardHeader>
        <CardContent class="text-sm space-y-2 text-muted-foreground">
          <p><strong class="text-foreground">Column Resize:</strong> Hover over column header right edge to see the resize handle. Drag to resize. Double-click to auto-fit.</p>
          <p><strong class="text-foreground">Drag Reorder:</strong> Drag a non-key column header and drop it on another header. A blue indicator shows the drop position.</p>
          <p><strong class="text-foreground">Virtual Scroll:</strong> With 150 rows loaded, scroll the table body. Only ~30 rows are rendered in the DOM (check DevTools).</p>
          <p><strong class="text-foreground">Bulk Actions:</strong> Select rows via checkboxes. The toolbar at the bottom shows Delete and Export (CSV/Excel) options.</p>
          <p><strong class="text-foreground">Row Edit:</strong> Click the pencil icon in the Actions column. Editable fields become inputs. Dirty fields show an amber indicator. Ctrl+Enter saves, Escape cancels.</p>
          <p><strong class="text-foreground">Excel Export:</strong> Click the Export dropdown in the toolbar. Choose "Export as Excel" to download an .xlsx file.</p>
          <p><strong class="text-foreground">Scoped Slots:</strong> Toggle "Custom Cells" to see custom renderers for Category (colored badges), Price (formatted with color), Stock (progress bar), and In Stock (dot indicator).</p>
        </CardContent>
      </Card>

      <!-- Data Grid Pro -->
      <SmartTable
        module="demo"
        entity-set="Products"
        :metadata="productMetadata"
        :data="sortedProducts"
        :total-count="sortedProducts.length"
        :current-page="1"
        :page-size="150"
        :is-loading="false"
        selection-mode="multi"
        title="Products"
        max-height="500px"
        :enable-export="true"
        :enable-bulk-actions="true"
        :enable-row-edit="true"
        :enable-virtual-scroll="true"
        :virtual-scroll-threshold="50"
        :row-height="40"
        :sort-field="sortField"
        :sort-direction="sortDir"
        :active-filters="activeFilters"
        @sort="handleSort"
        @filter="handleFilter"
        @export="handleExport"
        @bulk-delete="handleBulkDelete"
        @bulk-export="handleBulkExport"
        @row-save="handleRowSave"
        @selection-change="handleSelectionChange"
      >
        <!-- Toolbar slot: toggle for custom cells -->
        <template #toolbar-start>
          <Button
            variant="outline"
            size="sm"
            :class="slotsEnabled ? 'border-primary text-primary' : ''"
            @click="slotsEnabled = !slotsEnabled"
          >
            <Puzzle class="h-3.5 w-3.5 mr-1" />
            Custom Cells {{ slotsEnabled ? 'ON' : 'OFF' }}
          </Button>
        </template>

        <!-- Custom cell: Category — colored badge -->
        <template v-if="slotsEnabled" #cell-Category="{ value }">
          <span
            class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium"
            :class="getCategoryInfo(value).color"
          >
            {{ getCategoryInfo(value).label }}
          </span>
        </template>

        <!-- Custom cell: Price — formatted with color coding -->
        <template v-if="slotsEnabled" #cell-Price="{ value }">
          <span
            class="font-mono text-sm"
            :class="Number(value) >= 500 ? 'text-emerald-600 dark:text-emerald-400 font-semibold' : 'text-foreground'"
          >
            {{ formatPrice(value) }}
          </span>
        </template>

        <!-- Custom cell: Stock — mini progress bar -->
        <template v-if="slotsEnabled" #cell-Stock="{ value }">
          <div class="flex items-center gap-2">
            <div class="w-16 h-1.5 bg-muted rounded-full overflow-hidden">
              <div
                class="h-full rounded-full transition-all"
                :class="stockPercent(value) > 50 ? 'bg-emerald-500' : stockPercent(value) > 20 ? 'bg-amber-500' : 'bg-red-500'"
                :style="{ width: stockPercent(value) + '%' }"
              />
            </div>
            <span class="text-xs text-muted-foreground tabular-nums">{{ value }}</span>
          </div>
        </template>

        <!-- Custom cell: InStock — dot indicator -->
        <template v-if="slotsEnabled" #cell-InStock="{ value }">
          <div class="flex items-center gap-1.5">
            <span
              class="h-2 w-2 rounded-full"
              :class="value ? 'bg-emerald-500' : 'bg-red-500'"
            />
            <span class="text-xs">{{ value ? 'Available' : 'Out of stock' }}</span>
          </div>
        </template>
      </SmartTable>

      <!-- Event log -->
      <Card>
        <CardHeader>
          <div class="flex items-center justify-between">
            <CardTitle class="text-base">Event Log</CardTitle>
            <Button variant="ghost" size="sm" @click="eventLog = []">Clear</Button>
          </div>
        </CardHeader>
        <CardContent>
          <div v-if="eventLog.length === 0" class="text-sm text-muted-foreground py-4 text-center">
            Interact with the grid to see events logged here
          </div>
          <div v-else class="space-y-1 max-h-48 overflow-y-auto font-mono text-xs">
            <div
              v-for="(entry, idx) in eventLog"
              :key="idx"
              class="py-0.5 px-2 rounded"
              :class="idx === 0 ? 'bg-muted/50' : ''"
            >
              {{ entry }}
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
