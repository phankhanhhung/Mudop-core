/**
 * ValueListProvider — loads, caches, and searches association entity values
 * for use in dropdowns, filter fields, and value help dialogs.
 *
 * Inspired by SAP UI5's ValueList annotation handling.
 *
 * Usage:
 *   import { valueListProvider } from '@/odata/ValueListProvider'
 *
 *   const entries = await valueListProvider.getValues({
 *     module: 'sales',
 *     entitySet: 'Customers',
 *   })
 *
 *   const results = await valueListProvider.searchValues(
 *     { module: 'sales', entitySet: 'Products' },
 *     'widget',
 *     10
 *   )
 */

import { odataService } from '@/services/odataService'
import { metadataService } from '@/services/metadataService'
import type { FieldMetadata } from '@/types/metadata'

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface ValueListEntry {
  /** Primary key value */
  key: string
  /** Display label (best text field) */
  label: string
  /** Secondary display text */
  description?: string
  /** Full entity data */
  data: Record<string, unknown>
}

export interface ValueListConfig {
  /** Module context */
  module: string
  /** Target entity set to load values from */
  entitySet: string
  /** Field to use as display label (auto-detected if not specified) */
  displayField?: string
  /** Additional field to use as description */
  descriptionField?: string
  /** Pre-filter condition (OData $filter string) */
  filter?: string
  /** Max items to cache (default: 200) */
  maxItems?: number
  /** Cache TTL in ms (default: 300000 = 5 min) */
  cacheTtl?: number
}

interface CacheItem {
  entries: ValueListEntry[]
  timestamp: number
  keyField: string
  displayFieldName: string
  descriptionFieldName?: string
}

// ---------------------------------------------------------------------------
// Display field detection priority
// ---------------------------------------------------------------------------

const DISPLAY_FIELD_PRIORITY = [
  'name',
  'title',
  'displayname',
  'display_name',
  'code',
  'label',
  'description',
]

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Case-insensitive field access for OData responses (which may be PascalCase).
 */
function getField(item: Record<string, unknown>, fieldName: string): unknown {
  if (fieldName in item) return item[fieldName]
  const lower = fieldName.toLowerCase()
  for (const key of Object.keys(item)) {
    if (key.toLowerCase() === lower) return item[key]
  }
  return undefined
}

// ---------------------------------------------------------------------------
// ValueListProvider
// ---------------------------------------------------------------------------

export class ValueListProvider {
  private cache = new Map<string, CacheItem>()
  private searchCache = new Map<string, CacheItem>()
  private metadataCache = new Map<
    string,
    { keyField: string; displayFieldName: string; descriptionFieldName?: string }
  >()
  private defaultTtl = 300_000
  private defaultMaxItems = 200

  // =========================================================================
  // Public API
  // =========================================================================

  /**
   * Get all values for an entity (cached).
   * Used for small reference tables (< 200 items).
   */
  async getValues(config: ValueListConfig): Promise<ValueListEntry[]> {
    const cacheKey = this.buildCacheKey(config)
    const ttl = config.cacheTtl ?? this.defaultTtl

    // Return from cache if fresh
    const cached = this.cache.get(cacheKey)
    if (cached && Date.now() - cached.timestamp < ttl) {
      return cached.entries
    }

    // Resolve metadata (key field, display field)
    const meta = await this.resolveMetadata(config)
    const maxItems = config.maxItems ?? this.defaultMaxItems

    // Build query
    const selectFields = new Set<string>([meta.keyField, meta.displayFieldName])
    if (meta.descriptionFieldName) {
      selectFields.add(meta.descriptionFieldName)
    }

    const result = await odataService.query<Record<string, unknown>>(
      config.module,
      config.entitySet,
      {
        $select: Array.from(selectFields).join(','),
        $top: maxItems,
        $orderby: meta.displayFieldName,
        $filter: config.filter,
      },
      { skipCache: true }
    )

    const entries = result.value.map((item) =>
      this.mapToEntry(item, meta.keyField, meta.displayFieldName, meta.descriptionFieldName)
    )

    // Store in cache
    this.cache.set(cacheKey, {
      entries,
      timestamp: Date.now(),
      keyField: meta.keyField,
      displayFieldName: meta.displayFieldName,
      descriptionFieldName: meta.descriptionFieldName,
    })

    return entries
  }

  /**
   * Search values with a query string.
   * For large entity sets, this queries the server with $filter contains.
   */
  async searchValues(
    config: ValueListConfig,
    query: string,
    top?: number
  ): Promise<ValueListEntry[]> {
    const trimmed = query.trim()
    if (!trimmed) {
      return this.getValues(config)
    }

    const searchCacheKey = `${this.buildCacheKey(config)}:search:${trimmed.toLowerCase()}`
    const ttl = config.cacheTtl ?? this.defaultTtl

    // Check search cache
    const cached = this.searchCache.get(searchCacheKey)
    if (cached && Date.now() - cached.timestamp < ttl) {
      return cached.entries
    }

    const meta = await this.resolveMetadata(config)
    const maxResults = top ?? (config.maxItems ?? this.defaultMaxItems)

    // Build select fields
    const selectFields = new Set<string>([meta.keyField, meta.displayFieldName])
    if (meta.descriptionFieldName) {
      selectFields.add(meta.descriptionFieldName)
    }

    // Build filter: contains on the display field
    const escapedQuery = trimmed.replace(/'/g, "''")
    let filterExpr = `contains(${meta.displayFieldName}, '${escapedQuery}')`
    if (config.filter) {
      filterExpr = `${config.filter} and ${filterExpr}`
    }

    const result = await odataService.query<Record<string, unknown>>(
      config.module,
      config.entitySet,
      {
        $select: Array.from(selectFields).join(','),
        $top: maxResults,
        $orderby: meta.displayFieldName,
        $filter: filterExpr,
      },
      { skipCache: true }
    )

    const entries = result.value.map((item) =>
      this.mapToEntry(item, meta.keyField, meta.displayFieldName, meta.descriptionFieldName)
    )

    // Cache search results
    this.searchCache.set(searchCacheKey, {
      entries,
      timestamp: Date.now(),
      keyField: meta.keyField,
      displayFieldName: meta.displayFieldName,
      descriptionFieldName: meta.descriptionFieldName,
    })

    return entries
  }

  /**
   * Get a single value by key (display label lookup).
   * Checks cache first, falls back to server fetch.
   */
  async getValueByKey(
    config: ValueListConfig,
    key: string
  ): Promise<ValueListEntry | undefined> {
    // Check main cache first
    const cacheKey = this.buildCacheKey(config)
    const cached = this.cache.get(cacheKey)
    if (cached) {
      const found = cached.entries.find((e) => e.key === key)
      if (found) return found
    }

    // Check search caches
    for (const [, item] of this.searchCache) {
      const found = item.entries.find((e) => e.key === key)
      if (found) return found
    }

    // Fallback: fetch from server by ID
    try {
      const meta = await this.resolveMetadata(config)
      const selectFields = new Set<string>([meta.keyField, meta.displayFieldName])
      if (meta.descriptionFieldName) {
        selectFields.add(meta.descriptionFieldName)
      }

      const item = await odataService.getById<Record<string, unknown>>(
        config.module,
        config.entitySet,
        key,
        { $select: Array.from(selectFields).join(',') }
      )

      return this.mapToEntry(item, meta.keyField, meta.displayFieldName, meta.descriptionFieldName)
    } catch {
      return undefined
    }
  }

  /**
   * Pre-load values for multiple associations at once.
   * Useful for form initialization.
   */
  async preload(configs: ValueListConfig[]): Promise<void> {
    const promises = configs.map((config) => this.getValues(config))
    await Promise.allSettled(promises)
  }

  /**
   * Invalidate cache for an entity set.
   */
  invalidate(module: string, entitySet: string): void {
    const prefix = `${module}/${entitySet}`
    for (const key of this.cache.keys()) {
      if (key.startsWith(prefix)) {
        this.cache.delete(key)
      }
    }
    for (const key of this.searchCache.keys()) {
      if (key.startsWith(prefix)) {
        this.searchCache.delete(key)
      }
    }
  }

  /**
   * Clear all caches.
   */
  clearAll(): void {
    this.cache.clear()
    this.searchCache.clear()
    this.metadataCache.clear()
  }

  // =========================================================================
  // Private
  // =========================================================================

  private buildCacheKey(config: ValueListConfig): string {
    let key = `${config.module}/${config.entitySet}`
    if (config.filter) {
      key += `:filter:${config.filter}`
    }
    return key
  }

  /**
   * Resolve the key field, display field, and description field for an entity.
   * Uses metadata auto-detection if display/description fields are not specified.
   */
  private async resolveMetadata(
    config: ValueListConfig
  ): Promise<{
    keyField: string
    displayFieldName: string
    descriptionFieldName?: string
  }> {
    const metaKey = `${config.module}/${config.entitySet}`

    // If display field was explicitly provided, use it
    if (config.displayField) {
      return {
        keyField: await this.resolveKeyField(config.module, config.entitySet),
        displayFieldName: config.displayField,
        descriptionFieldName: config.descriptionField,
      }
    }

    // Check metadata cache
    const cachedMeta = this.metadataCache.get(metaKey)
    if (cachedMeta) {
      return cachedMeta
    }

    // Load entity metadata to auto-detect fields
    try {
      const entityMeta = await metadataService.getEntity(config.module, config.entitySet)

      const keyField =
        entityMeta.keys.length > 0 ? entityMeta.keys[0] : 'Id'

      // Auto-detect display field using priority list
      let displayFieldName: string | undefined
      for (const candidate of DISPLAY_FIELD_PRIORITY) {
        const found = entityMeta.fields.find(
          (f: FieldMetadata) => f.name.toLowerCase() === candidate
        )
        if (found) {
          displayFieldName = found.name
          break
        }
      }

      // Fallback: first non-key String field
      if (!displayFieldName) {
        const stringField = entityMeta.fields.find(
          (f: FieldMetadata) =>
            f.type === 'String' && !entityMeta.keys.includes(f.name)
        )
        if (stringField) {
          displayFieldName = stringField.name
        }
      }

      // Last resort: key field itself
      if (!displayFieldName) {
        displayFieldName = keyField
      }

      // Auto-detect description field (a second text field that is not the display field)
      let descriptionFieldName: string | undefined
      if (config.descriptionField) {
        descriptionFieldName = config.descriptionField
      } else {
        const descField = entityMeta.fields.find(
          (f: FieldMetadata) =>
            f.type === 'String' &&
            f.name !== displayFieldName &&
            !entityMeta.keys.includes(f.name) &&
            (f.name.toLowerCase() === 'description' ||
              f.name.toLowerCase() === 'desc')
        )
        if (descField) {
          descriptionFieldName = descField.name
        }
      }

      const resolved = { keyField, displayFieldName, descriptionFieldName }
      this.metadataCache.set(metaKey, resolved)
      return resolved
    } catch {
      // Fallback if metadata is unavailable
      const fallback = {
        keyField: 'Id',
        displayFieldName: 'Name',
        descriptionFieldName: config.descriptionField,
      }
      this.metadataCache.set(metaKey, fallback)
      return fallback
    }
  }

  private async resolveKeyField(module: string, entitySet: string): Promise<string> {
    const metaKey = `${module}/${entitySet}`
    const cachedMeta = this.metadataCache.get(metaKey)
    if (cachedMeta) {
      return cachedMeta.keyField
    }

    try {
      const entityMeta = await metadataService.getEntity(module, entitySet)
      return entityMeta.keys.length > 0 ? entityMeta.keys[0] : 'Id'
    } catch {
      return 'Id'
    }
  }

  /**
   * Map a raw OData entity row to a ValueListEntry.
   */
  private mapToEntry(
    item: Record<string, unknown>,
    keyField: string,
    displayFieldName: string,
    descriptionFieldName?: string
  ): ValueListEntry {
    const key = String(getField(item, keyField) ?? '')
    const label = String(getField(item, displayFieldName) ?? key)
    const description = descriptionFieldName
      ? (getField(item, descriptionFieldName) as string | undefined) ?? undefined
      : undefined

    return { key, label, description, data: item }
  }
}

// ---------------------------------------------------------------------------
// Singleton instance
// ---------------------------------------------------------------------------

export const valueListProvider = new ValueListProvider()
