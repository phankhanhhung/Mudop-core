/**
 * Smart Filter — metadata-driven filter generation.
 *
 * Automatically generates filter field definitions from entity metadata,
 * inspired by OpenUI5's SmartFilterBar. Each field type maps to an appropriate
 * filter widget (text input, date picker, dropdown, etc.).
 *
 * Features:
 * - Auto-generate filter fields from EntityMetadata
 * - Type-aware widget selection (String → text, Date → date, Enum → select)
 * - Respect FilterRestrictions annotations (NonFilterableProperties)
 * - Integration with $search for full-text search
 *
 * Usage:
 *   const smartFilter = new SmartFilter()
 *   const fields = smartFilter.generateFields(entityMetadata)
 *   const filterString = smartFilter.buildFilterString(activeFilters)
 */

import type { EntityMetadata, FieldType } from '@/types/metadata'
import type { FilterCondition, FilterOperator } from '@/types/odata'
import type { SmartFilterField, SmartFilterWidgetType } from './types'
import { buildODataFilter } from '@/utils/odataQueryBuilder'

// ---------------------------------------------------------------------------
// SmartFilter
// ---------------------------------------------------------------------------

export class SmartFilter {
  private fieldCache = new Map<string, SmartFilterField[]>()

  /**
   * Generate smart filter fields from entity metadata.
   * Results are cached per entity name.
   */
  generateFields(metadata: EntityMetadata): SmartFilterField[] {
    const cacheKey = `${metadata.namespace}.${metadata.name}`
    if (this.fieldCache.has(cacheKey)) {
      return this.fieldCache.get(cacheKey)!
    }

    const fields: SmartFilterField[] = []

    // Check for filter restrictions in annotations
    const filterRestrictions = metadata.annotations?.['Org.OData.Capabilities.V1.FilterRestrictions'] as
      | { NonFilterableProperties?: string[]; Filterable?: boolean }
      | undefined

    if (filterRestrictions?.Filterable === false) {
      return fields // Entity is not filterable at all
    }

    const nonFilterable = new Set(filterRestrictions?.NonFilterableProperties ?? [])

    // Check for sort restrictions
    const sortRestrictions = metadata.annotations?.['Org.OData.Capabilities.V1.SortRestrictions'] as
      | { NonSortableProperties?: string[] }
      | undefined
    const nonSortable = new Set(sortRestrictions?.NonSortableProperties ?? [])

    for (const field of metadata.fields) {
      // Skip computed, key fields, and non-filterable fields
      if (field.isComputed || nonFilterable.has(field.name)) continue
      // Skip key fields from filter UI (usually UUID)
      if (metadata.keys.includes(field.name)) continue

      const widgetType = SmartFilter.fieldTypeToWidget(field.type)
      const smartField: SmartFilterField = {
        name: field.name,
        label: field.displayName ?? field.name,
        widgetType,
        filterable: !nonFilterable.has(field.name),
        sortable: !nonSortable.has(field.name),
        defaultOperator: SmartFilter.defaultOperator(widgetType),
      }

      // Enum values
      if (field.type === 'Enum' && field.enumValues) {
        smartField.enumValues = field.enumValues.map(ev => ({
          name: ev.name,
          value: ev.value,
          displayName: ev.displayName ?? ev.name,
        }))
      }

      fields.push(smartField)
    }

    // Add association-based filters (FK fields)
    for (const assoc of metadata.associations) {
      if (assoc.foreignKey && !assoc.isComposition) {
        // Only add if not already present
        if (!fields.some(f => f.name === assoc.foreignKey)) {
          fields.push({
            name: assoc.foreignKey!,
            label: assoc.name,
            widgetType: 'association',
            filterable: true,
            sortable: true,
            associationTarget: assoc.targetEntity,
            defaultOperator: 'eq',
          })
        }
      }
    }

    this.fieldCache.set(cacheKey, fields)
    return fields
  }

  /**
   * Build OData $filter string from a set of active filters,
   * respecting the smart field definitions.
   */
  buildFilterString(
    filters: FilterCondition[],
    logic: 'and' | 'or' = 'and'
  ): string {
    if (filters.length === 0) return ''
    return buildODataFilter(filters, logic)
  }

  /**
   * Create a filter condition appropriate for the field type.
   */
  createFilterForField(
    field: SmartFilterField,
    value: unknown,
    operator?: FilterOperator
  ): FilterCondition {
    return {
      field: field.name,
      operator: operator ?? field.defaultOperator,
      value,
    }
  }

  /**
   * Create a between-filter (range) for numeric or date fields.
   */
  createRangeFilter(
    field: SmartFilterField,
    min: unknown,
    max: unknown
  ): FilterCondition[] {
    const conditions: FilterCondition[] = []
    if (min != null && min !== '') {
      conditions.push({ field: field.name, operator: 'ge', value: min })
    }
    if (max != null && max !== '') {
      conditions.push({ field: field.name, operator: 'le', value: max })
    }
    return conditions
  }

  /**
   * Get searchable fields (text fields that support $search or contains filter).
   */
  getSearchableFields(metadata: EntityMetadata): SmartFilterField[] {
    return this.generateFields(metadata).filter(
      f => f.widgetType === 'text'
    )
  }

  /**
   * Get sortable fields.
   */
  getSortableFields(metadata: EntityMetadata): SmartFilterField[] {
    return this.generateFields(metadata).filter(f => f.sortable)
  }

  /**
   * Clear cache.
   */
  clearCache(): void {
    this.fieldCache.clear()
  }

  static fieldTypeToWidget(fieldType: FieldType): SmartFilterWidgetType {
    switch (fieldType) {
      case 'String': return 'text'
      case 'Integer': return 'number'
      case 'Decimal': return 'decimal'
      case 'Boolean': return 'boolean'
      case 'Date': return 'date'
      case 'DateTime':
      case 'Timestamp': return 'datetime'
      case 'UUID': return 'uuid'
      case 'Enum': return 'enum'
      default: return 'text'
    }
  }

  static defaultOperator(widget: SmartFilterWidgetType): FilterOperator {
    switch (widget) {
      case 'text': return 'contains'
      case 'number':
      case 'decimal': return 'eq'
      case 'boolean': return 'eq'
      case 'date':
      case 'datetime': return 'eq'
      case 'enum': return 'eq'
      case 'uuid': return 'eq'
      case 'association': return 'eq'
      default: return 'eq'
    }
  }
}
