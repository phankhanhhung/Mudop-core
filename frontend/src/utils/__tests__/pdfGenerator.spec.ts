import { describe, it, expect } from 'vitest'
import { formatValue, computeAggregates } from '../pdfGenerator'
import type { ReportField } from '@/services/reportService'

// ---------------------------------------------------------------------------
// Helper
// ---------------------------------------------------------------------------

function makeField(overrides: Partial<ReportField> = {}): ReportField {
  return {
    name: 'Amount',
    label: 'Amount',
    width: 150,
    ...overrides,
  }
}

// ---------------------------------------------------------------------------
// formatValue()
// ---------------------------------------------------------------------------

describe('formatValue()', () => {
  // null / undefined
  it('returns empty string for null', () => {
    expect(formatValue(null)).toBe('')
  })

  it('returns empty string for undefined', () => {
    expect(formatValue(undefined)).toBe('')
  })

  // date format
  it('formats an ISO date string using toLocaleDateString()', () => {
    const isoDate = '2024-06-15'
    const result = formatValue(isoDate, 'date')
    // toLocaleDateString produces locale-dependent output; just verify it parsed
    expect(result).toBe(new Date(isoDate).toLocaleDateString())
  })

  it('formats a full ISO datetime string as a date using toLocaleDateString()', () => {
    const iso = '2024-06-15T12:30:00Z'
    expect(formatValue(iso, 'date')).toBe(new Date(iso).toLocaleDateString())
  })

  // datetime format
  it('formats an ISO datetime string using toLocaleString()', () => {
    const iso = '2024-06-15T12:30:00Z'
    expect(formatValue(iso, 'datetime')).toBe(new Date(iso).toLocaleString())
  })

  // currency format
  it('formats a number as USD currency string', () => {
    const result = formatValue(1234.5, 'currency')
    expect(result).toMatch(/\$1,234\.50/)
  })

  it('formats zero as USD currency $0.00', () => {
    const result = formatValue(0, 'currency')
    expect(result).toMatch(/\$0\.00/)
  })

  it('formats a negative number as USD currency', () => {
    const result = formatValue(-99.99, 'currency')
    expect(result).toContain('99.99')
  })

  // percent format
  it('formats 0.15 as "15.0%"', () => {
    expect(formatValue(0.15, 'percent')).toBe('15.0%')
  })

  it('formats 1 as "100.0%"', () => {
    expect(formatValue(1, 'percent')).toBe('100.0%')
  })

  it('formats 0 as "0.0%"', () => {
    expect(formatValue(0, 'percent')).toBe('0.0%')
  })

  it('formats 0.333 as "33.3%"', () => {
    expect(formatValue(0.333, 'percent')).toBe('33.3%')
  })

  // default (no format)
  it('returns string coercion for a number with no format', () => {
    expect(formatValue(42)).toBe('42')
  })

  it('returns the value as-is string for a string input with no format', () => {
    expect(formatValue('hello')).toBe('hello')
  })

  it('returns string coercion for a boolean with no format', () => {
    expect(formatValue(true)).toBe('true')
  })

  it('returns string coercion for an unknown format type', () => {
    expect(formatValue(99, 'unknown-format')).toBe('99')
  })
})

// ---------------------------------------------------------------------------
// computeAggregates()
// ---------------------------------------------------------------------------

describe('computeAggregates()', () => {
  const rows: Record<string, unknown>[] = [
    { Amount: 10, Qty: 2 },
    { Amount: 20, Qty: 3 },
    { Amount: 30, Qty: 5 },
  ]

  it('computes sum of numeric values', () => {
    const fields: ReportField[] = [makeField({ name: 'Amount', aggregate: 'sum' })]
    const result = computeAggregates(fields, rows)
    expect(result['Amount']).toBe('SUM: 60')
  })

  it('counts the number of rows (count)', () => {
    const fields: ReportField[] = [makeField({ name: 'Amount', aggregate: 'count' })]
    const result = computeAggregates(fields, rows)
    expect(result['Amount']).toBe('COUNT: 3')
  })

  it('computes average of numeric values', () => {
    const fields: ReportField[] = [makeField({ name: 'Amount', aggregate: 'avg' })]
    const result = computeAggregates(fields, rows)
    expect(result['Amount']).toBe('AVG: 20')
  })

  it('finds the minimum value', () => {
    const fields: ReportField[] = [makeField({ name: 'Amount', aggregate: 'min' })]
    const result = computeAggregates(fields, rows)
    expect(result['Amount']).toBe('MIN: 10')
  })

  it('finds the maximum value', () => {
    const fields: ReportField[] = [makeField({ name: 'Amount', aggregate: 'max' })]
    const result = computeAggregates(fields, rows)
    expect(result['Amount']).toBe('MAX: 30')
  })

  it('skips fields that have no aggregate defined', () => {
    const fields: ReportField[] = [
      makeField({ name: 'Amount', aggregate: 'sum' }),
      makeField({ name: 'Qty' }),  // no aggregate
    ]
    const result = computeAggregates(fields, rows)
    expect(result['Amount']).toBeDefined()
    expect(result['Qty']).toBeUndefined()
  })

  it('returns Record<string, string> type with label prefix', () => {
    const fields: ReportField[] = [makeField({ name: 'Amount', aggregate: 'sum' })]
    const result = computeAggregates(fields, rows)
    expect(typeof result['Amount']).toBe('string')
    expect(result['Amount']).toMatch(/^SUM:/)
  })

  it('handles null values by skipping them in sum calculation', () => {
    const rowsWithNull: Record<string, unknown>[] = [
      { Amount: 10 },
      { Amount: null },
      { Amount: 30 },
    ]
    const fields: ReportField[] = [makeField({ name: 'Amount', aggregate: 'sum' })]
    const result = computeAggregates(fields, rowsWithNull)
    expect(result['Amount']).toBe('SUM: 40')
  })

  it('handles undefined values by skipping them in count calculation', () => {
    const rowsWithUndefined: Record<string, unknown>[] = [
      { Amount: 5 },
      { Amount: undefined },
      { Amount: 15 },
    ]
    const fields: ReportField[] = [makeField({ name: 'Amount', aggregate: 'count' })]
    const result = computeAggregates(fields, rowsWithUndefined)
    expect(result['Amount']).toBe('COUNT: 2')
  })

  it('returns "AGGREGATE: -" when all values are null/undefined', () => {
    const rowsAllNull: Record<string, unknown>[] = [
      { Amount: null },
      { Amount: undefined },
    ]
    const fields: ReportField[] = [makeField({ name: 'Amount', aggregate: 'sum' })]
    const result = computeAggregates(fields, rowsAllNull)
    expect(result['Amount']).toBe('SUM: -')
  })

  it('returns "COUNT: -" when rows array is empty', () => {
    const fields: ReportField[] = [makeField({ name: 'Amount', aggregate: 'count' })]
    const result = computeAggregates(fields, [])
    expect(result['Amount']).toBe('COUNT: -')
  })

  it('applies field format to the aggregate result value', () => {
    const rowsForCurrency: Record<string, unknown>[] = [
      { Price: 1000 },
      { Price: 2000 },
    ]
    const fields: ReportField[] = [makeField({ name: 'Price', aggregate: 'sum', format: 'currency' })]
    const result = computeAggregates(fields, rowsForCurrency)
    expect(result['Price']).toMatch(/^SUM: \$3,000\.00/)
  })

  it('returns an empty object when fields list is empty', () => {
    const result = computeAggregates([], rows)
    expect(result).toEqual({})
  })

  it('handles multiple aggregated fields simultaneously', () => {
    const fields: ReportField[] = [
      makeField({ name: 'Amount', aggregate: 'sum' }),
      makeField({ name: 'Qty', aggregate: 'max' }),
    ]
    const result = computeAggregates(fields, rows)
    expect(result['Amount']).toBe('SUM: 60')
    expect(result['Qty']).toBe('MAX: 5')
  })
})
