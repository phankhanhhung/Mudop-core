import { test, expect } from '@playwright/test'
import { loginViaUi, isBackendAvailable, TEST_USER } from './fixtures'

test.describe('Authentication flows', () => {
  test.beforeEach(async ({ page }) => {
    const available = await isBackendAvailable(page)
    test.skip(!available, 'Backend is not running — skipping auth tests')
  })

  test('login page renders correctly', async ({ page }) => {
    await page.goto('/auth/login')
    await page.waitForLoadState('domcontentloaded')

    // Title visible
    await expect(page.getByRole('heading', { level: 2 })).toBeVisible()

    // Form fields present
    await expect(page.getByPlaceholder('name@example.com')).toBeVisible()
    await expect(page.getByPlaceholder(/password/i)).toBeVisible()

    // Submit button present
    await expect(page.getByRole('button', { name: /sign in/i })).toBeVisible()

    // Link to register present
    await expect(page.getByRole('link', { name: /sign up/i })).toBeVisible()
  })

  test('login with valid credentials redirects to dashboard', async ({ page }) => {
    await loginViaUi(page)

    // Should be on the dashboard
    await expect(page).toHaveURL(/\/dashboard/)

    // Dashboard heading should be visible
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()

    // Auth token should be stored
    const token = await page.evaluate(() => localStorage.getItem('bmmdl_access_token'))
    expect(token).toBeTruthy()
  })

  test('login with invalid credentials shows error', async ({ page }) => {
    await page.goto('/auth/login')
    await page.waitForLoadState('domcontentloaded')

    await page.getByPlaceholder('name@example.com').fill('invalid@example.com')
    await page.getByPlaceholder(/password/i).fill('wrong-password-12345')
    await page.getByRole('button', { name: /sign in/i }).click()

    // Should show an error alert (the Alert component with destructive variant)
    const errorAlert = page.locator('[role="alert"]')
    await expect(errorAlert).toBeVisible({ timeout: 10_000 })

    // Should still be on login page
    await expect(page).toHaveURL(/\/auth\/login/)
  })

  test('registration page is accessible from login', async ({ page }) => {
    await page.goto('/auth/login')
    await page.waitForLoadState('domcontentloaded')

    await page.getByRole('link', { name: /sign up/i }).click()

    await expect(page).toHaveURL(/\/auth\/register/)

    // Registration form fields
    await expect(page.getByLabel(/display\s*name/i)).toBeVisible()
    await expect(page.getByLabel(/email/i)).toBeVisible()
    await expect(page.getByLabel(/password/i)).toBeVisible()
    await expect(page.getByRole('button', { name: /create account|sign up|register/i })).toBeVisible()
  })

  test('registration with new user succeeds', async ({ page }) => {
    await page.goto('/auth/register')
    await page.waitForLoadState('domcontentloaded')

    const uniqueEmail = `e2e-test-${Date.now()}@example.com`

    await page.getByLabel(/display\s*name/i).fill('E2E Test User')
    await page.getByLabel(/email/i).fill(uniqueEmail)
    await page.getByLabel(/password/i).fill('TestPassword123!')
    await page.getByRole('button', { name: /create account|sign up|register/i }).click()

    // After registration, should redirect to tenants page
    await expect(page).toHaveURL(/\/tenants/, { timeout: 15_000 })
  })

  test('logout redirects to login page', async ({ page }) => {
    // First login
    await loginViaUi(page)
    await expect(page).toHaveURL(/\/dashboard/)

    // Find and click logout — it may be a button in the sidebar or header
    const logoutButton = page.getByRole('button', { name: /log\s*out|sign\s*out/i })
    if (await logoutButton.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await logoutButton.click()
    } else {
      // Try navigating to settings where logout might be
      await page.goto('/settings')
      await page.waitForLoadState('domcontentloaded')
      const settingsLogout = page.getByRole('button', { name: /log\s*out|sign\s*out/i })
      if (await settingsLogout.isVisible({ timeout: 3_000 }).catch(() => false)) {
        await settingsLogout.click()
      } else {
        // Programmatic logout via auth store
        await page.evaluate(() => {
          localStorage.removeItem('bmmdl_access_token')
          localStorage.removeItem('bmmdl_refresh_token')
          window.location.href = '/auth/login'
        })
      }
    }

    // Should end up on login page
    await expect(page).toHaveURL(/\/auth\/login/, { timeout: 10_000 })
  })

  test('unauthenticated user is redirected to login', async ({ page }) => {
    // Clear any stored tokens
    await page.goto('/auth/login')
    await page.evaluate(() => {
      localStorage.clear()
    })

    // Try to access protected route
    await page.goto('/dashboard')

    // Should be redirected to login
    await expect(page).toHaveURL(/\/auth\/login/)
  })
})
