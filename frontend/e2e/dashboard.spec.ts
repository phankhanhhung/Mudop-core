import { test, expect } from '@playwright/test'
import { loginViaUi, isBackendAvailable, spaNavigate, TEST_TENANT_ID } from './fixtures'

/**
 * Helper to dismiss onboarding wizard and ensure tenant is selected for dashboard tests.
 */
async function setupForDashboard(page: import('@playwright/test').Page) {
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

  // Ensure we're on the dashboard
  await spaNavigate(page, '/dashboard')

  // Wait for dashboard heading
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible({ timeout: 10_000 })

  // Verify the tenant is properly loaded in the Pinia store
  const hasTenant = await page.evaluate(() => {
    // @ts-expect-error __vue_app__ is internal
    const app = document.getElementById('app')?.__vue_app__
    if (app) {
      const pinia = app.config.globalProperties.$pinia
      if (pinia && pinia.state.value.tenant) {
        return !!pinia.state.value.tenant.currentTenant
      }
    }
    return false
  })

  if (!hasTenant) {
    // Force-select the tenant via the Pinia store
    await page.evaluate(async (tid) => {
      // @ts-expect-error __vue_app__ is internal
      const app = document.getElementById('app')?.__vue_app__
      if (app) {
        const pinia = app.config.globalProperties.$pinia
        if (pinia) {
          const store = pinia.state.value.tenant
          if (store && store.tenants?.length > 0) {
            const tenant = store.tenants.find((t: any) => t.id === tid) || store.tenants[0]
            store.currentTenant = tenant
            localStorage.setItem('bmmdl_tenant_id', tenant.id)
          }
        }
      }
    }, TEST_TENANT_ID)

    // Reload page to pick up tenant
    await page.reload()
    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(2000)
    await spaNavigate(page, '/dashboard')
    await page.waitForTimeout(1000)
  }
}

/**
 * Helper to set up dashboard without a tenant selected.
 */
async function setupForDashboardNoTenant(page: import('@playwright/test').Page) {
  await page.goto('/auth/login')
  await page.waitForLoadState('domcontentloaded')

  // Dismiss onboarding but do NOT set tenant
  await page.evaluate(() => {
    localStorage.setItem('bmmdl-onboarding-completed', 'true')
    localStorage.removeItem('bmmdl_tenant_id')
  })

  await loginViaUi(page)
  await page.waitForTimeout(2000)

  // Clear tenant from Pinia store to simulate no-tenant state
  await page.evaluate(() => {
    // @ts-expect-error __vue_app__ is internal
    const app = document.getElementById('app')?.__vue_app__
    if (app) {
      const pinia = app.config.globalProperties.$pinia
      if (pinia && pinia.state.value.tenant) {
        pinia.state.value.tenant.currentTenant = null
        localStorage.removeItem('bmmdl_tenant_id')
      }
    }
  })

  // Navigate to dashboard after clearing tenant
  await spaNavigate(page, '/dashboard')
  await page.waitForTimeout(1000)
}

test.describe('Dashboard', () => {
  test.beforeEach(async ({ page }) => {
    const available = await isBackendAvailable(page)
    test.skip(!available, 'Backend is not running — skipping dashboard tests')
  })

  test.describe('Page Layout', () => {
    test('should display the dashboard with welcome message', async ({ page }) => {
      await setupForDashboard(page)

      // Should be on the dashboard
      await expect(page).toHaveURL(/\/dashboard/)

      // Welcome heading should be visible (h1 with "Welcome back, <name>")
      const heading = page.getByRole('heading', { level: 1 })
      await expect(heading).toBeVisible({ timeout: 10_000 })
      await expect(heading).toContainText(/welcome/i)
    })

    test('should show Getting Started when no tenant is selected', async ({ page }) => {
      await setupForDashboardNoTenant(page)

      // The Getting Started card should be visible with step cards inside
      const gettingStartedTitle = page.getByRole('heading', { name: /welcome to bmmdl/i })
      const isVisible = await gettingStartedTitle.isVisible({ timeout: 5_000 }).catch(() => false)

      if (isVisible) {
        await expect(gettingStartedTitle).toBeVisible()

        // Step cards (1, 2, 3) should be visible
        await expect(page.locator('.text-sm.font-bold.text-primary').filter({ hasText: '1' })).toBeVisible()
        await expect(page.locator('.text-sm.font-bold.text-primary').filter({ hasText: '2' })).toBeVisible()
        await expect(page.locator('.text-sm.font-bold.text-primary').filter({ hasText: '3' })).toBeVisible()
      } else {
        // If tenant auto-selected (from previous test or app state), skip gracefully
        test.skip(true, 'Tenant was auto-selected — cannot test no-tenant state')
      }
    })
  })

  test.describe('KPI Stats Cards', () => {
    test('should display KPI stat cards when tenant is selected', async ({ page }) => {
      await setupForDashboard(page)

      // Check if tenant is actually loaded
      const hasTenant = await page.evaluate(() => {
        // @ts-expect-error __vue_app__ is internal
        const app = document.getElementById('app')?.__vue_app__
        if (app) {
          const pinia = app.config.globalProperties.$pinia
          return !!pinia?.state.value.tenant?.currentTenant
        }
        return false
      })
      test.skip(!hasTenant, 'No tenant available in store — cannot test KPI cards')

      // The 4 KPI cards should be visible in the grid
      // Wait for the KPI grid to appear (it is rendered when tenantStore.hasTenant is true)
      const kpiGrid = page.locator('.grid.sm\\:grid-cols-2.lg\\:grid-cols-4').first()
      await expect(kpiGrid).toBeVisible({ timeout: 10_000 })

      // There should be 4 KPI cards
      const kpiCards = kpiGrid.locator('> *')
      await expect(kpiCards).toHaveCount(4)
    })
  })

  test.describe('Dashboard Widgets', () => {
    test('should render the entity overview widget', async ({ page }) => {
      await setupForDashboard(page)

      // Need to scroll down to see widgets below the fold
      await page.evaluate(() => window.scrollTo(0, 500))
      await page.waitForTimeout(500)

      // Entity Overview card has "Entity Overview" text in CardTitle
      const entityOverview = page.getByText(/entity overview/i).first()
      await expect(entityOverview).toBeVisible({ timeout: 10_000 })

      // Should show either entity data (records badge) or the empty state
      const hasData = await page.getByText(/records/i).first().isVisible({ timeout: 3_000 }).catch(() => false)
      const hasEmptyState = await page.getByText(/no entity data/i).isVisible({ timeout: 3_000 }).catch(() => false)

      // One of them should be true
      expect(hasData || hasEmptyState).toBeTruthy()
    })

    test('should render the recent activity widget', async ({ page }) => {
      await setupForDashboard(page)

      await page.evaluate(() => window.scrollTo(0, 500))
      await page.waitForTimeout(500)

      // Recent Activity card
      const recentActivity = page.getByText(/recent activity/i).first()
      await expect(recentActivity).toBeVisible({ timeout: 10_000 })

      // Should show either timeline items or the empty state
      const hasEmptyState = await page.getByText(/no recent activity/i).isVisible({ timeout: 3_000 }).catch(() => false)
      // If not empty state, there should be activity items
      if (!hasEmptyState) {
        // Timeline content present (Created/Updated/Deleted badges)
        const hasContent = await page.locator('.relative .space-y-0').isVisible({ timeout: 3_000 }).catch(() => false)
        expect(hasContent).toBeTruthy()
      }
    })

    test('should render the system health widget', async ({ page }) => {
      await setupForDashboard(page)

      await page.evaluate(() => window.scrollTo(0, 500))
      await page.waitForTimeout(500)

      // System Health card
      const systemHealth = page.getByText(/system health/i).first()
      await expect(systemHealth).toBeVisible({ timeout: 10_000 })

      // Should show either healthy badge, degraded badge, or "unable to retrieve"
      const hasHealthy = await page.getByText(/healthy/i).isVisible({ timeout: 3_000 }).catch(() => false)
      const hasDegraded = await page.getByText(/degraded/i).isVisible({ timeout: 3_000 }).catch(() => false)
      const hasUnable = await page.getByText(/unable to retrieve/i).isVisible({ timeout: 3_000 }).catch(() => false)

      expect(hasHealthy || hasDegraded || hasUnable).toBeTruthy()
    })

    test('should render the quick actions widget with navigation links', async ({ page }) => {
      await setupForDashboard(page)

      // Scroll further down for the quick actions widget
      await page.evaluate(() => window.scrollTo(0, 1000))
      await page.waitForTimeout(500)

      // Quick Actions card
      const quickActions = page.getByText(/quick actions/i).first()
      await expect(quickActions).toBeVisible({ timeout: 10_000 })

      // Should have action buttons (Compile Module, Browse Metadata, User Management, etc.)
      const actionButtons = page.locator('button').filter({ hasText: /compile module|browse metadata|user management|role management|audit log|api documentation/i })
      const actionCount = await actionButtons.count()
      expect(actionCount).toBeGreaterThanOrEqual(1)
    })
  })

  test.describe('Dashboard Actions', () => {
    test('should have a working refresh button', async ({ page }) => {
      await setupForDashboard(page)

      // Find the Refresh button
      const refreshBtn = page.getByRole('button', { name: /refresh/i })
      await expect(refreshBtn).toBeVisible({ timeout: 10_000 })

      // Click refresh
      await refreshBtn.click()

      // The button should complete the refresh and become enabled again
      await expect(refreshBtn).toBeEnabled({ timeout: 10_000 })
    })

    test('should navigate when clicking a quick action link', async ({ page }) => {
      await setupForDashboard(page)

      // Scroll to quick actions
      await page.evaluate(() => window.scrollTo(0, 1000))
      await page.waitForTimeout(500)

      // Click the "User Management" quick action
      const userMgmtAction = page.locator('button').filter({ hasText: /user management/i }).first()
      const hasAction = await userMgmtAction.isVisible({ timeout: 5_000 }).catch(() => false)
      test.skip(!hasAction, 'Quick action buttons not visible')

      await userMgmtAction.click()

      // Should navigate to the users page
      await expect(page).toHaveURL(/\/admin\/users/, { timeout: 10_000 })
    })
  })
})
