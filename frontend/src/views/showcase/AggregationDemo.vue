<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { odataService } from '@/services'
import type { BatchRequest } from '@/types/odata'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import MessageStrip from '@/components/smart/MessageStrip.vue'
import AggregationBuilder from '@/components/entity/AggregationBuilder.vue'
import AggregationChart from '@/components/entity/AggregationChart.vue'
import { useMetadata } from '@/composables/useMetadata'
import { useAggregation } from '@/composables/useAggregation'
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table'
import {
  ArrowLeft,
  BarChart3,
  Zap,
  RefreshCw,
  Hash,
  TrendingUp,
  Calculator,
  ArrowUpDown,
  Download,
  Code2,
  ChevronRight,
  Globe,
  ShoppingCart,
  Users,
  Layers,
  Database,
  CheckCircle2,
} from 'lucide-vue-next'
import type { AggregationConfig, ChartType } from '@/types/aggregation'
import { useTemplateRef } from 'vue'

// ─────────────────────────────────────────────────────────────────────────────
// Entity selector — defaults to the showcase analytics module
// ─────────────────────────────────────────────────────────────────────────────

const DEFAULT_MODULE = 'showcase'

const moduleInput = ref(DEFAULT_MODULE)
const entityInput = ref('SalesOrder')

// Applied values (only change when user clicks "Load")
const activeModule = ref(DEFAULT_MODULE)
const activeEntity = ref('SalesOrder')

// Showcase entities pre-defined for quick switching
const showcaseEntities = [
  { entity: 'SalesOrder', label: 'Sales Orders', icon: ShoppingCart, description: 'Region, Category, Channel, Amount, Quantity' },
  { entity: 'ProductSale', label: 'Product Sales', icon: Layers, description: 'Category, Revenue, GrossMargin, Channel' },
  { entity: 'StaffKpi', label: 'Staff KPIs', icon: Users, description: 'Department, Role, ActualRevenue, DealsCount' },
]

function selectEntity(entity: string) {
  entityInput.value = entity
  moduleInput.value = DEFAULT_MODULE
  loadEntity()
}

function loadEntity() {
  activeModule.value = moduleInput.value
  activeEntity.value = entityInput.value
  aggReset()
}

// ─────────────────────────────────────────────────────────────────────────────
// Metadata
// ─────────────────────────────────────────────────────────────────────────────

const {
  metadata,
  fields,
  isLoading: metadataLoading,
  error: metadataError,
  load: loadMetadata,
} = useMetadata({
  module: activeModule.value,
  entity: activeEntity.value,
  autoLoad: true,
})

// Reload metadata when active entity changes
watch([activeModule, activeEntity], async () => {
  await loadMetadata()
})

// ─────────────────────────────────────────────────────────────────────────────
// Aggregation engine
// ─────────────────────────────────────────────────────────────────────────────

const {
  config,
  results,
  isLoading: aggLoading,
  error: aggError,
  execute,
  reset: aggReset,
  buildApplyString,
  summaryStats,
} = useAggregation({ module: activeModule.value, entitySet: activeEntity.value })

const chartType = ref<ChartType>('bar')
const builderRef = useTemplateRef<InstanceType<typeof AggregationBuilder>>('builderRef')

// Live $apply preview
const applyPreview = computed(() => {
  if (config.value.groupByFields.length === 0 && config.value.aggregations.length === 0) return ''
  try {
    return buildApplyString(config.value)
  } catch {
    return ''
  }
})

const odataPreview = computed(() => {
  if (!applyPreview.value) return ''
  return `/odata/${activeModule.value}/${activeEntity.value}?$apply=${encodeURIComponent(applyPreview.value)}`
})

// ─────────────────────────────────────────────────────────────────────────────
// Quick Start presets — 4 curated scenarios
// ─────────────────────────────────────────────────────────────────────────────

interface QuickStartPreset {
  id: string
  title: string
  description: string
  entity: string
  icon: typeof BarChart3
  config: AggregationConfig
  chartType: ChartType
  color: string
}

const quickStartPresets: QuickStartPreset[] = [
  {
    id: 'revenue-by-region',
    title: 'Revenue by Region',
    description: 'Total order amount grouped by region. Reveals geographic sales distribution.',
    entity: 'SalesOrder',
    icon: Globe,
    color: 'bg-blue-500/10 text-blue-600',
    chartType: 'bar',
    config: {
      groupByFields: ['Region'],
      aggregations: [
        { id: '1', func: 'sum', field: 'Amount', alias: 'TotalRevenue' },
        { id: '2', func: 'count', field: '', alias: 'OrderCount' },
      ],
    },
  },
  {
    id: 'category-margin',
    title: 'Margin by Category',
    description: 'Sum and average of GrossMargin per product category. Identifies most profitable lines.',
    entity: 'ProductSale',
    icon: ShoppingCart,
    color: 'bg-emerald-500/10 text-emerald-600',
    chartType: 'bar',
    config: {
      groupByFields: ['Category'],
      aggregations: [
        { id: '1', func: 'sum', field: 'GrossMargin', alias: 'TotalMargin' },
        { id: '2', func: 'avg', field: 'GrossMargin', alias: 'AvgMargin' },
        { id: '3', func: 'sum', field: 'Revenue', alias: 'TotalRevenue' },
      ],
    },
  },
  {
    id: 'channel-orders',
    title: 'Orders by Channel',
    description: 'Count and discount average per sales channel. Compare online vs retail performance.',
    entity: 'SalesOrder',
    icon: BarChart3,
    color: 'bg-amber-500/10 text-amber-600',
    chartType: 'doughnut',
    config: {
      groupByFields: ['Channel'],
      aggregations: [
        { id: '1', func: 'count', field: '', alias: 'OrderCount' },
        { id: '2', func: 'avg', field: 'Discount', alias: 'AvgDiscount' },
        { id: '3', func: 'sum', field: 'Amount', alias: 'TotalAmount' },
      ],
    },
  },
  {
    id: 'dept-performance',
    title: 'Department Performance',
    description: 'Actual vs target revenue per department. See which teams are hitting their goals.',
    entity: 'StaffKpi',
    icon: Users,
    color: 'bg-violet-500/10 text-violet-600',
    chartType: 'bar',
    config: {
      groupByFields: ['Department'],
      aggregations: [
        { id: '1', func: 'sum', field: 'TargetRevenue', alias: 'TotalTarget' },
        { id: '2', func: 'sum', field: 'ActualRevenue', alias: 'TotalActual' },
        { id: '3', func: 'avg', field: 'CustomerScore', alias: 'AvgScore' },
      ],
    },
  },
]

function applyPreset(preset: QuickStartPreset) {
  selectEntity(preset.entity)
  chartType.value = preset.chartType
  // Apply config after a tick so builder is mounted/reset
  setTimeout(() => {
    config.value = JSON.parse(JSON.stringify(preset.config))
    builderRef.value?.applyConfig(preset.config)
  }, 50)
}

// ─────────────────────────────────────────────────────────────────────────────
// Execute handler
// ─────────────────────────────────────────────────────────────────────────────

function handleExecute(cfg: AggregationConfig) {
  config.value = cfg
  execute()
}

// ─────────────────────────────────────────────────────────────────────────────
// Results table
// ─────────────────────────────────────────────────────────────────────────────

const tableSortField = ref<string | null>(null)
const tableSortDirection = ref<'asc' | 'desc'>('asc')

const resultColumns = computed(() => {
  if (!results.value || results.value.rawData.length === 0) return []
  return Object.keys(results.value.rawData[0])
})

const groupByColumns = computed(() => config.value.groupByFields)
const aggregateColumns = computed(() => config.value.aggregations.map(a => a.alias))

const sortedData = computed(() => {
  if (!results.value) return []
  const data = [...results.value.rawData]
  if (!tableSortField.value) return data
  const field = tableSortField.value
  const dir = tableSortDirection.value === 'asc' ? 1 : -1
  return data.sort((a, b) => {
    const va = a[field], vb = b[field]
    if (va == null && vb == null) return 0
    if (va == null) return dir
    if (vb == null) return -dir
    if (typeof va === 'number' && typeof vb === 'number') return (va - vb) * dir
    return String(va).localeCompare(String(vb)) * dir
  })
})

const totalRow = computed(() => {
  if (!results.value || results.value.rawData.length === 0) return null
  const totals: Record<string, unknown> = {}
  for (const col of resultColumns.value) {
    if (aggregateColumns.value.includes(col)) {
      const sum = results.value.rawData.reduce((acc, row) => {
        const val = row[col]
        return acc + (typeof val === 'number' ? val : Number(val) || 0)
      }, 0)
      totals[col] = Math.round(sum * 100) / 100
    } else {
      totals[col] = null
    }
  }
  return totals
})

function handleSort(field: string) {
  if (tableSortField.value === field) {
    tableSortDirection.value = tableSortDirection.value === 'asc' ? 'desc' : 'asc'
  } else {
    tableSortField.value = field
    tableSortDirection.value = 'asc'
  }
}

function formatNumber(val: unknown): string {
  if (val == null) return ''
  const num = typeof val === 'number' ? val : Number(val)
  if (isNaN(num)) return String(val)
  return num.toLocaleString(undefined, { maximumFractionDigits: 2 })
}

function handleExportCsv() {
  if (!results.value) return
  const cols = resultColumns.value
  const lines = [cols.join(',')]
  for (const row of sortedData.value) {
    const cells = cols.map(col => {
      const v = row[col]
      if (v == null) return ''
      const s = String(v)
      return s.includes(',') || s.includes('"') ? `"${s.replace(/"/g, '""')}"` : s
    })
    lines.push(cells.join(','))
  }
  const blob = new Blob(['\uFEFF' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8;' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `${activeEntity.value}_aggregation.csv`
  a.style.display = 'none'
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
}

const showODataUrl = ref(false)

// ─────────────────────────────────────────────────────────────────────────────
// Seed sample data
// ─────────────────────────────────────────────────────────────────────────────

const isSeeding = ref(false)
const seedResult = ref<{ created: number } | null>(null)
const seedError = ref<string | null>(null)

// Region: NorthAmerica=1, Europe=2, AsiaPacific=3, LatinAmerica=4, MiddleEast=5
// SaleChannel: Online=1, Retail=2, Wholesale=3, Direct=4
// OrderStatus: Draft=1, Confirmed=2, Shipped=3, Delivered=4, Cancelled=5
// Quarter: Q1=1, Q2=2, Q3=3, Q4=4

const SEED_SALES_ORDERS = [
  { OrderNumber: 'SO001', Region: 1, Category: 'Electronics',    SalesRep: 'Alice Johnson', Channel: 1, Status: 4, Amount: 15420.00, Discount:  5.00, Quantity: 12, OrderDate: '2025-01-15', Year: 2025, Quarter: 1 },
  { OrderNumber: 'SO002', Region: 2, Category: 'Clothing',       SalesRep: 'Bob Smith',     Channel: 2, Status: 3, Amount:  8750.50, Discount: 10.00, Quantity: 25, OrderDate: '2025-01-22', Year: 2025, Quarter: 1 },
  { OrderNumber: 'SO003', Region: 3, Category: 'Food & Beverage',SalesRep: 'Carol White',   Channel: 3, Status: 4, Amount: 32100.00, Discount:  0.00, Quantity: 50, OrderDate: '2025-02-03', Year: 2025, Quarter: 1 },
  { OrderNumber: 'SO004', Region: 4, Category: 'Home & Garden',  SalesRep: 'David Brown',   Channel: 4, Status: 4, Amount:  4800.00, Discount:  2.50, Quantity:  8, OrderDate: '2025-02-14', Year: 2025, Quarter: 1 },
  { OrderNumber: 'SO005', Region: 5, Category: 'Sports',         SalesRep: 'Emma Davis',    Channel: 1, Status: 4, Amount: 21300.75, Discount:  7.50, Quantity: 30, OrderDate: '2025-03-05', Year: 2025, Quarter: 1 },
  { OrderNumber: 'SO006', Region: 1, Category: 'Clothing',       SalesRep: 'Alice Johnson', Channel: 2, Status: 4, Amount: 11200.00, Discount: 15.00, Quantity: 40, OrderDate: '2025-04-10', Year: 2025, Quarter: 2 },
  { OrderNumber: 'SO007', Region: 2, Category: 'Electronics',    SalesRep: 'Bob Smith',     Channel: 1, Status: 4, Amount: 47500.00, Discount:  3.00, Quantity:  5, OrderDate: '2025-04-18', Year: 2025, Quarter: 2 },
  { OrderNumber: 'SO008', Region: 3, Category: 'Sports',         SalesRep: 'Carol White',   Channel: 4, Status: 3, Amount:  9850.25, Discount:  0.00, Quantity: 15, OrderDate: '2025-05-07', Year: 2025, Quarter: 2 },
  { OrderNumber: 'SO009', Region: 4, Category: 'Electronics',    SalesRep: 'David Brown',   Channel: 3, Status: 2, Amount: 28700.00, Discount:  5.00, Quantity: 22, OrderDate: '2025-05-21', Year: 2025, Quarter: 2 },
  { OrderNumber: 'SO010', Region: 5, Category: 'Home & Garden',  SalesRep: 'Emma Davis',    Channel: 2, Status: 4, Amount:  6100.50, Discount: 12.00, Quantity: 10, OrderDate: '2025-06-01', Year: 2025, Quarter: 2 },
  { OrderNumber: 'SO011', Region: 1, Category: 'Food & Beverage',SalesRep: 'Bob Smith',     Channel: 1, Status: 4, Amount: 19400.00, Discount:  0.00, Quantity: 60, OrderDate: '2025-07-09', Year: 2025, Quarter: 3 },
  { OrderNumber: 'SO012', Region: 2, Category: 'Sports',         SalesRep: 'Alice Johnson', Channel: 4, Status: 3, Amount: 13600.00, Discount:  8.00, Quantity: 20, OrderDate: '2025-07-25', Year: 2025, Quarter: 3 },
  { OrderNumber: 'SO013', Region: 3, Category: 'Clothing',       SalesRep: 'Emma Davis',    Channel: 2, Status: 4, Amount:  5450.00, Discount: 20.00, Quantity: 35, OrderDate: '2025-08-12', Year: 2025, Quarter: 3 },
  { OrderNumber: 'SO014', Region: 4, Category: 'Home & Garden',  SalesRep: 'Carol White',   Channel: 3, Status: 4, Amount: 11750.00, Discount:  4.00, Quantity: 18, OrderDate: '2025-08-29', Year: 2025, Quarter: 3 },
  { OrderNumber: 'SO015', Region: 5, Category: 'Electronics',    SalesRep: 'David Brown',   Channel: 1, Status: 3, Amount: 39200.00, Discount:  6.00, Quantity:  9, OrderDate: '2025-09-15', Year: 2025, Quarter: 3 },
  { OrderNumber: 'SO016', Region: 1, Category: 'Sports',         SalesRep: 'Carol White',   Channel: 1, Status: 4, Amount: 17800.00, Discount:  2.00, Quantity: 28, OrderDate: '2025-10-03', Year: 2025, Quarter: 4 },
  { OrderNumber: 'SO017', Region: 2, Category: 'Home & Garden',  SalesRep: 'Emma Davis',    Channel: 2, Status: 4, Amount:  8900.50, Discount:  9.00, Quantity: 14, OrderDate: '2025-10-18', Year: 2025, Quarter: 4 },
  { OrderNumber: 'SO018', Region: 3, Category: 'Electronics',    SalesRep: 'Alice Johnson', Channel: 3, Status: 2, Amount: 55000.00, Discount:  0.00, Quantity:  7, OrderDate: '2025-11-04', Year: 2025, Quarter: 4 },
  { OrderNumber: 'SO019', Region: 4, Category: 'Food & Beverage',SalesRep: 'Bob Smith',     Channel: 4, Status: 3, Amount: 24500.00, Discount: 11.00, Quantity: 80, OrderDate: '2025-11-20', Year: 2025, Quarter: 4 },
  { OrderNumber: 'SO020', Region: 5, Category: 'Clothing',       SalesRep: 'David Brown',   Channel: 1, Status: 4, Amount:  7200.00, Discount:  3.00, Quantity: 16, OrderDate: '2025-12-08', Year: 2025, Quarter: 4 },
]

const SEED_PRODUCT_SALES = [
  { ProductName: 'Laptop Pro 15',   Category: 'Electronics',    SubCategory: 'Computers',    Channel: 1, Region: 1, UnitPrice: 1299.00, UnitCost:  780.00, QuantitySold: 12, Revenue: 15588.00, GrossMargin:  6228.00, SaleDate: '2025-01-20', Year: 2025, Quarter: 1 },
  { ProductName: 'Wireless Earbuds',Category: 'Electronics',    SubCategory: 'Audio',        Channel: 1, Region: 2, UnitPrice:  149.00, UnitCost:   60.00, QuantitySold: 45, Revenue:  6705.00, GrossMargin:  4005.00, SaleDate: '2025-02-05', Year: 2025, Quarter: 1 },
  { ProductName: 'Running Shoes',   Category: 'Sports',         SubCategory: 'Footwear',     Channel: 2, Region: 3, UnitPrice:  129.00, UnitCost:   55.00, QuantitySold: 30, Revenue:  3870.00, GrossMargin:  2220.00, SaleDate: '2025-02-18', Year: 2025, Quarter: 1 },
  { ProductName: 'Winter Jacket',   Category: 'Clothing',       SubCategory: 'Outerwear',    Channel: 2, Region: 4, UnitPrice:  249.00, UnitCost:  110.00, QuantitySold: 20, Revenue:  4980.00, GrossMargin:  2780.00, SaleDate: '2025-03-10', Year: 2025, Quarter: 1 },
  { ProductName: 'Coffee Maker',    Category: 'Home & Garden',  SubCategory: 'Appliances',   Channel: 3, Region: 5, UnitPrice:   89.00, UnitCost:   35.00, QuantitySold: 50, Revenue:  4450.00, GrossMargin:  2700.00, SaleDate: '2025-04-02', Year: 2025, Quarter: 2 },
  { ProductName: '4K Smart TV',     Category: 'Electronics',    SubCategory: 'Televisions',  Channel: 1, Region: 1, UnitPrice:  799.00, UnitCost:  480.00, QuantitySold:  8, Revenue:  6392.00, GrossMargin:  2552.00, SaleDate: '2025-04-15', Year: 2025, Quarter: 2 },
  { ProductName: 'Yoga Mat',        Category: 'Sports',         SubCategory: 'Fitness',      Channel: 1, Region: 2, UnitPrice:   49.00, UnitCost:   15.00, QuantitySold: 80, Revenue:  3920.00, GrossMargin:  2720.00, SaleDate: '2025-05-01', Year: 2025, Quarter: 2 },
  { ProductName: 'Organic Coffee',  Category: 'Food & Beverage',SubCategory: 'Beverages',    Channel: 4, Region: 3, UnitPrice:   22.00, UnitCost:    8.00, QuantitySold:200, Revenue:  4400.00, GrossMargin:  2800.00, SaleDate: '2025-05-20', Year: 2025, Quarter: 2 },
  { ProductName: 'Desk Lamp LED',   Category: 'Home & Garden',  SubCategory: 'Lighting',     Channel: 2, Region: 4, UnitPrice:   35.00, UnitCost:   12.00, QuantitySold: 60, Revenue:  2100.00, GrossMargin:  1380.00, SaleDate: '2025-06-08', Year: 2025, Quarter: 2 },
  { ProductName: 'Gaming Headset',  Category: 'Electronics',    SubCategory: 'Audio',        Channel: 1, Region: 5, UnitPrice:   89.00, UnitCost:   40.00, QuantitySold: 35, Revenue:  3115.00, GrossMargin:  1715.00, SaleDate: '2025-07-12', Year: 2025, Quarter: 3 },
  { ProductName: 'Trail Backpack',  Category: 'Sports',         SubCategory: 'Outdoor',      Channel: 3, Region: 1, UnitPrice:  159.00, UnitCost:   70.00, QuantitySold: 18, Revenue:  2862.00, GrossMargin:  1602.00, SaleDate: '2025-08-03', Year: 2025, Quarter: 3 },
  { ProductName: 'Linen Trousers',  Category: 'Clothing',       SubCategory: 'Bottoms',      Channel: 2, Region: 2, UnitPrice:   79.00, UnitCost:   30.00, QuantitySold: 40, Revenue:  3160.00, GrossMargin:  1960.00, SaleDate: '2025-08-25', Year: 2025, Quarter: 3 },
  { ProductName: 'Protein Powder',  Category: 'Food & Beverage',SubCategory: 'Supplements',  Channel: 1, Region: 3, UnitPrice:   55.00, UnitCost:   20.00, QuantitySold: 90, Revenue:  4950.00, GrossMargin:  3150.00, SaleDate: '2025-09-14', Year: 2025, Quarter: 3 },
  { ProductName: 'Robot Vacuum',    Category: 'Home & Garden',  SubCategory: 'Cleaning',     Channel: 1, Region: 4, UnitPrice:  349.00, UnitCost:  190.00, QuantitySold: 15, Revenue:  5235.00, GrossMargin:  2385.00, SaleDate: '2025-11-11', Year: 2025, Quarter: 4 },
  { ProductName: 'Smartwatch Ultra',Category: 'Electronics',    SubCategory: 'Wearables',    Channel: 1, Region: 5, UnitPrice:  449.00, UnitCost:  220.00, QuantitySold: 22, Revenue:  9878.00, GrossMargin:  5038.00, SaleDate: '2025-12-01', Year: 2025, Quarter: 4 },
]

const SEED_STAFF_KPIS = [
  { EmployeeName: 'Alice Johnson',  Department: 'Sales',        Role: 'Senior',    Region: 1, Year: 2025, Quarter: 1, TargetRevenue:  80000.00, ActualRevenue:  91200.00, DealsCount: 18, CustomerScore: 4.5, CallsCount: 142 },
  { EmployeeName: 'Bob Smith',      Department: 'Sales',        Role: 'Manager',   Region: 2, Year: 2025, Quarter: 1, TargetRevenue: 120000.00, ActualRevenue: 108400.00, DealsCount: 22, CustomerScore: 4.1, CallsCount: 198 },
  { EmployeeName: 'Carol White',    Department: 'Marketing',    Role: 'Manager',   Region: 3, Year: 2025, Quarter: 1, TargetRevenue:  60000.00, ActualRevenue:  67500.00, DealsCount: 14, CustomerScore: 4.7, CallsCount:  88 },
  { EmployeeName: 'David Brown',    Department: 'Engineering',  Role: 'Director',  Region: 4, Year: 2025, Quarter: 1, TargetRevenue:  40000.00, ActualRevenue:  38200.00, DealsCount:  8, CustomerScore: 4.0, CallsCount:  55 },
  { EmployeeName: 'Emma Davis',     Department: 'Finance',      Role: 'Analyst',   Region: 5, Year: 2025, Quarter: 1, TargetRevenue:  30000.00, ActualRevenue:  31800.00, DealsCount:  6, CustomerScore: 4.3, CallsCount:  62 },
  { EmployeeName: 'Frank Miller',   Department: 'Operations',   Role: 'Manager',   Region: 1, Year: 2025, Quarter: 2, TargetRevenue:  50000.00, ActualRevenue:  47600.00, DealsCount: 11, CustomerScore: 3.9, CallsCount: 110 },
  { EmployeeName: 'Grace Lee',      Department: 'Sales',        Role: 'Analyst',   Region: 2, Year: 2025, Quarter: 2, TargetRevenue:  70000.00, ActualRevenue:  82300.00, DealsCount: 16, CustomerScore: 4.6, CallsCount: 155 },
  { EmployeeName: 'Henry Wilson',   Department: 'Marketing',    Role: 'Senior',    Region: 3, Year: 2025, Quarter: 2, TargetRevenue:  55000.00, ActualRevenue:  52100.00, DealsCount: 12, CustomerScore: 4.2, CallsCount:  97 },
  { EmployeeName: 'Iris Chen',      Department: 'Engineering',  Role: 'Senior',    Region: 4, Year: 2025, Quarter: 2, TargetRevenue:  45000.00, ActualRevenue:  49700.00, DealsCount:  9, CustomerScore: 4.4, CallsCount:  70 },
  { EmployeeName: 'James Wright',   Department: 'Finance',      Role: 'Manager',   Region: 5, Year: 2025, Quarter: 3, TargetRevenue:  35000.00, ActualRevenue:  33500.00, DealsCount:  7, CustomerScore: 3.8, CallsCount:  80 },
  { EmployeeName: 'Kate Nguyen',    Department: 'Sales',        Role: 'Director',  Region: 1, Year: 2025, Quarter: 3, TargetRevenue: 150000.00, ActualRevenue: 163400.00, DealsCount: 28, CustomerScore: 4.8, CallsCount: 220 },
  { EmployeeName: 'Leo Park',       Department: 'Operations',   Role: 'Analyst',   Region: 2, Year: 2025, Quarter: 4, TargetRevenue:  40000.00, ActualRevenue:  41200.00, DealsCount: 10, CustomerScore: 4.1, CallsCount:  95 },
]

async function seedShowcaseData() {
  isSeeding.value = true
  seedError.value = null
  seedResult.value = null

  const allRequests: BatchRequest[] = []

  for (const [i, row] of SEED_SALES_ORDERS.entries()) {
    allRequests.push({ id: `so-${i}`, method: 'POST', url: 'SalesOrder', body: row, headers: { 'Content-Type': 'application/json' } })
  }
  for (const [i, row] of SEED_PRODUCT_SALES.entries()) {
    allRequests.push({ id: `ps-${i}`, method: 'POST', url: 'ProductSale', body: row, headers: { 'Content-Type': 'application/json' } })
  }
  for (const [i, row] of SEED_STAFF_KPIS.entries()) {
    allRequests.push({ id: `sk-${i}`, method: 'POST', url: 'StaffKpi', body: row, headers: { 'Content-Type': 'application/json' } })
  }

  try {
    const responses = await odataService.batch(DEFAULT_MODULE, allRequests)
    const created = responses.filter(r => r.status === 201).length
    const failed = responses.filter(r => r.status >= 400).length
    if (failed > 0 && created === 0) {
      seedError.value = `Seeding failed: ${failed} requests returned errors. Is the showcase module installed?`
    } else {
      seedResult.value = { created }
      // Auto-execute if an aggregation is configured
      if (config.value.groupByFields.length > 0 || config.value.aggregations.length > 0) {
        execute()
      }
    }
  } catch (e) {
    seedError.value = e instanceof Error ? e.message : 'Batch request failed. Ensure the showcase module is installed via Admin → Modules.'
  } finally {
    isSeeding.value = false
  }
}
</script>

<template>
  <DefaultLayout>
    <div class="space-y-8 pb-12">

      <!-- ── Page Header ──────────────────────────────────────────────────── -->
      <div class="flex items-center gap-4">
        <RouterLink to="/showcase">
          <Button variant="ghost" size="sm">
            <ArrowLeft class="mr-2 h-4 w-4" />
            Showcase
          </Button>
        </RouterLink>
        <div>
          <h1 class="text-2xl font-bold text-foreground">OData $apply — Aggregation Playground</h1>
          <p class="text-sm text-muted-foreground mt-0.5">
            Explore <code class="text-xs bg-muted px-1.5 py-0.5 rounded">$apply</code> with
            <code class="text-xs bg-muted px-1.5 py-0.5 rounded">groupby</code> and
            <code class="text-xs bg-muted px-1.5 py-0.5 rounded">aggregate</code> transformations.
            Results visualised as charts and sortable tables.
          </p>
        </div>
      </div>

      <!-- ── Entity selector ─────────────────────────────────────────────── -->
      <Card>
        <CardHeader class="pb-3">
          <CardTitle class="text-base">Data Source</CardTitle>
          <CardDescription>
            Use the showcase analytics module (pre-installed) or point to any module/entity in your tenant.
          </CardDescription>
        </CardHeader>
        <CardContent class="space-y-4">
          <!-- Showcase entity quick-switch -->
          <div class="flex flex-wrap gap-2">
            <button
              v-for="se in showcaseEntities"
              :key="se.entity"
              class="flex items-center gap-2 px-3 py-2 rounded-lg border text-sm transition-all"
              :class="activeEntity === se.entity && activeModule === DEFAULT_MODULE
                ? 'border-primary bg-primary/5 text-primary font-medium'
                : 'border-border hover:border-primary/50 hover:bg-muted/50'"
              @click="selectEntity(se.entity)"
            >
              <component :is="se.icon" class="h-4 w-4 shrink-0" />
              <div class="text-left">
                <div class="font-medium leading-tight">{{ se.label }}</div>
                <div class="text-xs text-muted-foreground leading-tight">{{ se.description }}</div>
              </div>
            </button>
          </div>

          <!-- Seed data strip (showcase module only) -->
          <div v-if="activeModule === DEFAULT_MODULE" class="flex items-center gap-3 py-2 px-3 rounded-lg bg-muted/30 border border-dashed border-border">
            <Database class="h-4 w-4 text-muted-foreground shrink-0" />
            <div class="flex-1 min-w-0">
              <p class="text-xs text-muted-foreground">
                Populate the showcase module with
                <strong>{{ SEED_SALES_ORDERS.length + SEED_PRODUCT_SALES.length + SEED_STAFF_KPIS.length }} sample records</strong>
                ({{ SEED_SALES_ORDERS.length }} orders · {{ SEED_PRODUCT_SALES.length }} products · {{ SEED_STAFF_KPIS.length }} KPIs)
              </p>
              <p v-if="seedResult" class="text-xs text-emerald-600 font-medium mt-0.5">
                <CheckCircle2 class="inline h-3 w-3 mr-1" />{{ seedResult.created }} records created
              </p>
              <p v-if="seedError" class="text-xs text-destructive mt-0.5">{{ seedError }}</p>
            </div>
            <Button
              size="sm"
              variant="outline"
              :disabled="isSeeding"
              @click="seedShowcaseData"
            >
              <Spinner v-if="isSeeding" size="sm" class="mr-1.5" />
              <Database v-else class="mr-1.5 h-3.5 w-3.5" />
              {{ isSeeding ? 'Seeding…' : 'Seed Sample Data' }}
            </Button>
          </div>

          <div class="border-t pt-4">
            <p class="text-xs text-muted-foreground font-medium mb-2">Or enter any module / entity</p>
            <div class="flex items-center gap-2 flex-wrap">
              <div class="flex items-center gap-2">
                <label class="text-xs text-muted-foreground w-14">Module</label>
                <Input v-model="moduleInput" placeholder="showcase" class="w-36 h-8 text-sm" />
              </div>
              <ChevronRight class="h-4 w-4 text-muted-foreground" />
              <div class="flex items-center gap-2">
                <label class="text-xs text-muted-foreground w-14">Entity</label>
                <Input v-model="entityInput" placeholder="SalesOrder" class="w-36 h-8 text-sm" />
              </div>
              <Button size="sm" @click="loadEntity" :disabled="!moduleInput || !entityInput">
                Load
              </Button>
              <div v-if="metadataLoading" class="flex items-center gap-1.5 text-xs text-muted-foreground">
                <Spinner size="sm" />
                Loading metadata…
              </div>
              <Badge v-else-if="metadata" variant="outline" class="text-xs">
                {{ fields.length }} fields
              </Badge>
            </div>
          </div>

          <MessageStrip
            v-if="metadataError"
            type="error"
            :title="metadataError"
            :closable="true"
            @close="() => {}"
          />
        </CardContent>
      </Card>

      <template v-if="metadata && !metadataLoading">

        <!-- ── Quick Start presets ───────────────────────────────────────── -->
        <div>
          <div class="flex items-center gap-2 mb-3">
            <Zap class="h-5 w-5 text-amber-500" />
            <h2 class="text-lg font-semibold">Quick Start</h2>
            <span class="text-sm text-muted-foreground">— click a scenario to auto-fill the builder and run</span>
          </div>
          <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <button
              v-for="preset in quickStartPresets"
              :key="preset.id"
              class="text-left p-4 rounded-xl border hover:border-primary/50 hover:shadow-sm transition-all group"
              @click="applyPreset(preset)"
            >
              <div class="flex items-center gap-2 mb-2">
                <div class="h-8 w-8 rounded-lg flex items-center justify-center" :class="preset.color">
                  <component :is="preset.icon" class="h-4 w-4" />
                </div>
                <span class="font-semibold text-sm group-hover:text-primary transition-colors">
                  {{ preset.title }}
                </span>
              </div>
              <p class="text-xs text-muted-foreground leading-relaxed">{{ preset.description }}</p>
              <div class="flex flex-wrap gap-1 mt-2">
                <Badge variant="secondary" class="text-[10px] px-1.5 py-0">
                  {{ preset.entity }}
                </Badge>
                <Badge variant="outline" class="text-[10px] px-1.5 py-0">
                  {{ preset.config.groupByFields.join(', ') }}
                </Badge>
              </div>
            </button>
          </div>
        </div>

        <!-- ── Summary stats (when results exist) ───────────────────────── -->
        <div v-if="results" class="grid grid-cols-2 lg:grid-cols-4 gap-4">
          <Card class="hover:shadow-md transition-all">
            <CardContent class="p-4 flex items-center justify-between">
              <div>
                <p class="text-sm text-muted-foreground">Groups</p>
                <p class="text-2xl font-bold mt-0.5">{{ summaryStats.totalGroups }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center">
                <Hash class="h-5 w-5 text-primary" />
              </div>
            </CardContent>
          </Card>
          <Card class="hover:shadow-md transition-all">
            <CardContent class="p-4 flex items-center justify-between">
              <div>
                <p class="text-sm text-muted-foreground">Total (primary)</p>
                <p class="text-2xl font-bold mt-0.5 text-emerald-600">{{ formatNumber(summaryStats.primarySum) }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-emerald-500/10 flex items-center justify-center">
                <Calculator class="h-5 w-5 text-emerald-500" />
              </div>
            </CardContent>
          </Card>
          <Card class="hover:shadow-md transition-all">
            <CardContent class="p-4 flex items-center justify-between">
              <div>
                <p class="text-sm text-muted-foreground">Average</p>
                <p class="text-2xl font-bold mt-0.5 text-violet-600">{{ formatNumber(summaryStats.primaryAvg) }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-violet-500/10 flex items-center justify-center">
                <TrendingUp class="h-5 w-5 text-violet-500" />
              </div>
            </CardContent>
          </Card>
          <Card class="hover:shadow-md transition-all">
            <CardContent class="p-4 flex items-center justify-between">
              <div>
                <p class="text-sm text-muted-foreground">Range</p>
                <p class="text-lg font-bold mt-0.5 text-amber-600 tabular-nums">
                  {{ formatNumber(summaryStats.primaryMin) }} – {{ formatNumber(summaryStats.primaryMax) }}
                </p>
              </div>
              <div class="h-10 w-10 rounded-full bg-amber-500/10 flex items-center justify-center">
                <ArrowUpDown class="h-5 w-5 text-amber-500" />
              </div>
            </CardContent>
          </Card>
        </div>

        <!-- ── Builder ──────────────────────────────────────────────────── -->
        <Card>
          <CardHeader>
            <div class="flex items-center gap-2">
              <BarChart3 class="h-5 w-5" />
              <CardTitle>Build Aggregation</CardTitle>
            </div>
            <CardDescription>
              Select group-by fields and aggregation functions. The runtime translates your choices into an OData
              <code class="text-xs bg-muted px-1.5 py-0.5 rounded">$apply</code> query.
            </CardDescription>
          </CardHeader>
          <CardContent class="space-y-4">
            <AggregationBuilder
              ref="builderRef"
              :fields="fields"
              :isLoading="aggLoading"
              @execute="handleExecute"
            />

            <!-- Live $apply preview -->
            <div v-if="applyPreview" class="rounded-lg border bg-muted/30 p-4">
              <div class="flex items-center justify-between mb-2">
                <div class="flex items-center gap-2 text-sm font-medium">
                  <Code2 class="h-4 w-4 text-muted-foreground" />
                  Generated <code class="text-xs bg-muted px-1.5 py-0.5 rounded">$apply</code>
                </div>
                <Button
                  variant="ghost"
                  size="sm"
                  class="h-7 text-xs"
                  @click="showODataUrl = !showODataUrl"
                >
                  {{ showODataUrl ? 'Hide URL' : 'Show full URL' }}
                </Button>
              </div>
              <pre class="text-xs font-mono text-foreground/80 break-all whitespace-pre-wrap">{{ applyPreview }}</pre>
              <div v-if="showODataUrl" class="mt-2 pt-2 border-t">
                <p class="text-xs text-muted-foreground mb-1">Full OData request URL:</p>
                <pre class="text-xs font-mono text-primary/80 break-all whitespace-pre-wrap">{{ odataPreview }}</pre>
              </div>
            </div>

            <MessageStrip
              v-if="aggError"
              type="error"
              :title="aggError"
              :closable="true"
              @close="() => {}"
            />
          </CardContent>
        </Card>

        <!-- ── Loading indicator ─────────────────────────────────────────── -->
        <div v-if="aggLoading && !results" class="flex flex-col items-center justify-center py-16">
          <Spinner size="lg" />
          <p class="text-muted-foreground mt-3 text-sm">Executing aggregation query…</p>
        </div>

        <!-- ── Results ───────────────────────────────────────────────────── -->
        <template v-if="results">

          <!-- Chart -->
          <Card>
            <CardHeader class="pb-3">
              <div class="flex items-center justify-between">
                <div class="flex items-center gap-2">
                  <BarChart3 class="h-5 w-5" />
                  <CardTitle>Visualization</CardTitle>
                </div>
                <div class="flex items-center gap-2">
                  <Badge variant="secondary">{{ results.rawData.length }} groups</Badge>
                  <Button variant="outline" size="sm" @click="execute" :disabled="aggLoading">
                    <RefreshCw class="mr-1.5 h-3.5 w-3.5" />
                    Refresh
                  </Button>
                </div>
              </div>
            </CardHeader>
            <CardContent>
              <AggregationChart
                :results="results"
                :chartType="chartType"
                @update:chartType="chartType = $event"
              />
            </CardContent>
          </Card>

          <!-- Results table -->
          <Card>
            <CardHeader class="pb-3">
              <div class="flex items-center justify-between">
                <div class="flex items-center gap-2">
                  <CardTitle>Results Table</CardTitle>
                  <Badge variant="outline" class="text-xs">{{ results.rawData.length }} rows</Badge>
                </div>
                <Button variant="outline" size="sm" @click="handleExportCsv">
                  <Download class="mr-2 h-4 w-4" />
                  Export CSV
                </Button>
              </div>
            </CardHeader>
            <CardContent class="p-0">
              <div class="overflow-x-auto">
                <Table>
                  <TableHeader>
                    <TableRow class="hover:bg-transparent">
                      <TableHead
                        v-for="col in resultColumns"
                        :key="col"
                        class="cursor-pointer select-none whitespace-nowrap"
                        :class="{
                          'bg-blue-50/60 dark:bg-blue-950/20': groupByColumns.includes(col),
                          'bg-emerald-50/60 dark:bg-emerald-950/20': aggregateColumns.includes(col),
                        }"
                        @click="handleSort(col)"
                      >
                        <div class="flex items-center gap-1.5">
                          {{ col }}
                          <Badge
                            v-if="groupByColumns.includes(col)"
                            variant="outline"
                            class="text-[10px] px-1 py-0 font-normal"
                          >group</Badge>
                          <Badge
                            v-else-if="aggregateColumns.includes(col)"
                            variant="outline"
                            class="text-[10px] px-1 py-0 font-normal text-emerald-600"
                          >agg</Badge>
                          <ArrowUpDown
                            class="h-3 w-3 text-muted-foreground"
                            :class="{ 'text-foreground': tableSortField === col }"
                          />
                        </div>
                      </TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    <TableRow v-for="(row, i) in sortedData" :key="i">
                      <TableCell
                        v-for="col in resultColumns"
                        :key="col"
                        class="whitespace-nowrap"
                        :class="{
                          'bg-blue-50/30 dark:bg-blue-950/10 font-medium': groupByColumns.includes(col),
                          'bg-emerald-50/30 dark:bg-emerald-950/10 tabular-nums': aggregateColumns.includes(col),
                        }"
                      >
                        {{ aggregateColumns.includes(col) ? formatNumber(row[col]) : (row[col] ?? '') }}
                      </TableCell>
                    </TableRow>
                    <!-- Totals row -->
                    <TableRow v-if="totalRow" class="border-t-2 font-semibold bg-muted/50">
                      <TableCell
                        v-for="(col, i) in resultColumns"
                        :key="col"
                        class="whitespace-nowrap"
                      >
                        <template v-if="totalRow[col] != null">{{ formatNumber(totalRow[col]) }}</template>
                        <template v-else-if="i === 0">Total</template>
                      </TableCell>
                    </TableRow>
                  </TableBody>
                </Table>
              </div>
            </CardContent>
          </Card>

        </template>

        <!-- ── Empty state ───────────────────────────────────────────────── -->
        <Card v-if="!results && !aggLoading && !aggError">
          <CardContent class="flex flex-col items-center justify-center py-16">
            <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
              <BarChart3 class="h-8 w-8 text-muted-foreground" />
            </div>
            <h3 class="text-lg font-semibold mb-1">Configure &amp; Run</h3>
            <p class="text-muted-foreground text-sm text-center max-w-sm">
              Pick a Quick Start scenario above or use the builder to define group-by fields and aggregations,
              then click <strong>Run Aggregation</strong>.
            </p>
          </CardContent>
        </Card>

      </template>

      <!-- ── How it works ─────────────────────────────────────────────────── -->
      <Card>
        <CardHeader>
          <div class="flex items-center gap-2">
            <Code2 class="h-5 w-5 text-muted-foreground" />
            <CardTitle class="text-base">How OData $apply works</CardTitle>
          </div>
        </CardHeader>
        <CardContent>
          <div class="grid md:grid-cols-2 gap-6 text-sm">
            <div class="space-y-3">
              <p class="font-medium">Basic form</p>
              <pre class="bg-muted/60 rounded-lg p-3 text-xs font-mono overflow-x-auto">GET /odata/Module/Entity
  ?$apply=groupby(
    (FieldA,FieldB),
    aggregate(
      NumericField with sum as Total,
      $count as Count
    )
  )</pre>
              <p class="text-muted-foreground text-xs">
                <code>groupby()</code> partitions rows. <code>aggregate()</code> computes metrics per partition.
                Supported functions: <strong>sum, avg, min, max, count, countdistinct</strong>.
              </p>
            </div>
            <div class="space-y-3">
              <p class="font-medium">With pre-filter</p>
              <pre class="bg-muted/60 rounded-lg p-3 text-xs font-mono overflow-x-auto">GET /odata/Module/Entity
  ?$apply=filter(Status eq 'Confirmed')
    /groupby(
      (Region),
      aggregate(
        Amount with sum as Revenue
      )
    )</pre>
              <p class="text-muted-foreground text-xs">
                Chain a <code>filter()</code> transformation before <code>groupby()</code> to limit the input rows.
                This maps to a SQL <code>WHERE</code> clause before <code>GROUP BY</code>.
              </p>
            </div>
          </div>
        </CardContent>
      </Card>

    </div>
  </DefaultLayout>
</template>
