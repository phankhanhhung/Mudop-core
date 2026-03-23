/**
 * Deep Insert/Update Composable — handles composition-aware nested CRUD.
 *
 * Automatically:
 * - Detects composition associations from metadata
 * - Tracks composition changes (added/modified/removed items)
 * - Builds deep insert payloads (parent + nested compositions)
 * - Builds deep update payloads (parent changes + composition delta)
 * - Auto-populates FK fields for child entities
 * - Supports rollback of composition changes
 *
 * Usage:
 *   const {
 *     compositions,
 *     addCompositionItem, removeCompositionItem, updateCompositionItem,
 *     buildDeepInsertPayload, buildDeepUpdatePayload,
 *     resetCompositions
 *   } = useDeepOperation({ module: 'myapp', entitySet: 'Orders', metadata })
 */

import { ref, shallowRef, computed, type Ref, type ComputedRef } from 'vue'
import type { EntityMetadata, AssociationMetadata } from '@/types/metadata'

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface CompositionState {
  /** Association metadata */
  association: AssociationMetadata
  /** Current items */
  items: Ref<Record<string, unknown>[]>
  /** Original items (from server) */
  originalItems: Record<string, unknown>[]
  /** Items added in this session */
  addedItems: Ref<Record<string, unknown>[]>
  /** Items modified in this session (key → changed fields) */
  modifiedItems: Ref<Map<string, Record<string, unknown>>>
  /** Item keys marked for removal */
  removedKeys: Ref<Set<string>>
  /** Whether this composition has changes */
  isDirty: ComputedRef<boolean>
}

export interface UseDeepOperationOptions {
  module: string
  entitySet: string
  metadata: EntityMetadata
  /** Optional: parent entity key (for edit mode) */
  parentKey?: string
}

export interface UseDeepOperationReturn {
  /** All composition states keyed by association name */
  compositions: Ref<Map<string, CompositionState>>

  /** Initialize compositions from loaded entity data */
  initFromEntity: (entityData: Record<string, unknown>) => void

  /** Add an item to a composition */
  addCompositionItem: (associationName: string, item: Record<string, unknown>) => void

  /** Update an item in a composition */
  updateCompositionItem: (associationName: string, key: string, changes: Record<string, unknown>) => void

  /** Remove an item from a composition */
  removeCompositionItem: (associationName: string, key: string) => void

  /** Build deep insert payload (for create) */
  buildDeepInsertPayload: (parentData: Record<string, unknown>) => Record<string, unknown>

  /** Build deep update payload (for update) */
  buildDeepUpdatePayload: (parentPatch: Record<string, unknown>) => Record<string, unknown>

  /** Whether any composition has changes */
  hasCompositionChanges: ComputedRef<boolean>

  /** Reset all composition changes */
  resetCompositions: () => void

  /** Get composition items for display */
  getCompositionItems: (associationName: string) => Record<string, unknown>[]
}

export function useDeepOperation(options: UseDeepOperationOptions): UseDeepOperationReturn {
  const { metadata, parentKey } = options
  const compositions = shallowRef(new Map<string, CompositionState>())

  // Initialize composition states from metadata
  const compositionAssociations = metadata.associations.filter(a => a.isComposition)
  for (const assoc of compositionAssociations) {
    compositions.value.set(assoc.name, createCompositionState(assoc))
  }

  const hasCompositionChanges = computed(() => {
    for (const [, state] of compositions.value) {
      if (state.isDirty.value) return true
    }
    return false
  })

  /**
   * Initialize composition data from a loaded entity.
   */
  function initFromEntity(entityData: Record<string, unknown>): void {
    for (const assoc of compositionAssociations) {
      const navData = entityData[assoc.name]
      const state = compositions.value.get(assoc.name)
      if (!state) continue

      if (Array.isArray(navData)) {
        state.items.value = navData.map(item => ({ ...item as Record<string, unknown> }))
        state.originalItems = navData.map(item => ({ ...item as Record<string, unknown> }))
      } else {
        state.items.value = []
        state.originalItems = []
      }

      // Reset tracking
      state.addedItems.value = []
      state.modifiedItems.value = new Map()
      state.removedKeys.value = new Set()
    }
  }

  /**
   * Add an item to a composition.
   */
  function addCompositionItem(associationName: string, item: Record<string, unknown>): void {
    const state = compositions.value.get(associationName)
    if (!state) return

    // Auto-populate FK if available
    const assoc = state.association
    if (assoc.foreignKey && parentKey) {
      item[assoc.foreignKey] = parentKey
    }

    state.items.value.push({ ...item })
    state.addedItems.value.push({ ...item })
  }

  /**
   * Update an item in a composition.
   */
  function updateCompositionItem(
    associationName: string,
    key: string,
    changes: Record<string, unknown>
  ): void {
    const state = compositions.value.get(associationName)
    if (!state) return

    // Find and update the item
    const index = state.items.value.findIndex((item: Record<string, unknown>) => getItemKey(item) === key)
    if (index < 0) return

    const item = state.items.value[index]
    Object.assign(item, changes)

    // Track modification
    const existing = state.modifiedItems.value.get(key)
    if (existing) {
      Object.assign(existing, changes)
    } else {
      state.modifiedItems.value.set(key, { ...changes })
    }
  }

  /**
   * Remove an item from a composition.
   */
  function removeCompositionItem(associationName: string, key: string): void {
    const state = compositions.value.get(associationName)
    if (!state) return

    // Remove from items list
    state.items.value = state.items.value.filter((item: Record<string, unknown>) => getItemKey(item) !== key)

    // Check if it was a newly added item
    const addedIndex = state.addedItems.value.findIndex((item: Record<string, unknown>) => getItemKey(item) === key)
    if (addedIndex >= 0) {
      // Just remove from added (no server-side deletion needed)
      state.addedItems.value.splice(addedIndex, 1)
    } else {
      // Mark for server-side deletion
      state.removedKeys.value.add(key)
    }

    // Remove from modified tracking
    state.modifiedItems.value.delete(key)
  }

  /**
   * Build deep insert payload for creating a parent entity with compositions.
   */
  function buildDeepInsertPayload(parentData: Record<string, unknown>): Record<string, unknown> {
    const payload = { ...parentData }

    for (const [name, state] of compositions.value) {
      if (state.items.value.length > 0) {
        // Include all items in the create payload
        payload[name] = state.items.value.map((item: Record<string, unknown>) => {
          const cleaned = { ...item }
          // Remove keys that the server should generate
          delete cleaned['Id']
          delete cleaned['ID']
          delete cleaned['id']
          // Remove OData metadata
          for (const key of Object.keys(cleaned)) {
            if (key.startsWith('@')) delete cleaned[key]
          }
          return cleaned
        })
      }
    }

    return payload
  }

  /**
   * Build deep update payload for updating a parent entity with composition changes.
   */
  function buildDeepUpdatePayload(parentPatch: Record<string, unknown>): Record<string, unknown> {
    const payload = { ...parentPatch }

    for (const [name, state] of compositions.value) {
      if (!state.isDirty.value) continue

      const compositionPayload: Record<string, unknown>[] = []

      // Added items
      for (const item of state.addedItems.value) {
        const cleaned = { ...item }
        delete cleaned['Id']
        delete cleaned['ID']
        delete cleaned['id']
        for (const key of Object.keys(cleaned)) {
          if (key.startsWith('@')) delete cleaned[key]
        }
        compositionPayload.push(cleaned)
      }

      // Modified items (include full item with key for server identification)
      for (const [key, changes] of state.modifiedItems.value) {
        const original = state.originalItems.find((item: Record<string, unknown>) => getItemKey(item) === key)
        if (original) {
          compositionPayload.push({
            ...original,
            ...changes,
          })
        }
      }

      // Unchanged items that should be kept
      for (const item of state.originalItems) {
        const itemKey = getItemKey(item)
        if (
          !state.removedKeys.value.has(itemKey) &&
          !state.modifiedItems.value.has(itemKey)
        ) {
          compositionPayload.push({ ...item })
        }
      }

      payload[name] = compositionPayload
    }

    return payload
  }

  /**
   * Get current composition items for display.
   */
  function getCompositionItems(associationName: string): Record<string, unknown>[] {
    return compositions.value.get(associationName)?.items.value ?? []
  }

  /**
   * Reset all composition changes to original state.
   */
  function resetCompositions(): void {
    for (const [, state] of compositions.value) {
      state.items.value = state.originalItems.map((item: Record<string, unknown>) => ({ ...item }))
      state.addedItems.value = []
      state.modifiedItems.value = new Map()
      state.removedKeys.value = new Set()
    }
  }

  return {
    compositions,
    initFromEntity,
    addCompositionItem,
    updateCompositionItem,
    removeCompositionItem,
    buildDeepInsertPayload,
    buildDeepUpdatePayload,
    hasCompositionChanges,
    resetCompositions,
    getCompositionItems,
  }
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function createCompositionState(assoc: AssociationMetadata): CompositionState {
  const items = ref<Record<string, unknown>[]>([])
  const addedItems = ref<Record<string, unknown>[]>([])
  const modifiedItems = ref<Map<string, Record<string, unknown>>>(new Map())
  const removedKeys = ref<Set<string>>(new Set())

  const isDirty = computed(() =>
    addedItems.value.length > 0 ||
    modifiedItems.value.size > 0 ||
    removedKeys.value.size > 0
  )

  return {
    association: assoc,
    items,
    originalItems: [],
    addedItems,
    modifiedItems,
    removedKeys,
    isDirty,
  }
}

function getItemKey(item: Record<string, unknown>): string {
  return String(item['Id'] ?? item['ID'] ?? item['id'] ?? '')
}
