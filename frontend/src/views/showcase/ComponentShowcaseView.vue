<script setup lang="ts">
import { ref, reactive, onUnmounted } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { useConfirmDialog } from '@/composables/useConfirmDialog'
import { ConfirmDialog } from '@/components/common'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  MicroChart,
  SparklineChart,
  BarChart,
  DonutChart,
  TrendIndicator,
  KpiCardEnhanced,
} from '@/components/analytics'
import SmartField from '@/components/smart/SmartField.vue'
import SmartTable from '@/components/smart/SmartTable.vue'
import SmartForm from '@/components/smart/SmartForm.vue'
import SmartFilterBar from '@/components/smart/SmartFilterBar.vue'
import MessageStrip from '@/components/smart/MessageStrip.vue'
import DraftIndicator from '@/components/smart/DraftIndicator.vue'
import FlexibleColumnLayout from '@/components/layout/FlexibleColumnLayout.vue'
import ObjectPageLayout from '@/components/layout/ObjectPageLayout.vue'
import ObjectPageHeader from '@/components/layout/ObjectPageHeader.vue'
import ObjectPageHeaderKpi from '@/components/layout/ObjectPageHeaderKpi.vue'
import ObjectPageHeaderAttribute from '@/components/layout/ObjectPageHeaderAttribute.vue'
import ObjectPageSection from '@/components/layout/ObjectPageSection.vue'
import ObjectPageSubSection from '@/components/layout/ObjectPageSubSection.vue'
import AppTile from '@/components/shell/AppTile.vue'
import LaunchpadSection from '@/components/shell/LaunchpadSection.vue'
import type { FclLayout } from '@/composables/useFcl'
import type { EntityMetadata, FieldMetadata } from '@/types/metadata'
import type { DonutChartItem, BarChartItem } from '@/components/analytics'
import {
  DollarSign,
  ShoppingCart,
  Users,
  Package,
  BarChart3,
  FileText,
  Settings,
  Globe,
  Shield,
  Database,
  Layers,
  TrendingUp,
  Pencil,
  Trash2,
  Share2,
  X,
  Save,
  Truck,
  CreditCard,
  Clock,
  BookOpen,
  FolderTree,
  Table2,
  Upload,
  PanelTop,
  LayoutGrid,
  Wand2,
  MessageSquare,
  Hash,
  Columns3,
  // Phase F icons
  GalleryHorizontalEnd,
  Palette,
  SlidersHorizontal,
  Star,
  GitBranch,
  GanttChartSquare,
  PanelLeftClose,
  // Phase G icons
  Tags,
  AlignVerticalJustifyStart,
  MoreHorizontal,
  PanelRight,
  Calendar,
  Rss,
  Tag,
} from 'lucide-vue-next'

const confirmDialog = useConfirmDialog()

// ============================================================================
// Section 1: Analytics sample data
// ============================================================================

const microBarData = [4, 7, 3, 8, 5, 9, 6]
const microLineData = [2, 5, 3, 7, 4, 8, 6, 9]
const microBulletData = [75]
const microRadialData = [68]
const microStackedData = [30, 45, 25]

const sparklineRevenue = [
  12400, 13200, 11800, 14500, 15200, 14800, 16100, 17300, 16800, 18200, 19500, 21000,
]

const monthlySalesData: BarChartItem[] = [
  { label: 'Jan', value: 42000 },
  { label: 'Feb', value: 38500 },
  { label: 'Mar', value: 51200 },
  { label: 'Apr', value: 47800 },
  { label: 'May', value: 55100 },
  { label: 'Jun', value: 61400 },
]

const regionSalesData: BarChartItem[] = [
  { label: 'North America', value: 128000 },
  { label: 'Europe', value: 96000 },
  { label: 'Asia Pacific', value: 78000 },
  { label: 'Latin America', value: 42000 },
  { label: 'Middle East', value: 31000 },
]

const donutData: DonutChartItem[] = [
  { label: 'Enterprise', value: 45, color: 'hsl(var(--primary))' },
  { label: 'Professional', value: 30, color: 'rgb(16, 185, 129)' },
  { label: 'Starter', value: 15, color: 'rgb(245, 158, 11)' },
  { label: 'Trial', value: 10, color: 'rgb(139, 92, 246)' },
]

const isDonutMode = ref(true)

// ============================================================================
// Section 2: Smart Components sample data
// ============================================================================

const salesOrderFields: FieldMetadata[] = [
  {
    name: 'ID',
    type: 'UUID',
    displayName: 'Order ID',
    isRequired: true,
    isReadOnly: true,
    isComputed: false,
    annotations: {},
  },
  {
    name: 'OrderNumber',
    type: 'String',
    displayName: 'Order Number',
    isRequired: true,
    isReadOnly: false,
    isComputed: false,
    maxLength: 20,
    annotations: {},
  },
  {
    name: 'Customer',
    type: 'String',
    displayName: 'Customer',
    isRequired: true,
    isReadOnly: false,
    isComputed: false,
    maxLength: 100,
    annotations: {},
  },
  {
    name: 'OrderDate',
    type: 'Date',
    displayName: 'Order Date',
    isRequired: true,
    isReadOnly: false,
    isComputed: false,
    annotations: {},
  },
  {
    name: 'TotalAmount',
    type: 'Decimal',
    displayName: 'Total Amount',
    isRequired: true,
    isReadOnly: false,
    isComputed: false,
    precision: 18,
    scale: 2,
    annotations: {},
  },
  {
    name: 'Status',
    type: 'Enum',
    displayName: 'Status',
    isRequired: true,
    isReadOnly: false,
    isComputed: false,
    enumValues: [
      { name: 'Open', value: 1 },
      { name: 'Processing', value: 2 },
      { name: 'Shipped', value: 3 },
      { name: 'Delivered', value: 4 },
      { name: 'Cancelled', value: 5 },
    ],
    annotations: {},
  },
  {
    name: 'IsUrgent',
    type: 'Boolean',
    displayName: 'Urgent',
    isRequired: false,
    isReadOnly: false,
    isComputed: false,
    annotations: {},
  },
  {
    name: 'Notes',
    type: 'String',
    displayName: 'Notes',
    isRequired: false,
    isReadOnly: false,
    isComputed: false,
    maxLength: 2000,
    annotations: {},
  },
  {
    name: 'CreatedAt',
    type: 'DateTime',
    displayName: 'Created At',
    isRequired: false,
    isReadOnly: true,
    isComputed: true,
    annotations: {},
  },
]

const salesOrderMetadata: EntityMetadata = {
  name: 'SalesOrder',
  namespace: 'showcase',
  displayName: 'Sales Order',
  fields: salesOrderFields,
  keys: ['ID'],
  associations: [],
  annotations: {},
}

const sampleOrders: Record<string, unknown>[] = [
  {
    ID: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
    OrderNumber: 'SO-2026-001',
    Customer: 'Acme Corporation',
    OrderDate: '2026-01-15',
    TotalAmount: 24850.0,
    Status: 1,
    IsUrgent: false,
    Notes: 'Standard delivery terms',
    CreatedAt: '2026-01-15T09:30:00Z',
  },
  {
    ID: 'b2c3d4e5-f6a7-8901-bcde-f12345678901',
    OrderNumber: 'SO-2026-002',
    Customer: 'Global Industries Ltd',
    OrderDate: '2026-01-22',
    TotalAmount: 87200.5,
    Status: 2,
    IsUrgent: true,
    Notes: 'Express shipping requested',
    CreatedAt: '2026-01-22T14:15:00Z',
  },
  {
    ID: 'c3d4e5f6-a7b8-9012-cdef-123456789012',
    OrderNumber: 'SO-2026-003',
    Customer: 'TechStart Solutions',
    OrderDate: '2026-02-01',
    TotalAmount: 12340.75,
    Status: 3,
    IsUrgent: false,
    Notes: '',
    CreatedAt: '2026-02-01T08:45:00Z',
  },
  {
    ID: 'd4e5f6a7-b8c9-0123-defa-234567890123',
    OrderNumber: 'SO-2026-004',
    Customer: 'Meridian Healthcare',
    OrderDate: '2026-02-05',
    TotalAmount: 156780.0,
    Status: 4,
    IsUrgent: false,
    Notes: 'Delivered to warehouse B',
    CreatedAt: '2026-02-05T11:20:00Z',
  },
  {
    ID: 'e5f6a7b8-c9d0-1234-efab-345678901234',
    OrderNumber: 'SO-2026-005',
    Customer: 'Nordic Electronics AB',
    OrderDate: '2026-02-08',
    TotalAmount: 43500.25,
    Status: 5,
    IsUrgent: false,
    Notes: 'Cancelled by customer',
    CreatedAt: '2026-02-08T16:00:00Z',
  },
]

const smartFieldMode = ref<'display' | 'edit'>('display')
const smartFieldValues = ref<Record<string, unknown>>({
  ID: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
  OrderNumber: 'SO-2026-001',
  Customer: 'Acme Corporation',
  OrderDate: '2026-01-15',
  TotalAmount: 24850.0,
  Status: 1,
  IsUrgent: false,
  Notes: 'Standard delivery terms apply to this order.',
  CreatedAt: '2026-01-15T09:30:00Z',
})

function updateFieldValue(fieldName: string, value: unknown) {
  smartFieldValues.value[fieldName] = value
}

// ============================================================================
// Section 3: Flexible Column Layout
// ============================================================================

const fclLayout = ref<FclLayout>('OneColumn')

const fclLayouts: { label: string; value: FclLayout }[] = [
  { label: 'One Column', value: 'OneColumn' },
  { label: 'Two Columns (Mid)', value: 'TwoColumnsMidExpanded' },
  { label: 'Two Columns (Begin)', value: 'TwoColumnsBeginExpanded' },
  { label: 'Three Columns (Mid)', value: 'ThreeColumnsMidExpanded' },
  { label: 'Three Columns (End)', value: 'ThreeColumnsEndExpanded' },
  { label: 'Mid Fullscreen', value: 'MidColumnFullScreen' },
  { label: 'End Fullscreen', value: 'EndColumnFullScreen' },
]

// ============================================================================
// Section 4: Shell Components
// ============================================================================

const launchpadCollapsed = ref(false)
const adminCollapsed = ref(false)

// ============================================================================
// Section 7: Draft Indicator — inject demo drafts into localStorage BEFORE
// child components render (DraftManager restores from localStorage in constructor)
// ============================================================================
const DEMO_DRAFT_KEYS = [
  'draft-showcase-edit-demo',
  'draft-showcase-new-demo',
]

// Write immediately during setup so DraftIndicator's DraftManager sees them
// Metadata goes to localStorage, sensitive data to sessionStorage
localStorage.setItem('bmmdl_draft_meta_' + DEMO_DRAFT_KEYS[0], JSON.stringify({
  draftKey: DEMO_DRAFT_KEYS[0],
  entityKey: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
  module: 'showcase',
  entitySet: 'SalesOrders',
  state: 'editing',
  createdAt: new Date().toISOString(),
  lastSaved: new Date(Date.now() - 5 * 60_000).toISOString(),
  dirtyFields: ['Customer'],
}))
sessionStorage.setItem('bmmdl_draft_data_' + DEMO_DRAFT_KEYS[0], JSON.stringify({
  OrderNumber: 'SO-2026-001', Customer: 'Acme Corporation',
}))
localStorage.setItem('bmmdl_draft_meta_' + DEMO_DRAFT_KEYS[1], JSON.stringify({
  draftKey: DEMO_DRAFT_KEYS[1],
  module: 'showcase',
  entitySet: 'SalesOrders',
  state: 'new',
  createdAt: new Date(Date.now() - 12 * 60_000).toISOString(),
  dirtyFields: ['Customer'],
}))
sessionStorage.setItem('bmmdl_draft_data_' + DEMO_DRAFT_KEYS[1], JSON.stringify({
  Customer: 'New Customer Inc.',
}))

onUnmounted(() => {
  DEMO_DRAFT_KEYS.forEach(key => {
    localStorage.removeItem('bmmdl_draft_meta_' + key)
    sessionStorage.removeItem('bmmdl_draft_data_' + key)
  })
})

// ============================================================================
// Section 8: Object Page demo data
// ============================================================================

// Fields for the "General" sub-section
const opGeneralFields: FieldMetadata[] = [
  { name: 'OrderNumber', type: 'String', displayName: 'Order Number', isRequired: true, isReadOnly: false, isComputed: false, maxLength: 20, annotations: {} },
  { name: 'Customer', type: 'String', displayName: 'Customer', isRequired: true, isReadOnly: false, isComputed: false, maxLength: 100, annotations: {} },
  { name: 'OrderDate', type: 'Date', displayName: 'Order Date', isRequired: true, isReadOnly: false, isComputed: false, annotations: {} },
  { name: 'Status', type: 'Enum', displayName: 'Status', isRequired: true, isReadOnly: false, isComputed: false, enumValues: [{ name: 'Open', value: 1 }, { name: 'Processing', value: 2 }, { name: 'Shipped', value: 3 }, { name: 'Delivered', value: 4 }], annotations: {} },
  { name: 'Priority', type: 'Enum', displayName: 'Priority', isRequired: false, isReadOnly: false, isComputed: false, enumValues: [{ name: 'Low', value: 1 }, { name: 'Normal', value: 2 }, { name: 'High', value: 3 }, { name: 'Critical', value: 4 }], annotations: {} },
  { name: 'IsUrgent', type: 'Boolean', displayName: 'Urgent', isRequired: false, isReadOnly: false, isComputed: false, annotations: {} },
]

const opGeneralValues: Record<string, unknown> = {
  OrderNumber: 'SO-2026-001',
  Customer: 'Acme Corporation',
  OrderDate: '2026-01-15',
  Status: 1,
  Priority: 3,
  IsUrgent: true,
}

// Fields for "Financial" sub-section
const opFinanceFields: FieldMetadata[] = [
  { name: 'Subtotal', type: 'Decimal', displayName: 'Subtotal', isRequired: true, isReadOnly: false, isComputed: false, precision: 18, scale: 2, annotations: {} },
  { name: 'DiscountPercent', type: 'Decimal', displayName: 'Discount (%)', isRequired: false, isReadOnly: false, isComputed: false, precision: 5, scale: 2, annotations: {} },
  { name: 'TaxAmount', type: 'Decimal', displayName: 'Tax Amount', isRequired: true, isReadOnly: true, isComputed: true, precision: 18, scale: 2, annotations: {} },
  { name: 'TotalAmount', type: 'Decimal', displayName: 'Total Amount', isRequired: true, isReadOnly: true, isComputed: true, precision: 18, scale: 2, annotations: {} },
  { name: 'Currency', type: 'String', displayName: 'Currency', isRequired: true, isReadOnly: false, isComputed: false, maxLength: 3, annotations: {} },
  { name: 'PaymentTerms', type: 'String', displayName: 'Payment Terms', isRequired: false, isReadOnly: false, isComputed: false, maxLength: 50, annotations: {} },
]

const opFinanceValues: Record<string, unknown> = {
  Subtotal: 22150.00,
  DiscountPercent: 5.00,
  TaxAmount: 2700.00,
  TotalAmount: 24850.00,
  Currency: 'USD',
  PaymentTerms: 'Net 30',
}

// Fields for "Shipping" sub-section
const opShippingFields: FieldMetadata[] = [
  { name: 'ShipToAddress', type: 'String', displayName: 'Ship-To Address', isRequired: true, isReadOnly: false, isComputed: false, maxLength: 200, annotations: {} },
  { name: 'ShipToCity', type: 'String', displayName: 'City', isRequired: true, isReadOnly: false, isComputed: false, maxLength: 100, annotations: {} },
  { name: 'ShipToCountry', type: 'String', displayName: 'Country', isRequired: true, isReadOnly: false, isComputed: false, maxLength: 50, annotations: {} },
  { name: 'ShipToPostalCode', type: 'String', displayName: 'Postal Code', isRequired: true, isReadOnly: false, isComputed: false, maxLength: 10, annotations: {} },
  { name: 'ShippingMethod', type: 'Enum', displayName: 'Shipping Method', isRequired: true, isReadOnly: false, isComputed: false, enumValues: [{ name: 'Standard', value: 1 }, { name: 'Express', value: 2 }, { name: 'Overnight', value: 3 }, { name: 'Freight', value: 4 }], annotations: {} },
  { name: 'EstimatedDelivery', type: 'Date', displayName: 'Est. Delivery', isRequired: false, isReadOnly: true, isComputed: true, annotations: {} },
]

const opShippingValues: Record<string, unknown> = {
  ShipToAddress: '123 Enterprise Boulevard, Suite 400',
  ShipToCity: 'San Francisco',
  ShipToCountry: 'United States',
  ShipToPostalCode: '94105',
  ShippingMethod: 2,
  EstimatedDelivery: '2026-01-22',
}

// ObjectPage edit mode state
const opDraftValues = reactive<Record<string, unknown>>({
  ...opGeneralValues,
  ...opFinanceValues,
  ...opShippingValues,
})
const opIsEditing = ref(false)
const opShareCopied = ref(false)

function opStartEdit() {
  Object.assign(opDraftValues, opGeneralValues, opFinanceValues, opShippingValues)
  opIsEditing.value = true
}
function opCancelEdit() {
  Object.assign(opDraftValues, opGeneralValues, opFinanceValues, opShippingValues)
  opIsEditing.value = false
}
function opSaveEdit() {
  // Persist edits back to display values (demo only)
  Object.assign(opGeneralValues, Object.fromEntries(opGeneralFields.map(f => [f.name, opDraftValues[f.name]])))
  Object.assign(opFinanceValues, Object.fromEntries(opFinanceFields.filter(f => !f.isComputed).map(f => [f.name, opDraftValues[f.name]])))
  Object.assign(opShippingValues, Object.fromEntries(opShippingFields.filter(f => !f.isComputed).map(f => [f.name, opDraftValues[f.name]])))
  opIsEditing.value = false
}
async function opDelete() {
  const confirmed = await confirmDialog.confirm({
    title: 'Delete Sales Order',
    description: 'Delete SO-2026-001? (Demo only — no data will be changed.)',
    confirmLabel: 'Delete',
    variant: 'destructive',
  })
  if (confirmed) {
    alert('Sales order SO-2026-001 deleted. (Demo)')
  }
}
function opShare() {
  navigator.clipboard?.writeText(window.location.href).catch(() => {})
  opShareCopied.value = true
  setTimeout(() => { opShareCopied.value = false }, 2000)
}

// Line items for the "Items" section table
const lineItemFields: FieldMetadata[] = [
  { name: 'ItemNumber', type: 'Integer', displayName: 'Item', isRequired: true, isReadOnly: false, isComputed: false, annotations: {} },
  { name: 'ProductName', type: 'String', displayName: 'Product', isRequired: true, isReadOnly: false, isComputed: false, maxLength: 100, annotations: {} },
  { name: 'Quantity', type: 'Integer', displayName: 'Qty', isRequired: true, isReadOnly: false, isComputed: false, annotations: {} },
  { name: 'UnitPrice', type: 'Decimal', displayName: 'Unit Price', isRequired: true, isReadOnly: false, isComputed: false, precision: 18, scale: 2, annotations: {} },
  { name: 'LineTotal', type: 'Decimal', displayName: 'Line Total', isRequired: true, isReadOnly: true, isComputed: true, precision: 18, scale: 2, annotations: {} },
]

const lineItemMeta: EntityMetadata = {
  name: 'SalesOrderItem',
  namespace: 'showcase',
  displayName: 'Order Item',
  fields: lineItemFields,
  keys: ['ItemNumber'],
  associations: [],
  annotations: {},
}

const lineItemRows: Record<string, unknown>[] = [
  { ItemNumber: 10, ProductName: 'Enterprise Server License', Quantity: 2, UnitPrice: 8500.00, LineTotal: 17000.00 },
  { ItemNumber: 20, ProductName: 'Premium Support Plan (12 mo)', Quantity: 1, UnitPrice: 3200.00, LineTotal: 3200.00 },
  { ItemNumber: 30, ProductName: 'Data Migration Service', Quantity: 1, UnitPrice: 1500.00, LineTotal: 1500.00 },
  { ItemNumber: 40, ProductName: 'User Training Package', Quantity: 3, UnitPrice: 150.00, LineTotal: 450.00 },
]

// Notes for the "Notes" section
const opNotes = [
  { date: '2026-01-15 09:30', author: 'John Smith', text: 'Order created. Customer requested express shipping for initial deployment.' },
  { date: '2026-01-16 14:22', author: 'Sarah Johnson', text: 'Discount approved by regional manager. 5% volume discount applied.' },
  { date: '2026-01-17 11:05', author: 'System', text: 'Payment authorization received. Processing initiated.' },
]

// ============================================================================
// Section 9: Advanced Field Types
// ============================================================================

const advFieldMode = ref<'display' | 'edit'>('edit')

const advTimeField: FieldMetadata = {
  name: 'MeetingTime',
  type: 'Time',
  displayName: 'Meeting Time',
  isRequired: true,
  isReadOnly: false,
  isComputed: false,
  annotations: {},
}

const advCurrencyField: FieldMetadata = {
  name: 'UnitPrice',
  type: 'Decimal',
  displayName: 'Unit Price',
  isRequired: true,
  isReadOnly: false,
  isComputed: false,
  precision: 18,
  scale: 2,
  annotations: { '@Semantics.CurrencyCode': 'EUR' },
}

const advDateRangeField: FieldMetadata = {
  name: 'ContractPeriod',
  type: 'Date',
  displayName: 'Contract Period',
  isRequired: false,
  isReadOnly: false,
  isComputed: false,
  annotations: { '@UI.DateRange': true },
}

const advMultiSelectField: FieldMetadata = {
  name: 'Categories',
  type: 'Enum',
  displayName: 'Product Categories',
  isRequired: false,
  isReadOnly: false,
  isComputed: false,
  enumValues: [
    { name: 'Electronics', value: 'electronics', displayName: 'Electronics' },
    { name: 'Clothing', value: 'clothing', displayName: 'Clothing' },
    { name: 'Books', value: 'books', displayName: 'Books' },
    { name: 'HomeGarden', value: 'home', displayName: 'Home & Garden' },
    { name: 'Sports', value: 'sports', displayName: 'Sports' },
    { name: 'Toys', value: 'toys', displayName: 'Toys' },
  ],
  annotations: { '@UI.MultiSelect': true },
}

const advMaskedField: FieldMetadata = {
  name: 'Phone',
  type: 'String',
  displayName: 'Phone Number',
  isRequired: false,
  isReadOnly: false,
  isComputed: false,
  maxLength: 14,
  annotations: { '@UI.InputMask': 'phone' },
}

const advFieldValues = ref<Record<string, unknown>>({
  MeetingTime: '14:30:00',
  UnitPrice: 1249.99,
  ContractPeriod: { from: '2026-01-01', to: '2026-12-31' },
  Categories: ['electronics', 'books'],
  Phone: '5551234567',
})

function updateAdvFieldValue(fieldName: string, value: unknown) {
  advFieldValues.value[fieldName] = value
}
</script>

<template>
  <DefaultLayout>
    <div class="space-y-10 pb-12">
      <!-- Page Header -->
      <div>
        <h1 class="text-3xl font-bold text-foreground">Component Showcase</h1>
        <p class="mt-1 text-muted-foreground">
          Interactive gallery of all BMMDL UI components with sample data
        </p>
      </div>

      <!-- Phase Overviews -->
      <div class="flex flex-wrap gap-3">
        <RouterLink to="/showcase/recent-features">
          <Button class="gap-2" variant="default">
            <Shield class="h-4 w-4" />
            Recent Backend Features
          </Button>
        </RouterLink>
        <RouterLink to="/showcase/aggregation">
          <Button class="gap-2" variant="default">
            <BarChart3 class="h-4 w-4" />
            Aggregation Playground
          </Button>
        </RouterLink>
        <RouterLink to="/showcase/phase-f">
          <Button class="gap-2">
            <Layers class="h-4 w-4" />
            Phase F — Interactive & Layout
          </Button>
        </RouterLink>
        <RouterLink to="/showcase/phase-g">
          <Button class="gap-2">
            <Layers class="h-4 w-4" />
            Phase G — Enterprise Components
          </Button>
        </RouterLink>
      </div>

      <!-- Dedicated Demo Pages -->
      <div class="flex flex-wrap gap-3">
        <RouterLink to="/showcase/data-grid-pro">
          <Button variant="outline" class="gap-2">
            <Layers class="h-4 w-4" />
            Data Grid Pro
          </Button>
        </RouterLink>
        <RouterLink to="/showcase/advanced-fields">
          <Button variant="outline" class="gap-2">
            <Settings class="h-4 w-4" />
            Advanced Field Types
          </Button>
        </RouterLink>
        <RouterLink to="/showcase/tree-table">
          <Button variant="outline" class="gap-2">
            <FolderTree class="h-4 w-4" />
            Tree Table
          </Button>
        </RouterLink>
        <RouterLink to="/showcase/analytical-table">
          <Button variant="outline" class="gap-2">
            <Table2 class="h-4 w-4" />
            Analytical Table
          </Button>
        </RouterLink>
        <RouterLink to="/showcase/upload-collection">
          <Button variant="outline" class="gap-2">
            <Upload class="h-4 w-4" />
            Upload Collection
          </Button>
        </RouterLink>
        <RouterLink to="/showcase/dynamic-page">
          <Button variant="outline" class="gap-2">
            <PanelTop class="h-4 w-4" />
            Dynamic Page
          </Button>
        </RouterLink>
        <RouterLink to="/showcase/icon-tab-bar">
          <Button variant="outline" class="gap-2">
            <LayoutGrid class="h-4 w-4" />
            Icon Tab Bar
          </Button>
        </RouterLink>
        <RouterLink to="/showcase/p13n-dialog">
          <Button variant="outline" class="gap-2">
            <Settings class="h-4 w-4" />
            P13n Dialog
          </Button>
        </RouterLink>
        <RouterLink to="/showcase/wizard">
          <Button variant="outline" class="gap-2">
            <Wand2 class="h-4 w-4" />
            Wizard
          </Button>
        </RouterLink>
        <RouterLink to="/showcase/message-box">
          <Button variant="outline" class="gap-2">
            <MessageSquare class="h-4 w-4" />
            Message Box
          </Button>
        </RouterLink>
        <RouterLink to="/showcase/step-input">
          <Button variant="outline" class="gap-2">
            <Hash class="h-4 w-4" />
            Step Input
          </Button>
        </RouterLink>
        <RouterLink to="/showcase/responsive-table">
          <Button variant="outline" class="gap-2">
            <Columns3 class="h-4 w-4" />
            Responsive Table
          </Button>
        </RouterLink>
      </div>

      <!-- Phase F Demos -->
      <div>
        <h3 class="text-sm font-medium text-muted-foreground mb-3">Phase F — Interactive & Layout</h3>
        <div class="flex flex-wrap gap-3">
          <RouterLink to="/showcase/carousel">
            <Button variant="outline" class="gap-2">
              <GalleryHorizontalEnd class="h-4 w-4" />
              Carousel
            </Button>
          </RouterLink>
          <RouterLink to="/showcase/color-picker">
            <Button variant="outline" class="gap-2">
              <Palette class="h-4 w-4" />
              Color Picker
            </Button>
          </RouterLink>
          <RouterLink to="/showcase/range-slider">
            <Button variant="outline" class="gap-2">
              <SlidersHorizontal class="h-4 w-4" />
              Range Slider
            </Button>
          </RouterLink>
          <RouterLink to="/showcase/rating-indicator">
            <Button variant="outline" class="gap-2">
              <Star class="h-4 w-4" />
              Rating Indicator
            </Button>
          </RouterLink>
          <RouterLink to="/showcase/process-flow">
            <Button variant="outline" class="gap-2">
              <GitBranch class="h-4 w-4" />
              Process Flow
            </Button>
          </RouterLink>
          <RouterLink to="/showcase/gantt-chart">
            <Button variant="outline" class="gap-2">
              <GanttChartSquare class="h-4 w-4" />
              Gantt Chart
            </Button>
          </RouterLink>
          <RouterLink to="/showcase/split-app">
            <Button variant="outline" class="gap-2">
              <PanelLeftClose class="h-4 w-4" />
              Split App
            </Button>
          </RouterLink>
        </div>
      </div>

      <!-- Phase G Demos -->
      <div>
        <h3 class="text-sm font-medium text-muted-foreground mb-3">Phase G — Enterprise Components</h3>
        <div class="flex flex-wrap gap-3">
          <RouterLink to="/showcase/token-input">
            <Button variant="outline" class="gap-2">
              <Tags class="h-4 w-4" />
              Token Input
            </Button>
          </RouterLink>
          <RouterLink to="/showcase/timeline">
            <Button variant="outline" class="gap-2">
              <AlignVerticalJustifyStart class="h-4 w-4" />
              Timeline
            </Button>
          </RouterLink>
          <RouterLink to="/showcase/overflow-toolbar">
            <Button variant="outline" class="gap-2">
              <MoreHorizontal class="h-4 w-4" />
              Overflow Toolbar
            </Button>
          </RouterLink>
          <RouterLink to="/showcase/dynamic-side-content">
            <Button variant="outline" class="gap-2">
              <PanelRight class="h-4 w-4" />
              Dynamic Side Content
            </Button>
          </RouterLink>
          <RouterLink to="/showcase/planning-calendar">
            <Button variant="outline" class="gap-2">
              <Calendar class="h-4 w-4" />
              Planning Calendar
            </Button>
          </RouterLink>
          <RouterLink to="/showcase/feed-list">
            <Button variant="outline" class="gap-2">
              <Rss class="h-4 w-4" />
              Feed List
            </Button>
          </RouterLink>
          <RouterLink to="/showcase/info-label">
            <Button variant="outline" class="gap-2">
              <Tag class="h-4 w-4" />
              Info Label
            </Button>
          </RouterLink>
        </div>
      </div>

      <!-- ================================================================ -->
      <!-- SECTION 1: Analytics Charts                                       -->
      <!-- ================================================================ -->
      <section class="space-y-6">
        <div class="flex items-center gap-3">
          <BarChart3 class="h-6 w-6 text-primary" />
          <h2 class="text-2xl font-semibold text-foreground">Analytics Charts</h2>
        </div>

        <!-- MicroCharts -->
        <Card>
          <CardHeader>
            <CardTitle>Micro Charts</CardTitle>
          </CardHeader>
          <CardContent>
            <div class="flex flex-wrap items-end gap-8">
              <div class="text-center space-y-2">
                <MicroChart type="bar" :data="microBarData" color="primary" :width="80" :height="32" />
                <p class="text-xs text-muted-foreground">Bar</p>
              </div>
              <div class="text-center space-y-2">
                <MicroChart type="line" :data="microLineData" color="emerald" :width="80" :height="32" />
                <p class="text-xs text-muted-foreground">Line</p>
              </div>
              <div class="text-center space-y-2">
                <MicroChart type="bullet" :data="microBulletData" color="amber" :width="80" :height="32" />
                <p class="text-xs text-muted-foreground">Bullet</p>
              </div>
              <div class="text-center space-y-2">
                <MicroChart type="radial" :data="microRadialData" color="violet" :width="40" :height="40" />
                <p class="text-xs text-muted-foreground">Radial</p>
              </div>
              <div class="text-center space-y-2">
                <MicroChart type="stacked" :data="microStackedData" color="cyan" :width="80" :height="32" />
                <p class="text-xs text-muted-foreground">Stacked</p>
              </div>
            </div>
          </CardContent>
        </Card>

        <!-- Sparkline -->
        <Card>
          <CardHeader>
            <CardTitle>Sparkline Chart</CardTitle>
          </CardHeader>
          <CardContent>
            <div class="flex items-center gap-6">
              <div>
                <p class="text-sm text-muted-foreground mb-1">Monthly Revenue Trend (12 months)</p>
                <SparklineChart
                  :data="sparklineRevenue"
                  :width="300"
                  :height="60"
                  color="emerald"
                  :show-area="true"
                  :show-dots="true"
                  :show-min-max="true"
                  :animate="true"
                />
              </div>
            </div>
          </CardContent>
        </Card>

        <!-- Bar Charts -->
        <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
          <Card>
            <CardHeader>
              <CardTitle>Vertical Bar Chart</CardTitle>
            </CardHeader>
            <CardContent>
              <BarChart
                :data="monthlySalesData"
                title="Monthly Sales (USD)"
                :height="280"
                orientation="vertical"
                :show-values="true"
                :show-grid="true"
                :animate="true"
              />
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>Horizontal Bar Chart</CardTitle>
            </CardHeader>
            <CardContent>
              <BarChart
                :data="regionSalesData"
                title="Revenue by Region (USD)"
                :height="280"
                orientation="horizontal"
                :show-values="true"
                :show-grid="true"
                :animate="true"
              />
            </CardContent>
          </Card>
        </div>

        <!-- Donut / Pie Chart -->
        <Card>
          <CardHeader class="flex flex-row items-center justify-between">
            <CardTitle>{{ isDonutMode ? 'Donut' : 'Pie' }} Chart</CardTitle>
            <Button variant="outline" size="sm" @click="isDonutMode = !isDonutMode">
              Switch to {{ isDonutMode ? 'Pie' : 'Donut' }}
            </Button>
          </CardHeader>
          <CardContent>
            <DonutChart
              :data="donutData"
              title="Subscription Breakdown"
              :size="220"
              :donut="isDonutMode"
              :show-legend="true"
              :show-total="true"
              :animate="true"
            />
          </CardContent>
        </Card>

        <!-- Trend Indicator -->
        <Card>
          <CardHeader>
            <CardTitle>Trend Indicators</CardTitle>
          </CardHeader>
          <CardContent>
            <div class="flex flex-wrap items-center gap-10">
              <div class="space-y-1">
                <p class="text-sm text-muted-foreground">Revenue (% change)</p>
                <TrendIndicator :value="21000" :previous-value="18200" format="percent" />
              </div>
              <div class="space-y-1">
                <p class="text-sm text-muted-foreground">Orders (absolute)</p>
                <TrendIndicator :value="342" :previous-value="310" format="absolute" />
              </div>
              <div class="space-y-1">
                <p class="text-sm text-muted-foreground">Churn (inverted)</p>
                <TrendIndicator :value="12" :previous-value="18" format="percent" :invert-colors="true" />
              </div>
              <div class="space-y-1">
                <p class="text-sm text-muted-foreground">Flat (compact)</p>
                <TrendIndicator :value="500" :previous-value="500" format="compact" />
              </div>
            </div>
          </CardContent>
        </Card>

        <!-- KPI Cards -->
        <div class="grid grid-cols-1 md:grid-cols-3 gap-6">
          <KpiCardEnhanced
            title="Total Revenue"
            value="$1.24M"
            description="Year-to-date revenue"
            :icon="DollarSign"
            color="emerald"
            trend="up"
            trend-value="+15.3%"
            :sparkline-data="sparklineRevenue"
            :target="1500000"
            unit="USD"
          />
          <KpiCardEnhanced
            title="Active Orders"
            value="342"
            description="Currently in pipeline"
            :icon="ShoppingCart"
            color="primary"
            trend="up"
            trend-value="+10.3%"
            :sparkline-data="[280, 295, 310, 305, 320, 335, 328, 342]"
          />
          <KpiCardEnhanced
            title="Customers"
            value="1,847"
            description="Registered accounts"
            :icon="Users"
            color="violet"
            trend="down"
            trend-value="-2.1%"
            :sparkline-data="[1900, 1880, 1870, 1862, 1855, 1850, 1848, 1847]"
          />
        </div>
      </section>

      <!-- ================================================================ -->
      <!-- SECTION 2: Smart Components                                       -->
      <!-- ================================================================ -->
      <section class="space-y-6">
        <div class="flex items-center gap-3">
          <Layers class="h-6 w-6 text-primary" />
          <h2 class="text-2xl font-semibold text-foreground">Smart Components</h2>
        </div>

        <!-- SmartField -->
        <Card>
          <CardHeader class="flex flex-row items-center justify-between">
            <CardTitle>SmartField ({{ smartFieldMode }} mode)</CardTitle>
            <Button
              variant="outline"
              size="sm"
              @click="smartFieldMode = smartFieldMode === 'display' ? 'edit' : 'display'"
            >
              Switch to {{ smartFieldMode === 'display' ? 'Edit' : 'Display' }}
            </Button>
          </CardHeader>
          <CardContent>
            <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div v-for="field in salesOrderFields" :key="field.name">
                <SmartField
                  :field="field"
                  :model-value="smartFieldValues[field.name]"
                  :mode="smartFieldMode"
                  module="showcase"
                  entity-set="SalesOrders"
                  @update:model-value="updateFieldValue(field.name, $event)"
                />
              </div>
            </div>
          </CardContent>
        </Card>

        <!-- SmartTable -->
        <Card>
          <CardHeader>
            <CardTitle>SmartTable</CardTitle>
          </CardHeader>
          <CardContent>
            <SmartTable
              module="showcase"
              entity-set="SalesOrders"
              :metadata="salesOrderMetadata"
              :data="sampleOrders"
              :total-count="sampleOrders.length"
              :current-page="1"
              :page-size="10"
              :is-loading="false"
              selection-mode="multi"
              title="Sales Orders"
            />
          </CardContent>
        </Card>

        <!-- SmartForm -->
        <Card>
          <CardHeader>
            <CardTitle>SmartForm (create mode)</CardTitle>
          </CardHeader>
          <CardContent>
            <SmartForm
              module="showcase"
              entity-set="SalesOrders"
              :metadata="salesOrderMetadata"
              mode="create"
              :is-loading="false"
              :show-actions="true"
              submit-label="Create Order"
              cancel-label="Cancel"
            />
          </CardContent>
        </Card>
      </section>

      <!-- ================================================================ -->
      <!-- SECTION 3: SmartFilterBar                                         -->
      <!-- ================================================================ -->
      <section class="space-y-6">
        <div class="flex items-center gap-3">
          <TrendingUp class="h-6 w-6 text-primary" />
          <h2 class="text-2xl font-semibold text-foreground">SmartFilterBar</h2>
        </div>

        <Card>
          <CardHeader>
            <CardTitle>Filter Bar with Sales Order Metadata</CardTitle>
          </CardHeader>
          <CardContent>
            <SmartFilterBar
              module="showcase"
              entity-set="SalesOrders"
              :metadata="salesOrderMetadata"
              :show-search="true"
            />
          </CardContent>
        </Card>
      </section>

      <!-- ================================================================ -->
      <!-- SECTION 4: Flexible Column Layout                                 -->
      <!-- ================================================================ -->
      <section class="space-y-6">
        <div class="flex items-center gap-3">
          <Layers class="h-6 w-6 text-primary" />
          <h2 class="text-2xl font-semibold text-foreground">Flexible Column Layout</h2>
        </div>

        <Card>
          <CardHeader>
            <CardTitle>Layout: {{ fclLayout }}</CardTitle>
          </CardHeader>
          <CardContent class="space-y-4">
            <div class="flex flex-wrap gap-2">
              <Button
                v-for="layout in fclLayouts"
                :key="layout.value"
                :variant="fclLayout === layout.value ? 'default' : 'outline'"
                size="sm"
                @click="fclLayout = layout.value"
              >
                {{ layout.label }}
              </Button>
            </div>

            <div class="border rounded-lg overflow-hidden" style="height: 320px">
              <FlexibleColumnLayout :layout="fclLayout" @layout-change="fclLayout = $event">
                <template #begin>
                  <div class="h-full bg-blue-50 dark:bg-blue-950/30 p-4">
                    <h3 class="font-semibold text-blue-700 dark:text-blue-300 mb-2">Begin Column</h3>
                    <p class="text-sm text-blue-600 dark:text-blue-400">
                      Typically used for a master list, such as a table of entities or navigation items.
                    </p>
                  </div>
                </template>
                <template #mid>
                  <div class="h-full bg-emerald-50 dark:bg-emerald-950/30 p-4">
                    <h3 class="font-semibold text-emerald-700 dark:text-emerald-300 mb-2">Mid Column</h3>
                    <p class="text-sm text-emerald-600 dark:text-emerald-400">
                      Detail view for the selected entity from the begin column.
                    </p>
                  </div>
                </template>
                <template #end>
                  <div class="h-full bg-violet-50 dark:bg-violet-950/30 p-4">
                    <h3 class="font-semibold text-violet-700 dark:text-violet-300 mb-2">End Column</h3>
                    <p class="text-sm text-violet-600 dark:text-violet-400">
                      Sub-detail or editing panel for a composition item.
                    </p>
                  </div>
                </template>
              </FlexibleColumnLayout>
            </div>
          </CardContent>
        </Card>
      </section>

      <!-- ================================================================ -->
      <!-- SECTION 5: Object Page                                             -->
      <!-- ================================================================ -->
      <section class="space-y-6">
        <div class="flex items-center gap-3">
          <BookOpen class="h-6 w-6 text-primary" />
          <h2 class="text-2xl font-semibold text-foreground">Object Page</h2>
        </div>
        <p class="text-muted-foreground text-sm">
          The SAP Fiori Object Page pattern — sticky collapsible header with KPI facets,
          anchor tab bar for section navigation, and collapsible content sections.
          Scroll within the container below to see the header collapse and anchor bar appear.
        </p>

        <div class="border rounded-lg overflow-hidden" style="height: 700px">
          <ObjectPageLayout
            :show-anchor-bar="true"
            :header-collapsible="true"
            :upper-case-section-titles="true"
            class="h-full"
          >
            <template #header>
              <ObjectPageHeader
                title="SO-2026-001"
                subtitle="Acme Corporation — Enterprise Server Deployment"
                description="Standard enterprise order with express shipping. Volume discount of 5% approved by regional manager."
                avatar-initials="SO"
                avatar-color="bg-primary"
                :show-breadcrumb="true"
              >
                <template #breadcrumb>
                  <nav class="flex items-center gap-1.5 text-sm text-muted-foreground">
                    <span class="hover:text-foreground cursor-pointer">Sales Orders</span>
                    <span>/</span>
                    <span class="text-foreground font-medium">SO-2026-001</span>
                  </nav>
                </template>
                <template #status>
                  <Badge variant="default">Open</Badge>
                  <Badge variant="destructive">Urgent</Badge>
                </template>
                <template #kpis>
                  <ObjectPageHeaderKpi
                    label="Total Amount"
                    value="$24,850"
                    unit="USD"
                    trend="up"
                    trend-color="positive"
                    :icon="DollarSign"
                  />
                  <ObjectPageHeaderKpi
                    label="Line Items"
                    value="4"
                    unit="items"
                    :icon="Package"
                  />
                  <ObjectPageHeaderKpi
                    label="Delivery"
                    value="3"
                    unit="business days"
                    trend="down"
                    trend-color="positive"
                    :icon="Truck"
                  />
                  <ObjectPageHeaderKpi
                    label="Discount"
                    value="5%"
                    unit="volume"
                    trend="neutral"
                    :icon="CreditCard"
                  />
                </template>
                <template #attributes>
                  <ObjectPageHeaderAttribute label="Created by" value="John Smith" :icon="Users" />
                  <ObjectPageHeaderAttribute label="Created" value="Jan 15, 2026" :icon="Clock" />
                  <ObjectPageHeaderAttribute label="Module" value="sales" :icon="Database" />
                  <ObjectPageHeaderAttribute label="Payment" value="Net 30" :icon="CreditCard" />
                </template>
                <template #actions>
                  <template v-if="!opIsEditing">
                    <Button variant="outline" size="sm" @click="opStartEdit">
                      <Pencil class="mr-2 h-4 w-4" />
                      Edit
                    </Button>
                    <Button variant="outline" size="sm" @click="opShare">
                      <Share2 class="mr-2 h-4 w-4" />
                      {{ opShareCopied ? 'Copied!' : 'Share' }}
                    </Button>
                    <Button variant="destructive" size="sm" @click="opDelete">
                      <Trash2 class="mr-2 h-4 w-4" />
                      Delete
                    </Button>
                  </template>
                  <template v-else>
                    <Button variant="outline" size="sm" @click="opCancelEdit">
                      <X class="mr-2 h-4 w-4" />
                      Cancel
                    </Button>
                    <Button size="sm" @click="opSaveEdit">
                      <Save class="mr-2 h-4 w-4" />
                      Save
                    </Button>
                  </template>
                </template>
              </ObjectPageHeader>
            </template>

            <!-- Section 1: General Information -->
            <ObjectPageSection id="op-general" title="General Information" :icon="FileText">
              <ObjectPageSubSection title="Order Details" mode="columns-2">
                <SmartField
                  v-for="field in opGeneralFields"
                  :key="field.name"
                  :field="field"
                  :model-value="opDraftValues[field.name]"
                  :mode="opIsEditing && !field.isComputed && !field.isReadOnly ? 'edit' : 'display'"
                  @update:model-value="v => opDraftValues[field.name] = v"
                />
              </ObjectPageSubSection>
            </ObjectPageSection>

            <!-- Section 2: Line Items -->
            <ObjectPageSection id="op-items" title="Line Items" :icon="Package">
              <SmartTable
                module="showcase"
                entity-set="SalesOrderItems"
                :metadata="lineItemMeta"
                :data="lineItemRows"
                :total-count="lineItemRows.length"
                :current-page="1"
                :page-size="10"
                :is-loading="false"
                title="Order Items"
              />
            </ObjectPageSection>

            <!-- Section 3: Financial Details -->
            <ObjectPageSection id="op-finance" title="Financial Details" :icon="CreditCard">
              <ObjectPageSubSection title="Pricing & Payment" mode="columns-2">
                <SmartField
                  v-for="field in opFinanceFields"
                  :key="field.name"
                  :field="field"
                  :model-value="opDraftValues[field.name]"
                  :mode="opIsEditing && !field.isComputed && !field.isReadOnly ? 'edit' : 'display'"
                  @update:model-value="v => opDraftValues[field.name] = v"
                />
              </ObjectPageSubSection>
            </ObjectPageSection>

            <!-- Section 4: Shipping -->
            <ObjectPageSection id="op-shipping" title="Shipping Information" :icon="Truck">
              <ObjectPageSubSection title="Delivery Address" mode="columns-2">
                <SmartField
                  v-for="field in opShippingFields.slice(0, 4)"
                  :key="field.name"
                  :field="field"
                  :model-value="opDraftValues[field.name]"
                  :mode="opIsEditing && !field.isComputed && !field.isReadOnly ? 'edit' : 'display'"
                  @update:model-value="v => opDraftValues[field.name] = v"
                />
              </ObjectPageSubSection>
              <ObjectPageSubSection title="Delivery Options" mode="columns-2">
                <SmartField
                  v-for="field in opShippingFields.slice(4)"
                  :key="field.name"
                  :field="field"
                  :model-value="opDraftValues[field.name]"
                  :mode="opIsEditing && !field.isComputed && !field.isReadOnly ? 'edit' : 'display'"
                  @update:model-value="v => opDraftValues[field.name] = v"
                />
              </ObjectPageSubSection>
            </ObjectPageSection>

            <!-- Section 5: Notes & Activity -->
            <ObjectPageSection id="op-notes" title="Notes & Activity" :icon="FileText">
              <div class="space-y-4">
                <div
                  v-for="(note, index) in opNotes"
                  :key="index"
                  class="flex gap-3"
                >
                  <div class="flex flex-col items-center">
                    <div class="h-8 w-8 rounded-full bg-muted flex items-center justify-center shrink-0">
                      <Clock class="h-4 w-4 text-muted-foreground" />
                    </div>
                    <div v-if="index < opNotes.length - 1" class="w-px flex-1 bg-border mt-1" />
                  </div>
                  <div class="pb-4">
                    <div class="flex items-baseline gap-2">
                      <span class="text-sm font-medium">{{ note.author }}</span>
                      <span class="text-xs text-muted-foreground">{{ note.date }}</span>
                    </div>
                    <p class="text-sm text-muted-foreground mt-0.5">{{ note.text }}</p>
                  </div>
                </div>
              </div>
            </ObjectPageSection>
          </ObjectPageLayout>
        </div>
      </section>

      <!-- ================================================================ -->
      <!-- SECTION 6: Shell Components                                       -->
      <!-- ================================================================ -->
      <section class="space-y-6">
        <div class="flex items-center gap-3">
          <Globe class="h-6 w-6 text-primary" />
          <h2 class="text-2xl font-semibold text-foreground">Shell Components</h2>
        </div>

        <!-- AppTile Grid -->
        <Card>
          <CardHeader>
            <CardTitle>App Tiles</CardTitle>
          </CardHeader>
          <CardContent>
            <div class="grid grid-cols-2 md:grid-cols-4 gap-4">
              <AppTile
                title="Sales Orders"
                subtitle="Manage orders"
                :icon="ShoppingCart"
                to="/showcase"
                color="primary"
                :count="342"
                status="active"
              />
              <AppTile
                title="Customers"
                subtitle="Customer management"
                :icon="Users"
                to="/showcase"
                color="emerald"
                :count="1847"
              />
              <AppTile
                title="Products"
                subtitle="Product catalog"
                :icon="Package"
                to="/showcase"
                color="violet"
                :count="568"
                status="warning"
              />
              <AppTile
                title="Reports"
                subtitle="Analytics & reports"
                :icon="BarChart3"
                to="/showcase"
                color="amber"
              />
              <AppTile
                title="Documents"
                subtitle="File management"
                :icon="FileText"
                to="/showcase"
                color="cyan"
                :count="2340"
              />
              <AppTile
                title="Security"
                subtitle="Access control"
                :icon="Shield"
                to="/showcase"
                color="rose"
                status="error"
              />
              <AppTile
                title="Database"
                subtitle="Schema management"
                :icon="Database"
                to="/showcase"
                color="primary"
                size="wide"
              />
              <AppTile
                title="Settings"
                subtitle="System configuration"
                :icon="Settings"
                to="/showcase"
                color="amber"
              />
            </div>
          </CardContent>
        </Card>

        <!-- LaunchpadSection -->
        <Card>
          <CardHeader>
            <CardTitle>Launchpad Sections</CardTitle>
          </CardHeader>
          <CardContent class="space-y-6">
            <LaunchpadSection
              v-model:collapsed="launchpadCollapsed"
              title="Business Applications"
              subtitle="Core operational modules"
              :icon="Globe"
              :count="4"
            >
              <AppTile title="Sales" subtitle="Sales pipeline" :icon="DollarSign" to="/showcase" color="emerald" />
              <AppTile title="Purchasing" subtitle="Procurement" :icon="ShoppingCart" to="/showcase" color="primary" />
              <AppTile title="Inventory" subtitle="Stock management" :icon="Package" to="/showcase" color="violet" />
              <AppTile title="Finance" subtitle="Accounting" :icon="BarChart3" to="/showcase" color="amber" />
            </LaunchpadSection>

            <LaunchpadSection
              v-model:collapsed="adminCollapsed"
              title="Administration"
              subtitle="System tools and configuration"
              :icon="Settings"
              :count="3"
            >
              <AppTile title="Users" subtitle="User management" :icon="Users" to="/showcase" color="primary" />
              <AppTile title="Security" subtitle="Roles & permissions" :icon="Shield" to="/showcase" color="rose" />
              <AppTile title="Settings" subtitle="Global config" :icon="Settings" to="/showcase" color="amber" />
            </LaunchpadSection>
          </CardContent>
        </Card>
      </section>

      <!-- ================================================================ -->
      <!-- SECTION 7: Messaging                                              -->
      <!-- ================================================================ -->
      <section class="space-y-6">
        <div class="flex items-center gap-3">
          <FileText class="h-6 w-6 text-primary" />
          <h2 class="text-2xl font-semibold text-foreground">Messaging</h2>
        </div>

        <Card>
          <CardHeader>
            <CardTitle>Message Strips</CardTitle>
          </CardHeader>
          <CardContent class="space-y-3">
            <MessageStrip
              type="error"
              title="Validation Failed"
              description="The order total exceeds the customer's credit limit of $50,000."
              :closable="true"
            />
            <MessageStrip
              type="warning"
              title="Low Inventory"
              description="Product SKU-4821 has only 3 units remaining in stock."
              :closable="true"
            />
            <MessageStrip
              type="info"
              title="Scheduled Maintenance"
              description="System maintenance is planned for February 15, 2026 from 02:00-04:00 UTC."
              :closable="true"
            />
            <MessageStrip
              type="success"
              title="Order Confirmed"
              description="Sales order SO-2026-001 has been successfully submitted and confirmed."
              :closable="true"
            />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Draft Indicator</CardTitle>
          </CardHeader>
          <CardContent class="space-y-6">
            <!-- Entity-level: shows "Draft" badge for a specific entity being edited -->
            <div>
              <p class="text-sm font-medium text-foreground mb-2">Entity-level (single record being edited)</p>
              <div class="flex items-center gap-3">
                <DraftIndicator
                  module="showcase"
                  entity-set="SalesOrders"
                  entity-key="a1b2c3d4-e5f6-7890-abcd-ef1234567890"
                />
                <span class="text-sm text-muted-foreground">
                  Shows a "Draft" badge when the entity has unsaved changes
                </span>
              </div>
            </div>

            <!-- Entity-set level: shows draft count button with popover listing all drafts -->
            <div>
              <p class="text-sm font-medium text-foreground mb-2">Entity-set level (all drafts for a list view)</p>
              <div class="flex items-center gap-3">
                <DraftIndicator module="showcase" entity-set="SalesOrders" />
                <span class="text-sm text-muted-foreground">
                  Shows draft count with a popover to resume or discard each draft
                </span>
              </div>
            </div>
          </CardContent>
        </Card>
      </section>

      <!-- ================================================================ -->
      <!-- SECTION 9: Advanced Field Types                                    -->
      <!-- ================================================================ -->
      <section class="space-y-6">
        <div class="flex items-center gap-3">
          <Layers class="h-6 w-6 text-primary" />
          <h2 class="text-2xl font-semibold text-foreground">Advanced Field Types</h2>
        </div>

        <Card>
          <CardHeader>
            <div class="flex items-center justify-between">
              <CardTitle>Annotation-Driven Fields</CardTitle>
              <Button
                size="sm"
                variant="outline"
                @click="advFieldMode = advFieldMode === 'display' ? 'edit' : 'display'"
              >
                <Pencil class="h-4 w-4 mr-1" />
                {{ advFieldMode === 'display' ? 'Edit' : 'Display' }}
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
              <!-- TimePicker -->
              <div>
                <p class="text-xs text-muted-foreground mb-2">field.type = "Time"</p>
                <SmartField
                  :field="advTimeField"
                  :modelValue="advFieldValues.MeetingTime"
                  :mode="advFieldMode"
                  @update:modelValue="(v: unknown) => updateAdvFieldValue('MeetingTime', v)"
                />
              </div>

              <!-- Currency -->
              <div>
                <p class="text-xs text-muted-foreground mb-2">@Semantics.CurrencyCode: "EUR"</p>
                <SmartField
                  :field="advCurrencyField"
                  :modelValue="advFieldValues.UnitPrice"
                  :mode="advFieldMode"
                  @update:modelValue="(v: unknown) => updateAdvFieldValue('UnitPrice', v)"
                />
              </div>

              <!-- DateRange -->
              <div>
                <p class="text-xs text-muted-foreground mb-2">@UI.DateRange: true</p>
                <SmartField
                  :field="advDateRangeField"
                  :modelValue="advFieldValues.ContractPeriod"
                  :mode="advFieldMode"
                  @update:modelValue="(v: unknown) => updateAdvFieldValue('ContractPeriod', v)"
                />
              </div>

              <!-- MultiComboBox -->
              <div>
                <p class="text-xs text-muted-foreground mb-2">@UI.MultiSelect: true</p>
                <SmartField
                  :field="advMultiSelectField"
                  :modelValue="advFieldValues.Categories"
                  :mode="advFieldMode"
                  @update:modelValue="(v: unknown) => updateAdvFieldValue('Categories', v)"
                />
              </div>

              <!-- MaskedInput -->
              <div>
                <p class="text-xs text-muted-foreground mb-2">@UI.InputMask: "phone"</p>
                <SmartField
                  :field="advMaskedField"
                  :modelValue="advFieldValues.Phone"
                  :mode="advFieldMode"
                  @update:modelValue="(v: unknown) => updateAdvFieldValue('Phone', v)"
                />
              </div>
            </div>
          </CardContent>
        </Card>
      </section>
    </div>

    <ConfirmDialog
      :open="confirmDialog.isOpen.value"
      :title="confirmDialog.title.value"
      :description="confirmDialog.description.value"
      :confirm-label="confirmDialog.confirmLabel.value"
      :cancel-label="confirmDialog.cancelLabel.value"
      :variant="confirmDialog.variant.value"
      @confirm="confirmDialog.handleConfirm"
      @cancel="confirmDialog.handleCancel"
      @update:open="confirmDialog.isOpen.value = $event"
    />
  </DefaultLayout>
</template>
