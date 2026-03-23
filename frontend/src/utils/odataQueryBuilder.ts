import type { FilterCondition, FilterOperator } from '@/types/odata'

/**
 * Escapes a string value for use in OData filter expressions
 */
export function escapeODataString(value: string): string {
  return value.replace(/'/g, "''")
}

/**
 * Formats a value for OData filter expressions based on its type
 */
export function formatODataValue(value: unknown): string {
  if (value === null || value === undefined) {
    return 'null'
  }

  if (typeof value === 'string') {
    return `'${escapeODataString(value)}'`
  }

  if (typeof value === 'boolean') {
    return value ? 'true' : 'false'
  }

  if (typeof value === 'number') {
    return value.toString()
  }

  if (value instanceof Date) {
    return value.toISOString()
  }

  // UUID or other string types
  return `'${escapeODataString(String(value))}'`
}

/**
 * Validates that a field name is a safe OData identifier (letters, digits, underscores, dots, slashes)
 */
function isValidFieldName(field: string): boolean {
  if (field.includes('..')) return false
  return /^[a-zA-Z_][a-zA-Z0-9_./]*$/.test(field)
}

/**
 * Builds a single filter expression
 */
export function buildFilterExpression(condition: FilterCondition): string {
  const { field, operator, value } = condition

  if (!isValidFieldName(field)) {
    throw new Error(`Invalid OData field name: ${field}`)
  }

  const formattedValue = formatODataValue(value)

  switch (operator) {
    case 'eq':
      return `${field} eq ${formattedValue}`
    case 'ne':
      return `${field} ne ${formattedValue}`
    case 'gt':
      return `${field} gt ${formattedValue}`
    case 'ge':
      return `${field} ge ${formattedValue}`
    case 'lt':
      return `${field} lt ${formattedValue}`
    case 'le':
      return `${field} le ${formattedValue}`
    case 'contains':
      return `contains(${field}, ${formattedValue})`
    case 'startswith':
      return `startswith(${field}, ${formattedValue})`
    case 'endswith':
      return `endswith(${field}, ${formattedValue})`
    default:
      throw new Error(`Unknown filter operator: ${operator}`)
  }
}

/**
 * Builds a complete OData filter string from multiple conditions
 */
export function buildODataFilter(
  conditions: FilterCondition[],
  logicalOperator: 'and' | 'or' = 'and'
): string {
  if (conditions.length === 0) {
    return ''
  }

  const expressions = conditions.map(buildFilterExpression)
  return expressions.join(` ${logicalOperator} `)
}

/**
 * Helper to create filter conditions
 */
export function createFilter(
  field: string,
  operator: FilterOperator,
  value: unknown
): FilterCondition {
  return { field, operator, value }
}

/**
 * Helper functions for common filter operations
 */
export const filters = {
  equals: (field: string, value: unknown): FilterCondition =>
    createFilter(field, 'eq', value),

  notEquals: (field: string, value: unknown): FilterCondition =>
    createFilter(field, 'ne', value),

  greaterThan: (field: string, value: unknown): FilterCondition =>
    createFilter(field, 'gt', value),

  greaterOrEqual: (field: string, value: unknown): FilterCondition =>
    createFilter(field, 'ge', value),

  lessThan: (field: string, value: unknown): FilterCondition =>
    createFilter(field, 'lt', value),

  lessOrEqual: (field: string, value: unknown): FilterCondition =>
    createFilter(field, 'le', value),

  contains: (field: string, value: string): FilterCondition =>
    createFilter(field, 'contains', value),

  startsWith: (field: string, value: string): FilterCondition =>
    createFilter(field, 'startswith', value),

  endsWith: (field: string, value: string): FilterCondition =>
    createFilter(field, 'endswith', value)
}

/**
 * Builds an $expand string with nested options
 */
export interface ExpandOptions {
  $select?: string[]
  $filter?: FilterCondition[]
  $orderby?: string
  $top?: number
  $expand?: Record<string, ExpandOptions>
}

export function buildExpandString(
  expands: Record<string, ExpandOptions | true>
): string {
  const parts: string[] = []

  for (const [key, options] of Object.entries(expands)) {
    if (!isValidFieldName(key)) {
      throw new Error(`Invalid OData expand field name: ${key}`)
    }

    if (options === true) {
      parts.push(key)
    } else {
      const nestedOptions: string[] = []

      if (options.$select?.length) {
        nestedOptions.push(`$select=${options.$select.join(',')}`)
      }
      if (options.$filter?.length) {
        nestedOptions.push(`$filter=${buildODataFilter(options.$filter)}`)
      }
      if (options.$orderby) {
        nestedOptions.push(`$orderby=${options.$orderby}`)
      }
      if (options.$top !== undefined) {
        nestedOptions.push(`$top=${options.$top}`)
      }
      if (options.$expand) {
        nestedOptions.push(`$expand=${buildExpandString(options.$expand)}`)
      }

      if (nestedOptions.length > 0) {
        parts.push(`${key}(${nestedOptions.join(';')})`)
      } else {
        parts.push(key)
      }
    }
  }

  return parts.join(',')
}
