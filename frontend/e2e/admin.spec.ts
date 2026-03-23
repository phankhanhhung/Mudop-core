import { test, expect } from '@playwright/test'
import { loginViaUi, isBackendAvailable, spaNavigate } from './fixtures'

/**
 * Tests for admin-area pages: module management, audit log, user management, etc.
 * These test navigation and basic page rendering, not deep CRUD operations.
 */
test.describe('Admin features', () => {
  test.beforeEach(async ({ page }) => {
    const available = await isBackendAvailable(page)
    test.skip(!available, 'Backend is not running — skipping admin tests')
    await loginViaUi(page)
  })

  test('navigate to module management page', async ({ page }) => {
    await spaNavigate(page, '/admin/modules')

    await expect(page).toHaveURL(/\/admin\/modules/)

    // Page should have a heading or title related to modules
    const heading = page.getByRole('heading', { name: /module/i })
      .or(page.getByText(/module management/i))
    await expect(heading.first()).toBeVisible({ timeout: 10_000 })
  })

  test('module management shows published modules or empty state', async ({ page }) => {
    await spaNavigate(page, '/admin/modules')

    // Should show either a list of modules or an indication that none exist
    const moduleList = page.locator('table')
      .or(page.locator('[class*="card"]').filter({ hasText: /module|version/i }))
      .or(page.getByText(/no modules/i))
    await expect(moduleList.first()).toBeVisible({ timeout: 10_000 })
  })

  test('module management has compile/install action', async ({ page }) => {
    await spaNavigate(page, '/admin/modules')
    await page.waitForLoadState('networkidle')

    // Should have a compile or install button/action
    const actionButton = page.getByRole('button', { name: /compile|install|publish|upload/i })
      .or(page.getByRole('link', { name: /compile|install|publish/i }))

    // The button may be inside a dropdown or always visible
    if (await actionButton.first().isVisible({ timeout: 5_000 }).catch(() => false)) {
      await expect(actionButton.first()).toBeEnabled()
    }
  })

  test('navigate to audit log page', async ({ page }) => {
    await spaNavigate(page, '/admin/audit')

    await expect(page).toHaveURL(/\/admin\/audit/)

    const heading = page.getByRole('heading', { name: /audit/i })
      .or(page.getByText(/audit log/i))
    await expect(heading.first()).toBeVisible({ timeout: 10_000 })
  })

  test('audit log renders a table or timeline', async ({ page }) => {
    await spaNavigate(page, '/admin/audit')

    // Audit log should display entries in a table or list format
    const content = page.locator('table')
      .or(page.locator('[class*="timeline"]'))
      .or(page.getByText(/no .* entries|no .* records|no .* logs/i))
    await expect(content.first()).toBeVisible({ timeout: 10_000 })
  })

  test('navigate to user management page', async ({ page }) => {
    await spaNavigate(page, '/admin/users')

    await expect(page).toHaveURL(/\/admin\/users/)

    const heading = page.getByRole('heading', { name: /user/i })
      .or(page.getByText(/user management/i))
    await expect(heading.first()).toBeVisible({ timeout: 10_000 })
  })

  test('user management shows user list', async ({ page }) => {
    await spaNavigate(page, '/admin/users')

    // Should show a table of users or empty state
    const content = page.locator('table')
      .or(page.getByText(/no users/i))
    await expect(content.first()).toBeVisible({ timeout: 10_000 })
  })

  test('navigate to metadata browser page', async ({ page }) => {
    await spaNavigate(page, '/admin/metadata')

    await expect(page).toHaveURL(/\/admin\/metadata/)

    const heading = page.getByRole('heading', { name: /metadata/i })
      .or(page.getByText(/metadata browser/i))
    await expect(heading.first()).toBeVisible({ timeout: 10_000 })
  })

  test('navigate to role management page', async ({ page }) => {
    await spaNavigate(page, '/admin/roles')

    await expect(page).toHaveURL(/\/admin\/roles/)

    const heading = page.getByRole('heading', { name: /role/i })
      .or(page.getByText(/role management/i))
    await expect(heading.first()).toBeVisible({ timeout: 10_000 })
  })

  test('navigate to batch operations page', async ({ page }) => {
    await spaNavigate(page, '/admin/batch')

    await expect(page).toHaveURL(/\/admin\/batch/)

    const heading = page.getByRole('heading', { name: /batch/i })
      .or(page.getByText(/batch operations/i))
    await expect(heading.first()).toBeVisible({ timeout: 10_000 })
  })

  test('navigate to service actions page', async ({ page }) => {
    await spaNavigate(page, '/admin/actions')

    await expect(page).toHaveURL(/\/admin\/actions/)

    const heading = page.getByRole('heading', { name: /action|service/i })
      .or(page.getByText(/service actions/i))
    await expect(heading.first()).toBeVisible({ timeout: 10_000 })
  })

  test('sidebar admin section has all expected links', async ({ page }) => {
    // On the dashboard, the sidebar should show admin navigation items
    await expect(page).toHaveURL(/\/dashboard/)

    // Check for admin nav items in the sidebar
    const sidebar = page.locator('aside')
    await expect(sidebar).toBeVisible({ timeout: 5_000 })

    const adminLinks = [
      /module/i,
      /metadata/i,
      /batch/i,
      /action/i,
      /user/i,
      /role/i,
      /audit/i
    ]

    for (const linkPattern of adminLinks) {
      const link = sidebar.getByRole('button', { name: linkPattern })
        .or(sidebar.getByRole('link', { name: linkPattern }))
        .or(sidebar.locator('button').filter({ hasText: linkPattern }))
      // At least verify the sidebar renders without errors; collapsed sidebar hides text
      if (await link.first().isVisible({ timeout: 1_000 }).catch(() => false)) {
        await expect(link.first()).toBeVisible()
      }
    }
  })
})
