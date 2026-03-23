import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'

// Mocks must be declared before importing the module under test so that Vitest
// can hoist them correctly.

const mockRouterPush = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push: mockRouterPush }),
}))

// lucide-vue-next ships SVG components; stub them for jsdom
vi.mock('lucide-vue-next', () => ({
  GripVertical: { template: '<span data-icon="GripVertical" />' },
}))

import KanbanCard from '../KanbanCard.vue'

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const card = {
  id: 'c1',
  data: {
    name: 'Test Card',
    description: 'A description',
  },
}

function makeProps(overrides: Record<string, unknown> = {}) {
  return {
    card,
    titleField: 'name',
    subtitleField: 'description',
    module: 'crm',
    entity: 'Task',
    keyField: 'ID',
    ...overrides,
  }
}

function mountCard(propsOverrides: Record<string, unknown> = {}) {
  return mount(KanbanCard, {
    props: makeProps(propsOverrides),
    global: {
      mocks: { $t: (k: string) => k },
    },
  })
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('KanbanCard', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.resetAllMocks()
  })

  it('renders title field value', () => {
    const wrapper = mountCard({ titleField: 'name' })
    expect(wrapper.text()).toContain('Test Card')
  })

  it('renders subtitle when subtitleField is provided', () => {
    const wrapper = mountCard({ subtitleField: 'description' })
    expect(wrapper.text()).toContain('A description')
  })

  it('does not render subtitle when subtitleField is empty', () => {
    const wrapper = mountCard({ subtitleField: '' })
    // The second <p> (subtitle) should not be present
    const paragraphs = wrapper.findAll('p')
    // Only the title paragraph should exist
    expect(paragraphs).toHaveLength(1)
    expect(wrapper.text()).not.toContain('A description')
  })

  it('does not render subtitle when the subtitle data value is null', () => {
    const cardWithNullDesc = {
      id: 'c2',
      data: { name: 'Another Card', description: null },
    }
    const wrapper = mount(KanbanCard, {
      props: makeProps({ card: cardWithNullDesc, subtitleField: 'description' }),
      global: { mocks: { $t: (k: string) => k } },
    })
    const paragraphs = wrapper.findAll('p')
    expect(paragraphs).toHaveLength(1)
  })

  it('shows (no title) fallback for missing title field', () => {
    const wrapper = mountCard({ titleField: 'nonExistentField' })
    expect(wrapper.text()).toContain('(no title)')
  })

  it('navigates to detail on card click', async () => {
    const wrapper = mountCard()
    await wrapper.find('.kanban-card').trigger('click')
    expect(mockRouterPush).toHaveBeenCalledWith('/odata/crm/Task/c1')
  })

  it('does not navigate when the drag handle is clicked', async () => {
    const wrapper = mountCard()
    await wrapper.find('.kanban-card-handle').trigger('click')
    // click.stop prevents propagation to the card div
    expect(mockRouterPush).not.toHaveBeenCalled()
  })

  it('has .kanban-card-handle element', () => {
    const wrapper = mountCard()
    expect(wrapper.find('.kanban-card-handle').exists()).toBe(true)
  })

  it('has data-id attribute equal to card.id', () => {
    const wrapper = mountCard()
    expect(wrapper.find('.kanban-card').attributes('data-id')).toBe('c1')
  })

  it('renders numeric card id in data-id attribute', () => {
    const numericCard = { id: 42, data: { name: 'Numeric ID Card' } }
    const wrapper = mount(KanbanCard, {
      props: makeProps({ card: numericCard }),
      global: { mocks: { $t: (k: string) => k } },
    })
    expect(wrapper.find('.kanban-card').attributes('data-id')).toBe('42')
  })

  it('builds navigation path using module, entity and card id', async () => {
    const wrapper = mount(KanbanCard, {
      props: makeProps({ module: 'sales', entity: 'Order', card: { id: 'ord-99', data: { name: 'Order X' } } }),
      global: { mocks: { $t: (k: string) => k } },
    })
    await wrapper.find('.kanban-card').trigger('click')
    expect(mockRouterPush).toHaveBeenCalledWith('/odata/sales/Order/ord-99')
  })
})
