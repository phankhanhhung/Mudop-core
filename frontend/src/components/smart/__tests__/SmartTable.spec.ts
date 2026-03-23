import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { computed, ref } from 'vue'

// ---------------------------------------------------------------------------
// vi.mock declarations MUST be before all imports (Vitest hoisting)
// ---------------------------------------------------------------------------

vi.mock('@/composables/useSmartTable', () => ({
  useSmartTable: () => ({
    columns: computed(() => [
      { field: 'ID', label: 'ID', type: 'UUID', visible: true, sortable: false, filterable: false, width: 120, minWidth: 60, align: 'left', importance: 1, isKey: true, isEnum: false, order: 0 },
      { field: 'Name', label: 'Name', type: 'String', visible: true, sortable: true, filterable: true, width: 200, minWidth: 60, align: 'left', importance: 2, isKey: false, isEnum: false, order: 1 },
    ]),
    visibleColumns: computed(() => [
      { field: 'ID', label: 'ID', type: 'UUID', visible: true, sortable: false, filterable: false, width: 120, minWidth: 60, align: 'left', importance: 1, isKey: true, isEnum: false, order: 0 },
      { field: 'Name', label: 'Name', type: 'String', visible: true, sortable: true, filterable: true, width: 200, minWidth: 60, align: 'left', importance: 2, isKey: false, isEnum: false, order: 1 },
    ]),
    toggleColumn: vi.fn(),
    setColumnWidth: vi.fn(),
    reorderColumns: vi.fn(),
    resetColumns: vi.fn(),
    saveLayout: vi.fn(),
    loadLayout: vi.fn().mockReturnValue(false),
  })
}))

vi.mock('@/composables/useColumnResize', () => ({
  useColumnResize: () => ({
    isResizing: ref(false),
    resizingField: ref(null),
    startResize: vi.fn(),
    autoFitColumn: vi.fn(),
  })
}))

vi.mock('@/composables/useColumnDragReorder', () => ({
  useColumnDragReorder: () => ({
    isDragging: ref(false),
    justDragged: ref(false),
    draggingField: ref(null),
    dropIndicator: ref(null),
    handleDragStart: vi.fn(),
    handleDragOver: vi.fn(),
    handleDragLeave: vi.fn(),
    handleDrop: vi.fn(),
    handleDragEnd: vi.fn(),
  })
}))

vi.mock('@/composables/useVirtualScroll', () => ({
  useVirtualScroll: () => ({
    virtualRows: computed(() => []),
    totalSize: computed(() => 0),
    topPadding: computed(() => 0),
    bottomPadding: computed(() => 0),
    scrollToIndex: vi.fn(),
  })
}))

vi.mock('@/composables/useRowEdit', () => ({
  useRowEdit: () => ({
    editingRowId: ref(null),
    editValues: ref({}),
    isEditing: computed(() => false),
    dirtyFields: computed(() => new Set<string>()),
    startEdit: vi.fn(),
    updateField: vi.fn(),
    isDirty: vi.fn().mockReturnValue(false),
    getChanges: vi.fn().mockReturnValue({}),
    cancelEdit: vi.fn(),
  })
}))

vi.mock('../SmartCellDisplay.vue', () => ({
  default: {
    name: 'SmartCellDisplay',
    template: '<span class="cell-display">{{ value }}</span>',
    props: ['column', 'value', 'row'],
  }
}))

vi.mock('../ObjectIdentifier.vue', () => ({
  default: {
    name: 'ObjectIdentifier',
    template: '<span class="object-id"><slot /></span>',
    props: ['title', 'text', 'titleActive', 'emphasized'],
  }
}))

vi.mock('../SmartTableEditRow.vue', () => ({
  default: {
    name: 'SmartTableEditRow',
    template: '<tr data-testid="edit-row"></tr>',
    props: ['columns', 'rowData', 'rowId', 'metadata', 'module', 'entitySet', 'selectionMode'],
  }
}))

vi.mock('../BulkActionToolbar.vue', () => ({
  default: {
    name: 'BulkActionToolbar',
    template: '<div data-testid="bulk-toolbar"></div>',
    props: ['selectedCount', 'totalCount', 'enableExport'],
  }
}))

vi.mock('../ViewSettingsDialog.vue', () => ({
  default: {
    name: 'ViewSettingsDialog',
    template: '<div data-testid="view-settings"></div>',
    props: ['open', 'columns', 'currentSort', 'currentFilters'],
  }
}))

vi.mock('../ResponsiveTable.vue', () => ({
  default: {
    name: 'ResponsiveTable',
    template: '<div data-testid="responsive-table"><slot /></div>',
    props: ['columns', 'data'],
  }
}))

vi.mock('@/components/smart/ValueHelpDialog.vue', () => ({
  default: {
    name: 'ValueHelpDialog',
    template: '<div data-testid="value-help"></div>',
    props: ['open', 'module', 'targetEntity'],
  }
}))

vi.mock('lucide-vue-next', () => ({
  ArrowUp: { template: '<span data-icon="ArrowUp" />' },
  ArrowDown: { template: '<span data-icon="ArrowDown" />' },
  Filter: { template: '<span data-icon="Filter" />' },
  Download: { template: '<span data-icon="Download" />' },
  RefreshCw: { template: '<span data-icon="RefreshCw" />' },
  Pencil: { template: '<span data-icon="Pencil" />' },
  Trash2: { template: '<span data-icon="Trash2" />' },
  Settings2: { template: '<span data-icon="Settings2" />' },
  GripVertical: { template: '<span data-icon="GripVertical" />' },
  Check: { template: '<span data-icon="Check" />' },
  ChevronDown: { template: '<span data-icon="ChevronDown" />' },
  SlidersHorizontal: { template: '<span data-icon="SlidersHorizontal" />' },
}))

// ---------------------------------------------------------------------------
// Imports come AFTER all vi.mock declarations
// ---------------------------------------------------------------------------

import SmartTable from '../SmartTable.vue'
import type { EntityMetadata } from '@/types/metadata'
import type { FilterCondition } from '@/types/odata'

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function createMetadata(extra: Partial<EntityMetadata> = {}): EntityMetadata {
  return {
    name: 'Customer',
    namespace: 'sales',
    displayName: 'Customer',
    fields: [
      { name: 'ID', type: 'UUID', isKey: true, isRequired: true, isReadOnly: false, isComputed: false, displayName: 'ID', annotations: {} },
      { name: 'Name', type: 'String', isKey: false, isRequired: true, isReadOnly: false, isComputed: false, displayName: 'Name', maxLength: 100, annotations: {} },
      { name: 'Amount', type: 'Decimal', isKey: false, isRequired: false, isReadOnly: false, isComputed: false, displayName: 'Amount', annotations: {} },
    ],
    keys: ['ID'],
    associations: [],
    annotations: {},
    boundActions: [],
    boundFunctions: [],
    ...extra,
  }
}

const sampleData: Record<string, unknown>[] = [
  { ID: 'uuid-1', Name: 'Alice', Amount: 100 },
  { ID: 'uuid-2', Name: 'Bob', Amount: 200 },
  { ID: 'uuid-3', Name: 'Carol', Amount: 300 },
]

function mountTable(overrides: Record<string, unknown> = {}) {
  return mount(SmartTable, {
    props: {
      module: 'sales',
      entitySet: 'Customers',
      metadata: createMetadata(),
      data: sampleData,
      totalCount: sampleData.length,
      ...overrides,
    },
    global: {
      mocks: { $t: (k: string) => k },
      stubs: {
        Teleport: true,
      },
    },
  })
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('SmartTable', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  // Test 1
  it('renders table element', () => {
    const wrapper = mountTable()
    expect(wrapper.find('table').exists()).toBe(true)
  })

  // Test 2
  it('renders column headers from metadata fields', () => {
    const wrapper = mountTable()
    const text = wrapper.text()
    // The mock visibleColumns returns ID and Name
    expect(text).toContain('ID')
    expect(text).toContain('Name')
  })

  // Test 3
  it('renders a row for each data item', () => {
    const wrapper = mountTable()
    // Each data row becomes a <tr role="row"> in the tbody
    const rows = wrapper.findAll('[role="row"]')
    // At least as many rows as data items (plus header row)
    expect(rows.length).toBeGreaterThanOrEqual(sampleData.length)
  })

  // Test 4
  it('shows loading spinner when isLoading=true', () => {
    const wrapper = mountTable({ isLoading: true })
    // The loading overlay is present in DOM when isLoading=true
    // It renders a Spinner (svg with animate-spin class)
    const loadingOverlay = wrapper.find('[aria-live="polite"]')
    expect(loadingOverlay.exists()).toBe(true)
  })

  // Test 5
  it('hides loading spinner when isLoading=false', () => {
    const wrapper = mountTable({ isLoading: false })
    const loadingOverlay = wrapper.find('[aria-live="polite"]')
    expect(loadingOverlay.exists()).toBe(false)
  })

  // Test 6
  it('shows error message when error prop is set', () => {
    const wrapper = mountTable({ error: 'Something went wrong' })
    const errorEl = wrapper.find('.text-destructive')
    expect(errorEl.exists()).toBe(true)
    expect(errorEl.text()).toContain('Something went wrong')
  })

  // Test 7
  it('hides error when error is null', () => {
    const wrapper = mountTable({ error: null })
    // The error alert div uses border-destructive/50 class
    const errorEl = wrapper.find('.border-destructive\\/50')
    expect(errorEl.exists()).toBe(false)
  })

  // Test 8
  it('shows empty state message when data is empty', () => {
    const wrapper = mountTable({ data: [], totalCount: 0 })
    expect(wrapper.text()).toContain('No records found')
  })

  // Test 9
  it('emits row-click when a row is clicked', async () => {
    const wrapper = mountTable()
    // Find data rows (role="row" in the tbody, not the header row)
    const rows = wrapper.findAll('tbody [role="row"]')
    expect(rows.length).toBeGreaterThan(0)
    await rows[0].trigger('click')

    const emitted = wrapper.emitted('row-click')
    expect(emitted).toBeTruthy()
    expect(emitted![0]).toBeDefined()
  })

  // Test 10
  it('renders correct number of rows', () => {
    const data = [
      { ID: 'a', Name: 'Alpha', Amount: 1 },
      { ID: 'b', Name: 'Beta', Amount: 2 },
    ]
    const wrapper = mountTable({ data, totalCount: 2 })
    const rows = wrapper.findAll('tbody [role="row"]')
    // 2 data rows
    expect(rows.length).toBe(2)
  })

  // Test 11
  it('shows title when title prop is provided', () => {
    const wrapper = mountTable({ title: 'Customer List' })
    expect(wrapper.text()).toContain('Customer List')
  })

  // Test 12
  it('shows refresh button', () => {
    const wrapper = mountTable()
    // Refresh button has title="Refresh data"
    const refreshBtn = wrapper.find('[title="Refresh data"]')
    expect(refreshBtn.exists()).toBe(true)
  })

  // Test 13
  it('aria attributes: table has role=grid, header cells have role=columnheader', () => {
    const wrapper = mountTable()
    // The Table component renders a <table> with role="grid"
    const table = wrapper.find('table')
    expect(table.attributes('role')).toBe('grid')

    // Header cells have role="columnheader"
    const headerCells = wrapper.findAll('[role="columnheader"]')
    expect(headerCells.length).toBeGreaterThan(0)
  })
})
