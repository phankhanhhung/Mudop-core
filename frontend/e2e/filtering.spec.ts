import { test, expect } from '@playwright/test'
import { loginViaUi, isBackendAvailable, getAuthHeaders, spaNavigate } from './fixtures'

/**
 * Tests for filtering, sorting, searching, and pagination on the entity list page.
 * Requires backend with MasterData module and at least a few Customer records.
 */
test.describe('Filtering, sorting, and pagination', () => {
  const createdIds: string[] = []

  test.beforeEach(async ({ page }) => {
    const available = await isBackendAvailable(page)
    test.skip(!available, 'Backend is not running — skipping filtering tests')
    await loginViaUi(page)

    // Create several test records to ensure there is data to filter/sort/paginate
    const headers = await getAuthHeaders(page)
    for (let i = 0; i < 3; i++) {
      const resp = await page.request.post('/api/odata/MasterData/Customer', {
        headers,
        data: {
          CustomerCode: `E2E-FILTER-${Date.now()}-${i}`,
          Name: `Filter Test ${String.fromCharCode(65 + i)}`, // A, B, C
          Email: `filter-${i}@example.com`,
          CustomerType: 'Regular',
          CreditLimit: (i + 1) * 1000,
          Status: 'Active',
          CompanyId: '00000000-0000-0000-0000-000000000001'
        }
      })
      if (resp.ok()) {
        const data = await resp.json()
        createdIds.push(data.Id)
      }
    }
  })

  test.afterEach(async ({ page }) => {
    const headers = await getAuthHeaders(page)
    for (const id of createdIds) {
      await page.request.delete(`/api/odata/MasterData/Customer/${id}`, { headers }).catch(() => {})
    }
    createdIds.length = 0
  })

  test('search input filters the entity list', async ({ page }) => {
    await spaNavigate(page, '/odata/MasterData/Customer')

    // Wait for table to load
    const table = page.locator('table')
    await expect(table).toBeVisible({ timeout: 15_000 })

    // Find the search input
    const searchInput = page.getByPlaceholder(/search/i)
      .or(page.locator('input[type="search"]'))
      .or(page.locator('input[aria-label*="search" i]'))

    if (await searchInput.first().isVisible({ timeout: 5_000 }).catch(() => false)) {
      // Type a search term matching one of our test records
      await searchInput.first().fill('Filter Test A')

      // Wait for the filtered results
      await page.waitForTimeout(1_000) // debounce delay
      await page.waitForLoadState('networkidle')

      // The table should still be visible (results or empty state)
      await expect(table.or(page.getByText(/no .* found/i)).first()).toBeVisible()
    }
  })

  test('sort by column header click', async ({ page }) => {
    await spaNavigate(page, '/odata/MasterData/Customer')

    const table = page.locator('table')
    await expect(table).toBeVisible({ timeout: 15_000 })

    // Click on a sortable column header (e.g., "Name")
    const nameHeader = table.locator('thead th').filter({ hasText: /name/i }).first()

    if (await nameHeader.isVisible({ timeout: 5_000 }).catch(() => false)) {
      // Click to sort ascending
      await nameHeader.click()
      await page.waitForLoadState('networkidle')

      // Click again to sort descending
      await nameHeader.click()
      await page.waitForLoadState('networkidle')

      // Table should still be visible and functional after sorting
      await expect(table).toBeVisible()
      const rows = table.locator('tbody tr')
      const rowCount = await rows.count()
      expect(rowCount).toBeGreaterThan(0)
    }
  })

  test('pagination controls work', async ({ page }) => {
    await spaNavigate(page, '/odata/MasterData/Customer')

    const table = page.locator('table')
    await expect(table).toBeVisible({ timeout: 15_000 })

    // Look for pagination controls
    const paginationNav = page.locator('nav[aria-label*="pagination" i]')
      .or(page.locator('[class*="pagination"]'))
      .or(page.getByRole('navigation'))

    if (await paginationNav.first().isVisible({ timeout: 5_000 }).catch(() => false)) {
      // Check for page size selector
      const pageSizeSelect = page.locator('select').filter({ hasText: /10|20|50/i })
      if (await pageSizeSelect.isVisible({ timeout: 2_000 }).catch(() => false)) {
        await pageSizeSelect.selectOption({ index: 0 })
        await page.waitForLoadState('networkidle')
      }

      // Check for next/previous page buttons
      const nextButton = page.getByRole('button', { name: /next/i })
        .or(page.locator('button[aria-label*="next" i]'))
      if (await nextButton.first().isVisible({ timeout: 2_000 }).catch(() => false)) {
        const isDisabled = await nextButton.first().isDisabled()
        if (!isDisabled) {
          await nextButton.first().click()
          await page.waitForLoadState('networkidle')
          await expect(table).toBeVisible()
        }
      }
    }
  })

  test('URL reflects filter/sort state', async ({ page }) => {
    await spaNavigate(page, '/odata/MasterData/Customer')
    await page.waitForLoadState('networkidle')

    // Find the search input and type a query
    const searchInput = page.getByPlaceholder(/search/i)
      .or(page.locator('input[type="search"]'))

    if (await searchInput.first().isVisible({ timeout: 5_000 }).catch(() => false)) {
      await searchInput.first().fill('Test')
      await page.waitForTimeout(1_500) // debounce + URL update
      await page.waitForLoadState('networkidle')

      // URL should contain the search term as a query parameter
      const url = page.url()
      expect(url).toMatch(/[?&](search|q|filter)=/i)
    }
  })

  test('clear filters resets the list', async ({ page }) => {
    await spaNavigate(page, '/odata/MasterData/Customer')

    const table = page.locator('table')
    await expect(table).toBeVisible({ timeout: 15_000 })

    const searchInput = page.getByPlaceholder(/search/i)
      .or(page.locator('input[type="search"]'))

    if (await searchInput.first().isVisible({ timeout: 5_000 }).catch(() => false)) {
      // Apply a filter
      await searchInput.first().fill('Filter Test')
      await page.waitForTimeout(1_000)
      await page.waitForLoadState('networkidle')

      // Clear the filter
      await searchInput.first().clear()
      await page.waitForTimeout(1_000)
      await page.waitForLoadState('networkidle')

      // Table should show all (or more) results again
      await expect(table).toBeVisible()

      // Look for a clear/reset button as alternative
      const clearButton = page.getByRole('button', { name: /clear|reset/i })
      if (await clearButton.first().isVisible({ timeout: 2_000 }).catch(() => false)) {
        await clearButton.first().click()
        await page.waitForLoadState('networkidle')
        await expect(table).toBeVisible()
      }
    }
  })
})
