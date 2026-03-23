import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'

// Mocks must be declared before the component import so Vitest can hoist them.

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
}))

vi.mock('lucide-vue-next', () => ({
  ZoomIn: { template: '<span data-icon="ZoomIn" />' },
  ZoomOut: { template: '<span data-icon="ZoomOut" />' },
  Maximize2: { template: '<span data-icon="Maximize2" />' },
  RotateCcw: { template: '<span data-icon="RotateCcw" />' },
}))

// Mock the Button UI component so we don't need the full shadcn tree
vi.mock('@/components/ui/button', () => ({
  Button: {
    template: '<button @click="$emit(\'click\')"><slot /></button>',
    emits: ['click'],
  },
}))

// Stub requestAnimationFrame / cancelAnimationFrame globally so that the
// force-simulation loop runs synchronously in jsdom / happy-dom
vi.stubGlobal('requestAnimationFrame', (cb: FrameRequestCallback) => { cb(0); return 0 })
vi.stubGlobal('cancelAnimationFrame', vi.fn())

import EntityRelationshipDiagram from '../EntityRelationshipDiagram.vue'
import type { ErdNode, ErdEdge } from '../EntityRelationshipDiagram.vue'

// ---------------------------------------------------------------------------
// Sample data
// ---------------------------------------------------------------------------

const sampleNodes: ErdNode[] = [
  {
    id: 'crm.Customer',
    name: 'Customer',
    namespace: 'crm',
    moduleName: 'crm',
    fieldCount: 5,
    keyFields: ['ID'],
    isAbstract: false,
    x: 0, y: 0, vx: 0, vy: 0,
    width: 160, height: 56, pinned: false,
  },
  {
    id: 'crm.Address',
    name: 'Address',
    namespace: 'crm',
    moduleName: 'crm',
    fieldCount: 3,
    keyFields: ['ID'],
    isAbstract: false,
    x: 200, y: 0, vx: 0, vy: 0,
    width: 160, height: 56, pinned: false,
  },
]

const sampleEdges: ErdEdge[] = [
  {
    id: 'crm.Customer→crm.Address:address',
    sourceId: 'crm.Customer',
    targetId: 'crm.Address',
    label: '[0..1]',
    isComposition: true,
    associationName: 'address',
  },
]

// ---------------------------------------------------------------------------
// Mount helper
// ---------------------------------------------------------------------------

function mountDiagram(
  nodes: ErdNode[] = sampleNodes,
  edges: ErdEdge[] = [],
) {
  return mount(EntityRelationshipDiagram, {
    props: {
      inputNodes: nodes,
      inputEdges: edges,
    },
    global: {
      mocks: { $t: (k: string) => k },
    },
    attachTo: document.body,
  })
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('EntityRelationshipDiagram', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  // 1. renders one node per inputNode
  it('renders one node per inputNode', async () => {
    const wrapper = mountDiagram(sampleNodes, [])
    await nextTick()

    // Each node is rendered as a <g class="node ..."> inside the SVG
    const nodeGroups = wrapper.findAll('g.node')
    expect(nodeGroups.length).toBe(sampleNodes.length)

    wrapper.unmount()
  })

  // 2. renders node name text
  it('renders node name text for each node', async () => {
    const wrapper = mountDiagram(sampleNodes, [])
    await nextTick()

    const text = wrapper.text()
    expect(text).toContain('Customer')
    expect(text).toContain('Address')

    wrapper.unmount()
  })

  // 3. renders edge path for associations
  it('renders edge path when edges are provided', async () => {
    const wrapper = mountDiagram(sampleNodes, sampleEdges)
    await nextTick()

    // Each edge is rendered as a <path fill="none" ...>
    const edgePaths = wrapper.findAll('path[fill="none"]')
    expect(edgePaths.length).toBeGreaterThanOrEqual(1)

    wrapper.unmount()
  })

  // 4. composition edge has no stroke-dasharray (solid line)
  it('composition edge renders without dashed stroke', async () => {
    const compositionEdges: ErdEdge[] = [
      {
        id: 'crm.Customer→crm.Address:address',
        sourceId: 'crm.Customer',
        targetId: 'crm.Address',
        label: '[0..1]',
        isComposition: true,
        associationName: 'address',
      },
    ]
    const wrapper = mountDiagram(sampleNodes, compositionEdges)
    await nextTick()

    const edgePaths = wrapper.findAll('path[fill="none"]')
    expect(edgePaths.length).toBeGreaterThanOrEqual(1)

    // The composition path should have stroke-dasharray="none" (not a dashed pattern like "6 4")
    const compPath = edgePaths[0]
    const dasharray = compPath.attributes('stroke-dasharray')
    // The template sets :stroke-dasharray="edge.isComposition ? 'none' : '6 4'"
    expect(dasharray).toBe('none')

    wrapper.unmount()
  })

  // 5. association edge has stroke-dasharray with dashes
  it('association (non-composition) edge renders with dashed stroke', async () => {
    const assocEdges: ErdEdge[] = [
      {
        id: 'crm.Customer→sales.Order:orders',
        sourceId: 'crm.Customer',
        targetId: 'crm.Address',
        label: '[0..*]',
        isComposition: false,
        associationName: 'orders',
      },
    ]
    const wrapper = mountDiagram(sampleNodes, assocEdges)
    await nextTick()

    const edgePaths = wrapper.findAll('path[fill="none"]')
    expect(edgePaths.length).toBeGreaterThanOrEqual(1)

    const assocPath = edgePaths[0]
    const dasharray = assocPath.attributes('stroke-dasharray')
    // The template sets stroke-dasharray="6 4" for non-composition edges
    expect(dasharray).toBe('6 4')

    wrapper.unmount()
  })

  // 6. emits 'navigate' event on node click (mousedown + mouseup without drag)
  it('emits navigate event on node click', async () => {
    const wrapper = mountDiagram(sampleNodes, [])
    await nextTick()

    const nodeGroups = wrapper.findAll('g.node')
    expect(nodeGroups.length).toBeGreaterThanOrEqual(1)

    // Trigger mousedown on the first node group
    await nodeGroups[0].trigger('mousedown', { clientX: 0, clientY: 0 })

    // Trigger mouseup on window (simulates releasing without dragging)
    const mouseUpEvent = new MouseEvent('mouseup', { clientX: 0, clientY: 0 })
    window.dispatchEvent(mouseUpEvent)

    await nextTick()

    expect(wrapper.emitted('navigate')).toBeTruthy()
    const emittedPayload = wrapper.emitted('navigate')![0]
    expect(emittedPayload[0]).toBe('crm.Customer')

    wrapper.unmount()
  })

  // 7. shows empty state when no nodes
  it('shows empty state text when no nodes are provided', async () => {
    const wrapper = mountDiagram([], [])
    await nextTick()

    const text = wrapper.text()
    expect(text).toContain('No entities')

    wrapper.unmount()
  })

  // 8. zoom controls are rendered
  it('renders zoom control buttons', async () => {
    const wrapper = mountDiagram(sampleNodes, [])
    await nextTick()

    // The controls section contains 4 buttons (zoom in, zoom out, fit, reset)
    const buttons = wrapper.findAll('button')
    expect(buttons.length).toBeGreaterThanOrEqual(4)

    // Verify the icon stubs are present for zoom controls
    expect(wrapper.find('[data-icon="ZoomIn"]').exists()).toBe(true)
    expect(wrapper.find('[data-icon="ZoomOut"]').exists()).toBe(true)
    expect(wrapper.find('[data-icon="Maximize2"]').exists()).toBe(true)
    expect(wrapper.find('[data-icon="RotateCcw"]').exists()).toBe(true)

    wrapper.unmount()
  })

  // 9. abstract entity node has dashed stroke-dasharray on its background rect
  it('abstract entity node rect has stroke-dasharray set', async () => {
    const nodesWithAbstract: ErdNode[] = [
      ...sampleNodes,
      {
        id: 'crm.BaseEntity',
        name: 'BaseEntity',
        namespace: 'crm',
        moduleName: 'crm',
        fieldCount: 2,
        keyFields: ['ID'],
        isAbstract: true,
        x: 0, y: 200, vx: 0, vy: 0,
        width: 160, height: 56, pinned: false,
      },
    ]

    const wrapper = mountDiagram(nodesWithAbstract, [])
    await nextTick()

    // The abstract node's background rect should have a dashed stroke-dasharray
    // The template renders: :stroke-dasharray="node.isAbstract ? '4 3' : 'none'"
    // We need to find a rect that has stroke-dasharray="4 3"
    const allRects = wrapper.findAll('rect')
    const abstractRect = allRects.find(r => r.attributes('stroke-dasharray') === '4 3')
    expect(abstractRect).toBeDefined()

    wrapper.unmount()
  })
})
