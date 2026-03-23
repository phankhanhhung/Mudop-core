import { createApp } from 'vue'
import { createPinia } from 'pinia'
import router from './router'
import i18n, { applyLocaleDirection, validateLocale } from './i18n'
import App from './App.vue'

import './assets/css/main.css'

// Create app
const app = createApp(App)

// Use plugins
app.use(createPinia())
app.use(router)
app.use(i18n)

// Initialize stores before mounting
import { useAuthStore } from './stores/auth'
import { useTenantStore } from './stores/tenant'
import { useMetadataStore } from './stores/metadata'
import { useUiStore } from './stores/ui'

const authStore = useAuthStore()
const tenantStore = useTenantStore()
const metadataStore = useMetadataStore()
const uiStore = useUiStore()

// Initialize UI theme and locale direction
uiStore.initTheme()
applyLocaleDirection(i18n.global.locale.value)

// Load stored non-English locale messages so that refreshing with a
// saved locale (e.g. AR or FR) shows the correct translations immediately,
// then initialize auth, then mount the app.
async function loadLocale(): Promise<void> {
  const storedLocale = validateLocale(i18n.global.locale.value)
  if (storedLocale !== i18n.global.locale.value) {
    // Stored locale was invalid — reset to validated value
    ;(i18n.global.locale as any).value = storedLocale
    localStorage.setItem('locale', storedLocale)
    applyLocaleDirection(storedLocale)
  }
  if (storedLocale === 'en') return
  try {
    const messages = await import(`./locales/${storedLocale}.json`)
    i18n.global.setLocaleMessage(storedLocale, messages.default)
  } catch {
    // Locale file missing — fall back to English silently
    localStorage.removeItem('locale')
    ;(i18n.global.locale as any).value = 'en'
    applyLocaleDirection('en')
  }
}

loadLocale()
  .then(() => authStore.initialize())
  .then(async () => {
    // If the user was restored, load tenants + metadata in the background.
    // Failures here are non-fatal — the user is still authenticated.
    if (authStore.isAuthenticated) {
      try {
        await tenantStore.fetchTenants()
        // Auto-select first tenant if none persisted
        if (!tenantStore.hasTenant && tenantStore.tenants.length > 0) {
          tenantStore.selectTenant(tenantStore.tenants[0])
        }
        await metadataStore.fetchModules()
      } catch {
        // Non-fatal: user may not have tenants yet
      }
    }
  })
  .finally(() => {
    // Mount app after initialization
    app.mount('#app')
  })
