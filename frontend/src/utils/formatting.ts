import i18n from '@/i18n'
import { usePreferences, type DateFormat, type NumberFormat } from '@/utils/preferences'

/** Get the current i18n locale (BCP 47 tag) */
function currentLocale(): string {
  return i18n.global.locale.value || 'en'
}

/** Map dateFormat preference to Intl options */
function dateFormatOptions(pref: DateFormat): Intl.DateTimeFormatOptions {
  switch (pref) {
    case 'us':
      return { year: 'numeric', month: '2-digit', day: '2-digit' } // MM/DD/YYYY
    case 'eu':
      return { year: 'numeric', month: '2-digit', day: '2-digit' } // DD.MM.YYYY or DD/MM/YYYY
    case 'iso':
    default:
      return { year: 'numeric', month: '2-digit', day: '2-digit' } // YYYY-MM-DD
  }
}

/** Get locale string for date formatting based on preference */
function dateLocale(pref: DateFormat): string {
  switch (pref) {
    case 'us':
      return 'en-US'
    case 'eu':
      return 'de-DE'
    case 'iso':
      return 'sv-SE' // Swedish locale produces ISO-like YYYY-MM-DD
    default:
      return currentLocale()
  }
}

/** Get locale string for number formatting based on preference */
function numberLocale(pref: NumberFormat): string {
  switch (pref) {
    case 'de':
      return 'de-DE' // 1.234,56
    case 'en':
    default:
      return 'en-US' // 1,234.56
  }
}

/** RTL languages */
const RTL_LOCALES = new Set(['ar', 'he', 'fa', 'ur'])

export function isRtl(locale?: string): boolean {
  return RTL_LOCALES.has(locale ?? currentLocale())
}

// ─── Date / Time ──────────────────────────────────────────────────────────

export function formatDate(value: string | Date | null | undefined): string {
  if (!value) return '-'
  const d = value instanceof Date ? value : new Date(value)
  if (isNaN(d.getTime())) return String(value)
  const { preferences } = usePreferences()
  const pref = preferences.value.dateFormat
  return new Intl.DateTimeFormat(dateLocale(pref), dateFormatOptions(pref)).format(d)
}

export function formatDateTime(value: string | Date | null | undefined): string {
  if (!value) return '-'
  const d = value instanceof Date ? value : new Date(value)
  if (isNaN(d.getTime())) return String(value)
  const { preferences } = usePreferences()
  const pref = preferences.value.dateFormat
  return new Intl.DateTimeFormat(dateLocale(pref), {
    ...dateFormatOptions(pref),
    hour: '2-digit',
    minute: '2-digit'
  }).format(d)
}

export function formatTime(value: string | Date | null | undefined): string {
  if (!value) return '-'
  const d = value instanceof Date ? value : new Date(value)
  if (isNaN(d.getTime())) return String(value)
  const { preferences } = usePreferences()
  const pref = preferences.value.dateFormat
  return new Intl.DateTimeFormat(dateLocale(pref), {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  }).format(d)
}

export function formatRelativeTime(value: string | Date | null | undefined): string {
  if (!value) return '-'
  const d = value instanceof Date ? value : new Date(value)
  if (isNaN(d.getTime())) return String(value)

  const now = Date.now()
  const diffMs = now - d.getTime()
  const diffSec = Math.floor(diffMs / 1000)
  const diffMin = Math.floor(diffSec / 60)
  const diffHr = Math.floor(diffMin / 60)
  const diffDay = Math.floor(diffHr / 24)

  const { preferences } = usePreferences()
  const rtf = new Intl.RelativeTimeFormat(dateLocale(preferences.value.dateFormat), { numeric: 'auto' })

  if (diffSec < 60) return rtf.format(-diffSec, 'second')
  if (diffMin < 60) return rtf.format(-diffMin, 'minute')
  if (diffHr < 24) return rtf.format(-diffHr, 'hour')
  if (diffDay < 30) return rtf.format(-diffDay, 'day')
  return formatDate(d)
}

// ─── Numbers ──────────────────────────────────────────────────────────────

export function formatNumber(
  value: number | null | undefined,
  options?: Intl.NumberFormatOptions
): string {
  if (value === null || value === undefined) return '-'
  const { preferences } = usePreferences()
  return new Intl.NumberFormat(numberLocale(preferences.value.numberFormat), options).format(value)
}

export function formatInteger(value: number | null | undefined): string {
  return formatNumber(value, { maximumFractionDigits: 0 })
}

export function formatDecimal(
  value: number | null | undefined,
  fractionDigits = 2
): string {
  return formatNumber(value, {
    minimumFractionDigits: fractionDigits,
    maximumFractionDigits: fractionDigits
  })
}

export function formatCurrency(
  value: number | null | undefined,
  currency = 'USD'
): string {
  if (value === null || value === undefined) return '-'
  const { preferences } = usePreferences()
  return new Intl.NumberFormat(numberLocale(preferences.value.numberFormat), {
    style: 'currency',
    currency
  }).format(value)
}

export function formatPercent(value: number | null | undefined): string {
  if (value === null || value === undefined) return '-'
  const { preferences } = usePreferences()
  return new Intl.NumberFormat(numberLocale(preferences.value.numberFormat), {
    style: 'percent',
    minimumFractionDigits: 0,
    maximumFractionDigits: 1
  }).format(value)
}

// ─── File Size (locale-aware) ─────────────────────────────────────────────

export function formatFileSize(bytes: number | null | undefined): string {
  if (bytes === null || bytes === undefined || bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  const num = bytes / Math.pow(k, i)
  return `${formatNumber(num, { maximumFractionDigits: 1 })} ${sizes[i]}`
}

// ─── Boolean ──────────────────────────────────────────────────────────────

export function formatBoolean(value: unknown): string {
  if (value === null || value === undefined) return '-'
  const { t } = i18n.global
  return value ? t('common.yes') : t('common.no')
}
