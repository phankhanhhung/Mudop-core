/**
 * Change Tracker — tracks dirty state for entity instances.
 *
 * Provides:
 * - Snapshot original state on first load
 * - Track per-field dirty state
 * - Compute minimal PATCH payloads (only changed fields)
 * - Detect if entity hasPendingChanges
 * - Reset to original state
 *
 * Inspired by OpenUI5's OData V4 model change tracking, adapted for Vue reactivity.
 */

export interface TrackedEntity {
  /** Original snapshot of the entity */
  original: Record<string, unknown>
  /** Set of field names that have been modified */
  dirtyFields: Set<string>
}

export class ChangeTracker {
  private tracked = new Map<string, TrackedEntity>()

  /**
   * Take a snapshot of the entity's current state.
   * This becomes the baseline for change detection.
   */
  snapshot(key: string, data: Record<string, unknown>): void {
    this.tracked.set(key, {
      original: this.deepClone(data),
      dirtyFields: new Set(),
    })
  }

  /**
   * Mark a field as dirty.
   */
  markDirty(key: string, field: string): void {
    const entry = this.tracked.get(key)
    if (entry) {
      entry.dirtyFields.add(field)
    }
  }

  /**
   * Mark multiple fields as dirty.
   */
  markFieldsDirty(key: string, fields: string[]): void {
    const entry = this.tracked.get(key)
    if (entry) {
      for (const field of fields) {
        entry.dirtyFields.add(field)
      }
    }
  }

  /**
   * Check if an entity has any dirty fields.
   */
  isDirty(key: string): boolean {
    const entry = this.tracked.get(key)
    return entry ? entry.dirtyFields.size > 0 : false
  }

  /**
   * Get the list of dirty field names.
   */
  getDirtyFields(key: string): string[] {
    const entry = this.tracked.get(key)
    return entry ? [...entry.dirtyFields] : []
  }

  /**
   * Get the original snapshot.
   */
  getOriginal(key: string): Record<string, unknown> | undefined {
    return this.tracked.get(key)?.original
  }

  /**
   * Build a minimal patch payload containing only dirty fields.
   */
  buildPatch(key: string, currentData: Record<string, unknown>): Record<string, unknown> | null {
    const entry = this.tracked.get(key)
    if (!entry || entry.dirtyFields.size === 0) return null

    const patch: Record<string, unknown> = {}
    for (const field of entry.dirtyFields) {
      patch[field] = currentData[field]
    }
    return patch
  }

  /**
   * Compute a diff between original and current data, auto-detecting dirty fields.
   * Useful when you want to detect changes without manual markDirty calls.
   */
  computeDiff(
    key: string,
    currentData: Record<string, unknown>
  ): { field: string; oldValue: unknown; newValue: unknown }[] {
    const entry = this.tracked.get(key)
    if (!entry) return []

    const diffs: { field: string; oldValue: unknown; newValue: unknown }[] = []
    const allKeys = new Set([
      ...Object.keys(entry.original),
      ...Object.keys(currentData),
    ])

    for (const field of allKeys) {
      // Skip OData metadata fields
      if (field.startsWith('@') || field.startsWith('_')) continue

      const oldVal = entry.original[field]
      const newVal = currentData[field]

      if (!this.isEqual(oldVal, newVal)) {
        diffs.push({ field, oldValue: oldVal, newValue: newVal })
      }
    }

    return diffs
  }

  /**
   * Auto-detect and mark all changed fields between original and current data.
   */
  detectChanges(key: string, currentData: Record<string, unknown>): string[] {
    const diffs = this.computeDiff(key, currentData)
    const changedFields = diffs.map(d => d.field)
    this.markFieldsDirty(key, changedFields)
    return changedFields
  }

  /**
   * Clear dirty state for an entity (after successful save).
   */
  clearDirty(key: string): void {
    const entry = this.tracked.get(key)
    if (entry) {
      entry.dirtyFields.clear()
    }
  }

  /**
   * Update the snapshot after a successful save.
   * Clears dirty fields and sets new baseline.
   */
  updateSnapshot(key: string, newData: Record<string, unknown>): void {
    this.tracked.set(key, {
      original: this.deepClone(newData),
      dirtyFields: new Set(),
    })
  }

  /**
   * Remove tracking for an entity.
   */
  remove(key: string): void {
    this.tracked.delete(key)
  }

  /**
   * Check if any tracked entity has pending changes.
   */
  hasAnyChanges(): boolean {
    for (const [, entry] of this.tracked) {
      if (entry.dirtyFields.size > 0) return true
    }
    return false
  }

  /**
   * Get all entities with pending changes.
   */
  getAllDirtyKeys(): string[] {
    const keys: string[] = []
    for (const [key, entry] of this.tracked) {
      if (entry.dirtyFields.size > 0) keys.push(key)
    }
    return keys
  }

  /**
   * Get total count of dirty entities.
   */
  getDirtyCount(): number {
    let count = 0
    for (const [, entry] of this.tracked) {
      if (entry.dirtyFields.size > 0) count++
    }
    return count
  }

  /**
   * Clear all tracked entities.
   */
  clear(): void {
    this.tracked.clear()
  }

  // =========================================================================
  // Private helpers
  // =========================================================================

  private deepClone(obj: Record<string, unknown>): Record<string, unknown> {
    // structuredClone is available in modern browsers
    if (typeof structuredClone === 'function') {
      try {
        return structuredClone(obj)
      } catch {
        // Fall through to JSON clone for non-cloneable types
      }
    }
    return JSON.parse(JSON.stringify(obj))
  }

  private isEqual(a: unknown, b: unknown): boolean {
    if (a === b) return true
    if (a == null && b == null) return true
    if (a == null || b == null) return false

    // Date comparison
    if (a instanceof Date && b instanceof Date) {
      return a.getTime() === b.getTime()
    }

    // Array comparison
    if (Array.isArray(a) && Array.isArray(b)) {
      if (a.length !== b.length) return false
      for (let i = 0; i < a.length; i++) {
        if (!this.isEqual(a[i], b[i])) return false
      }
      return true
    }

    // Object comparison
    if (typeof a === 'object' && typeof b === 'object') {
      const aObj = a as Record<string, unknown>
      const bObj = b as Record<string, unknown>
      const keys = new Set([...Object.keys(aObj), ...Object.keys(bObj)])
      for (const key of keys) {
        if (!this.isEqual(aObj[key], bObj[key])) return false
      }
      return true
    }

    return false
  }
}
