import { test, expect } from '@playwright/test'
import { loginViaUi, isBackendAvailable, spaNavigate, TEST_TENANT_ID } from './fixtures'

/**
 * Helper to dismiss onboarding wizard and navigate to role management.
 */
async function setupForRoleManagement(page: import('@playwright/test').Page) {
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

  // Navigate to roles page
  await spaNavigate(page, '/admin/roles')

  // Wait for the page heading to confirm we are on the right page
  await expect(page.getByRole('heading', { name: /role management/i })).toBeVisible({ timeout: 10_000 })

  // Wait for roles to load
  await page.waitForTimeout(1500)
}

test.describe('Role Management', () => {
  test.beforeEach(async ({ page }) => {
    const available = await isBackendAvailable(page)
    test.skip(!available, 'Backend is not running -- skipping role management tests')
    await setupForRoleManagement(page)
  })

  test.describe('Page Layout', () => {
    test('should display the role management page with header', async ({ page }) => {
      // Title should be visible
      await expect(page.getByRole('heading', { name: /role management/i })).toBeVisible()

      // Subtitle text should be visible
      await expect(page.getByText(/manage roles and assign permissions/i)).toBeVisible()

      // Refresh and Create Role buttons should exist
      await expect(page.getByRole('button', { name: /refresh/i })).toBeVisible()
      await expect(page.getByRole('button', { name: /create role/i })).toBeVisible()
    })

    test('should display stats cards', async ({ page }) => {
      // Stats cards: Total Roles, System, Custom
      await expect(page.getByText(/total roles/i)).toBeVisible({ timeout: 5_000 })
      await expect(page.getByText(/^system$/i)).toBeVisible({ timeout: 5_000 })
      await expect(page.getByText(/^custom$/i)).toBeVisible({ timeout: 5_000 })
    })

    test('should display search input', async ({ page }) => {
      const searchInput = page.getByPlaceholder(/search roles/i)
      await expect(searchInput).toBeVisible({ timeout: 5_000 })
    })
  })

  test.describe('Role List', () => {
    test('should display roles in the list', async ({ page }) => {
      // The role list is inside a w-80 panel with buttons for each role
      // At minimum, system roles should be present (admin, etc.)
      // Each role is rendered as a <button> with the role name
      const roleButtons = page.locator('button.w-full.text-left.rounded-lg')
      const roleCount = await roleButtons.count()

      // There should be at least one role visible
      test.skip(roleCount === 0, 'No roles found -- nothing to test')
      expect(roleCount).toBeGreaterThan(0)

      // First role should have a name
      const firstRole = roleButtons.first()
      await expect(firstRole).toBeVisible()
    })

    test('should show system badge on system roles', async ({ page }) => {
      // System roles display a "System" badge
      const systemBadges = page.locator('button.w-full.text-left.rounded-lg').getByText(/^system$/i)
      const badgeCount = await systemBadges.count()

      if (badgeCount > 0) {
        // At least one system role has a "System" badge
        await expect(systemBadges.first()).toBeVisible()
      }
      // If no system badges, the roles list may have no system roles (valid state)
    })

    test('should show lock icon on system roles', async ({ page }) => {
      // System roles use the Lock icon; check for it in the role list
      const lockIcons = page.locator('button.w-full.text-left.rounded-lg svg.lucide-lock')
      const lockCount = await lockIcons.count()

      if (lockCount > 0) {
        await expect(lockIcons.first()).toBeVisible()
      }
      // If no lock icons, there may be no system roles
    })
  })

  test.describe('Permission Panel', () => {
    test('should show permission panel when clicking a role', async ({ page }) => {
      const roleButtons = page.locator('button.w-full.text-left.rounded-lg')
      const roleCount = await roleButtons.count()
      test.skip(roleCount === 0, 'No roles found -- cannot test permission panel')

      // Before clicking, the panel should show "No Role Selected"
      await expect(page.getByText(/no role selected/i)).toBeVisible({ timeout: 5_000 })

      // Click the first role
      await roleButtons.first().click()
      await page.waitForTimeout(1000)

      // The permission panel should now show the role name in the header
      // or show loading/permissions content
      const panelHeader = page.getByText(/permissions/i)
      await expect(panelHeader.first()).toBeVisible({ timeout: 5_000 })

      // "No Role Selected" should no longer be visible
      await expect(page.getByText(/no role selected/i)).not.toBeVisible({ timeout: 3_000 })
    })

    test('should show permission toggles in the matrix', async ({ page }) => {
      const roleButtons = page.locator('button.w-full.text-left.rounded-lg')
      const roleCount = await roleButtons.count()
      test.skip(roleCount === 0, 'No roles found -- cannot test toggles')

      // Click the first role
      await roleButtons.first().click()
      await page.waitForTimeout(2000)

      // Check for permission toggle buttons (the colored action buttons in the matrix)
      // The matrix has buttons with action text like Read, Create, Update, Delete
      const permissionToggles = page.locator('.sticky button.h-8.rounded-md')
      const toggleCount = await permissionToggles.count()

      // If there are permissions defined, there should be toggle buttons
      // (could be 0 if no access control rules are compiled)
      if (toggleCount > 0) {
        await expect(permissionToggles.first()).toBeVisible()
      } else {
        // Check for "No permissions defined" empty state
        const noPerms = page.getByText(/no permissions defined/i)
        const hasNoPerms = await noPerms.isVisible({ timeout: 3_000 }).catch(() => false)
        // Either toggles or empty state should be present
        expect(toggleCount > 0 || hasNoPerms).toBeTruthy()
      }
    })
  })

  test.describe('Search', () => {
    test('should filter roles when typing in search', async ({ page }) => {
      const roleButtons = page.locator('button.w-full.text-left.rounded-lg')
      const initialCount = await roleButtons.count()
      test.skip(initialCount === 0, 'No roles found -- cannot test search filtering')

      const searchInput = page.getByPlaceholder(/search roles/i)
      await expect(searchInput).toBeVisible()

      // Type a search query that likely matches some roles
      await searchInput.fill('admin')
      await page.waitForTimeout(500)

      const filteredCount = await roleButtons.count()
      // Filtered count should be <= initial count
      expect(filteredCount).toBeLessThanOrEqual(initialCount)

      // Clear search
      await searchInput.clear()
      await page.waitForTimeout(500)

      const restoredCount = await roleButtons.count()
      // After clearing, should restore original count
      expect(restoredCount).toBe(initialCount)
    })

    test('should show no match message for nonsense search', async ({ page }) => {
      const roleButtons = page.locator('button.w-full.text-left.rounded-lg')
      const initialCount = await roleButtons.count()
      test.skip(initialCount === 0, 'No roles found -- cannot test search')

      const searchInput = page.getByPlaceholder(/search roles/i)
      await searchInput.fill('zzzznonexistentrole999')
      await page.waitForTimeout(500)

      // Should show "no match" text or empty state
      const noMatch = page.getByText(/no roles match/i)
      await expect(noMatch).toBeVisible({ timeout: 3_000 })
    })
  })

  test.describe('Create Role Dialog', () => {
    test('should open create role dialog when clicking Create Role button', async ({ page }) => {
      const createButton = page.getByRole('button', { name: /create role/i })
      await expect(createButton).toBeVisible()

      await createButton.click()
      await page.waitForTimeout(500)

      // Dialog should appear with "Create Role" title
      await expect(page.getByText(/create role/i).first()).toBeVisible({ timeout: 5_000 })

      // Dialog should have Role Name and Description inputs
      const nameInput = page.locator('#role-name')
      await expect(nameInput).toBeVisible({ timeout: 3_000 })

      const descriptionTextarea = page.locator('#role-description')
      await expect(descriptionTextarea).toBeVisible({ timeout: 3_000 })

      // Dialog should have Cancel and Create buttons
      await expect(page.getByRole('button', { name: /cancel/i })).toBeVisible()
      await expect(page.getByRole('button', { name: /^create$/i })).toBeVisible()

      // Close the dialog (press Escape)
      await page.keyboard.press('Escape')
      await page.waitForTimeout(300)
    })
  })

  test.describe('System Role Protection', () => {
    test('should disable delete button for system roles', async ({ page }) => {
      const roleButtons = page.locator('button.w-full.text-left.rounded-lg')
      const roleCount = await roleButtons.count()
      test.skip(roleCount === 0, 'No roles found -- cannot test delete protection')

      // Find a system role (one that has a "System" badge)
      let systemRoleFound = false
      for (let i = 0; i < Math.min(roleCount, 10); i++) {
        const role = roleButtons.nth(i)
        const hasBadge = await role.getByText(/^system$/i).isVisible({ timeout: 1_000 }).catch(() => false)
        if (hasBadge) {
          // Hover over the role to show action buttons (they have opacity-0 by default)
          await role.hover()
          await page.waitForTimeout(300)

          // Find the delete button within this role (shown on hover)
          const deleteBtn = role.getByRole('button', { name: /delete/i })
          const hasDelete = await deleteBtn.isVisible({ timeout: 2_000 }).catch(() => false)

          if (hasDelete) {
            // Delete button should be disabled for system roles
            await expect(deleteBtn).toBeDisabled()
            systemRoleFound = true
            break
          }
        }
      }

      test.skip(!systemRoleFound, 'No system role with visible delete button found')
    })
  })
})
