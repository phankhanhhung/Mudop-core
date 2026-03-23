import { createI18n } from 'vue-i18n'
import en from './locales/en.json'

export const SUPPORTED_LOCALES = [
  { code: 'en', name: 'English', dir: 'ltr' as const },
  { code: 'de', name: 'Deutsch', dir: 'ltr' as const },
  { code: 'fr', name: 'Français', dir: 'ltr' as const },
  { code: 'ar', name: 'العربية', dir: 'rtl' as const }
] as const

export type SupportedLocale = (typeof SUPPORTED_LOCALES)[number]['code']

const VALID_LOCALE_CODES: readonly string[] = SUPPORTED_LOCALES.map(l => l.code)

/** Returns the locale if it is in the supported list, otherwise 'en'. */
export function validateLocale(locale: string | null | undefined): SupportedLocale {
  return locale && VALID_LOCALE_CODES.includes(locale) ? (locale as SupportedLocale) : 'en'
}

const RTL_LOCALES = new Set(['ar', 'he', 'fa', 'ur'])

export function isRtlLocale(locale: string): boolean {
  return RTL_LOCALES.has(locale)
}

/** Apply dir attribute and RTL class to document based on locale */
export function applyLocaleDirection(locale: string): void {
  const dir = isRtlLocale(locale) ? 'rtl' : 'ltr'
  document.documentElement.setAttribute('dir', dir)
  document.documentElement.classList.toggle('rtl', dir === 'rtl')
}

const i18n = createI18n({
  legacy: false,
  locale: validateLocale(localStorage.getItem('locale')),
  fallbackLocale: 'en',
  messages: { en }
})

export default i18n
