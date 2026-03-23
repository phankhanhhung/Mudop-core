import type { FilterCondition } from '@/types/odata'
import { buildODataFilter } from '@/utils/odataQueryBuilder'

/**
 * Represents a filter group with a logical operator and a list of conditions.
 * Supports one level of nesting: a root group contains conditions but not sub-groups.
 */
export interface FilterGroup {
  logic: 'and' | 'or'
  conditions: FilterCondition[]
}

/**
 * Convert a single FilterGroup to an OData $filter string.
 * Each condition is joined by the group's logical operator.
 * Returns an empty string if the group has no conditions.
 */
export function buildFilterGroupString(group: FilterGroup): string {
  const validConditions = group.conditions.filter(
    (c) => c.field && c.operator && c.value !== undefined && c.value !== ''
  )

  if (validConditions.length === 0) {
    return ''
  }

  return buildODataFilter(validConditions, group.logic)
}

/**
 * Convert multiple FilterGroups to a combined OData $filter string.
 * Groups are joined with AND at the top level. Each group's internal
 * conditions use the group's own logical operator.
 *
 * If a group has multiple conditions, its output is wrapped in parentheses
 * when combined with other groups.
 */
export function buildAdvancedFilter(groups: FilterGroup[]): string {
  const groupStrings = groups
    .map((group) => {
      const str = buildFilterGroupString(group)
      if (!str) return ''
      // Wrap multi-condition groups in parentheses when combining
      const validCount = group.conditions.filter(
        (c) => c.field && c.operator && c.value !== undefined && c.value !== ''
      ).length
      if (validCount > 1 && groups.length > 1) {
        return `(${str})`
      }
      return str
    })
    .filter((s) => s.length > 0)

  if (groupStrings.length === 0) {
    return ''
  }

  return groupStrings.join(' and ')
}

/**
 * Parse a simple FilterCondition[] into a single FilterGroup.
 * Defaults to AND logic.
 */
export function conditionsToGroup(conditions: FilterCondition[]): FilterGroup {
  return {
    logic: 'and',
    conditions: [...conditions]
  }
}

/**
 * Validate a raw OData filter string with basic syntax checks.
 * This performs lightweight validation -- it does not fully parse OData grammar,
 * but catches common mistakes like unmatched parentheses, empty expressions,
 * and obviously malformed input.
 */
export function validateRawFilter(raw: string): { valid: boolean; error?: string } {
  const trimmed = raw.trim()

  if (trimmed.length === 0) {
    return { valid: true }
  }

  // Check for balanced parentheses
  let depth = 0
  for (const ch of trimmed) {
    if (ch === '(') depth++
    if (ch === ')') depth--
    if (depth < 0) {
      return { valid: false, error: 'Unmatched closing parenthesis' }
    }
  }
  if (depth !== 0) {
    return { valid: false, error: 'Unmatched opening parenthesis' }
  }

  // Check for balanced single quotes
  let inString = false
  for (let i = 0; i < trimmed.length; i++) {
    if (trimmed[i] === "'") {
      // Check for escaped quote ('')
      if (inString && i + 1 < trimmed.length && trimmed[i + 1] === "'") {
        i++ // skip escaped quote
        continue
      }
      inString = !inString
    }
  }
  if (inString) {
    return { valid: false, error: 'Unmatched single quote in string literal' }
  }

  // Check that expression doesn't start or end with a logical operator
  const logicalPattern = /^(and|or)\b|\b(and|or)$/i
  if (logicalPattern.test(trimmed)) {
    return { valid: false, error: 'Expression cannot start or end with a logical operator' }
  }

  return { valid: true }
}
