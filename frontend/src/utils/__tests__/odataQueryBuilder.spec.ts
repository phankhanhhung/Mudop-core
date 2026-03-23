import { describe, it, expect } from 'vitest'
import {
  escapeODataString,
  formatODataValue,
  buildFilterExpression,
  buildODataFilter,
  createFilter,
  filters,
  buildExpandString
} from '../odataQueryBuilder'
import type { FilterCondition } from '@/types/odata'

describe('escapeODataString', () => {
  it('escapes single quotes by doubling them', () => {
    expect(escapeODataString("O'Brien")).toBe("O''Brien")
  })

  it('returns strings without quotes unchanged', () => {
    expect(escapeODataString('hello')).toBe('hello')
  })

  it('handles multiple quotes', () => {
    expect(escapeODataString("it's a 'test'")).toBe("it''s a ''test''")
  })
})

describe('formatODataValue', () => {
  it('formats null as "null"', () => {
    expect(formatODataValue(null)).toBe('null')
  })

  it('formats undefined as "null"', () => {
    expect(formatODataValue(undefined)).toBe('null')
  })

  it('wraps strings in single quotes', () => {
    expect(formatODataValue('hello')).toBe("'hello'")
  })

  it('escapes single quotes in strings', () => {
    expect(formatODataValue("O'Brien")).toBe("'O''Brien'")
  })

  it('formats booleans as lowercase strings', () => {
    expect(formatODataValue(true)).toBe('true')
    expect(formatODataValue(false)).toBe('false')
  })

  it('formats numbers as-is', () => {
    expect(formatODataValue(42)).toBe('42')
    expect(formatODataValue(3.14)).toBe('3.14')
  })

  it('formats Date objects as ISO strings', () => {
    const d = new Date('2024-01-15T10:30:00.000Z')
    expect(formatODataValue(d)).toBe('2024-01-15T10:30:00.000Z')
  })
})

describe('buildFilterExpression', () => {
  it('builds eq expression', () => {
    const result = buildFilterExpression({ field: 'Name', operator: 'eq', value: 'Alice' })
    expect(result).toBe("Name eq 'Alice'")
  })

  it('builds ne expression', () => {
    const result = buildFilterExpression({ field: 'Status', operator: 'ne', value: 'Inactive' })
    expect(result).toBe("Status ne 'Inactive'")
  })

  it('builds gt expression', () => {
    const result = buildFilterExpression({ field: 'Age', operator: 'gt', value: 18 })
    expect(result).toBe('Age gt 18')
  })

  it('builds ge expression', () => {
    const result = buildFilterExpression({ field: 'Age', operator: 'ge', value: 18 })
    expect(result).toBe('Age ge 18')
  })

  it('builds lt expression', () => {
    const result = buildFilterExpression({ field: 'Price', operator: 'lt', value: 100 })
    expect(result).toBe('Price lt 100')
  })

  it('builds le expression', () => {
    const result = buildFilterExpression({ field: 'Price', operator: 'le', value: 100 })
    expect(result).toBe('Price le 100')
  })

  it('builds contains expression', () => {
    const result = buildFilterExpression({ field: 'Name', operator: 'contains', value: 'ali' })
    expect(result).toBe("contains(Name, 'ali')")
  })

  it('builds startswith expression', () => {
    const result = buildFilterExpression({ field: 'Name', operator: 'startswith', value: 'Al' })
    expect(result).toBe("startswith(Name, 'Al')")
  })

  it('builds endswith expression', () => {
    const result = buildFilterExpression({ field: 'Name', operator: 'endswith', value: 'ce' })
    expect(result).toBe("endswith(Name, 'ce')")
  })

  it('throws on unknown operator', () => {
    expect(() =>
      buildFilterExpression({ field: 'X', operator: 'unknown' as any, value: 1 })
    ).toThrow('Unknown filter operator: unknown')
  })
})

describe('buildODataFilter', () => {
  it('returns empty string for empty conditions', () => {
    expect(buildODataFilter([])).toBe('')
  })

  it('builds a single condition', () => {
    const conditions: FilterCondition[] = [
      { field: 'Name', operator: 'eq', value: 'Alice' }
    ]
    expect(buildODataFilter(conditions)).toBe("Name eq 'Alice'")
  })

  it('joins multiple conditions with "and" by default', () => {
    const conditions: FilterCondition[] = [
      { field: 'Name', operator: 'eq', value: 'Alice' },
      { field: 'Age', operator: 'gt', value: 18 }
    ]
    expect(buildODataFilter(conditions)).toBe("Name eq 'Alice' and Age gt 18")
  })

  it('joins multiple conditions with "or" when specified', () => {
    const conditions: FilterCondition[] = [
      { field: 'Status', operator: 'eq', value: 'Active' },
      { field: 'Status', operator: 'eq', value: 'Pending' }
    ]
    expect(buildODataFilter(conditions, 'or')).toBe(
      "Status eq 'Active' or Status eq 'Pending'"
    )
  })
})

describe('createFilter', () => {
  it('creates a FilterCondition object', () => {
    const result = createFilter('Name', 'eq', 'Alice')
    expect(result).toEqual({ field: 'Name', operator: 'eq', value: 'Alice' })
  })
})

describe('filters helpers', () => {
  it('equals creates eq condition', () => {
    expect(filters.equals('Name', 'Alice')).toEqual({
      field: 'Name', operator: 'eq', value: 'Alice'
    })
  })

  it('notEquals creates ne condition', () => {
    expect(filters.notEquals('Status', 'Inactive')).toEqual({
      field: 'Status', operator: 'ne', value: 'Inactive'
    })
  })

  it('greaterThan creates gt condition', () => {
    expect(filters.greaterThan('Age', 18)).toEqual({
      field: 'Age', operator: 'gt', value: 18
    })
  })

  it('lessThan creates lt condition', () => {
    expect(filters.lessThan('Price', 100)).toEqual({
      field: 'Price', operator: 'lt', value: 100
    })
  })

  it('contains creates contains condition', () => {
    expect(filters.contains('Name', 'ali')).toEqual({
      field: 'Name', operator: 'contains', value: 'ali'
    })
  })

  it('startsWith creates startswith condition', () => {
    expect(filters.startsWith('Name', 'Al')).toEqual({
      field: 'Name', operator: 'startswith', value: 'Al'
    })
  })

  it('endsWith creates endswith condition', () => {
    expect(filters.endsWith('Name', 'ce')).toEqual({
      field: 'Name', operator: 'endswith', value: 'ce'
    })
  })
})

describe('buildExpandString', () => {
  it('returns simple property name for true value', () => {
    expect(buildExpandString({ Orders: true })).toBe('Orders')
  })

  it('returns comma-separated list for multiple expands', () => {
    const result = buildExpandString({ Orders: true, Address: true })
    expect(result).toBe('Orders,Address')
  })

  it('returns property name without parens when options are empty', () => {
    expect(buildExpandString({ Orders: {} })).toBe('Orders')
  })

  it('includes $select option', () => {
    const result = buildExpandString({
      Orders: { $select: ['Id', 'Total'] }
    })
    expect(result).toBe('Orders($select=Id,Total)')
  })

  it('includes $filter option', () => {
    const result = buildExpandString({
      Orders: {
        $filter: [{ field: 'Status', operator: 'eq', value: 'Active' }]
      }
    })
    expect(result).toBe("Orders($filter=Status eq 'Active')")
  })

  it('includes $orderby option', () => {
    const result = buildExpandString({
      Orders: { $orderby: 'CreatedAt desc' }
    })
    expect(result).toBe('Orders($orderby=CreatedAt desc)')
  })

  it('includes $top option', () => {
    const result = buildExpandString({
      Orders: { $top: 5 }
    })
    expect(result).toBe('Orders($top=5)')
  })

  it('combines multiple nested options with semicolon', () => {
    const result = buildExpandString({
      Orders: {
        $select: ['Id', 'Total'],
        $orderby: 'CreatedAt desc',
        $top: 10
      }
    })
    expect(result).toBe('Orders($select=Id,Total;$orderby=CreatedAt desc;$top=10)')
  })

  it('handles nested $expand recursively', () => {
    const result = buildExpandString({
      Orders: {
        $expand: {
          Items: { $select: ['ProductName', 'Quantity'] }
        }
      }
    })
    expect(result).toBe('Orders($expand=Items($select=ProductName,Quantity))')
  })

  it('handles mix of simple and complex expands', () => {
    const result = buildExpandString({
      Orders: { $top: 5 },
      Address: true
    })
    expect(result).toBe('Orders($top=5),Address')
  })
})
