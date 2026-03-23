/**
 * Draft Manager — client-side draft lifecycle management.
 *
 * Provides:
 * - Draft lifecycle: New → Editing → Preparing → Active/Discarded
 * - Auto-save to localStorage at configurable intervals
 * - Navigation guard integration (warn on leaving with unsaved drafts)
 * - Draft expiration (auto-discard after timeout)
 * - Multiple concurrent drafts per entity type
 *
 * Server-side draft persistence can be added later — this implementation
 * works entirely client-side using localStorage.
 *
 * Usage:
 *   const drafts = new DraftManager()
 *
 *   // Create new draft
 *   const draft = drafts.createDraft('myapp', 'Customers')
 *   draft.data.name = 'New Customer'
 *
 *   // Edit existing entity
 *   const editDraft = drafts.editDraft('myapp', 'Customers', '123', existingData)
 *
 *   // Activate (save)
 *   await drafts.activate(draft.draftKey)
 *
 *   // Discard
 *   drafts.discard(draft.draftKey)
 */

import { ref, computed, type ComputedRef } from 'vue'
import type { DraftInstance, DraftConfig } from './types'

const STORAGE_PREFIX = 'bmmdl_draft_meta_'
const DATA_STORAGE_PREFIX = 'bmmdl_draft_data_'
const DRAFT_INDEX_KEY = 'bmmdl_draft_index'
let draftKeyCounter = 0

export class DraftManager {
  private config: Required<DraftConfig>
  private drafts = ref<Map<string, DraftInstance>>(new Map())
  private autoSaveTimers = new Map<string, ReturnType<typeof setInterval>>()
  private expirationTimers = new Map<string, ReturnType<typeof setTimeout>>()

  /** Reactive count of active drafts */
  readonly activeDraftCount: ComputedRef<number>

  /** Whether any draft has unsaved changes */
  readonly hasUnsavedDrafts: ComputedRef<boolean>

  constructor(config?: Partial<DraftConfig>) {
    this.config = {
      enabled: config?.enabled ?? true,
      autoSaveInterval: config?.autoSaveInterval ?? 30_000,
      timeout: config?.timeout ?? 1_800_000, // 30 min
    }

    this.activeDraftCount = computed(() => {
      let count = 0
      for (const [, draft] of this.drafts.value) {
        if (draft.state === 'new' || draft.state === 'editing') count++
      }
      return count
    })

    this.hasUnsavedDrafts = computed(() => {
      for (const [, draft] of this.drafts.value) {
        if (draft.dirtyFields.size > 0) return true
      }
      return false
    })

    // Restore persisted drafts
    this.restoreFromStorage()
  }

  /**
   * Create a new draft for creating an entity.
   */
  createDraft(
    module: string,
    entitySet: string,
    initialData?: Record<string, unknown>
  ): DraftInstance {
    const draftKey = `draft-${++draftKeyCounter}-${Date.now()}`

    const draft: DraftInstance = {
      draftKey,
      module,
      entitySet,
      data: initialData ? { ...initialData } : {},
      state: 'new',
      createdAt: new Date(),
      dirtyFields: new Set(),
    }

    this.drafts.value.set(draftKey, draft)
    this.startAutoSave(draftKey)
    this.startExpirationTimer(draftKey)
    this.persistDraft(draft)

    return draft
  }

  /**
   * Create a draft for editing an existing entity.
   */
  editDraft(
    module: string,
    entitySet: string,
    entityKey: string,
    existingData: Record<string, unknown>
  ): DraftInstance {
    // Check if a draft already exists for this entity
    for (const [, draft] of this.drafts.value) {
      if (
        draft.module === module &&
        draft.entitySet === entitySet &&
        draft.entityKey === entityKey &&
        (draft.state === 'editing' || draft.state === 'new')
      ) {
        return draft
      }
    }

    const draftKey = `draft-${++draftKeyCounter}-${Date.now()}`

    const draft: DraftInstance = {
      draftKey,
      entityKey,
      module,
      entitySet,
      data: { ...existingData },
      state: 'editing',
      createdAt: new Date(),
      dirtyFields: new Set(),
    }

    this.drafts.value.set(draftKey, draft)
    this.startAutoSave(draftKey)
    this.startExpirationTimer(draftKey)
    this.persistDraft(draft)

    return draft
  }

  /**
   * Update a field in a draft.
   */
  updateField(draftKey: string, field: string, value: unknown): void {
    const draft = this.drafts.value.get(draftKey)
    if (!draft) return

    draft.data[field] = value
    draft.dirtyFields.add(field)
  }

  /**
   * Prepare a draft for validation.
   * In a full implementation, this would send to server for validation.
   */
  prepare(draftKey: string): DraftInstance | undefined {
    const draft = this.drafts.value.get(draftKey)
    if (!draft) return undefined

    draft.state = 'preparing'
    this.persistDraft(draft)
    return draft
  }

  /**
   * Activate (finalize) a draft.
   * Returns the draft data for the caller to submit to the server.
   */
  activate(draftKey: string): { data: Record<string, unknown>; entityKey?: string } | undefined {
    const draft = this.drafts.value.get(draftKey)
    if (!draft) return undefined

    draft.state = 'active'
    this.stopAutoSave(draftKey)
    this.stopExpirationTimer(draftKey)
    this.removeDraftFromStorage(draftKey)

    const result = {
      data: { ...draft.data },
      entityKey: draft.entityKey,
    }

    this.drafts.value.delete(draftKey)

    return result
  }

  /**
   * Discard a draft.
   */
  discard(draftKey: string): void {
    const draft = this.drafts.value.get(draftKey)
    if (!draft) return

    draft.state = 'discarded'
    this.stopAutoSave(draftKey)
    this.stopExpirationTimer(draftKey)
    this.removeDraftFromStorage(draftKey)
    this.drafts.value.delete(draftKey)
  }

  /**
   * Get a draft by key.
   */
  getDraft(draftKey: string): DraftInstance | undefined {
    return this.drafts.value.get(draftKey)
  }

  /**
   * Get all active drafts for a module/entity.
   */
  getDraftsFor(module: string, entitySet: string): DraftInstance[] {
    const result: DraftInstance[] = []
    for (const [, draft] of this.drafts.value) {
      if (
        draft.module === module &&
        draft.entitySet === entitySet &&
        (draft.state === 'new' || draft.state === 'editing')
      ) {
        result.push(draft)
      }
    }
    return result
  }

  /**
   * Get draft for a specific entity (if exists).
   */
  getDraftForEntity(module: string, entitySet: string, entityKey: string): DraftInstance | undefined {
    for (const [, draft] of this.drafts.value) {
      if (
        draft.module === module &&
        draft.entitySet === entitySet &&
        draft.entityKey === entityKey &&
        (draft.state === 'editing' || draft.state === 'new')
      ) {
        return draft
      }
    }
    return undefined
  }

  /**
   * Check if navigation should be blocked due to unsaved drafts.
   * Returns a message for the navigation guard, or null if safe to navigate.
   */
  getNavigationGuardMessage(): string | null {
    if (!this.hasUnsavedDrafts.value) return null
    return 'You have unsaved changes. Are you sure you want to leave?'
  }

  /**
   * Discard all active drafts.
   */
  discardAll(): void {
    const keys = [...this.drafts.value.keys()]
    for (const key of keys) {
      this.discard(key)
    }
  }

  /**
   * Destroy the draft manager and release resources.
   */
  destroy(): void {
    for (const [key] of this.autoSaveTimers) {
      this.stopAutoSave(key)
    }
    for (const [key] of this.expirationTimers) {
      this.stopExpirationTimer(key)
    }
  }

  // =========================================================================
  // Auto-save
  // =========================================================================

  private startAutoSave(draftKey: string): void {
    if (!this.config.enabled || this.config.autoSaveInterval <= 0) return

    const timer = setInterval(() => {
      const draft = this.drafts.value.get(draftKey)
      if (draft && draft.dirtyFields.size > 0) {
        this.persistDraft(draft)
        draft.lastSaved = new Date()
        draft.dirtyFields.clear()
      }
    }, this.config.autoSaveInterval)

    this.autoSaveTimers.set(draftKey, timer)
  }

  private stopAutoSave(draftKey: string): void {
    const timer = this.autoSaveTimers.get(draftKey)
    if (timer) {
      clearInterval(timer)
      this.autoSaveTimers.delete(draftKey)
    }
  }

  // =========================================================================
  // Expiration
  // =========================================================================

  private startExpirationTimer(draftKey: string): void {
    if (this.config.timeout <= 0) return

    const timer = setTimeout(() => {
      this.discard(draftKey)
    }, this.config.timeout)

    this.expirationTimers.set(draftKey, timer)
  }

  private stopExpirationTimer(draftKey: string): void {
    const timer = this.expirationTimers.get(draftKey)
    if (timer) {
      clearTimeout(timer)
      this.expirationTimers.delete(draftKey)
    }
  }

  // =========================================================================
  // Storage persistence
  // =========================================================================

  private persistDraft(draft: DraftInstance): void {
    try {
      // Store metadata (non-sensitive) in localStorage so draft keys survive tab closes
      const metadata = {
        draftKey: draft.draftKey,
        entityKey: draft.entityKey,
        module: draft.module,
        entitySet: draft.entitySet,
        state: draft.state,
        dirtyFields: [...draft.dirtyFields],
        createdAt: draft.createdAt.toISOString(),
        lastSaved: draft.lastSaved?.toISOString(),
      }
      localStorage.setItem(STORAGE_PREFIX + draft.draftKey, JSON.stringify(metadata))

      // Store sensitive entity data in sessionStorage (cleared on tab/browser close)
      sessionStorage.setItem(DATA_STORAGE_PREFIX + draft.draftKey, JSON.stringify(draft.data))

      // Update the draft index so restoreFromStorage doesn't need to scan all keys
      this.addToDraftIndex(draft.draftKey)
    } catch {
      // Storage might be full — silently fail
    }
  }

  private removeDraftFromStorage(draftKey: string): void {
    localStorage.removeItem(STORAGE_PREFIX + draftKey)
    sessionStorage.removeItem(DATA_STORAGE_PREFIX + draftKey)
    this.removeFromDraftIndex(draftKey)
  }

  private restoreFromStorage(): void {
    try {
      const draftKeys = this.getDraftIndex()
      const survivingKeys: string[] = []

      for (const draftKey of draftKeys) {
        const key = STORAGE_PREFIX + draftKey

        const raw = localStorage.getItem(key)
        if (!raw) continue

        const parsed = JSON.parse(raw)

        // Check if expired
        const createdAt = new Date(parsed.createdAt)
        if (Date.now() - createdAt.getTime() > this.config.timeout) {
          localStorage.removeItem(key)
          sessionStorage.removeItem(DATA_STORAGE_PREFIX + draftKey)
          continue
        }

        // Retrieve sensitive data from sessionStorage
        const dataRaw = sessionStorage.getItem(DATA_STORAGE_PREFIX + draftKey)
        if (!dataRaw) {
          // Data was lost (browser restart) — discard orphaned metadata
          localStorage.removeItem(key)
          continue
        }

        let data: Record<string, unknown>
        try {
          data = JSON.parse(dataRaw)
        } catch {
          // Corrupted data — discard
          localStorage.removeItem(key)
          sessionStorage.removeItem(DATA_STORAGE_PREFIX + draftKey)
          continue
        }

        const draft: DraftInstance = {
          draftKey: parsed.draftKey,
          entityKey: parsed.entityKey,
          module: parsed.module,
          entitySet: parsed.entitySet,
          state: parsed.state,
          data,
          dirtyFields: new Set(parsed.dirtyFields ?? []),
          createdAt,
          lastSaved: parsed.lastSaved ? new Date(parsed.lastSaved) : undefined,
        }

        this.drafts.value.set(draft.draftKey, draft)
        this.startAutoSave(draft.draftKey)
        this.startExpirationTimer(draft.draftKey)
        survivingKeys.push(draftKey)
      }

      // Update index to only contain surviving (non-expired, non-corrupted) keys
      this.setDraftIndex(survivingKeys)
    } catch {
      // Corrupted storage — clear all drafts
    }
  }

  // =========================================================================
  // Draft index — avoids scanning all localStorage keys
  // =========================================================================

  private getDraftIndex(): string[] {
    try {
      const raw = localStorage.getItem(DRAFT_INDEX_KEY)
      if (!raw) return []
      return JSON.parse(raw) as string[]
    } catch {
      return []
    }
  }

  private setDraftIndex(keys: string[]): void {
    try {
      localStorage.setItem(DRAFT_INDEX_KEY, JSON.stringify(keys))
    } catch {
      // Storage full — silently fail
    }
  }

  private addToDraftIndex(draftKey: string): void {
    const keys = this.getDraftIndex()
    if (!keys.includes(draftKey)) {
      keys.push(draftKey)
      this.setDraftIndex(keys)
    }
  }

  private removeFromDraftIndex(draftKey: string): void {
    const keys = this.getDraftIndex()
    const idx = keys.indexOf(draftKey)
    if (idx !== -1) {
      keys.splice(idx, 1)
      this.setDraftIndex(keys)
    }
  }
}
