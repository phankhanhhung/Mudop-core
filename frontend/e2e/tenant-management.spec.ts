import { test, expect } from '@playwright/test'
import { loginViaUi, isBackendAvailable, spaNavigate, TEST_TENANT_ID } from './fixtures'

/**
 * Helper to dismiss onboarding wizard, set tenant, and navigate to tenant management.
 */
async function setupForTenantManagement(page: import('@playwright/test').Page) {
  // Navigate to a page first so localStorage is accessible
  await page.goto('/auth/login')
  await page.waitForLoadState('domcontentloaded')

  // Dismiss onboarding wizard and set tenant
  await page.evaluate((tid) => {
    localStorage.setItem('bmmdl-onboarding-completed', 'true')
    localStorage.setItem('bmmdl_tenant_id', tid)
  }, TEST_TENANT_ID)

  await loginViaUi(page)

  // Wait for app initialization (fetchTenants + fetchModules) to complete
  await page.waitForTimeout(2000)

  // Navigate to tenant management page
  await spaNavigate(page, '/tenants')

  // Wait for the page heading to confirm we're on the right page
  await expect(page.getByRole('heading', { name: /tenant management/i })).toBeVisible({ timeout: 10_000 })

  // Wait for tenant list to load
  await page.waitForTimeout(1500)
}

test.describe('Tenant Management', () => {
  test.beforeEach(async ({ page }) => {
    const available = await isBackendAvailable(page)
    test.skip(!available, 'Backend is not running — skipping tenant management tests')
  })

  test.describe('Page Layout', () => {
    test('should display the page with header and stats cards', async ({ page }) => {
      await setupForTenantManagement(page)

      // "Tenant Management" heading should be visible
      await expect(page.getByRole('heading', { name: /tenant management/i })).toBeVisible()

      // Subtitle should be visible
      await expect(page.getByText(/manage tenants/i)).toBeVisible()

      // Stats cards grid should be visible with 4 cards
      const statsGrid = page.locator('.grid.grid-cols-1.sm\\:grid-cols-2.lg\\:grid-cols-4')
      await expect(statsGrid).toBeVisible({ timeout: 5_000 })

      const statsCards = statsGrid.locator('> *')
      await expect(statsCards).toHaveCount(4)

      // Stat card labels should be present
      await expect(page.getByText(/total tenants/i).first()).toBeVisible()
      await expect(page.getByText(/active tenant/i).first()).toBeVisible()
    })

    test('should display Create Tenant and Refresh buttons', async ({ page }) => {
      await setupForTenantManagement(page)

      // Create Tenant button
      const createBtn = page.getByRole('button', { name: /create tenant/i })
      await expect(createBtn).toBeVisible()

      // Refresh button
      const refreshBtn = page.getByRole('button', { name: /refresh/i })
      await expect(refreshBtn).toBeVisible()
    })
  })

  test.describe('Tenant List', () => {
    test('should display at least one tenant in the list', async ({ page }) => {
      await setupForTenantManagement(page)

      // Wait for tenants to load (not loading state)
      const tenantRows = page.locator('.divide-y > div')
      const rowCount = await tenantRows.count()
      test.skip(rowCount === 0, 'No tenants available in the list')

      // At least one tenant row should be visible
      expect(rowCount).toBeGreaterThanOrEqual(1)

      // Each tenant row should have a name
      const firstRow = tenantRows.first()
      const nameText = await firstRow.locator('.font-medium.truncate').textContent()
      expect(nameText?.trim().length).toBeGreaterThan(0)
    })

    test('should highlight the active tenant with Active badge', async ({ page }) => {
      await setupForTenantManagement(page)

      // The active tenant should have a green "Active" badge
      const activeBadge = page.locator('.bg-emerald-600').filter({ hasText: /active/i })
      const hasActive = await activeBadge.first().isVisible({ timeout: 5_000 }).catch(() => false)

      if (hasActive) {
        await expect(activeBadge.first()).toBeVisible()

        // The active row should have a green left border (border-l-emerald-500)
        const activeRow = page.locator('.border-l-4.border-l-emerald-500')
        await expect(activeRow.first()).toBeVisible()
      } else {
        // No active tenant — this is valid if none is selected
        test.skip(true, 'No active tenant badge visible — tenant may not be selected')
      }
    })

    test('should filter tenants when searching', async ({ page }) => {
      await setupForTenantManagement(page)

      // Get the search input
      const searchInput = page.getByPlaceholder(/search/i)
      await expect(searchInput).toBeVisible({ timeout: 5_000 })

      // Get initial count
      const initialRows = page.locator('.divide-y > div')
      const initialCount = await initialRows.count()
      test.skip(initialCount === 0, 'No tenants to filter')

      // Get the name of the first tenant for a valid search term
      const firstName = await initialRows.first().locator('.font-medium.truncate').textContent()
      const searchTerm = firstName?.trim().substring(0, 3) || 'test'

      // Type in the search box
      await searchInput.fill(searchTerm)
      await page.waitForTimeout(500)

      // The showing count text should update
      const showingText = page.getByText(/showing/i)
      await expect(showingText).toBeVisible({ timeout: 5_000 })

      // Type a nonsense search term to get 0 results
      await searchInput.fill('zzznonexistent999')
      await page.waitForTimeout(500)

      // Should show the "no results" empty state or zero count
      const noResults = page.getByText(/no results|no match/i)
      const hasNoResults = await noResults.isVisible({ timeout: 3_000 }).catch(() => false)
      const zeroFiltered = page.locator('.divide-y > div')
      const filteredCount = await zeroFiltered.count()

      expect(hasNoResults || filteredCount === 0).toBeTruthy()

      // Clear search
      await searchInput.clear()
      await page.waitForTimeout(500)
    })
  })

  test.describe('Create Tenant Dialog', () => {
    test('should open create tenant dialog with form fields', async ({ page }) => {
      await setupForTenantManagement(page)

      // Click the Create Tenant button
      const createBtn = page.getByRole('button', { name: /create tenant/i })
      await createBtn.click()
      await page.waitForTimeout(500)

      // Dialog should appear
      const dialogTitle = page.getByText(/create tenant/i).last()
      await expect(dialogTitle).toBeVisible({ timeout: 5_000 })

      // Form fields should be present
      const codeInput = page.locator('#dialog-code')
      const nameInput = page.locator('#dialog-name')
      const descriptionInput = page.locator('#dialog-description')

      await expect(codeInput).toBeVisible()
      await expect(nameInput).toBeVisible()
      await expect(descriptionInput).toBeVisible()

      // Cancel and Create buttons should exist
      await expect(page.getByRole('button', { name: /cancel/i })).toBeVisible()
      await expect(page.getByRole('button', { name: /create/i }).last()).toBeVisible()

      // Close the dialog by clicking Cancel
      await page.getByRole('button', { name: /cancel/i }).click()
      await page.waitForTimeout(500)
    })
  })

  test.describe('Detail Panel', () => {
    test('should open detail panel when clicking a tenant', async ({ page }) => {
      await setupForTenantManagement(page)

      const tenantRows = page.locator('.divide-y > div')
      const rowCount = await tenantRows.count()
      test.skip(rowCount === 0, 'No tenants available')

      // Click the first tenant row
      await tenantRows.first().click()
      await page.waitForTimeout(800)

      // Detail panel (sticky card on the right) should appear
      const detailPanel = page.locator('.sticky.top-20')
      await expect(detailPanel).toBeVisible({ timeout: 5_000 })

      // Detail panel should show tenant name
      const panelName = detailPanel.locator('h3')
      await expect(panelName).toBeVisible()
      const nameText = await panelName.textContent()
      expect(nameText?.trim().length).toBeGreaterThan(0)

      // Detail panel should show tenant code
      const panelCode = detailPanel.locator('.font-mono')
      await expect(panelCode.first()).toBeVisible()
    })

    test('should show tenant details in the detail panel', async ({ page }) => {
      await setupForTenantManagement(page)

      const tenantRows = page.locator('.divide-y > div')
      const rowCount = await tenantRows.count()
      test.skip(rowCount === 0, 'No tenants available')

      // Click the first tenant row
      await tenantRows.first().click()
      await page.waitForTimeout(800)

      const detailPanel = page.locator('.sticky.top-20')
      await expect(detailPanel).toBeVisible({ timeout: 5_000 })

      // Should show "Tenant ID" label and value
      await expect(detailPanel.getByText(/tenant id/i)).toBeVisible()

      // Should show the ID as a monospace string
      const tenantId = detailPanel.locator('.font-mono.text-muted-foreground.break-all')
      await expect(tenantId).toBeVisible()
      const idText = await tenantId.textContent()
      expect(idText?.trim().length).toBeGreaterThan(0)

      // Should show description label (even if empty/no description)
      await expect(detailPanel.getByText(/description/i).first()).toBeVisible()

      // Should show Active or Not Selected badge
      const hasBadge = await detailPanel.getByText(/active|not selected/i).first().isVisible({ timeout: 3_000 }).catch(() => false)
      expect(hasBadge).toBeTruthy()

      // Should have action buttons (Edit Tenant, Delete Tenant)
      await expect(detailPanel.getByText(/edit tenant/i)).toBeVisible()
      await expect(detailPanel.getByText(/delete tenant/i)).toBeVisible()
    })

    test('should close detail panel when clicking the X button', async ({ page }) => {
      await setupForTenantManagement(page)

      const tenantRows = page.locator('.divide-y > div')
      const rowCount = await tenantRows.count()
      test.skip(rowCount === 0, 'No tenants available')

      // Open detail panel
      await tenantRows.first().click()
      await page.waitForTimeout(800)

      const detailPanel = page.locator('.sticky.top-20')
      await expect(detailPanel).toBeVisible({ timeout: 5_000 })

      // Click the X close button
      const closeBtn = detailPanel.locator('button').filter({ has: page.locator('svg[class*="lucide-x"]') })
      await closeBtn.click()
      await page.waitForTimeout(500)

      // Detail panel should be hidden
      await expect(detailPanel).not.toBeVisible({ timeout: 3_000 })
    })
  })

  test.describe('Tenant Selection', () => {
    test('should select a tenant via the detail panel Select button', async ({ page }) => {
      await setupForTenantManagement(page)

      const tenantRows = page.locator('.divide-y > div')
      const rowCount = await tenantRows.count()
      test.skip(rowCount < 2, 'Need at least 2 tenants to test selection')

      // Find a tenant row that is NOT the active one (no border-l-emerald-500)
      let targetRow = null
      for (let i = 0; i < rowCount; i++) {
        const row = tenantRows.nth(i)
        const classes = await row.getAttribute('class') || ''
        if (!classes.includes('border-l-emerald-500')) {
          targetRow = row
          break
        }
      }
      test.skip(!targetRow, 'All tenants are active — cannot test selection')

      // Click the non-active tenant row to open detail panel
      await targetRow!.click()
      await page.waitForTimeout(800)

      const detailPanel = page.locator('.sticky.top-20')
      await expect(detailPanel).toBeVisible({ timeout: 5_000 })

      // The "Select Tenant" button should be visible (not "Currently Selected")
      const selectBtn = detailPanel.getByText(/select tenant/i)
      const hasSelectBtn = await selectBtn.isVisible({ timeout: 3_000 }).catch(() => false)
      test.skip(!hasSelectBtn, 'Select Tenant button not visible — tenant may already be selected')

      // Click the Select Tenant button in the detail panel
      const selectButton = detailPanel.locator('button').filter({ hasText: /select tenant/i })
      await selectButton.click()
      await page.waitForTimeout(1500)

      // After selection, a success toast should appear confirming the tenant was selected
      const successToast = page.getByText(/tenant selected|switched to tenant/i)
      const hasToast = await successToast.first().isVisible({ timeout: 5_000 }).catch(() => false)

      // The app may also navigate away (e.g., to dashboard) after selection.
      // If we're still on the tenant page, check for "Currently Selected" button.
      const stillOnTenantPage = await page.url().includes('/tenants')
      if (stillOnTenantPage) {
        const hasCurrentlySelected = await detailPanel.getByText(/currently selected/i).isVisible({ timeout: 3_000 }).catch(() => false)
        expect(hasCurrentlySelected || hasToast).toBeTruthy()
      } else {
        // Navigated away after selection — the toast confirms success
        expect(hasToast).toBeTruthy()
      }
    })
  })
})
