import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'

// ---------------------------------------------------------------------------
// Mocks — must be declared before importing the component under test
// ---------------------------------------------------------------------------

// SortableJS: stub Sortable.create so we can assert it is called on mount
vi.mock('sortablejs', () => ({
  default: {
    create: vi.fn(() => ({
      option: vi.fn(),
      destroy: vi.fn(),
    })),
  },
}))

// KanbanCard: replace with a minimal stub so we avoid transitive deps
vi.mock('../KanbanCard.vue', () => ({
  default: {
    name: 'KanbanCard',
    template: '<div class="mock-card" :data-id="card.id" />',
    props: ['card', 'titleField', 'subtitleField', 'module', 'entity', 'keyField'],
  },
}))

// vue-router: KanbanCard uses useRouter — still needed by the stub resolution chain
vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
}))

import KanbanColumn from '../KanbanColumn.vue'
import Sortable from 'sortablejs'

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const cards = [
  { id: 'c1', data: { name: 'Card 1' } },
  { id: 'c2', data: { name: 'Card 2' } },
]

function makeColumn(overrides: Record<string, unknown> = {}) {
  return {
    value: 'Open',
    label: 'Open',
    cards,
    isLoading: false,
    ...overrides,
  }
}

function makeProps(columnOverrides: Record<string, unknown> = {}, propOverrides: Record<string, unknown> = {}) {
  return {
    column: makeColumn(columnOverrides),
    titleField: 'name',
    subtitleField: '',
    module: 'crm',
    entity: 'Task',
    keyField: 'ID',
    ...propOverrides,
  }
}

function mountColumn(columnOverrides: Record<string, unknown> = {}, propOverrides: Record<string, unknown> = {}) {
  return mount(KanbanColumn, {
    props: makeProps(columnOverrides, propOverrides),
    global: {
      mocks: { $t: (k: string) => k },
    },
  })
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('KanbanColumn', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.resetAllMocks()
  })

  // -------------------------------------------------------------------------
  // Rendering
  // -------------------------------------------------------------------------

  it('renders column label', () => {
    const wrapper = mountColumn({ label: 'Open' })
    expect(wrapper.text()).toContain('Open')
  })

  it('renders card count badge showing number of cards', () => {
    const wrapper = mountColumn({ cards })
    expect(wrapper.text()).toContain('2')
  })

  it('renders a mock card for each card in the column', () => {
    const wrapper = mountColumn({ cards })
    expect(wrapper.findAll('.mock-card')).toHaveLength(2)
  })

  it('renders 0 in badge when cards array is empty', () => {
    const wrapper = mountColumn({ cards: [] })
    expect(wrapper.text()).toContain('0')
  })

  // -------------------------------------------------------------------------
  // Loading state
  // -------------------------------------------------------------------------

  it('shows shimmer loading skeleton when column.isLoading is true', () => {
    const wrapper = mountColumn({ isLoading: true, cards })
    // The loading template renders divs with animate-pulse
    const shimmers = wrapper.findAll('.animate-pulse')
    expect(shimmers.length).toBeGreaterThan(0)
  })

  it('does not render mock cards while loading', () => {
    const wrapper = mountColumn({ isLoading: true, cards })
    expect(wrapper.findAll('.mock-card')).toHaveLength(0)
  })

  it('does not show shimmer when not loading', () => {
    const wrapper = mountColumn({ isLoading: false, cards })
    expect(wrapper.findAll('.animate-pulse')).toHaveLength(0)
  })

  // -------------------------------------------------------------------------
  // Empty state
  // -------------------------------------------------------------------------

  it('shows "No items" placeholder when cards array is empty and not loading', () => {
    const wrapper = mountColumn({ cards: [], isLoading: false })
    expect(wrapper.text()).toContain('No items')
  })

  it('does not show "No items" when cards are present', () => {
    const wrapper = mountColumn({ cards, isLoading: false })
    expect(wrapper.text()).not.toContain('No items')
  })

  // -------------------------------------------------------------------------
  // data-col-value attribute
  // -------------------------------------------------------------------------

  it('sets data-col-value attribute on root element', () => {
    const wrapper = mountColumn({ value: 'InProgress' })
    expect(wrapper.attributes('data-col-value')).toBe('InProgress')
  })

  // -------------------------------------------------------------------------
  // SortableJS initialization
  // -------------------------------------------------------------------------

  it('initializes SortableJS on mount', () => {
    mountColumn()
    expect(vi.mocked(Sortable.create)).toHaveBeenCalledTimes(1)
  })

  it('passes kanban-cards group and kanban-card-handle handle to SortableJS', () => {
    mountColumn()
    const callArgs = vi.mocked(Sortable.create).mock.calls[0]
    // First arg is the DOM element; second is the options object
    const sortableOptions = callArgs[1] as Record<string, unknown>
    expect(sortableOptions.group).toBe('kanban-cards')
    expect(sortableOptions.handle).toBe('.kanban-card-handle')
  })

  it('destroys SortableJS on unmount', () => {
    const destroyMock = vi.fn()
    vi.mocked(Sortable.create).mockReturnValue({
      option: vi.fn(),
      destroy: destroyMock,
    } as never)

    const wrapper = mountColumn()
    wrapper.unmount()

    expect(destroyMock).toHaveBeenCalledTimes(1)
  })

  // -------------------------------------------------------------------------
  // Emits
  // -------------------------------------------------------------------------

  it('emits move event when SortableJS onEnd fires for cross-column drag', () => {
    // Capture the onEnd callback from SortableJS options
    let capturedOnEnd: ((evt: Record<string, unknown>) => void) | null = null
    vi.mocked(Sortable.create).mockImplementation((_el, options) => {
      capturedOnEnd = (options as Record<string, Function>).onEnd as (evt: Record<string, unknown>) => void
      return { option: vi.fn(), destroy: vi.fn() } as never
    })

    const wrapper = mountColumn({ value: 'Open' })

    // Simulate a card being dragged from this column to a different DOM element
    const fakeFromEl = document.createElement('div')
    const fakeToEl = document.createElement('div')
    fakeToEl.dataset.colValue = 'Done'

    const fakeItem = document.createElement('div')
    fakeItem.dataset.id = 'c1'
    // Place item in fromEl to simulate DOM before revert
    fakeFromEl.appendChild(fakeItem)

    // Make fakeToEl.closest('[data-col-value]') return itself
    fakeToEl.setAttribute('data-col-value', 'Done')

    capturedOnEnd!({
      item: fakeItem,
      from: fakeFromEl,
      to: fakeToEl,
      oldIndex: 0,
      newIndex: 0,
    })

    const emitted = wrapper.emitted('move')
    expect(emitted).toBeTruthy()
    expect(emitted![0]).toEqual(['c1', 'Open', 'Done'])
  })

  it('does not emit move event when drag stays in same column', () => {
    let capturedOnEnd: ((evt: Record<string, unknown>) => void) | null = null
    vi.mocked(Sortable.create).mockImplementation((_el, options) => {
      capturedOnEnd = (options as Record<string, Function>).onEnd as (evt: Record<string, unknown>) => void
      return { option: vi.fn(), destroy: vi.fn() } as never
    })

    const wrapper = mountColumn({ value: 'Open' })

    // from === to — same column reorder.
    // Attach to body so happy-dom can perform appendChild correctly.
    const fakeContainer = document.createElement('div')
    document.body.appendChild(fakeContainer)

    const fakeItem = document.createElement('div')
    fakeItem.dataset.id = 'c1'
    const fakeSibling = document.createElement('div')
    fakeContainer.appendChild(fakeItem)
    fakeContainer.appendChild(fakeSibling)

    capturedOnEnd!({
      item: fakeItem,
      from: fakeContainer,
      to: fakeContainer,
      oldIndex: 0,
      newIndex: 1,
    })

    document.body.removeChild(fakeContainer)

    expect(wrapper.emitted('move')).toBeFalsy()
  })
})
