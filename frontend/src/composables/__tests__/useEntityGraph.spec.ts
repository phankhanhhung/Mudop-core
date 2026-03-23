import { describe, it, expect, beforeEach, vi } from 'vitest'

// Mock metadataService before importing anything that uses it
vi.mock('@/services/metadataService', () => ({
  metadataService: {
    getModules: vi.fn(),
    getEntities: vi.fn(),
    getEntity: vi.fn(),
  },
}))

import { useEntityGraph } from '../useEntityGraph'
import { metadataService } from '@/services/metadataService'
import { nextTick } from 'vue'

// ---------------------------------------------------------------------------
// Sample data
// ---------------------------------------------------------------------------

const mockModules = [
  { name: 'crm', version: '1.0', description: 'CRM module', services: [] },
  { name: 'sales', version: '1.0', description: 'Sales module', services: [] },
]

const mockEntities = {
  crm: [
    {
      name: 'Customer',
      namespace: 'crm',
      fields: [
        { name: 'ID', type: 'UUID', isRequired: true, isReadOnly: false, isComputed: false, annotations: {} },
        { name: 'Name', type: 'String', isRequired: false, isReadOnly: false, isComputed: false, annotations: {} },
      ],
      keys: ['ID'],
      associations: [
        { name: 'orders', targetEntity: 'sales.Order', cardinality: 'Many', isComposition: false },
        { name: 'address', targetEntity: 'crm.Address', cardinality: 'ZeroOrOne', isComposition: true },
      ],
      isAbstract: false,
      annotations: {},
    },
    {
      name: 'Address',
      namespace: 'crm',
      fields: [
        { name: 'ID', type: 'UUID', isRequired: true, isReadOnly: false, isComputed: false, annotations: {} },
        { name: 'Street', type: 'String', isRequired: false, isReadOnly: false, isComputed: false, annotations: {} },
      ],
      keys: ['ID'],
      associations: [],
      isAbstract: false,
      annotations: {},
    },
  ],
  sales: [
    {
      name: 'Order',
      namespace: 'sales',
      fields: [
        { name: 'ID', type: 'UUID', isRequired: true, isReadOnly: false, isComputed: false, annotations: {} },
        { name: 'Amount', type: 'Decimal', isRequired: false, isReadOnly: false, isComputed: false, annotations: {} },
      ],
      keys: ['ID'],
      associations: [],
      isAbstract: false,
      annotations: {},
    },
  ],
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const mockGetModules = metadataService.getModules as ReturnType<typeof vi.fn>
const mockGetEntities = metadataService.getEntities as ReturnType<typeof vi.fn>

function setupFullLoad() {
  mockGetModules.mockResolvedValue(mockModules)
  mockGetEntities.mockImplementation((moduleName: string) =>
    Promise.resolve(mockEntities[moduleName as keyof typeof mockEntities] ?? [])
  )
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('useEntityGraph', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  // 1. loadModules populates modules list
  it('loadModules populates modules list', async () => {
    mockGetModules.mockResolvedValue(mockModules)
    const { modules, loadModules } = useEntityGraph()

    await loadModules()

    expect(modules.value).toHaveLength(2)
    expect(modules.value[0].name).toBe('crm')
    expect(modules.value[1].name).toBe('sales')
  })

  // 2. loadAll loads entities for each module
  it('loadAll loads entities for each module', async () => {
    setupFullLoad()
    const { allEntities, loadAll } = useEntityGraph()

    await loadAll()

    // 2 crm + 1 sales = 3 total entries
    expect(allEntities.value).toHaveLength(3)
  })

  // 3. buildGraph creates nodes for visible entities
  it('buildGraph creates nodes for visible entities', async () => {
    setupFullLoad()
    const { nodes, loadAll } = useEntityGraph()

    await loadAll()
    // Watch triggers buildGraph; nextTick lets Vue flush it
    await nextTick()

    expect(nodes.value).toHaveLength(3)
    const ids = nodes.value.map(n => n.id)
    expect(ids).toContain('crm.Customer')
    expect(ids).toContain('crm.Address')
    expect(ids).toContain('sales.Order')
  })

  // 4. buildGraph creates edges from associations
  it('buildGraph creates edges from associations', async () => {
    setupFullLoad()
    const { edges, loadAll } = useEntityGraph()

    await loadAll()
    await nextTick()

    // Customer → sales.Order (Many) and Customer → crm.Address (ZeroOrOne composition)
    expect(edges.value.length).toBeGreaterThanOrEqual(2)

    const compositionEdge = edges.value.find(e => e.associationName === 'address')
    expect(compositionEdge).toBeDefined()
    expect(compositionEdge!.isComposition).toBe(true)
    expect(compositionEdge!.sourceId).toBe('crm.Customer')
    expect(compositionEdge!.targetId).toBe('crm.Address')

    const assocEdge = edges.value.find(e => e.associationName === 'orders')
    expect(assocEdge).toBeDefined()
    expect(assocEdge!.isComposition).toBe(false)
    expect(assocEdge!.sourceId).toBe('crm.Customer')
    expect(assocEdge!.targetId).toBe('sales.Order')
  })

  // 5. cardinalityLabel converts correctly — verified through edge labels
  it('cardinalityLabel converts cardinality strings to notation via edges', async () => {
    const entitiesWithAllCardinalities = [
      {
        name: 'Source',
        namespace: 'test',
        fields: [],
        keys: ['ID'],
        associations: [
          { name: 'many', targetEntity: 'test.Many', cardinality: 'Many', isComposition: false },
          { name: 'zeroOrOne', targetEntity: 'test.ZeroOrOne', cardinality: 'ZeroOrOne', isComposition: false },
          { name: 'oneOrMore', targetEntity: 'test.OneOrMore', cardinality: 'OneOrMore', isComposition: false },
          { name: 'one', targetEntity: 'test.One', cardinality: 'One', isComposition: false },
        ],
        isAbstract: false,
        annotations: {},
      },
      {
        name: 'Many',
        namespace: 'test',
        fields: [],
        keys: ['ID'],
        associations: [],
        isAbstract: false,
        annotations: {},
      },
      {
        name: 'ZeroOrOne',
        namespace: 'test',
        fields: [],
        keys: ['ID'],
        associations: [],
        isAbstract: false,
        annotations: {},
      },
      {
        name: 'OneOrMore',
        namespace: 'test',
        fields: [],
        keys: ['ID'],
        associations: [],
        isAbstract: false,
        annotations: {},
      },
      {
        name: 'One',
        namespace: 'test',
        fields: [],
        keys: ['ID'],
        associations: [],
        isAbstract: false,
        annotations: {},
      },
    ]

    mockGetModules.mockResolvedValue([{ name: 'test', version: '1.0', services: [] }])
    mockGetEntities.mockResolvedValue(entitiesWithAllCardinalities)

    const { edges, loadAll } = useEntityGraph()
    await loadAll()
    await nextTick()

    const manyEdge = edges.value.find(e => e.associationName === 'many')
    expect(manyEdge!.label).toBe('[0..*]')

    const zeroOrOneEdge = edges.value.find(e => e.associationName === 'zeroOrOne')
    expect(zeroOrOneEdge!.label).toBe('[0..1]')

    const oneOrMoreEdge = edges.value.find(e => e.associationName === 'oneOrMore')
    expect(oneOrMoreEdge!.label).toBe('[1..*]')

    const oneEdge = edges.value.find(e => e.associationName === 'one')
    expect(oneEdge!.label).toBe('[1..1]')
  })

  // 6. toggleModule removes module from selection
  it('toggleModule removes module from selection', async () => {
    mockGetModules.mockResolvedValue(mockModules)
    const { loadModules, toggleModule, selectedModules } = useEntityGraph()
    await loadModules()

    // Both modules should be selected initially
    expect(selectedModules.value).toContain('crm')

    toggleModule('crm')

    expect(selectedModules.value).not.toContain('crm')
  })

  // 7. toggleModule re-adds removed module
  it('toggleModule re-adds removed module', async () => {
    mockGetModules.mockResolvedValue(mockModules)
    const { loadModules, toggleModule, selectedModules } = useEntityGraph()
    await loadModules()

    // Remove then re-add
    toggleModule('crm')
    expect(selectedModules.value).not.toContain('crm')

    toggleModule('crm')
    expect(selectedModules.value).toContain('crm')
  })

  // 8. selectOnlyModule selects only that module
  it('selectOnlyModule selects only that module', async () => {
    mockGetModules.mockResolvedValue(mockModules)
    const { loadModules, selectOnlyModule, selectedModules } = useEntityGraph()
    await loadModules()

    selectOnlyModule('sales')

    expect(selectedModules.value).toEqual(['sales'])
    expect(selectedModules.value).not.toContain('crm')
  })

  // 9. selectAllModules selects all modules
  it('selectAllModules selects all modules', async () => {
    mockGetModules.mockResolvedValue(mockModules)
    const { loadModules, selectOnlyModule, selectAllModules, selectedModules } = useEntityGraph()
    await loadModules()

    // Start with only sales selected
    selectOnlyModule('sales')
    expect(selectedModules.value).toEqual(['sales'])

    selectAllModules()

    const moduleNames = mockModules.map(m => m.name)
    for (const name of moduleNames) {
      expect(selectedModules.value).toContain(name)
    }
    expect(selectedModules.value).toHaveLength(moduleNames.length)
  })

  // 10. loadAll sets isLoading during fetch and clears on completion
  it('loadAll sets isLoading during fetch and clears on completion', async () => {
    let resolveModules!: (v: typeof mockModules) => void
    mockGetModules.mockReturnValue(new Promise<typeof mockModules>(r => { resolveModules = r }))
    mockGetEntities.mockResolvedValue([])

    const { isLoading, loadAll } = useEntityGraph()

    const loadPromise = loadAll()

    // Still loading because getModules has not resolved
    expect(isLoading.value).toBe(true)

    resolveModules(mockModules)
    await loadPromise

    expect(isLoading.value).toBe(false)
  })

  // 11. loadAll sets error on failure
  it('loadAll sets error on failure', async () => {
    mockGetModules.mockRejectedValue(new Error('Network failure'))

    const { error, loadAll } = useEntityGraph()

    await loadAll()

    expect(error.value).toBe('Network failure')
  })

  // 12. edges skip targets not in visible nodes
  it('edges skip targets not in visible nodes when module is deselected', async () => {
    setupFullLoad()
    const { edges, loadAll, toggleModule } = useEntityGraph()

    await loadAll()
    await nextTick()

    // Initially both modules selected; edge to sales.Order should exist
    expect(edges.value.some(e => e.targetId === 'sales.Order')).toBe(true)

    // Deselect sales module — sales.Order node disappears from visibleEntities
    toggleModule('sales')
    await nextTick()

    // No edges should point to sales.Order any more
    expect(edges.value.some(e => e.targetId === 'sales.Order')).toBe(false)
  })
})
