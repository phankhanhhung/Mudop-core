import { test as base, expect, type Page } from '@playwright/test'

/**
 * Shared test credentials and configuration.
 * These match the dev environment defaults.
 */
export const TEST_USER = {
  email: 'phankhanhhung@gmail.com',
  password: 'a'
}

export const TEST_TENANT_ID = '675fbd5a-965b-4369-a95e-ebd1ecb303f4'

/**
 * Login via the UI login form and wait for dashboard redirect.
 */
export async function loginViaUi(page: Page, credentials = TEST_USER) {
  await page.goto('/auth/login')
  await page.waitForLoadState('domcontentloaded')

  // Pre-set tenant in localStorage so the login flow restores it
  await page.evaluate((tid) => {
    localStorage.setItem('bmmdl_tenant_id', tid)
  }, TEST_TENANT_ID)

  await page.getByPlaceholder('name@example.com').fill(credentials.email)
  await page.getByPlaceholder(/password/i).fill(credentials.password)
  await page.getByRole('button', { name: /sign in/i }).click()

  await page.waitForURL(/\/dashboard/, { timeout: 15_000 })
  await page.waitForLoadState('networkidle')
}

/**
 * Get auth headers for direct API calls in tests.
 */
export async function getAuthHeaders(page: Page) {
  const token = await page.evaluate(() => localStorage.getItem('bmmdl_access_token'))
  return {
    Authorization: `Bearer ${token}`,
    'X-Tenant-Id': TEST_TENANT_ID
  }
}

/**
 * Navigate within the SPA using Vue Router (avoids full page reload).
 */
export async function spaNavigate(page: Page, path: string) {
  await page.evaluate((p) => {
    // @ts-expect-error __vue_app__ is internal
    const app = document.getElementById('app')?.__vue_app__
    if (app) {
      app.config.globalProperties.$router.push(p)
    }
  }, path)
  await page.waitForTimeout(500)
  await page.waitForLoadState('networkidle')
}

/**
 * Check whether the backend is reachable. Returns true if /api responds.
 */
export async function isBackendAvailable(page: Page): Promise<boolean> {
  try {
    const resp = await page.request.get('/api/auth/me', {
      timeout: 3_000
    })
    // Any response (even 401) means the backend is up
    return resp.status() > 0
  } catch {
    return false
  }
}

/**
 * Extended test fixture that provides an already-authenticated page.
 * Tests using `authenticatedTest` get a `page` that is already logged in.
 */
export const authenticatedTest = base.extend<{ authenticatedPage: Page }>({
  authenticatedPage: async ({ page }, use) => {
    await loginViaUi(page)
    await use(page)
  }
})

export { expect }
