import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { usePreferences } from '../preferences'

// Mock the i18n module before importing the module under test
vi.mock('@/i18n', () => {
  return {
    default: {
      global: {
        locale: { value: 'en' },
        t: (key: string) => {
          const translations: Record<string, string> = {
            'common.yes': 'Yes',
            'common.no': 'No'
          }
          return translations[key] ?? key
        }
      }
    }
  }
})

import {
  formatFileSize,
  formatBoolean,
  formatRelativeTime,
  formatDate,
  formatNumber,
  formatInteger,
  formatDecimal,
  formatCurrency,
  isRtl
} from '../formatting'

describe('formatFileSize', () => {
  beforeEach(() => {
    localStorage.clear()
    const { resetPreferences } = usePreferences()
    resetPreferences()
  })

  it('returns "0 B" for null', () => {
    expect(formatFileSize(null)).toBe('0 B')
  })

  it('returns "0 B" for undefined', () => {
    expect(formatFileSize(undefined)).toBe('0 B')
  })

  it('returns "0 B" for zero', () => {
    expect(formatFileSize(0)).toBe('0 B')
  })

  it('formats bytes', () => {
    const result = formatFileSize(500)
    expect(result).toMatch(/500.*B/)
  })

  it('formats kilobytes', () => {
    const result = formatFileSize(1024)
    expect(result).toMatch(/1.*KB/)
  })

  it('formats megabytes', () => {
    const result = formatFileSize(1024 * 1024)
    expect(result).toMatch(/1.*MB/)
  })

  it('formats gigabytes', () => {
    const result = formatFileSize(1024 * 1024 * 1024)
    expect(result).toMatch(/1.*GB/)
  })

  it('formats fractional sizes', () => {
    const result = formatFileSize(1536) // 1.5 KB
    expect(result).toMatch(/1\.5.*KB/)
  })
})

describe('formatBoolean', () => {
  it('returns "Yes" for true', () => {
    expect(formatBoolean(true)).toBe('Yes')
  })

  it('returns "No" for false', () => {
    expect(formatBoolean(false)).toBe('No')
  })

  it('returns "-" for null', () => {
    expect(formatBoolean(null)).toBe('-')
  })

  it('returns "-" for undefined', () => {
    expect(formatBoolean(undefined)).toBe('-')
  })

  it('returns "Yes" for truthy values', () => {
    expect(formatBoolean(1)).toBe('Yes')
    expect(formatBoolean('hello')).toBe('Yes')
  })

  it('returns "No" for falsy non-null values', () => {
    expect(formatBoolean(0)).toBe('No')
    expect(formatBoolean('')).toBe('No')
  })
})

describe('formatRelativeTime', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    localStorage.clear()
    const { resetPreferences } = usePreferences()
    resetPreferences()
  })

  it('returns "-" for null', () => {
    expect(formatRelativeTime(null)).toBe('-')
  })

  it('returns "-" for undefined', () => {
    expect(formatRelativeTime(undefined)).toBe('-')
  })

  it('returns the original string for invalid dates', () => {
    expect(formatRelativeTime('not-a-date')).toBe('not-a-date')
  })

  it('formats seconds ago', () => {
    const now = new Date('2024-01-15T12:00:30Z')
    vi.setSystemTime(now)
    const result = formatRelativeTime('2024-01-15T12:00:00Z')
    // Should contain "30" and "second" in some form
    expect(result).toMatch(/30|second/i)
  })

  it('formats minutes ago', () => {
    const now = new Date('2024-01-15T12:05:00Z')
    vi.setSystemTime(now)
    const result = formatRelativeTime('2024-01-15T12:00:00Z')
    expect(result).toMatch(/5|minute/i)
  })

  it('formats hours ago', () => {
    const now = new Date('2024-01-15T15:00:00Z')
    vi.setSystemTime(now)
    const result = formatRelativeTime('2024-01-15T12:00:00Z')
    expect(result).toMatch(/3|hour/i)
  })

  it('formats days ago', () => {
    const now = new Date('2024-01-18T12:00:00Z')
    vi.setSystemTime(now)
    const result = formatRelativeTime('2024-01-15T12:00:00Z')
    expect(result).toMatch(/3|day/i)
  })

  it('falls back to formatDate for dates older than 30 days', () => {
    const now = new Date('2024-03-15T12:00:00Z')
    vi.setSystemTime(now)
    const result = formatRelativeTime('2024-01-15T12:00:00Z')
    // Should be a formatted date, not a relative string
    expect(result).toMatch(/Jan|2024|15/)
  })

  afterEach(() => {
    vi.useRealTimers()
  })
})

describe('formatDate', () => {
  beforeEach(() => {
    localStorage.clear()
    const { resetPreferences } = usePreferences()
    resetPreferences()
  })

  it('returns "-" for null', () => {
    expect(formatDate(null)).toBe('-')
  })

  it('returns "-" for undefined', () => {
    expect(formatDate(undefined)).toBe('-')
  })

  it('returns original string for invalid dates', () => {
    expect(formatDate('not-a-date')).toBe('not-a-date')
  })

  it('formats a valid date string', () => {
    const result = formatDate('2024-01-15')
    // Should contain the year and day in some locale-specific format
    expect(result).toMatch(/2024/)
    expect(result).toMatch(/15/)
  })

  it('formats a Date object', () => {
    const result = formatDate(new Date('2024-06-20T00:00:00Z'))
    expect(result).toMatch(/2024/)
  })

  it('formats date with ISO format preference (YYYY-MM-DD)', () => {
    const { updatePreference } = usePreferences()
    updatePreference('dateFormat', 'iso')
    // sv-SE locale produces YYYY-MM-DD format
    const result = formatDate('2024-03-15T00:00:00Z')
    expect(result).toMatch(/2024/)
    expect(result).toMatch(/03/)
    expect(result).toMatch(/15/)
  })

  it('formats date with US format preference (MM/DD/YYYY)', () => {
    const { updatePreference } = usePreferences()
    updatePreference('dateFormat', 'us')
    const result = formatDate('2024-03-15T00:00:00Z')
    // en-US locale produces MM/DD/YYYY
    expect(result).toMatch(/03\/15\/2024/)
  })

  it('formats date with EU format preference (DD.MM.YYYY)', () => {
    const { updatePreference } = usePreferences()
    updatePreference('dateFormat', 'eu')
    const result = formatDate('2024-03-15T00:00:00Z')
    // de-DE locale produces DD.MM.YYYY
    expect(result).toMatch(/15\.03\.2024/)
  })
})

describe('formatNumber', () => {
  beforeEach(() => {
    localStorage.clear()
    const { resetPreferences } = usePreferences()
    resetPreferences()
  })

  it('returns "-" for null', () => {
    expect(formatNumber(null)).toBe('-')
  })

  it('returns "-" for undefined', () => {
    expect(formatNumber(undefined)).toBe('-')
  })

  it('formats a number', () => {
    const result = formatNumber(1234)
    // Locale-specific; at minimum should contain the digits
    expect(result).toMatch(/1.*234/)
  })

  it('formats number with EN format (comma thousands separator)', () => {
    const { updatePreference } = usePreferences()
    updatePreference('numberFormat', 'en')
    const result = formatNumber(1234567)
    // en-US: 1,234,567
    expect(result).toBe('1,234,567')
  })

  it('formats number with DE format (dot thousands separator)', () => {
    const { updatePreference } = usePreferences()
    updatePreference('numberFormat', 'de')
    const result = formatNumber(1234567)
    // de-DE: 1.234.567
    expect(result).toBe('1.234.567')
  })
})

describe('formatInteger', () => {
  beforeEach(() => {
    localStorage.clear()
    const { resetPreferences } = usePreferences()
    resetPreferences()
  })

  it('formats without decimal places', () => {
    const result = formatInteger(1234)
    expect(result).not.toMatch(/\./)
    expect(result).toMatch(/1.*234/)
  })
})

describe('formatDecimal', () => {
  beforeEach(() => {
    localStorage.clear()
    const { resetPreferences } = usePreferences()
    resetPreferences()
  })

  it('formats with 2 decimal places by default', () => {
    const result = formatDecimal(1234.5)
    expect(result).toMatch(/1.*234\.50|1.*234,50/)
  })

  it('formats decimal with EN locale', () => {
    const { updatePreference } = usePreferences()
    updatePreference('numberFormat', 'en')
    const result = formatDecimal(1234.5)
    // en-US: 1,234.50
    expect(result).toBe('1,234.50')
  })

  it('formats decimal with DE locale', () => {
    const { updatePreference } = usePreferences()
    updatePreference('numberFormat', 'de')
    const result = formatDecimal(1234.5)
    // de-DE: 1.234,50
    expect(result).toBe('1.234,50')
  })
})

describe('formatCurrency', () => {
  beforeEach(() => {
    localStorage.clear()
    const { resetPreferences } = usePreferences()
    resetPreferences()
  })

  it('returns "-" for null', () => {
    expect(formatCurrency(null)).toBe('-')
  })

  it('returns "-" for undefined', () => {
    expect(formatCurrency(undefined)).toBe('-')
  })

  it('formats currency with EN locale', () => {
    const { updatePreference } = usePreferences()
    updatePreference('numberFormat', 'en')
    const result = formatCurrency(1234.56)
    // en-US USD formatting
    expect(result).toMatch(/\$/)
    expect(result).toMatch(/1.*234\.56/)
  })

  it('formats currency with DE locale', () => {
    const { updatePreference } = usePreferences()
    updatePreference('numberFormat', 'de')
    const result = formatCurrency(1234.56)
    // de-DE USD formatting uses different separator
    expect(result).toMatch(/1.*234,56/)
  })

  it('formats with specified currency code', () => {
    const { updatePreference } = usePreferences()
    updatePreference('numberFormat', 'en')
    const result = formatCurrency(99.99, 'EUR')
    expect(result).toMatch(/99\.99/)
  })
})

describe('null/undefined handling across all formatters', () => {
  beforeEach(() => {
    localStorage.clear()
    const { resetPreferences } = usePreferences()
    resetPreferences()
  })

  it('formatDate returns "-" for null and undefined', () => {
    expect(formatDate(null)).toBe('-')
    expect(formatDate(undefined)).toBe('-')
  })

  it('formatNumber returns "-" for null and undefined', () => {
    expect(formatNumber(null)).toBe('-')
    expect(formatNumber(undefined)).toBe('-')
  })

  it('formatInteger returns "-" for null and undefined', () => {
    expect(formatInteger(null)).toBe('-')
    expect(formatInteger(undefined)).toBe('-')
  })

  it('formatDecimal returns "-" for null and undefined', () => {
    expect(formatDecimal(null)).toBe('-')
    expect(formatDecimal(undefined)).toBe('-')
  })

  it('formatCurrency returns "-" for null and undefined', () => {
    expect(formatCurrency(null)).toBe('-')
    expect(formatCurrency(undefined)).toBe('-')
  })

  it('formatBoolean returns "-" for null and undefined', () => {
    expect(formatBoolean(null)).toBe('-')
    expect(formatBoolean(undefined)).toBe('-')
  })

  it('formatFileSize returns "0 B" for null and undefined', () => {
    expect(formatFileSize(null)).toBe('0 B')
    expect(formatFileSize(undefined)).toBe('0 B')
  })
})

describe('isRtl', () => {
  it('returns true for Arabic', () => {
    expect(isRtl('ar')).toBe(true)
  })

  it('returns true for Hebrew', () => {
    expect(isRtl('he')).toBe(true)
  })

  it('returns false for English', () => {
    expect(isRtl('en')).toBe(false)
  })
})
