import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { ref } from 'vue'

// Mock i18n (transitively imported by other modules)
vi.mock('@/i18n', () => ({
  default: {
    global: {
      locale: { value: 'en' },
      t: (key: string) => key,
    },
  },
}))

// Mock odataService — must appear before any import that uses it
vi.mock('@/services/odataService', () => ({
  odataService: {
    query: vi.fn(),
    update: vi.fn(),
  },
}))

import { useKanban } from '../useKanban'
import { odataService } from '@/services/odataService'
import type { FieldMetadata } from '@/types/metadata'

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const statusFieldMeta: FieldMetadata = {
  name: 'status',
  type: 'Enum',
  isRequired: false,
  isReadOnly: false,
  isComputed: false,
  annotations: {},
  enumValues: [
    { name: 'Open', value: 'Open', displayName: 'Open' },
    { name: 'InProgress', value: 'InProgress', displayName: 'In Progress' },
    { name: 'Done', value: 'Done', displayName: 'Done' },
  ],
}

function makeOptions(overrides: Record<string, unknown> = {}) {
  return {
    module: ref('test'),
    entity: ref('Task'),
    statusField: ref<FieldMetadata | null>(statusFieldMeta),
    titleField: ref('name'),
    subtitleField: ref(''),
    searchQuery: ref(''),
    keyField: ref('ID'),
    ...overrides,
  }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('useKanban', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.resetAllMocks()
  })

  // -------------------------------------------------------------------------
  // buildColumns
  // -------------------------------------------------------------------------

  describe('buildColumns', () => {
    it('builds columns from enum values', () => {
      const { buildColumns, columns } = useKanban(makeOptions())

      buildColumns()

      expect(columns.value).toHaveLength(3)
      expect(columns.value[0].value).toBe('Open')
      expect(columns.value[0].label).toBe('Open')
      expect(columns.value[1].value).toBe('InProgress')
      expect(columns.value[1].label).toBe('In Progress')
      expect(columns.value[2].value).toBe('Done')
      expect(columns.value[2].label).toBe('Done')
    })

    it('each built column starts with empty cards and isLoading false', () => {
      const { buildColumns, columns } = useKanban(makeOptions())

      buildColumns()

      for (const col of columns.value) {
        expect(col.cards).toEqual([])
        expect(col.isLoading).toBe(false)
      }
    })

    it('does nothing when statusField is null', () => {
      const options = makeOptions({ statusField: ref<FieldMetadata | null>(null) })
      const { buildColumns, columns } = useKanban(options)

      buildColumns()

      expect(columns.value).toHaveLength(0)
    })

    it('falls back to enum name when displayName is absent', () => {
      const fieldWithoutDisplayName: FieldMetadata = {
        ...statusFieldMeta,
        enumValues: [
          { name: 'Open', value: 'Open' },   // no displayName
        ],
      }
      const options = makeOptions({ statusField: ref<FieldMetadata | null>(fieldWithoutDisplayName) })
      const { buildColumns, columns } = useKanban(options)

      buildColumns()

      expect(columns.value[0].label).toBe('Open')
    })
  })

  // -------------------------------------------------------------------------
  // loadCards
  // -------------------------------------------------------------------------

  describe('loadCards', () => {
    it('distributes items into correct columns', async () => {
      vi.mocked(odataService.query).mockResolvedValue({
        value: [
          { ID: '1', name: 'Task A', status: 'Open' },
          { ID: '2', name: 'Task B', status: 'Open' },
          { ID: '3', name: 'Task C', status: 'Done' },
        ],
      } as never)

      const { loadCards, columns } = useKanban(makeOptions())
      await loadCards()

      const openCol = columns.value.find((c) => c.value === 'Open')
      const inProgressCol = columns.value.find((c) => c.value === 'InProgress')
      const doneCol = columns.value.find((c) => c.value === 'Done')

      expect(openCol?.cards).toHaveLength(2)
      expect(inProgressCol?.cards).toHaveLength(0)
      expect(doneCol?.cards).toHaveLength(1)
    })

    it('sets isLoading to true during fetch and false after', async () => {
      let resolveQuery!: (v: unknown) => void
      vi.mocked(odataService.query).mockReturnValue(
        new Promise((r) => { resolveQuery = r }),
      )

      const { loadCards, isLoading } = useKanban(makeOptions())

      const promise = loadCards()
      expect(isLoading.value).toBe(true)

      resolveQuery({ value: [] })
      await promise

      expect(isLoading.value).toBe(false)
    })

    it('sets error on query failure', async () => {
      vi.mocked(odataService.query).mockRejectedValue(new Error('Network failure'))

      const { loadCards, error } = useKanban(makeOptions())
      await loadCards()

      expect(error.value).toBe('Network failure')
    })

    it('sets non-Error throws as string in error', async () => {
      vi.mocked(odataService.query).mockRejectedValue('timeout')

      const { loadCards, error } = useKanban(makeOptions())
      await loadCards()

      expect(error.value).toBe('timeout')
    })

    it('resets error to null before each fetch', async () => {
      vi.mocked(odataService.query)
        .mockRejectedValueOnce(new Error('First error'))
        .mockResolvedValue({ value: [] } as never)

      const { loadCards, error } = useKanban(makeOptions())

      await loadCards()
      expect(error.value).toBe('First error')

      await loadCards()
      expect(error.value).toBeNull()
    })

    it('does nothing when statusField is null', async () => {
      const options = makeOptions({ statusField: ref<FieldMetadata | null>(null) })
      const { loadCards } = useKanban(options)

      await loadCards()

      expect(vi.mocked(odataService.query)).not.toHaveBeenCalled()
    })

    it('calls odataService.query with the correct module and entity', async () => {
      vi.mocked(odataService.query).mockResolvedValue({ value: [] } as never)

      const { loadCards } = useKanban(makeOptions())
      await loadCards()

      expect(vi.mocked(odataService.query)).toHaveBeenCalledWith(
        'test',
        'Task',
        expect.objectContaining({ $orderby: 'status asc' }),
      )
    })
  })

  // -------------------------------------------------------------------------
  // moveCard
  // -------------------------------------------------------------------------

  describe('moveCard', () => {
    it('moves card optimistically before await', async () => {
      vi.mocked(odataService.query).mockResolvedValue({
        value: [{ ID: 'card-1', name: 'My Task', status: 'Open' }],
      } as never)

      let resolveUpdate!: () => void
      vi.mocked(odataService.update).mockReturnValue(
        new Promise<void>((r) => { resolveUpdate = r }),
      )

      const { loadCards, moveCard, columns } = useKanban(makeOptions())
      await loadCards()

      const openColBefore = columns.value.find((c) => c.value === 'Open')
      expect(openColBefore?.cards).toHaveLength(1)

      const movePromise = moveCard('card-1', 'Open', 'Done')

      // Optimistic: card already in Done, not in Open
      const openColAfter = columns.value.find((c) => c.value === 'Open')
      const doneColAfter = columns.value.find((c) => c.value === 'Done')
      expect(openColAfter?.cards).toHaveLength(0)
      expect(doneColAfter?.cards).toHaveLength(1)

      resolveUpdate()
      await movePromise
    })

    it('rolls back on update failure', async () => {
      vi.mocked(odataService.query).mockResolvedValue({
        value: [{ ID: 'card-1', name: 'My Task', status: 'Open' }],
      } as never)
      vi.mocked(odataService.update).mockRejectedValue(new Error('Save failed'))

      const { loadCards, moveCard, columns, error } = useKanban(makeOptions())
      await loadCards()

      await moveCard('card-1', 'Open', 'Done')

      // Card should be back in Open
      const openCol = columns.value.find((c) => c.value === 'Open')
      const doneCol = columns.value.find((c) => c.value === 'Done')
      expect(openCol?.cards).toHaveLength(1)
      expect(doneCol?.cards).toHaveLength(0)
      expect(error.value).toBe('Save failed')
    })

    it('calls odataService.update with the correct arguments', async () => {
      vi.mocked(odataService.query).mockResolvedValue({
        value: [{ ID: 'card-1', name: 'My Task', status: 'Open' }],
      } as never)
      vi.mocked(odataService.update).mockResolvedValue(undefined as never)

      const { loadCards, moveCard } = useKanban(makeOptions())
      await loadCards()
      await moveCard('card-1', 'Open', 'Done')

      expect(vi.mocked(odataService.update)).toHaveBeenCalledWith(
        'test',
        'Task',
        'card-1',
        { status: 'Done' },
      )
    })

    it('does nothing when source column does not exist', async () => {
      vi.mocked(odataService.query).mockResolvedValue({ value: [] } as never)

      const { loadCards, moveCard } = useKanban(makeOptions())
      await loadCards()

      // Should not throw
      await moveCard('card-1', 'NonExistent', 'Done')
      expect(vi.mocked(odataService.update)).not.toHaveBeenCalled()
    })

    it('does nothing when card is not found in source column', async () => {
      vi.mocked(odataService.query).mockResolvedValue({ value: [] } as never)

      const { loadCards, moveCard } = useKanban(makeOptions())
      await loadCards()

      await moveCard('ghost-id', 'Open', 'Done')
      expect(vi.mocked(odataService.update)).not.toHaveBeenCalled()
    })
  })

  // -------------------------------------------------------------------------
  // filteredColumns
  // -------------------------------------------------------------------------

  describe('filteredColumns', () => {
    it('returns all columns when searchQuery is empty', async () => {
      vi.mocked(odataService.query).mockResolvedValue({
        value: [
          { ID: '1', name: 'Alpha', status: 'Open' },
          { ID: '2', name: 'Beta', status: 'Done' },
        ],
      } as never)

      const options = makeOptions()
      const { loadCards, filteredColumns } = useKanban(options)
      await loadCards()

      const total = filteredColumns.value.reduce((s, c) => s + c.cards.length, 0)
      expect(total).toBe(2)
    })

    it('filters cards by title matching searchQuery', async () => {
      vi.mocked(odataService.query).mockResolvedValue({
        value: [
          { ID: '1', name: 'Fix login bug', status: 'Open' },
          { ID: '2', name: 'Add dark mode', status: 'Open' },
          { ID: '3', name: 'Improve perf', status: 'Done' },
        ],
      } as never)

      const searchQuery = ref('dark')
      const options = makeOptions({ searchQuery })
      const { loadCards, filteredColumns } = useKanban(options)
      await loadCards()

      const total = filteredColumns.value.reduce((s, c) => s + c.cards.length, 0)
      expect(total).toBe(1)

      const openCol = filteredColumns.value.find((c) => c.value === 'Open')
      expect(openCol?.cards[0].data['name']).toBe('Add dark mode')
    })

    it('filters cards by subtitle when subtitleField is set', async () => {
      vi.mocked(odataService.query).mockResolvedValue({
        value: [
          { ID: '1', name: 'Task A', description: 'backend work', status: 'Open' },
          { ID: '2', name: 'Task B', description: 'frontend work', status: 'Open' },
        ],
      } as never)

      const searchQuery = ref('frontend')
      const options = makeOptions({ searchQuery, subtitleField: ref('description') })
      const { loadCards, filteredColumns } = useKanban(options)
      await loadCards()

      const total = filteredColumns.value.reduce((s, c) => s + c.cards.length, 0)
      expect(total).toBe(1)
    })

    it('is case-insensitive', async () => {
      vi.mocked(odataService.query).mockResolvedValue({
        value: [
          { ID: '1', name: 'Important Task', status: 'Open' },
        ],
      } as never)

      const searchQuery = ref('IMPORTANT')
      const options = makeOptions({ searchQuery })
      const { loadCards, filteredColumns } = useKanban(options)
      await loadCards()

      const total = filteredColumns.value.reduce((s, c) => s + c.cards.length, 0)
      expect(total).toBe(1)
    })

    it('trims whitespace from searchQuery before filtering', async () => {
      vi.mocked(odataService.query).mockResolvedValue({
        value: [
          { ID: '1', name: 'Alpha', status: 'Open' },
          { ID: '2', name: 'Beta', status: 'Done' },
        ],
      } as never)

      const searchQuery = ref('   ')   // only whitespace → treat as empty
      const options = makeOptions({ searchQuery })
      const { loadCards, filteredColumns } = useKanban(options)
      await loadCards()

      const total = filteredColumns.value.reduce((s, c) => s + c.cards.length, 0)
      expect(total).toBe(2)
    })
  })

  // -------------------------------------------------------------------------
  // totalCards
  // -------------------------------------------------------------------------

  describe('totalCards', () => {
    it('counts all cards across all columns', async () => {
      vi.mocked(odataService.query).mockResolvedValue({
        value: [
          { ID: '1', name: 'Task A', status: 'Open' },
          { ID: '2', name: 'Task B', status: 'Open' },
          { ID: '3', name: 'Task C', status: 'Done' },
        ],
      } as never)

      const { loadCards, totalCards } = useKanban(makeOptions())
      await loadCards()

      expect(totalCards.value).toBe(3)
    })

    it('is zero before any load', () => {
      const { totalCards } = useKanban(makeOptions())
      expect(totalCards.value).toBe(0)
    })

    it('is zero when all columns are empty', async () => {
      vi.mocked(odataService.query).mockResolvedValue({ value: [] } as never)

      const { loadCards, totalCards } = useKanban(makeOptions())
      await loadCards()

      expect(totalCards.value).toBe(0)
    })
  })
})
