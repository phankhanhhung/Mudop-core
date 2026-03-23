import { test, expect, type Page } from '@playwright/test'

const TENANT_ID = '675fbd5a-965b-4369-a95e-ebd1ecb303f4'
const USER_EMAIL = 'phankhanhhung@gmail.com'
const USER_PASSWORD = 'a'

/** Login via the UI login form and select tenant */
async function loginAndSelectTenant(page: Page) {
  await page.goto('/auth/login')
  await page.waitForLoadState('domcontentloaded')

  // Pre-set tenant in localStorage so the login flow's fetchTenants() restores it
  await page.evaluate((tid) => {
    localStorage.setItem('bmmdl_tenant_id', tid)
  }, TENANT_ID)

  // Fill login form
  await page.getByPlaceholder('name@example.com').fill(USER_EMAIL)
  await page.getByPlaceholder('Enter your password').fill(USER_PASSWORD)
  await page.getByRole('button', { name: 'Sign In' }).click()

  // Wait for redirect to dashboard after successful login
  await page.waitForURL(/\/dashboard/, { timeout: 15000 })
  await page.waitForLoadState('networkidle')
}

/** Navigate within the SPA using Vue Router (avoids full page reload) */
async function spaNavigate(page: Page, path: string) {
  await page.evaluate((p) => {
    // @ts-expect-error __vue_app__ is internal
    const app = document.getElementById('app')?.__vue_app__
    if (app) {
      app.config.globalProperties.$router.push(p)
    }
  }, path)
  // Wait for route to resolve and data to load
  await page.waitForTimeout(500)
  await page.waitForLoadState('networkidle')
}

/** Get auth header for API calls */
async function getAuthHeaders(page: Page) {
  const token = await page.evaluate(() => localStorage.getItem('bmmdl_access_token'))
  return {
    Authorization: `Bearer ${token}`,
    'X-Tenant-Id': TENANT_ID
  }
}

/** Create a customer via API and return its ID */
async function createCustomerViaApi(page: Page, name: string): Promise<string> {
  const headers = await getAuthHeaders(page)
  const resp = await page.request.post(`/api/odata/MasterData/Customer`, {
    headers,
    data: {
      CustomerCode: `TEST-${Date.now()}`,
      Name: name,
      Email: `${name.toLowerCase().replace(/\s/g, '')}@test.com`,
      CustomerType: 'Regular',
      CreditLimit: 1000,
      Status: 'Active',
      CompanyId: '00000000-0000-0000-0000-000000000001'
    }
  })
  expect(resp.ok()).toBeTruthy()
  const data = await resp.json()
  return data.Id
}

/** Delete a customer via API */
async function deleteCustomerViaApi(page: Page, id: string) {
  const headers = await getAuthHeaders(page)
  await page.request.delete(`/api/odata/MasterData/Customer/${id}`, { headers })
}

/** Create an address via API */
async function createAddressViaApi(
  page: Page,
  customerId: string,
  street: string
): Promise<string> {
  const headers = await getAuthHeaders(page)
  const resp = await page.request.post(`/api/odata/MasterData/CustomerAddress`, {
    headers,
    data: {
      Street: street,
      City: 'Test City',
      State: 'CA',
      Country: 'US',
      PostalCode: '10001',
      AddressType: 'Billing',
      CustomerId: customerId
    }
  })
  expect(resp.ok()).toBeTruthy()
  const data = await resp.json()
  return data.Id
}

test.describe('Composition CRUD', () => {
  let customerId: string

  test.beforeEach(async ({ page }) => {
    await loginAndSelectTenant(page)
    customerId = await createCustomerViaApi(page, 'Composition Test')
  })

  test.afterEach(async ({ page }) => {
    if (customerId) {
      await deleteCustomerViaApi(page, customerId).catch(() => {})
    }
  })

  test('composition section shows Add button and empty state', async ({ page }) => {
    await spaNavigate(page, `/odata/MasterData/Customer/${customerId}`)

    // Wait for composition section heading
    const heading = page.getByRole('heading', { name: /addresses/i })
    await expect(heading).toBeVisible({ timeout: 10000 })

    // The composition Card is the ancestor div with border class
    const compositionCard = page.locator('div.border', { has: heading })

    // Should show Add button within the composition section
    const addButton = compositionCard.getByRole('button', { name: /add/i }).first()
    await expect(addButton).toBeVisible()

    // Should show empty state text
    await expect(compositionCard.getByText(/no .* records/i)).toBeVisible()
  })

  test('Add button navigates to child create page with parent FK', async ({ page }) => {
    await spaNavigate(page, `/odata/MasterData/Customer/${customerId}`)

    const heading = page.getByRole('heading', { name: /addresses/i })
    await expect(heading).toBeVisible({ timeout: 10000 })
    const compositionCard = page.locator('div.border', { has: heading })

    // Click Add button within the composition section
    await compositionCard.getByRole('button', { name: /add/i }).first().click()

    // Should navigate to CustomerAddress create page
    await expect(page).toHaveURL(/\/odata\/MasterData\/CustomerAddress\/new/)

    // URL should have parentFk and parentId query params
    const url = page.url()
    expect(url).toContain('parentFk=CustomerId')
    expect(url).toContain(`parentId=${customerId}`)
    expect(url).toContain('parentEntity=Customer')

    // Back button should mention parent entity
    await expect(page.getByText('Back to Customer')).toBeVisible({ timeout: 5000 })
  })

  test('create child from composition and see it on parent detail', async ({ page }) => {
    await spaNavigate(page, `/odata/MasterData/Customer/${customerId}`)

    const heading = page.getByRole('heading', { name: /addresses/i })
    await expect(heading).toBeVisible({ timeout: 10000 })
    const compositionCard = page.locator('div.border', { has: heading })

    // Click Add within the composition section
    await compositionCard.getByRole('button', { name: /add/i }).first().click()
    await expect(page).toHaveURL(/\/odata\/MasterData\/CustomerAddress\/new/)

    // Fill in the address form (all required fields)
    await page.getByLabel(/addresstype/i).selectOption('Billing')
    await page.getByLabel(/street/i).fill('123 Test Street')
    await page.getByLabel(/city/i).fill('Test City')
    await page.getByLabel(/state/i).fill('CA')
    await page.getByLabel(/country/i).fill('US')
    await page.getByLabel(/postal/i).fill('90210')
    await page.getByLabel(/isdefault/i).check()

    // Submit
    await page.getByRole('button', { name: /create/i }).click()

    // Should redirect back to parent detail view
    await page.waitForURL(new RegExp(`/odata/MasterData/Customer/${customerId}`), {
      timeout: 10000
    })

    // Composition section should show the new address
    await expect(page.getByText('123 Test Street')).toBeVisible({ timeout: 10000 })
  })

  test('edit button navigates to child edit page with parent context', async ({ page }) => {
    // Create address via API
    const addressId = await createAddressViaApi(page, customerId, 'Edit Test Street')

    await spaNavigate(page, `/odata/MasterData/Customer/${customerId}`)

    // Wait for address to appear
    await expect(page.getByText('Edit Test Street')).toBeVisible({ timeout: 10000 })

    // Click edit (pencil) button on the row
    const row = page.locator('tr', { hasText: 'Edit Test Street' })
    await row.getByRole('button').first().click()

    // Should navigate to child edit page with parent context
    await expect(page).toHaveURL(
      new RegExp(`/odata/MasterData/CustomerAddress/${addressId}/edit`)
    )
    const url = page.url()
    expect(url).toContain('parentFk=CustomerId')
    expect(url).toContain(`parentId=${customerId}`)
    expect(url).toContain('parentEntity=Customer')

    // Back button should mention parent entity
    await expect(page.getByText('Back to Customer')).toBeVisible({ timeout: 5000 })
  })

  test('edit child and save returns to parent detail', async ({ page }) => {
    // Create address via API
    await createAddressViaApi(page, customerId, 'Save Return Street')

    await spaNavigate(page, `/odata/MasterData/Customer/${customerId}`)

    // Wait for address to appear and click edit
    await expect(page.getByText('Save Return Street')).toBeVisible({ timeout: 10000 })
    const row = page.locator('tr', { hasText: 'Save Return Street' })
    await row.getByRole('button').first().click()

    // Should be on edit page
    await expect(page).toHaveURL(/\/odata\/MasterData\/CustomerAddress\/.*\/edit/)

    // Click Save Changes
    await page.getByRole('button', { name: /save/i }).click()

    // Should redirect back to parent Customer detail (not CustomerAddress detail)
    await page.waitForURL(new RegExp(`/odata/MasterData/Customer/${customerId}`), {
      timeout: 10000
    })

    // The address should still be visible on the parent detail
    await expect(page.getByText('Save Return Street')).toBeVisible({ timeout: 10000 })
  })

  test('delete button removes child from composition', async ({ page }) => {
    // Create 2 addresses via API
    await createAddressViaApi(page, customerId, 'Keep This Street')
    await createAddressViaApi(page, customerId, 'Delete This Street')

    await spaNavigate(page, `/odata/MasterData/Customer/${customerId}`)

    // Wait for both addresses
    await expect(page.getByText('Keep This Street')).toBeVisible({ timeout: 10000 })
    await expect(page.getByText('Delete This Street')).toBeVisible()

    // Accept the confirm dialog
    page.on('dialog', (dialog) => dialog.accept())

    // Click delete (trash) button on "Delete This Street" row
    const deleteRow = page.locator('tr', { hasText: 'Delete This Street' })
    await deleteRow.getByRole('button').nth(1).click()

    // "Delete This Street" should disappear
    await expect(page.getByText('Delete This Street')).not.toBeVisible({ timeout: 10000 })

    // "Keep This Street" should still be visible
    await expect(page.getByText('Keep This Street')).toBeVisible()
  })

  test('deep update orphan deletion via API', async ({ page }) => {
    const headers = await getAuthHeaders(page)

    // Create 3 addresses via deep update
    await page.request.patch(`/api/odata/MasterData/Customer/${customerId}`, {
      headers,
      data: {
        Addresses: [
          {
            Street: 'Orphan 1',
            City: 'A',
            State: 'X',
            Country: 'US',
            PostalCode: '10001',
            AddressType: 'Billing'
          },
          {
            Street: 'Orphan 2',
            City: 'B',
            State: 'Y',
            Country: 'US',
            PostalCode: '20002',
            AddressType: 'Shipping'
          },
          {
            Street: 'Keep Me',
            City: 'C',
            State: 'Z',
            Country: 'US',
            PostalCode: '30003',
            AddressType: 'Billing'
          }
        ]
      }
    })

    // Verify 3 exist
    const listResp = await page.request.get(
      `/api/odata/MasterData/Customer/${customerId}?$expand=Addresses`,
      { headers }
    )
    const listData = await listResp.json()
    expect(listData.Addresses).toHaveLength(3)

    // Deep update: keep only "Keep Me"
    const keepAddr = listData.Addresses.find(
      (a: Record<string, unknown>) => a.Street === 'Keep Me'
    )
    const patchResp = await page.request.patch(
      `/api/odata/MasterData/Customer/${customerId}`,
      {
        headers,
        data: { Addresses: [{ Id: keepAddr.Id, Street: 'Keep Me Updated' }] }
      }
    )
    expect(patchResp.ok()).toBeTruthy()

    // Verify only 1 remains
    const afterResp = await page.request.get(
      `/api/odata/MasterData/Customer/${customerId}?$expand=Addresses`,
      { headers }
    )
    const afterData = await afterResp.json()
    expect(afterData.Addresses).toHaveLength(1)
    expect(afterData.Addresses[0].Street).toBe('Keep Me Updated')
  })
})
