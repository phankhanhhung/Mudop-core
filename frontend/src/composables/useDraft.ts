import { ref, computed, onUnmounted, type Ref, type ComputedRef } from 'vue'
import { DraftManager } from '@/odata/DraftManager'
import type { DraftInstance } from '@/odata/types'

// Singleton DraftManager shared across the app
let sharedDraftManager: DraftManager | null = null

function getSharedDraftManager(): DraftManager {
  if (!sharedDraftManager) {
    sharedDraftManager = new DraftManager()
  }
  return sharedDraftManager
}

export interface UseDraftOptions {
  module: string
  entitySet: string
  entityKey?: string // undefined = creating new entity
  enabled?: boolean // default true
  autoSaveInterval?: number // default 30000ms
}

export interface UseDraftReturn {
  // State
  draftKey: Ref<string | null>
  isDraftActive: ComputedRef<boolean>
  lastSaved: Ref<Date | null>
  hasDraft: ComputedRef<boolean>

  // Actions
  initDraft: (data?: Record<string, unknown>) => void
  updateDraftField: (field: string, value: unknown) => void
  activateDraft: () => { data: Record<string, unknown>; entityKey?: string } | undefined
  discardDraft: () => void

  // Resume
  existingDraft: ComputedRef<DraftInstance | undefined>
  resumeDraft: () => Record<string, unknown> | undefined

  // Navigation guard
  guardMessage: ComputedRef<string | null>
}

export function useDraft(options: UseDraftOptions): UseDraftReturn {
  const { module, entitySet, entityKey, enabled = true } = options

  const draftManager = getSharedDraftManager()
  const draftKey = ref<string | null>(null)
  const lastSaved = ref<Date | null>(null)

  const isDraftActive = computed(() => {
    if (!draftKey.value) return false
    const draft = draftManager.getDraft(draftKey.value)
    return draft?.state === 'new' || draft?.state === 'editing'
  })

  const hasDraft = computed(() => {
    if (draftKey.value) return true
    if (entityKey) {
      return !!draftManager.getDraftForEntity(module, entitySet, entityKey)
    }
    return draftManager.getDraftsFor(module, entitySet).length > 0
  })

  const existingDraft = computed<DraftInstance | undefined>(() => {
    if (!enabled) return undefined
    if (entityKey) {
      return draftManager.getDraftForEntity(module, entitySet, entityKey)
    }
    // For new entities, check if there are any 'new' drafts for this entity set
    const drafts = draftManager.getDraftsFor(module, entitySet)
    return drafts.find((d) => d.state === 'new' && !d.entityKey)
  })

  const guardMessage = computed<string | null>(() => {
    if (!draftKey.value) return null
    const draft = draftManager.getDraft(draftKey.value)
    if (!draft || draft.dirtyFields.size === 0) return null
    return draftManager.getNavigationGuardMessage()
  })

  function initDraft(data?: Record<string, unknown>): void {
    if (!enabled) return

    // Check if an existing draft is already tracked
    if (draftKey.value) return

    let draft: DraftInstance
    if (entityKey) {
      draft = draftManager.editDraft(module, entitySet, entityKey, data ?? {})
    } else {
      draft = draftManager.createDraft(module, entitySet, data)
    }
    draftKey.value = draft.draftKey
  }

  function updateDraftField(field: string, value: unknown): void {
    if (!draftKey.value) return
    draftManager.updateField(draftKey.value, field, value)

    const draft = draftManager.getDraft(draftKey.value)
    if (draft?.lastSaved) {
      lastSaved.value = draft.lastSaved
    }
  }

  function activateDraft(): { data: Record<string, unknown>; entityKey?: string } | undefined {
    if (!draftKey.value) return undefined
    const result = draftManager.activate(draftKey.value)
    draftKey.value = null
    lastSaved.value = null
    return result
  }

  function discardDraft(): void {
    if (!draftKey.value) return
    draftManager.discard(draftKey.value)
    draftKey.value = null
    lastSaved.value = null
  }

  function resumeDraft(): Record<string, unknown> | undefined {
    const existing = existingDraft.value
    if (!existing) return undefined

    // Track this draft as our active one
    draftKey.value = existing.draftKey
    if (existing.lastSaved) {
      lastSaved.value = existing.lastSaved
    }
    return { ...existing.data }
  }

  // Cleanup on component unmount: persist any remaining dirty fields
  // but do NOT discard — the draft should survive navigation
  onUnmounted(() => {
    // Nothing to clean up — drafts are managed by the singleton
    // and auto-save handles persistence
  })

  return {
    draftKey,
    isDraftActive,
    lastSaved,
    hasDraft,
    initDraft,
    updateDraftField,
    activateDraft,
    discardDraft,
    existingDraft,
    resumeDraft,
    guardMessage,
  }
}
