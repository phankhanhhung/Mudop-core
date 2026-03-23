import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { ref } from 'vue'

// vi.mock declarations must come before all imports that use them (hoisted by Vitest)

const mockFetchEntity = vi.fn()
vi.mock('@/stores/metadata', () => ({
  useMetadataStore: () => ({
    fetchEntity: (...args: unknown[]) => mockFetchEntity(...args),
  })
}))

const mockGetChildren = vi.fn()
const mockDelete = vi.fn()
vi.mock('@/services', () => ({
  odataService: {
    getChildren: (...args: unknown[]) => mockGetChildren(...args),
    delete: (...args: unknown[]) => mockDelete(...args),
  },
  etagStore: { get: vi.fn(), set: vi.fn(), remove: vi.fn(), clear: vi.fn() },
}))

const mockSuccess = vi.fn()
const mockError = vi.fn()
vi.mock('@/stores/ui', () => ({
  useUiStore: () => ({
    success: mockSuccess,
    error: mockError,
    showToast: vi.fn(),
  })
}))

const mockConfirm = vi.fn()
vi.mock('@/composables/useConfirmDialog', () => ({
  useConfirmDialog: () => ({
    isOpen: ref(false),
    title: ref(''),
    description: ref(''),
    confirmLabel: ref('Confirm'),
    cancelLabel: ref('Cancel'),
    variant: ref('default'),
    confirm: (...args: unknown[]) => mockConfirm(...args),
    handleConfirm: vi.fn(),
    handleCancel: vi.fn(),
  })
}))

const mockPush = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push: mockPush, replace: vi.fn() }),
  useRoute: () => ({ params: {} }),
  RouterLink: { template: '<a><slot /></a>' },
}))

vi.mock('@/utils/formValidator', () => ({
  formatValue: (value: unknown) => (value == null ? '' : String(value)),
}))

// Mock UI components that are not under test
vi.mock('@/components/ui/card', () => ({
  Card: { template: '<div class="card"><slot /></div>' },
  CardContent: { template: '<div class="card-content"><slot /></div>' },
  CardHeader: { template: '<div class="card-header"><slot /></div>' },
  CardTitle: { template: '<div class="card-title"><slot /></div>' },
}))

vi.mock('@/components/ui/table', () => ({
  Table: { template: '<table><slot /></table>' },
  TableBody: { template: '<tbody><slot /></tbody>' },
  TableCell: { template: '<td><slot /></td>' },
  TableHead: { template: '<th><slot /></th>' },
  TableHeader: { template: '<thead><slot /></thead>' },
  TableRow: { template: '<tr><slot /></tr>' },
}))

vi.mock('@/components/ui/badge', () => ({
  Badge: { template: '<span class="badge"><slot /></span>' },
}))

vi.mock('@/components/ui/button', () => ({
  Button: { template: '<button @click="$emit(\'click\')"><slot /></button>', emits: ['click'] },
}))

vi.mock('@/components/ui/spinner', () => ({
  Spinner: { template: '<div data-testid="spinner" />' },
}))

vi.mock('@/components/common', () => ({
  ConfirmDialog: { template: '<div data-testid="confirm-dialog" />' },
}))

vi.mock('lucide-vue-next', () => ({
  Plus: { template: '<span data-icon="Plus" />' },
  Pencil: { template: '<span data-icon="Pencil" />' },
  Trash2: { template: '<span data-icon="Trash2" />' },
}))

import CompositionSection from '../CompositionSection.vue'
import type { AssociationMetadata, EntityMetadata, FieldMetadata } from '@/types/metadata'

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeField(overrides: Partial<FieldMetadata> = {}): FieldMetadata {
  return {
    name: 'Name',
    type: 'String',
    displayName: 'Name',
    isRequired: false,
    isReadOnly: false,
    isComputed: false,
    annotations: {},
    ...overrides,
  }
}

function createChildMetadata(): EntityMetadata {
  return {
    name: 'OrderItem',
    namespace: 'sales',
    fields: [
      makeField({ name: 'ID', type: 'UUID', displayName: 'ID' }),
      makeField({ name: 'OrderId', type: 'UUID', displayName: 'Order' }),
      makeField({ name: 'ProductName', type: 'String', displayName: 'Product' }),
      makeField({ name: 'Quantity', type: 'Integer', displayName: 'Qty' }),
      makeField({ name: 'CreatedAt', type: 'DateTime', displayName: 'Created' }),
    ],
    keys: ['ID'],
    associations: [],
    annotations: {},
  }
}

function createAssociation(): AssociationMetadata {
  return {
    name: 'items',
    targetEntity: 'sales.OrderItem',
    cardinality: 'Many',
    foreignKey: 'OrderId',
    isComposition: true,
  }
}

function mountCompositionSection(propOverrides: Record<string, unknown> = {}) {
  return mount(CompositionSection, {
    props: {
      module: 'sales',
      parentEntity: 'Order',
      parentId: 'order-123',
      association: createAssociation(),
      ...propOverrides,
    },
    global: {
      mocks: { $t: (k: string) => k },
    },
  })
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('CompositionSection', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()

    // Default: metadata + one child row
    mockFetchEntity.mockResolvedValue(createChildMetadata())
    mockGetChildren.mockResolvedValue({
      value: [{ ID: '1', ProductName: 'Widget', Quantity: 3 }],
    })
  })

  // 1. renders section title with association name
  it('renders section title with association name', async () => {
    const wrapper = mountCompositionSection()
    await flushPromises()

    // association.name = 'items' → displayName = 'items' (no capital letters to split)
    expect(wrapper.text()).toContain('items')
  })

  // 2. shows loading spinner while fetching data
  it('shows loading spinner while fetching data', async () => {
    // Use a promise that never resolves during the test so isLoading stays true
    // We need a way to observe the loading state mid-flight.
    // Strategy: capture the isLoading value from the component's internal ref
    // by checking the vm directly immediately after mounting.
    let resolveMetadata!: (v: EntityMetadata) => void
    mockFetchEntity.mockReturnValue(new Promise<EntityMetadata>((r) => { resolveMetadata = r }))
    mockGetChildren.mockReturnValue(new Promise(() => {})) // never resolves

    const wrapper = mountCompositionSection()

    // After mount but before any async resolution, isLoading should be true.
    // Access the internal reactive state via the component vm.
    const vm = wrapper.vm as unknown as { isLoading: boolean }
    expect(vm.isLoading).toBe(true)

    // Resolve to avoid hanging
    resolveMetadata(createChildMetadata())
    await flushPromises()
  })

  // 3. fetches child metadata on mount
  it('fetches child metadata on mount', async () => {
    mountCompositionSection()
    await flushPromises()

    // resolveTarget('sales.OrderItem') → { module: 'sales', entity: 'OrderItem' }
    expect(mockFetchEntity).toHaveBeenCalledWith('sales', 'OrderItem')
  })

  // 4. fetches child rows on mount after metadata loaded
  it('fetches child rows on mount after metadata loaded', async () => {
    mountCompositionSection()
    await flushPromises()

    expect(mockGetChildren).toHaveBeenCalledWith(
      'sales',
      'Order',
      'order-123',
      'items'
    )
  })

  // 5. renders table rows for each child item
  it('renders table rows for each child item', async () => {
    mockGetChildren.mockResolvedValue({
      value: [
        { ID: '1', ProductName: 'Widget', Quantity: 3 },
        { ID: '2', ProductName: 'Gadget', Quantity: 7 },
      ],
    })

    const wrapper = mountCompositionSection()
    await flushPromises()

    // There should be 2 data rows (tr elements inside tbody)
    const rows = wrapper.findAll('tbody tr')
    expect(rows.length).toBe(2)
  })

  // 6. hides system/audit fields (CreatedAt not shown as column)
  it('hides system/audit fields (CreatedAt not shown as column)', async () => {
    const wrapper = mountCompositionSection()
    await flushPromises()

    // Column headers are th elements — CreatedAt must not appear
    const headers = wrapper.findAll('th')
    const headerTexts = headers.map((h) => h.text())
    expect(headerTexts).not.toContain('Created')
    expect(headerTexts.some((t) => t.toLowerCase().includes('created'))).toBe(false)
  })

  // 7. hides parent FK field (OrderId not shown as column)
  it('hides parent FK field (OrderId not shown as column)', async () => {
    const wrapper = mountCompositionSection()
    await flushPromises()

    const headers = wrapper.findAll('th')
    const headerTexts = headers.map((h) => h.text())
    // parentEntity='Order' → parentFkField='OrderId'
    expect(headerTexts).not.toContain('Order')
    expect(headerTexts.some((t) => t === 'Order' || t === 'OrderId')).toBe(false)
  })

  // 8. hides ID field
  it('hides ID field', async () => {
    const wrapper = mountCompositionSection()
    await flushPromises()

    const headers = wrapper.findAll('th')
    const headerTexts = headers.map((h) => h.text())
    expect(headerTexts).not.toContain('ID')
  })

  // 9. shows visible data fields (ProductName, Quantity shown as columns)
  it('shows visible data fields (ProductName, Quantity shown as columns)', async () => {
    const wrapper = mountCompositionSection()
    await flushPromises()

    const headers = wrapper.findAll('th')
    const headerTexts = headers.map((h) => h.text())
    expect(headerTexts).toContain('Product')
    expect(headerTexts).toContain('Qty')
  })

  // 10. renders add button for composition
  it('renders add button for composition', async () => {
    const wrapper = mountCompositionSection()
    await flushPromises()

    const buttons = wrapper.findAll('button')
    expect(buttons.length).toBeGreaterThan(0)
    // At least one button should exist (the Add button in the header)
    const buttonTexts = buttons.map((b) => b.text())
    expect(buttonTexts.some((t) => t.includes('Add'))).toBe(true)
  })

  // 11. add button navigates to create child route
  it('add button navigates to create child route', async () => {
    const wrapper = mountCompositionSection()
    await flushPromises()

    // Find the "Add" button in the card header (first button)
    const headerButtons = wrapper.findAll('button')
    const addButton = headerButtons[0]
    await addButton.trigger('click')

    expect(mockPush).toHaveBeenCalledWith(
      expect.objectContaining({
        path: '/odata/sales/OrderItem/new',
        query: expect.objectContaining({
          parentFk: 'OrderId',
          parentId: 'order-123',
        }),
      })
    )
  })

  // 12. delete button triggers confirm dialog
  it('delete button triggers confirm dialog', async () => {
    mockConfirm.mockResolvedValue(false) // user cancels

    const wrapper = mountCompositionSection()
    await flushPromises()

    // Find delete button — it's the last button in the action cell of the first row
    const allButtons = wrapper.findAll('button')
    // Buttons: [Add (header), Edit (row 1), Delete (row 1)]
    const deleteButton = allButtons[allButtons.length - 1]
    await deleteButton.trigger('click')
    await flushPromises()

    expect(mockConfirm).toHaveBeenCalledWith(
      expect.objectContaining({
        title: expect.stringContaining('Delete'),
        variant: 'destructive',
      })
    )
  })

  // 13. shows error message when fetch fails
  it('shows error message when fetch fails', async () => {
    mockFetchEntity.mockRejectedValue(new Error('Network error'))

    const wrapper = mountCompositionSection()
    await flushPromises()

    // Error message rendered by v-else-if="error"
    const errorEl = wrapper.find('p.text-destructive')
    expect(errorEl.exists()).toBe(true)
    expect(errorEl.text()).toContain('Network error')
  })
})
