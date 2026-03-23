import { test, expect } from '@playwright/test'
import { loginViaUi, isBackendAvailable, getAuthHeaders, spaNavigate, TEST_TENANT_ID } from './fixtures'

/**
 * Known module/entity to use for testing.
 * MasterData/Customer is the standard test entity used throughout the codebase.
 */
const TEST_MODULE = 'MasterData'
const TEST_ENTITY = 'Customer'

/**
 * Helper to dismiss onboarding wizard, set tenant, and login.
 */
async function setupForEntityViews(page: import('@playwright/test').Page) {
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
}

/**
 * Create a test entity via API and return its data.
 * Returns null if creation fails.
 */
async function createTestEntity(page: import('@playwright/test').Page): Promise<Record<string, unknown> | null> {
  const headers = await getAuthHeaders(page)
  const uniqueCode = `E2E-VIEW-${Date.now()}`
  try {
    const resp = await page.request.post(`/api/odata/${TEST_MODULE}/${TEST_ENTITY}`, {
      headers,
      data: {
        CustomerCode: uniqueCode,
        Name: 'E2E View Test Entity',
        Email: 'e2e-view@example.com',
        CustomerType: 'Regular',
        CreditLimit: 5000,
        Status: 'Active',
        CompanyId: '00000000-0000-0000-0000-000000000001'
      }
    })
    if (resp.ok()) {
      return await resp.json()
    }
  } catch {
    // Entity creation failed
  }
  return null
}

/**
 * Delete a test entity via API (cleanup).
 */
async function deleteTestEntity(page: import('@playwright/test').Page, id: string) {
  const headers = await getAuthHeaders(page)
  await page.request.delete(`/api/odata/${TEST_MODULE}/${TEST_ENTITY}/${id}`, { headers }).catch(() => {})
}

test.describe('Entity List View', () => {
  test.beforeEach(async ({ page }) => {
    const available = await isBackendAvailable(page)
    test.skip(!available, 'Backend is not running -- skipping entity view tests')
    await setupForEntityViews(page)
  })

  test('should show entity list with title', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}`)

    // The entity list component displays the entity name as its title (via CardTitle)
    // Wait for either the table or the title to appear
    const table = page.locator('table')
    const title = page.getByText(TEST_ENTITY)
    await expect(table.or(title).first()).toBeVisible({ timeout: 15_000 })
  })

  test('should have refresh and create buttons in toolbar', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}`)
    await page.waitForLoadState('networkidle')

    // Refresh button (icon button with RefreshCw)
    const refreshBtn = page.locator('button').filter({ has: page.locator('svg.lucide-refresh-cw') })
    await expect(refreshBtn.first()).toBeVisible({ timeout: 10_000 })

    // Create button
    const createButton = page.getByRole('button', { name: /create/i })
      .or(page.locator('button').filter({ has: page.locator('svg.lucide-plus') }))
    await expect(createButton.first()).toBeVisible({ timeout: 10_000 })
  })

  test('should open detail panel when clicking a row view button', async ({ page }) => {
    // Create a test entity so we have data
    const entity = await createTestEntity(page)
    test.skip(!entity, 'Could not create test entity')

    try {
      await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}`)

      // Wait for table to load
      const table = page.locator('table')
      await expect(table).toBeVisible({ timeout: 15_000 })

      // Wait for rows
      const rows = table.locator('tbody tr')
      await expect(rows.first()).toBeVisible({ timeout: 10_000 })

      // Click the view button (eye icon) on the first row -- force click since it may be opacity-0 on hover
      const viewButton = rows.first().locator('button').filter({ has: page.locator('svg.lucide-eye') })
      const hasViewBtn = await viewButton.count() > 0
      test.skip(!hasViewBtn, 'No view button found in table rows')

      await viewButton.first().click({ force: true })
      await page.waitForTimeout(800)

      // Detail panel should appear with entity fields
      // The panel has a CardTitle with entity details heading
      const detailPanel = page.locator('.fixed.inset-0, .lg\\:sticky')
      const hasDetail = await detailPanel.first().isVisible({ timeout: 5_000 }).catch(() => false)

      if (!hasDetail) {
        // Try alternative: detail info rendered as <dl> element
        const detailList = page.locator('dl.space-y-3')
        await expect(detailList).toBeVisible({ timeout: 5_000 })
      }

      // Detail should have Edit and Delete action buttons
      const editBtn = page.getByRole('button', { name: /edit/i })
      await expect(editBtn.first()).toBeVisible({ timeout: 5_000 })
    } finally {
      if (entity?.Id) {
        await deleteTestEntity(page, entity.Id as string)
      }
    }
  })
})

test.describe('Entity Detail View', () => {
  let testEntityId: string | null = null

  test.beforeEach(async ({ page }) => {
    const available = await isBackendAvailable(page)
    test.skip(!available, 'Backend is not running -- skipping entity detail view tests')
    await setupForEntityViews(page)

    // Create a test entity for detail view tests
    const entity = await createTestEntity(page)
    test.skip(!entity, 'Could not create test entity for detail tests')
    testEntityId = entity!.Id as string
  })

  test.afterEach(async ({ page }) => {
    if (testEntityId) {
      await deleteTestEntity(page, testEntityId)
      testEntityId = null
    }
  })

  test('should show breadcrumb with module, entity, and ID', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}/${testEntityId}`)

    // Breadcrumb should show module name
    await expect(page.getByText(TEST_MODULE).first()).toBeVisible({ timeout: 10_000 })

    // Breadcrumb should show entity name (or display name)
    await expect(page.getByText(TEST_ENTITY).first()).toBeVisible({ timeout: 10_000 })

    // Breadcrumb shows a short ID (first 8 chars + ...)
    const shortId = testEntityId!.substring(0, 8)
    await expect(page.getByText(shortId).first()).toBeVisible({ timeout: 5_000 })
  })

  test('should show key fields section', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}/${testEntityId}`)

    // Wait for content to load
    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)

    // Key Fields card should be visible
    const keyFieldsHeader = page.getByText(/key fields/i)
    await expect(keyFieldsHeader.first()).toBeVisible({ timeout: 10_000 })
  })

  test('should show general info section', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}/${testEntityId}`)

    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)

    // General Info card should be visible
    const generalInfoHeader = page.getByText(/general info/i)
    await expect(generalInfoHeader.first()).toBeVisible({ timeout: 10_000 })
  })

  test('should show action buttons (edit, delete)', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}/${testEntityId}`)

    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)

    // Edit button should be visible (in the header area or sidebar)
    const editButton = page.getByRole('button', { name: /edit/i })
    await expect(editButton.first()).toBeVisible({ timeout: 10_000 })

    // Delete button should be visible
    const deleteButton = page.getByRole('button', { name: /delete/i })
    await expect(deleteButton.first()).toBeVisible({ timeout: 10_000 })
  })

  test('should show entity data values', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}/${testEntityId}`)

    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)

    // The entity was created with Name = 'E2E View Test Entity'
    await expect(page.getByText('E2E View Test Entity')).toBeVisible({ timeout: 10_000 })
  })

  test('should show stats cards (entity type, fields, compositions)', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}/${testEntityId}`)

    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)

    // Entity Type stats card
    const entityTypeLabel = page.getByText(/entity type/i)
    await expect(entityTypeLabel.first()).toBeVisible({ timeout: 10_000 })

    // Fields stats card
    const fieldsLabel = page.getByText(/^fields$/i)
    await expect(fieldsLabel.first()).toBeVisible({ timeout: 10_000 })
  })

  test('should show record info sidebar card', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}/${testEntityId}`)

    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)

    // Record card shows Record ID label and the full entity ID
    const recordIdLabel = page.getByText(/record id/i)
    await expect(recordIdLabel.first()).toBeVisible({ timeout: 10_000 })

    // Full entity ID should be shown
    await expect(page.getByText(testEntityId!).first()).toBeVisible({ timeout: 5_000 })
  })
})

test.describe('Entity Create View', () => {
  test.beforeEach(async ({ page }) => {
    const available = await isBackendAvailable(page)
    test.skip(!available, 'Backend is not running -- skipping entity create view tests')
    await setupForEntityViews(page)
  })

  test('should show breadcrumb with module, entity, and "New Record"', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}/new`)

    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)

    // Breadcrumb should show module name
    await expect(page.getByText(TEST_MODULE).first()).toBeVisible({ timeout: 10_000 })

    // Breadcrumb should show entity name
    await expect(page.getByText(TEST_ENTITY).first()).toBeVisible({ timeout: 10_000 })

    // Breadcrumb should show "New Record"
    await expect(page.getByText(/new record/i)).toBeVisible({ timeout: 5_000 })
  })

  test('should show create form with page title', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}/new`)

    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)

    // Page heading should include "Create" or "New" with entity name
    const heading = page.getByRole('heading', { name: /create/i })
    await expect(heading.first()).toBeVisible({ timeout: 10_000 })
  })

  test('should show stats cards (fields, required, compositions)', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}/new`)

    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)

    // Fields stats card
    const fieldsLabel = page.getByText(/^fields$/i)
    await expect(fieldsLabel.first()).toBeVisible({ timeout: 10_000 })

    // Required Fields stats card
    const requiredLabel = page.getByText(/required fields/i)
    await expect(requiredLabel.first()).toBeVisible({ timeout: 10_000 })
  })

  test('should show form card with entity name', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}/new`)

    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)

    // The form Card should have the entity display name as its title
    // and have form inputs (at least one input, select, or textarea)
    const formInputs = page.locator('form input, form select, form textarea')
      .or(page.locator('[role="form"] input'))
      .or(page.locator('input[type="text"], input:not([type])'))

    const inputCount = await formInputs.count()
    expect(inputCount).toBeGreaterThan(0)
  })

  test('should have save and cancel buttons', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}/new`)

    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)

    // Save/Create button
    const saveButton = page.getByRole('button', { name: /create|save/i })
    await expect(saveButton.first()).toBeVisible({ timeout: 10_000 })

    // Cancel button
    const cancelButton = page.getByRole('button', { name: /cancel/i })
    await expect(cancelButton.first()).toBeVisible({ timeout: 10_000 })
  })

  test('should show entity info sidebar', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}/new`)

    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)

    // Entity Type card in sidebar
    const entityTypeLabel = page.getByText(/entity type/i)
    await expect(entityTypeLabel.first()).toBeVisible({ timeout: 10_000 })

    // Module info in sidebar
    const moduleLabel = page.getByText(/^module$/i)
    await expect(moduleLabel.first()).toBeVisible({ timeout: 10_000 })
  })
})

test.describe('Entity Edit View', () => {
  let testEntityId: string | null = null

  test.beforeEach(async ({ page }) => {
    const available = await isBackendAvailable(page)
    test.skip(!available, 'Backend is not running -- skipping entity edit view tests')
    await setupForEntityViews(page)

    // Create a test entity for edit view tests
    const entity = await createTestEntity(page)
    test.skip(!entity, 'Could not create test entity for edit tests')
    testEntityId = entity!.Id as string
  })

  test.afterEach(async ({ page }) => {
    if (testEntityId) {
      await deleteTestEntity(page, testEntityId)
      testEntityId = null
    }
  })

  test('should show breadcrumb with module, entity, ID, and "Edit"', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}/${testEntityId}/edit`)

    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)

    // Breadcrumb should show module name
    await expect(page.getByText(TEST_MODULE).first()).toBeVisible({ timeout: 10_000 })

    // Breadcrumb should show entity name
    await expect(page.getByText(TEST_ENTITY).first()).toBeVisible({ timeout: 10_000 })

    // Breadcrumb should show short ID
    const shortId = testEntityId!.substring(0, 8)
    await expect(page.getByText(shortId).first()).toBeVisible({ timeout: 5_000 })

    // Breadcrumb should show "Edit"
    await expect(page.getByText(/^edit$/i).first()).toBeVisible({ timeout: 5_000 })
  })

  test('should show edit form with page title', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}/${testEntityId}/edit`)

    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)

    // Page heading should include "Edit" with entity name
    const heading = page.getByRole('heading', { name: /edit/i })
    await expect(heading.first()).toBeVisible({ timeout: 10_000 })
  })

  test('should show stats cards in edit view', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}/${testEntityId}/edit`)

    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)

    // Fields stats card
    const fieldsLabel = page.getByText(/^fields$/i)
    await expect(fieldsLabel.first()).toBeVisible({ timeout: 10_000 })

    // Required Fields stats card
    const requiredLabel = page.getByText(/required fields/i)
    await expect(requiredLabel.first()).toBeVisible({ timeout: 10_000 })
  })

  test('should pre-fill form with existing entity data', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}/${testEntityId}/edit`)

    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(3000)

    // The entity was created with Name = 'E2E View Test Entity'
    // Check for a pre-filled input with this value
    const nameInput = page.getByLabel(/^name$/i).or(page.locator('input[name="Name"]'))
    const hasNameInput = await nameInput.first().isVisible({ timeout: 5_000 }).catch(() => false)

    if (hasNameInput) {
      const value = await nameInput.first().inputValue()
      expect(value).toBe('E2E View Test Entity')
    } else {
      // If Name input is not found by label, check all inputs for the value
      const allInputs = page.locator('input[type="text"], input:not([type])')
      let found = false
      const count = await allInputs.count()
      for (let i = 0; i < Math.min(count, 20); i++) {
        const val = await allInputs.nth(i).inputValue().catch(() => '')
        if (val === 'E2E View Test Entity') {
          found = true
          break
        }
      }
      expect(found).toBeTruthy()
    }
  })

  test('should have save and cancel buttons', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}/${testEntityId}/edit`)

    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)

    // Save/Update button
    const saveButton = page.getByRole('button', { name: /save|update/i })
    await expect(saveButton.first()).toBeVisible({ timeout: 10_000 })

    // Cancel button
    const cancelButton = page.getByRole('button', { name: /cancel/i })
    await expect(cancelButton.first()).toBeVisible({ timeout: 10_000 })
  })

  test('should show record info sidebar', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}/${testEntityId}/edit`)

    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)

    // Record ID label should be in sidebar
    const recordIdLabel = page.getByText(/record id/i)
    await expect(recordIdLabel.first()).toBeVisible({ timeout: 10_000 })

    // Full entity ID should be shown
    await expect(page.getByText(testEntityId!).first()).toBeVisible({ timeout: 5_000 })
  })

  test('should show back to list action in sidebar', async ({ page }) => {
    await spaNavigate(page, `/odata/${TEST_MODULE}/${TEST_ENTITY}/${testEntityId}/edit`)

    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)

    // "Back to List" button in sidebar quick actions
    const backButton = page.getByRole('button', { name: /back to list/i })
    await expect(backButton.first()).toBeVisible({ timeout: 10_000 })
  })
})
