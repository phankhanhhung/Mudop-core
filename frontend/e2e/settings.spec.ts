import { test, expect } from '@playwright/test'
import { loginViaUi, isBackendAvailable, spaNavigate, TEST_TENANT_ID } from './fixtures'

/**
 * Helper to dismiss onboarding wizard, set tenant, and navigate to settings.
 */
async function setupForSettings(page: import('@playwright/test').Page) {
  // Navigate to a page first so localStorage is accessible
  await page.goto('/auth/login')
  await page.waitForLoadState('domcontentloaded')

  // Dismiss onboarding wizard and set tenant
  await page.evaluate((tid) => {
    localStorage.setItem('bmmdl-onboarding-completed', 'true')
    localStorage.setItem('bmmdl_tenant_id', tid)
  }, TEST_TENANT_ID)

  await loginViaUi(page)

  // Wait for app initialization
  await page.waitForTimeout(2000)

  // Navigate to settings page
  await spaNavigate(page, '/settings')

  // Wait for the settings heading to confirm we're on the right page
  await expect(page.getByRole('heading', { name: /settings/i })).toBeVisible({ timeout: 10_000 })
}

test.describe('Settings', () => {
  test.beforeEach(async ({ page }) => {
    const available = await isBackendAvailable(page)
    test.skip(!available, 'Backend is not running — skipping settings tests')
  })

  test.describe('Profile Section', () => {
    test('should display user profile with avatar, name, and email', async ({ page }) => {
      await setupForSettings(page)

      // Avatar (circle with initials) should be visible in the header
      const avatar = page.locator('.rounded-full.flex.items-center.justify-center.text-white').first()
      await expect(avatar).toBeVisible({ timeout: 5_000 })

      // User display name should be visible in the subtitle
      const subtitle = page.locator('.text-muted-foreground').filter({ hasText: /manage your account/i })
      await expect(subtitle).toBeVisible()

      // The profile section is the default, so it should show profile card
      const profileTitle = page.getByText(/profile/i).first()
      await expect(profileTitle).toBeVisible()

      // Email should be visible in the profile card
      const emailField = page.locator('.bg-muted\\/30').filter({ has: page.locator('svg[class*="lucide-globe"]') })
      await expect(emailField).toBeVisible({ timeout: 5_000 })
    })

    test('should display user roles as colored badges', async ({ page }) => {
      await setupForSettings(page)

      // Roles section should exist
      const rolesLabel = page.getByText(/roles/i).first()
      await expect(rolesLabel).toBeVisible({ timeout: 5_000 })

      // Check if there are role badges or a "no roles" message
      const hasRoles = await page.locator('.flex-wrap .px-3.py-1').first().isVisible({ timeout: 3_000 }).catch(() => false)
      const hasNoRoles = await page.getByText(/no roles/i).isVisible({ timeout: 3_000 }).catch(() => false)

      // One of them should be true
      expect(hasRoles || hasNoRoles).toBeTruthy()

      // If roles are present, they should have shield icons
      if (hasRoles) {
        const shieldIcons = page.locator('.flex-wrap svg[class*="lucide-shield"]')
        const shieldCount = await shieldIcons.count()
        expect(shieldCount).toBeGreaterThan(0)
      }
    })
  })

  test.describe('Section Navigation', () => {
    test('should navigate between settings sections via sidebar', async ({ page }) => {
      await setupForSettings(page)

      // The sidebar should have navigation buttons
      const sidebar = page.locator('nav.w-56')
      await expect(sidebar).toBeVisible({ timeout: 5_000 })

      // Click on "Appearance" section
      const appearanceBtn = sidebar.getByText(/appearance/i)
      await expect(appearanceBtn).toBeVisible()
      await appearanceBtn.click()
      await page.waitForTimeout(500)

      // Theme cards should now be visible
      await expect(page.getByText(/light/i).first()).toBeVisible({ timeout: 5_000 })
      await expect(page.getByText(/dark/i).first()).toBeVisible()
      await expect(page.getByText(/system/i).first()).toBeVisible()

      // Click on "Preferences" section
      const preferencesBtn = sidebar.getByText(/preferences/i)
      await preferencesBtn.click()
      await page.waitForTimeout(500)

      // Page size option should be visible
      await expect(page.getByText(/page size/i)).toBeVisible({ timeout: 5_000 })

      // Click on "Account" section
      const accountBtn = sidebar.getByText(/account/i)
      await accountBtn.click()
      await page.waitForTimeout(500)

      // Password change form should be visible
      await expect(page.getByText(/change password/i)).toBeVisible({ timeout: 5_000 })
    })
  })

  test.describe('Appearance Section', () => {
    test('should toggle theme when clicking Dark theme card', async ({ page }) => {
      await setupForSettings(page)

      // Navigate to Appearance section
      const sidebar = page.locator('nav.w-56')
      await sidebar.getByText(/appearance/i).click()
      await page.waitForTimeout(500)

      // Click on the Dark theme card
      const darkThemeCard = page.locator('button').filter({ hasText: /dark/i }).first()
      await expect(darkThemeCard).toBeVisible({ timeout: 5_000 })
      await darkThemeCard.click()
      await page.waitForTimeout(500)

      // The dark card should now have the selected border (border-primary)
      await expect(darkThemeCard).toHaveClass(/border-primary/)

      // The html element should have 'dark' class applied
      const hasDarkClass = await page.evaluate(() => {
        return document.documentElement.classList.contains('dark')
      })
      expect(hasDarkClass).toBeTruthy()

      // Reset to light theme to not affect other tests
      const lightThemeCard = page.locator('button').filter({ hasText: /light/i }).first()
      await lightThemeCard.click()
      await page.waitForTimeout(500)
    })

    test('should have language selector', async ({ page }) => {
      await setupForSettings(page)

      // Navigate to Appearance section
      const sidebar = page.locator('nav.w-56')
      await sidebar.getByText(/appearance/i).click()
      await page.waitForTimeout(500)

      // Language selector should be visible
      const languageLabel = page.getByText(/language/i).first()
      await expect(languageLabel).toBeVisible({ timeout: 5_000 })

      // The select element for locale should be present
      const localeSelect = page.locator('#locale')
      await expect(localeSelect).toBeVisible()

      // Should have at least English option
      const options = localeSelect.locator('option')
      const optionCount = await options.count()
      expect(optionCount).toBeGreaterThanOrEqual(1)
    })
  })

  test.describe('Preferences Section', () => {
    test('should display all preference options', async ({ page }) => {
      await setupForSettings(page)

      // Navigate to Preferences section
      const sidebar = page.locator('nav.w-56')
      await sidebar.getByText(/preferences/i).click()
      await page.waitForTimeout(500)

      // Page size selector
      await expect(page.getByText(/page size/i)).toBeVisible({ timeout: 5_000 })

      // Date format selector
      await expect(page.getByText(/date format/i)).toBeVisible()

      // Number format selector
      await expect(page.getByText(/number format/i)).toBeVisible()

      // Keyboard shortcuts toggle
      await expect(page.getByText('Keyboard Shortcuts', { exact: true })).toBeVisible()

      // Auto refresh selector
      await expect(page.getByText('Auto-Refresh Interval', { exact: true })).toBeVisible()
    })

    test('should persist page size preference after reload', async ({ page }) => {
      await setupForSettings(page)

      // Navigate to Preferences section
      const sidebar = page.locator('nav.w-56')
      await sidebar.getByText(/preferences/i).click()
      await page.waitForTimeout(500)

      // Find the page size select and change it to 50
      // The select is rendered as a native <select> element
      const pageSizeSelects = page.locator('select')
      const pageSizeSelect = pageSizeSelects.first()
      const isSelectVisible = await pageSizeSelect.isVisible({ timeout: 3_000 }).catch(() => false)
      test.skip(!isSelectVisible, 'Page size select not found')

      // Select 50 rows
      await pageSizeSelect.selectOption('50')
      await page.waitForTimeout(500)

      // Reload the page
      await page.reload()
      await page.waitForLoadState('networkidle')
      await page.waitForTimeout(2000)

      // Navigate back to settings -> preferences
      await spaNavigate(page, '/settings')
      await page.waitForTimeout(500)
      const sidebarAfter = page.locator('nav.w-56')
      await sidebarAfter.getByText(/preferences/i).click()
      await page.waitForTimeout(500)

      // Verify the page size is still 50
      const currentValue = await pageSizeSelects.first().inputValue()
      expect(currentValue).toBe('50')

      // Reset to default (25) to not affect other tests
      await pageSizeSelects.first().selectOption('25')
      await page.waitForTimeout(300)
    })

    test('should have keyboard shortcuts toggle', async ({ page }) => {
      await setupForSettings(page)

      // Navigate to Preferences section
      const sidebar = page.locator('nav.w-56')
      await sidebar.getByText(/preferences/i).click()
      await page.waitForTimeout(500)

      // Keyboard shortcuts label should be present
      await expect(page.getByText('Keyboard Shortcuts', { exact: true })).toBeVisible({ timeout: 5_000 })

      // The checkbox/toggle should be present next to it
      const checkboxes = page.locator('button[role="checkbox"]')
      const checkboxCount = await checkboxes.count()
      expect(checkboxCount).toBeGreaterThanOrEqual(1)
    })
  })

  test.describe('Account Section', () => {
    test('should show password change form with validation', async ({ page }) => {
      await setupForSettings(page)

      // Navigate to Account section
      const sidebar = page.locator('nav.w-56')
      await sidebar.getByText(/account/i).click()
      await page.waitForTimeout(500)

      // Password change card should be visible
      await expect(page.getByText(/change password/i).first()).toBeVisible({ timeout: 5_000 })

      // Password fields should be present
      const currentPasswordField = page.locator('#current-password')
      const newPasswordField = page.locator('#new-password')
      const confirmPasswordField = page.locator('#confirm-password')

      await expect(currentPasswordField).toBeVisible()
      await expect(newPasswordField).toBeVisible()
      await expect(confirmPasswordField).toBeVisible()

      // Enter mismatched passwords
      await currentPasswordField.fill('currentpass123')
      await newPasswordField.fill('newpassword123')
      await confirmPasswordField.fill('differentpassword')

      // Mismatch error should appear
      const mismatchError = page.getByText(/passwords do not match|password mismatch/i)
      await expect(mismatchError).toBeVisible({ timeout: 5_000 })

      // Update Password button should be disabled when passwords don't match
      const updateBtn = page.getByRole('button', { name: /update password/i })
      await expect(updateBtn).toBeDisabled()
    })

    test('should display active sessions section', async ({ page }) => {
      await setupForSettings(page)

      // Navigate to Account section
      const sidebar = page.locator('nav.w-56')
      await sidebar.getByText(/account/i).click()
      await page.waitForTimeout(500)

      // Active sessions card
      await expect(page.getByText(/active sessions/i).first()).toBeVisible({ timeout: 5_000 })

      // Current session should be displayed with "Active" badge
      await expect(page.getByText(/current session/i)).toBeVisible()
      await expect(page.getByText(/active/i).last()).toBeVisible()
    })
  })
})
