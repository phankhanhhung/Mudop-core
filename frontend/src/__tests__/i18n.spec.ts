import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import { isRtlLocale, applyLocaleDirection } from '@/i18n'
import en from '@/locales/en.json'

// ─── Helpers ─────────────────────────────────────────────────────────────────

/** Recursively collect every leaf key path in a nested object, e.g. "common.save" */
function collectKeys(obj: Record<string, unknown>, prefix = ''): string[] {
  return Object.entries(obj).flatMap(([k, v]) => {
    const path = prefix ? `${prefix}.${k}` : k
    if (v !== null && typeof v === 'object' && !Array.isArray(v)) {
      return collectKeys(v as Record<string, unknown>, path)
    }
    return [path]
  })
}

const EN_KEYS = new Set(collectKeys(en as Record<string, unknown>))

// ─── isRtlLocale ─────────────────────────────────────────────────────────────

describe('isRtlLocale()', () => {
  it('returns true for Arabic (ar)', () => {
    expect(isRtlLocale('ar')).toBe(true)
  })

  it('returns true for Hebrew (he)', () => {
    expect(isRtlLocale('he')).toBe(true)
  })

  it('returns true for Farsi (fa)', () => {
    expect(isRtlLocale('fa')).toBe(true)
  })

  it('returns true for Urdu (ur)', () => {
    expect(isRtlLocale('ur')).toBe(true)
  })

  it('returns false for English (en)', () => {
    expect(isRtlLocale('en')).toBe(false)
  })

  it('returns false for French (fr)', () => {
    expect(isRtlLocale('fr')).toBe(false)
  })

  it('returns false for German (de)', () => {
    expect(isRtlLocale('de')).toBe(false)
  })
})

// ─── applyLocaleDirection ─────────────────────────────────────────────────────

describe('applyLocaleDirection()', () => {
  let originalDir: string
  let originalClassList: string[]

  beforeEach(() => {
    originalDir = document.documentElement.getAttribute('dir') ?? ''
    originalClassList = [...document.documentElement.classList]
  })

  afterEach(() => {
    // Restore
    document.documentElement.setAttribute('dir', originalDir)
    document.documentElement.classList.remove('rtl')
    for (const cls of originalClassList) {
      document.documentElement.classList.add(cls)
    }
  })

  it('sets dir="rtl" and adds rtl class for Arabic', () => {
    applyLocaleDirection('ar')
    expect(document.documentElement.getAttribute('dir')).toBe('rtl')
    expect(document.documentElement.classList.contains('rtl')).toBe(true)
  })

  it('sets dir="ltr" and removes rtl class for English', () => {
    // First set to RTL
    applyLocaleDirection('ar')
    // Then switch back
    applyLocaleDirection('en')
    expect(document.documentElement.getAttribute('dir')).toBe('ltr')
    expect(document.documentElement.classList.contains('rtl')).toBe(false)
  })

  it('sets dir="ltr" for French', () => {
    applyLocaleDirection('fr')
    expect(document.documentElement.getAttribute('dir')).toBe('ltr')
    expect(document.documentElement.classList.contains('rtl')).toBe(false)
  })
})

// ─── FR locale key coverage ───────────────────────────────────────────────────

describe('fr.json key coverage', () => {
  it('exists and has the same top-level sections as en.json', async () => {
    const fr = await import('@/locales/fr.json')
    expect(fr).toBeDefined()
    const frKeys = new Set(collectKeys(fr.default as Record<string, unknown>))

    const missingInFr = [...EN_KEYS].filter(k => !frKeys.has(k))
    const extraInFr = [...frKeys].filter(k => !EN_KEYS.has(k))

    expect(missingInFr, `Keys in en.json missing from fr.json: ${missingInFr.slice(0, 10).join(', ')}`).toHaveLength(0)
    expect(extraInFr, `Extra keys in fr.json not in en.json: ${extraInFr.slice(0, 10).join(', ')}`).toHaveLength(0)
  })

  it('has no empty string values (all keys are translated)', async () => {
    const fr = await import('@/locales/fr.json')
    const allPairs: [string, string][] = []
    function collectLeaves(obj: Record<string, unknown>, prefix = '') {
      for (const [k, v] of Object.entries(obj)) {
        const path = prefix ? `${prefix}.${k}` : k
        if (v !== null && typeof v === 'object') {
          collectLeaves(v as Record<string, unknown>, path)
        } else {
          allPairs.push([path, String(v)])
        }
      }
    }
    collectLeaves(fr.default as Record<string, unknown>)
    const empty = allPairs.filter(([, v]) => v.trim() === '')
    expect(empty.map(([k]) => k), 'fr.json has empty values').toHaveLength(0)
  })
})

// ─── AR locale key coverage ───────────────────────────────────────────────────

describe('ar.json key coverage', () => {
  it('exists and has the same top-level sections as en.json', async () => {
    const ar = await import('@/locales/ar.json')
    expect(ar).toBeDefined()
    const arKeys = new Set(collectKeys(ar.default as Record<string, unknown>))

    const missingInAr = [...EN_KEYS].filter(k => !arKeys.has(k))
    const extraInAr = [...arKeys].filter(k => !EN_KEYS.has(k))

    expect(missingInAr, `Keys in en.json missing from ar.json: ${missingInAr.slice(0, 10).join(', ')}`).toHaveLength(0)
    expect(extraInAr, `Extra keys in ar.json not in en.json: ${extraInAr.slice(0, 10).join(', ')}`).toHaveLength(0)
  })

  it('has no empty string values (all keys are translated)', async () => {
    const ar = await import('@/locales/ar.json')
    const allPairs: [string, string][] = []
    function collectLeaves(obj: Record<string, unknown>, prefix = '') {
      for (const [k, v] of Object.entries(obj)) {
        const path = prefix ? `${prefix}.${k}` : k
        if (v !== null && typeof v === 'object') {
          collectLeaves(v as Record<string, unknown>, path)
        } else {
          allPairs.push([path, String(v)])
        }
      }
    }
    collectLeaves(ar.default as Record<string, unknown>)
    const empty = allPairs.filter(([, v]) => v.trim() === '')
    expect(empty.map(([k]) => k), 'ar.json has empty values').toHaveLength(0)
  })
})
