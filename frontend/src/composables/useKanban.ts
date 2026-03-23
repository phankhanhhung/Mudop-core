import { ref, computed, type Ref } from 'vue'
import { odataService } from '@/services/odataService'
import type { FieldMetadata } from '@/types/metadata'

export interface KanbanColumn {
  value: string | number
  label: string
  cards: KanbanCard[]
  isLoading: boolean
}

export interface KanbanCard {
  id: string | number
  data: Record<string, unknown>
}

export interface KanbanOptions {
  module: Ref<string>
  entity: Ref<string>
  statusField: Ref<FieldMetadata | null>
  titleField: Ref<string>
  subtitleField: Ref<string>
  searchQuery: Ref<string>
  keyField: Ref<string>
}

export function useKanban(options: KanbanOptions) {
  const { module, entity, statusField, titleField, subtitleField, searchQuery, keyField } = options

  const columns = ref<KanbanColumn[]>([])
  const isLoading = ref(false)
  const error = ref<string | null>(null)

  // Build columns from enum values on the status field
  function buildColumns() {
    if (!statusField.value?.enumValues) return
    columns.value = statusField.value.enumValues.map((ev) => ({
      value: ev.value,
      label: ev.displayName ?? ev.name,
      cards: [],
      isLoading: false,
    }))
  }

  // Load all cards and distribute into columns
  async function loadCards() {
    if (!statusField.value) return
    isLoading.value = true
    error.value = null
    try {
      const result = await odataService.query<Record<string, unknown>>(
        module.value,
        entity.value,
        { $orderby: `${statusField.value.name} asc` },
      )

      // Rebuild columns first so they are fresh
      buildColumns()

      const items = result.value ?? []
      for (const col of columns.value) {
        col.cards = items
          .filter((item) => {
            const val = item[statusField.value!.name]
            // Compare as string — OData may return string or number
            return String(val) === String(col.value)
          })
          .map((item) => ({
            id: item[keyField.value] as string | number,
            data: item,
          }))
      }
    } catch (err) {
      error.value = err instanceof Error ? err.message : String(err)
    } finally {
      isLoading.value = false
    }
  }

  // Move a card from one column to another (optimistic update with rollback)
  async function moveCard(
    cardId: string | number,
    fromColValue: string | number,
    toColValue: string | number,
  ) {
    if (!statusField.value) return

    const fromCol = columns.value.find((c) => String(c.value) === String(fromColValue))
    const toCol = columns.value.find((c) => String(c.value) === String(toColValue))
    if (!fromCol || !toCol) return

    const cardIndex = fromCol.cards.findIndex((c) => String(c.id) === String(cardId))
    if (cardIndex === -1) return

    // Optimistic: remove from source and add to destination
    const [card] = fromCol.cards.splice(cardIndex, 1)
    card.data = { ...card.data, [statusField.value.name]: toColValue }
    toCol.cards.push(card)

    try {
      await odataService.update(
        module.value,
        entity.value,
        String(cardId),
        { [statusField.value.name]: toColValue },
      )
    } catch (err) {
      // Rollback: remove from destination and restore to source at original index
      const revertIndex = toCol.cards.findIndex((c) => String(c.id) === String(cardId))
      if (revertIndex !== -1) {
        const [revertCard] = toCol.cards.splice(revertIndex, 1)
        revertCard.data = { ...revertCard.data, [statusField.value.name]: fromColValue }
        fromCol.cards.splice(cardIndex, 0, revertCard)
      }
      error.value = err instanceof Error ? err.message : String(err)
    }
  }

  // Client-side filtered columns based on search query (title + subtitle match)
  const filteredColumns = computed<KanbanColumn[]>(() => {
    const q = searchQuery.value.trim().toLowerCase()
    if (!q) return columns.value
    return columns.value.map((col) => ({
      ...col,
      cards: col.cards.filter((card) => {
        const titleVal = String(card.data[titleField.value] ?? '')
        const subtitleVal = subtitleField.value
          ? String(card.data[subtitleField.value] ?? '')
          : ''
        return titleVal.toLowerCase().includes(q) || subtitleVal.toLowerCase().includes(q)
      }),
    }))
  })

  const totalCards = computed(() =>
    columns.value.reduce((sum, col) => sum + col.cards.length, 0),
  )

  return {
    columns,
    filteredColumns,
    isLoading,
    error,
    totalCards,
    buildColumns,
    loadCards,
    moveCard,
  }
}
