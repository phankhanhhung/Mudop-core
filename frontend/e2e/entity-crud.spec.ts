import { test, expect } from '@playwright/test'
import { loginViaUi, isBackendAvailable, getAuthHeaders, spaNavigate, TEST_TENANT_ID } from './fixtures'

/**
 * Entity CRUD tests exercise the full lifecycle of creating, reading,
 * editing, and deleting an entity through the UI. These tests require
 * the backend to be running with the MasterData module installed.
 */
test.describe('Entity CRUD operations', () => {
  // Track entities created during tests for cleanup
  let createdEntityIds: string[] = []

  test.beforeEach(async ({ page }) => {
    const available = await isBackendAvailable(page)
    test.skip(!available, 'Backend is not running — skipping entity CRUD tests')
    createdEntityIds = []
    await loginViaUi(page)
  })

  test.afterEach(async ({ page }) => {
    // Clean up any entities created during the test
    if (createdEntityIds.length > 0) {
      const headers = await getAuthHeaders(page)
      for (const id of createdEntityIds) {
        await page.request
          .delete(`/api/odata/MasterData/Customer/${id}`, { headers })
          .catch(() => {})
      }
    }
  })

  test('navigate to entity list page', async ({ page }) => {
    await spaNavigate(page, '/odata/MasterData/Customer')

    // Should show the entity list view
    await expect(page).toHaveURL(/\/odata\/MasterData\/Customer/)

    // Wait for content to load — should see a table or loading indicator
    const table = page.locator('table')
    const emptyState = page.getByText(/no .* found|no records|no data/i)
    const loading = page.locator('[class*="spinner"], [class*="loading"]')

    // At least one of these should appear
    await expect(table.or(emptyState).or(loading).first()).toBeVisible({ timeout: 15_000 })
  })

  test('entity list has create button', async ({ page }) => {
    await spaNavigate(page, '/odata/MasterData/Customer')

    // Wait for page to stabilize
    await page.waitForLoadState('networkidle')

    // Should have a "Create" or "New" button/link
    const createButton = page.getByRole('link', { name: /create|new/i })
      .or(page.getByRole('button', { name: /create|new/i }))
    await expect(createButton.first()).toBeVisible({ timeout: 10_000 })
  })

  test('create new entity via form', async ({ page }) => {
    await spaNavigate(page, '/odata/MasterData/Customer/new')

    await expect(page).toHaveURL(/\/odata\/MasterData\/Customer\/new/)

    // Fill in required fields
    const uniqueCode = `E2E-${Date.now()}`
    const nameField = page.getByLabel(/^name$/i).or(page.locator('input[name="Name"]'))
    const codeField = page.getByLabel(/code/i).or(page.locator('input[name="CustomerCode"]'))

    if (await codeField.first().isVisible({ timeout: 3_000 }).catch(() => false)) {
      await codeField.first().fill(uniqueCode)
    }
    if (await nameField.first().isVisible({ timeout: 3_000 }).catch(() => false)) {
      await nameField.first().fill('E2E Test Customer')
    }

    // Try to fill email
    const emailField = page.getByLabel(/email/i).or(page.locator('input[name="Email"]'))
    if (await emailField.first().isVisible({ timeout: 2_000 }).catch(() => false)) {
      await emailField.first().fill('e2e-test@example.com')
    }

    // Submit the form
    const submitButton = page.getByRole('button', { name: /create|save|submit/i })
    await expect(submitButton.first()).toBeVisible()
    await submitButton.first().click()

    // Should redirect to detail view or list view after creation
    await page.waitForURL(/\/odata\/MasterData\/Customer\/(?!new)/, { timeout: 15_000 })

    // Extract entity ID from URL for cleanup
    const url = page.url()
    const match = url.match(/Customer\/([^/?]+)/)
    if (match) {
      createdEntityIds.push(match[1])
    }
  })

  test('view entity detail page', async ({ page }) => {
    // Create entity via API for reliable test data
    const headers = await getAuthHeaders(page)
    const resp = await page.request.post('/api/odata/MasterData/Customer', {
      headers,
      data: {
        CustomerCode: `E2E-DETAIL-${Date.now()}`,
        Name: 'Detail View Test',
        Email: 'detail-test@example.com',
        CustomerType: 'Regular',
        CreditLimit: 5000,
        Status: 'Active',
        CompanyId: '00000000-0000-0000-0000-000000000001'
      }
    })
    expect(resp.ok()).toBeTruthy()
    const entity = await resp.json()
    createdEntityIds.push(entity.Id)

    // Navigate to detail page
    await spaNavigate(page, `/odata/MasterData/Customer/${entity.Id}`)

    // Should show entity details
    await expect(page.getByText('Detail View Test')).toBeVisible({ timeout: 10_000 })

    // Should have edit and delete action buttons
    const editButton = page.getByRole('link', { name: /edit/i })
      .or(page.getByRole('button', { name: /edit/i }))
    await expect(editButton.first()).toBeVisible({ timeout: 5_000 })
  })

  test('edit entity via form', async ({ page }) => {
    // Create entity via API
    const headers = await getAuthHeaders(page)
    const resp = await page.request.post('/api/odata/MasterData/Customer', {
      headers,
      data: {
        CustomerCode: `E2E-EDIT-${Date.now()}`,
        Name: 'Before Edit',
        Email: 'before-edit@example.com',
        CustomerType: 'Regular',
        CreditLimit: 1000,
        Status: 'Active',
        CompanyId: '00000000-0000-0000-0000-000000000001'
      }
    })
    expect(resp.ok()).toBeTruthy()
    const entity = await resp.json()
    createdEntityIds.push(entity.Id)

    // Navigate to edit page
    await spaNavigate(page, `/odata/MasterData/Customer/${entity.Id}/edit`)

    await expect(page).toHaveURL(/\/edit/)

    // Update the name field
    const nameField = page.getByLabel(/^name$/i).or(page.locator('input[name="Name"]'))
    if (await nameField.first().isVisible({ timeout: 5_000 }).catch(() => false)) {
      await nameField.first().clear()
      await nameField.first().fill('After Edit')
    }

    // Save changes
    const saveButton = page.getByRole('button', { name: /save|update/i })
    await expect(saveButton.first()).toBeVisible()
    await saveButton.first().click()

    // Should redirect back to detail view
    await page.waitForURL(new RegExp(`/odata/MasterData/Customer/${entity.Id}(?!/edit)`), {
      timeout: 15_000
    })

    // Verify the updated name shows
    await expect(page.getByText('After Edit')).toBeVisible({ timeout: 10_000 })
  })

  test('delete entity with confirmation', async ({ page }) => {
    // Create entity via API
    const headers = await getAuthHeaders(page)
    const resp = await page.request.post('/api/odata/MasterData/Customer', {
      headers,
      data: {
        CustomerCode: `E2E-DELETE-${Date.now()}`,
        Name: 'Delete Me',
        Email: 'delete-me@example.com',
        CustomerType: 'Regular',
        CreditLimit: 1000,
        Status: 'Active',
        CompanyId: '00000000-0000-0000-0000-000000000001'
      }
    })
    expect(resp.ok()).toBeTruthy()
    const entity = await resp.json()
    // Don't add to cleanup list since we're deleting it

    // Navigate to detail page
    await spaNavigate(page, `/odata/MasterData/Customer/${entity.Id}`)
    await expect(page.getByText('Delete Me')).toBeVisible({ timeout: 10_000 })

    // Accept the confirmation dialog that will appear
    page.on('dialog', (dialog) => dialog.accept())

    // Click delete button
    const deleteButton = page.getByRole('button', { name: /delete/i })
    if (await deleteButton.first().isVisible({ timeout: 5_000 }).catch(() => false)) {
      await deleteButton.first().click()

      // Should redirect to entity list or show confirmation
      // The app may use a custom confirm dialog rather than browser dialog
      const confirmButton = page.getByRole('button', { name: /confirm|yes|delete/i })
      if (await confirmButton.isVisible({ timeout: 3_000 }).catch(() => false)) {
        await confirmButton.click()
      }

      // Should redirect to list view after deletion
      await page.waitForURL(/\/odata\/MasterData\/Customer(?!\/)/, { timeout: 15_000 })
    }
  })

  test('entity list shows data in table format', async ({ page }) => {
    // Create a test entity so the list is not empty
    const headers = await getAuthHeaders(page)
    const resp = await page.request.post('/api/odata/MasterData/Customer', {
      headers,
      data: {
        CustomerCode: `E2E-LIST-${Date.now()}`,
        Name: 'List Test Customer',
        Email: 'list-test@example.com',
        CustomerType: 'Regular',
        CreditLimit: 2000,
        Status: 'Active',
        CompanyId: '00000000-0000-0000-0000-000000000001'
      }
    })
    if (resp.ok()) {
      const entity = await resp.json()
      createdEntityIds.push(entity.Id)
    }

    await spaNavigate(page, '/odata/MasterData/Customer')

    // Table should be visible with headers
    const table = page.locator('table')
    await expect(table).toBeVisible({ timeout: 15_000 })

    // Table should have header row
    const headerCells = table.locator('thead th')
    const headerCount = await headerCells.count()
    expect(headerCount).toBeGreaterThan(0)

    // Table should have at least one data row
    const rows = table.locator('tbody tr')
    await expect(rows.first()).toBeVisible({ timeout: 10_000 })
  })
})
