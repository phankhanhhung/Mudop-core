import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'

// Mock localStorage before importing DraftManager
const mockStorage: Record<string, string> = {}
const mockLocalStorage = {
  getItem: vi.fn((key: string) => mockStorage[key] ?? null),
  setItem: vi.fn((key: string, val: string) => { mockStorage[key] = val }),
  removeItem: vi.fn((key: string) => { delete mockStorage[key] }),
  clear: vi.fn(() => { Object.keys(mockStorage).forEach(k => delete mockStorage[k]) }),
  get length() { return Object.keys(mockStorage).length },
  key: vi.fn((index: number) => Object.keys(mockStorage)[index] ?? null),
}

// Mock sessionStorage for sensitive draft data
const mockSessionStore: Record<string, string> = {}
const mockSessionStorage = {
  getItem: vi.fn((key: string) => mockSessionStore[key] ?? null),
  setItem: vi.fn((key: string, val: string) => { mockSessionStore[key] = val }),
  removeItem: vi.fn((key: string) => { delete mockSessionStore[key] }),
  clear: vi.fn(() => { Object.keys(mockSessionStore).forEach(k => delete mockSessionStore[k]) }),
  get length() { return Object.keys(mockSessionStore).length },
  key: vi.fn((index: number) => Object.keys(mockSessionStore)[index] ?? null),
}

vi.stubGlobal('localStorage', mockLocalStorage)
vi.stubGlobal('sessionStorage', mockSessionStorage)

import { DraftManager } from '../DraftManager'

describe('DraftManager', () => {
  let manager: DraftManager

  beforeEach(() => {
    vi.useFakeTimers()
    // Clear mock storage
    Object.keys(mockStorage).forEach(k => delete mockStorage[k])
    Object.keys(mockSessionStore).forEach(k => delete mockSessionStore[k])
    vi.clearAllMocks()
    manager = new DraftManager({ autoSaveInterval: 30_000, timeout: 1_800_000 })
  })

  afterEach(() => {
    manager.destroy()
    vi.useRealTimers()
  })

  // =========================================================================
  // createDraft
  // =========================================================================

  describe('createDraft()', () => {
    it('creates new draft instance with state "new"', () => {
      const draft = manager.createDraft('myapp', 'Customers')

      expect(draft.state).toBe('new')
      expect(draft.module).toBe('myapp')
      expect(draft.entitySet).toBe('Customers')
      expect(draft.data).toEqual({})
    })

    it('draft has unique draftKey', () => {
      const draft1 = manager.createDraft('myapp', 'Customers')
      const draft2 = manager.createDraft('myapp', 'Customers')

      expect(draft1.draftKey).not.toBe(draft2.draftKey)
      expect(draft1.draftKey).toMatch(/^draft-/)
      expect(draft2.draftKey).toMatch(/^draft-/)
    })

    it('sets initial data when provided', () => {
      const draft = manager.createDraft('myapp', 'Customers', { name: 'Alice', status: 'Active' })

      expect(draft.data).toEqual({ name: 'Alice', status: 'Active' })
    })

    it('has empty dirtyFields on creation', () => {
      const draft = manager.createDraft('myapp', 'Customers')

      expect(draft.dirtyFields.size).toBe(0)
    })

    it('has a createdAt timestamp', () => {
      const now = new Date('2025-06-15T12:00:00Z')
      vi.setSystemTime(now)

      const draft = manager.createDraft('myapp', 'Customers')

      expect(draft.createdAt.getTime()).toBe(now.getTime())
    })
  })

  // =========================================================================
  // editDraft
  // =========================================================================

  describe('editDraft()', () => {
    it('creates draft with state "editing" and existing entity key', () => {
      const draft = manager.editDraft('myapp', 'Customers', '123', { name: 'Alice' })

      expect(draft.state).toBe('editing')
      expect(draft.entityKey).toBe('123')
      expect(draft.data).toEqual({ name: 'Alice' })
    })

    it('returns existing draft if one already exists for the same entity', () => {
      const draft1 = manager.editDraft('myapp', 'Customers', '123', { name: 'Alice' })
      const draft2 = manager.editDraft('myapp', 'Customers', '123', { name: 'Bob' })

      expect(draft1.draftKey).toBe(draft2.draftKey)
      // Original data is preserved (not overwritten)
      expect(draft2.data).toEqual({ name: 'Alice' })
    })
  })

  // =========================================================================
  // getDraft
  // =========================================================================

  describe('getDraft()', () => {
    it('retrieves draft by draftKey', () => {
      const created = manager.createDraft('myapp', 'Customers', { name: 'Alice' })

      const retrieved = manager.getDraft(created.draftKey)

      expect(retrieved).toBeDefined()
      expect(retrieved!.draftKey).toBe(created.draftKey)
      expect(retrieved!.data).toEqual({ name: 'Alice' })
    })

    it('returns undefined for unknown key', () => {
      expect(manager.getDraft('nonexistent')).toBeUndefined()
    })
  })

  // =========================================================================
  // updateField
  // =========================================================================

  describe('updateField()', () => {
    it('marks field as dirty', () => {
      const draft = manager.createDraft('myapp', 'Customers', { name: 'Alice' })

      manager.updateField(draft.draftKey, 'name', 'Bob')

      expect(draft.data.name).toBe('Bob')
      expect(draft.dirtyFields.has('name')).toBe(true)
    })

    it('does nothing for unknown draftKey', () => {
      // Should not throw
      manager.updateField('nonexistent', 'name', 'Bob')
    })
  })

  // =========================================================================
  // activeDraftCount
  // =========================================================================

  describe('activeDraftCount', () => {
    it('tracks active drafts (new or editing state)', () => {
      expect(manager.activeDraftCount.value).toBe(0)

      manager.createDraft('myapp', 'Customers')
      expect(manager.activeDraftCount.value).toBe(1)

      manager.editDraft('myapp', 'Orders', '1', { total: 100 })
      expect(manager.activeDraftCount.value).toBe(2)
    })

    it('decrements when draft is discarded', () => {
      const draft = manager.createDraft('myapp', 'Customers')
      expect(manager.activeDraftCount.value).toBe(1)

      manager.discard(draft.draftKey)
      expect(manager.activeDraftCount.value).toBe(0)
    })
  })

  // =========================================================================
  // hasUnsavedDrafts
  // =========================================================================

  describe('hasUnsavedDrafts', () => {
    it('true when dirty fields exist', () => {
      const draft = manager.createDraft('myapp', 'Customers', { name: 'Alice' })
      expect(manager.hasUnsavedDrafts.value).toBe(false)

      manager.updateField(draft.draftKey, 'name', 'Bob')
      expect(manager.hasUnsavedDrafts.value).toBe(true)
    })

    it('false when no dirty fields', () => {
      manager.createDraft('myapp', 'Customers')
      expect(manager.hasUnsavedDrafts.value).toBe(false)
    })
  })

  // =========================================================================
  // discard
  // =========================================================================

  describe('discard()', () => {
    it('removes draft and cleans up', () => {
      const draft = manager.createDraft('myapp', 'Customers')

      manager.discard(draft.draftKey)

      expect(manager.getDraft(draft.draftKey)).toBeUndefined()
      expect(manager.activeDraftCount.value).toBe(0)
    })

    it('removes draft from storage', () => {
      const draft = manager.createDraft('myapp', 'Customers')
      const storageKey = `bmmdl_draft_meta_${draft.draftKey}`

      // Draft was persisted on creation
      expect(mockStorage[storageKey]).toBeDefined()

      manager.discard(draft.draftKey)

      expect(mockStorage[storageKey]).toBeUndefined()
    })

    it('does nothing for unknown key', () => {
      // Should not throw
      manager.discard('nonexistent')
    })
  })

  // =========================================================================
  // activate
  // =========================================================================

  describe('activate()', () => {
    it('returns draft data and removes draft', () => {
      const draft = manager.createDraft('myapp', 'Customers', { name: 'Alice' })

      const result = manager.activate(draft.draftKey)

      expect(result).toBeDefined()
      expect(result!.data).toEqual({ name: 'Alice' })
      expect(manager.getDraft(draft.draftKey)).toBeUndefined()
    })

    it('returns entityKey for edit drafts', () => {
      const draft = manager.editDraft('myapp', 'Customers', '456', { name: 'Bob' })

      const result = manager.activate(draft.draftKey)

      expect(result!.entityKey).toBe('456')
    })

    it('returns undefined for unknown key', () => {
      expect(manager.activate('nonexistent')).toBeUndefined()
    })
  })

  // =========================================================================
  // getDraftsFor (getAllDrafts for entity)
  // =========================================================================

  describe('getDraftsFor()', () => {
    it('returns all drafts for a module + entitySet', () => {
      manager.createDraft('myapp', 'Customers')
      manager.createDraft('myapp', 'Customers')
      manager.createDraft('myapp', 'Orders')

      const customerDrafts = manager.getDraftsFor('myapp', 'Customers')
      expect(customerDrafts).toHaveLength(2)

      const orderDrafts = manager.getDraftsFor('myapp', 'Orders')
      expect(orderDrafts).toHaveLength(1)
    })

    it('returns empty array when no drafts match', () => {
      expect(manager.getDraftsFor('myapp', 'Products')).toEqual([])
    })
  })

  // =========================================================================
  // getDraftForEntity
  // =========================================================================

  describe('getDraftForEntity()', () => {
    it('finds draft for specific entity key', () => {
      manager.editDraft('myapp', 'Customers', '123', { name: 'Alice' })

      const found = manager.getDraftForEntity('myapp', 'Customers', '123')
      expect(found).toBeDefined()
      expect(found!.entityKey).toBe('123')
    })

    it('returns undefined when no draft exists for entity', () => {
      expect(manager.getDraftForEntity('myapp', 'Customers', '999')).toBeUndefined()
    })
  })

  // =========================================================================
  // localStorage persistence
  // =========================================================================

  describe('storage persistence', () => {
    it('persists metadata to localStorage and data to sessionStorage', () => {
      const draft = manager.createDraft('myapp', 'Customers', { name: 'Alice' })

      const metaKey = `bmmdl_draft_meta_${draft.draftKey}`
      const dataKey = `bmmdl_draft_data_${draft.draftKey}`

      // Metadata in localStorage (no sensitive data)
      expect(mockStorage[metaKey]).toBeDefined()
      const parsed = JSON.parse(mockStorage[metaKey])
      expect(parsed.module).toBe('myapp')
      expect(parsed.entitySet).toBe('Customers')
      expect(parsed.data).toBeUndefined() // data NOT in localStorage

      // Sensitive data in sessionStorage
      expect(mockSessionStore[dataKey]).toBeDefined()
      const data = JSON.parse(mockSessionStore[dataKey])
      expect(data).toEqual({ name: 'Alice' })
    })

    it('restores from localStorage + sessionStorage on construction', () => {
      // Create a draft and note its key
      const draft = manager.createDraft('myapp', 'Customers', { name: 'Alice' })
      const draftKey = draft.draftKey

      // Destroy old manager
      manager.destroy()

      // Create new manager — it should restore from both storages
      const newManager = new DraftManager({ timeout: 1_800_000 })

      const restored = newManager.getDraft(draftKey)
      expect(restored).toBeDefined()
      expect(restored!.module).toBe('myapp')
      expect(restored!.entitySet).toBe('Customers')
      expect(restored!.data).toEqual({ name: 'Alice' })

      newManager.destroy()
    })

    it('discards orphaned metadata when sessionStorage data is missing', () => {
      // Create a draft
      const draft = manager.createDraft('myapp', 'Customers', { name: 'Secret' })
      const draftKey = draft.draftKey
      manager.destroy()

      // Simulate browser restart: clear sessionStorage but keep localStorage
      Object.keys(mockSessionStore).forEach(k => delete mockSessionStore[k])

      // New manager should discard the orphaned metadata
      const newManager = new DraftManager({ timeout: 1_800_000 })
      expect(newManager.getDraft(draftKey)).toBeUndefined()
      expect(mockStorage[`bmmdl_draft_meta_${draftKey}`]).toBeUndefined()

      newManager.destroy()
    })

    it('corrupted localStorage does not crash', () => {
      // Put corrupted data
      mockStorage['bmmdl_draft_meta_corrupted'] = 'not-valid-json{{'

      // Should not throw
      expect(() => {
        const mgr = new DraftManager()
        mgr.destroy()
      }).not.toThrow()
    })

    it('removes data from both storages on discard', () => {
      const draft = manager.createDraft('myapp', 'Customers', { name: 'Alice' })
      const metaKey = `bmmdl_draft_meta_${draft.draftKey}`
      const dataKey = `bmmdl_draft_data_${draft.draftKey}`

      expect(mockStorage[metaKey]).toBeDefined()
      expect(mockSessionStore[dataKey]).toBeDefined()

      manager.discard(draft.draftKey)

      expect(mockStorage[metaKey]).toBeUndefined()
      expect(mockSessionStore[dataKey]).toBeUndefined()
    })
  })

  // =========================================================================
  // Draft expiration
  // =========================================================================

  describe('draft expiration', () => {
    it('draft is discarded after timeout', () => {
      const draft = manager.createDraft('myapp', 'Customers', { name: 'Alice' })

      expect(manager.getDraft(draft.draftKey)).toBeDefined()

      // Advance time past the timeout (30 minutes)
      vi.advanceTimersByTime(1_800_001)

      expect(manager.getDraft(draft.draftKey)).toBeUndefined()
      expect(manager.activeDraftCount.value).toBe(0)
    })

    it('draft not discarded before timeout', () => {
      const draft = manager.createDraft('myapp', 'Customers')

      // Advance less than timeout
      vi.advanceTimersByTime(1_799_000)

      expect(manager.getDraft(draft.draftKey)).toBeDefined()
    })
  })

  // =========================================================================
  // Auto-save
  // =========================================================================

  describe('auto-save', () => {
    it('persists dirty draft at auto-save interval', () => {
      const draft = manager.createDraft('myapp', 'Customers', { name: 'Alice' })

      // Mark a field dirty
      manager.updateField(draft.draftKey, 'name', 'Bob')

      const initialSetItemCount = mockLocalStorage.setItem.mock.calls.length

      // Advance to auto-save interval
      vi.advanceTimersByTime(30_000)

      // setItem should have been called again
      expect(mockLocalStorage.setItem.mock.calls.length).toBeGreaterThan(initialSetItemCount)

      // After auto-save, dirty fields should be cleared
      expect(draft.dirtyFields.size).toBe(0)
      expect(draft.lastSaved).toBeDefined()
    })
  })

  // =========================================================================
  // discardAll
  // =========================================================================

  describe('discardAll()', () => {
    it('removes all active drafts', () => {
      manager.createDraft('myapp', 'Customers')
      manager.createDraft('myapp', 'Orders')
      manager.editDraft('myapp', 'Products', '1', { name: 'Widget' })

      expect(manager.activeDraftCount.value).toBe(3)

      manager.discardAll()

      expect(manager.activeDraftCount.value).toBe(0)
    })
  })

  // =========================================================================
  // getNavigationGuardMessage
  // =========================================================================

  describe('getNavigationGuardMessage()', () => {
    it('returns null when no unsaved drafts', () => {
      manager.createDraft('myapp', 'Customers')
      expect(manager.getNavigationGuardMessage()).toBeNull()
    })

    it('returns message when unsaved drafts exist', () => {
      const draft = manager.createDraft('myapp', 'Customers')
      manager.updateField(draft.draftKey, 'name', 'Bob')

      const msg = manager.getNavigationGuardMessage()
      expect(msg).not.toBeNull()
      expect(msg).toContain('unsaved changes')
    })
  })

  // =========================================================================
  // prepare
  // =========================================================================

  describe('prepare()', () => {
    it('sets draft state to preparing', () => {
      const draft = manager.createDraft('myapp', 'Customers')

      const prepared = manager.prepare(draft.draftKey)

      expect(prepared).toBeDefined()
      expect(prepared!.state).toBe('preparing')
    })

    it('returns undefined for unknown key', () => {
      expect(manager.prepare('nonexistent')).toBeUndefined()
    })
  })
})
