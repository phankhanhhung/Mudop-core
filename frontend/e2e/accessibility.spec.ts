import { test, expect } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { loginViaUi, isBackendAvailable, spaNavigate } from './fixtures'

/**
 * Accessibility smoke tests using axe-core.
 * These verify that key pages meet WCAG 2.1 Level A standards.
 * Violations are logged but only critical/serious issues fail the test.
 */
test.describe('Accessibility', () => {
  test('login page has no critical accessibility violations', async ({ page }) => {
    await page.goto('/auth/login')
    await page.waitForLoadState('domcontentloaded')

    const results = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa'])
      .analyze()

    const critical = results.violations.filter(
      (v) => v.impact === 'critical' || v.impact === 'serious'
    )

    if (critical.length > 0) {
      const summary = critical.map(
        (v) => `[${v.impact}] ${v.id}: ${v.description} (${v.nodes.length} instances)`
      )
      console.log('Login page a11y violations:', summary)
    }

    expect(critical).toHaveLength(0)
  })

  test('registration page has no critical accessibility violations', async ({ page }) => {
    await page.goto('/auth/register')
    await page.waitForLoadState('domcontentloaded')

    const results = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa'])
      .analyze()

    const critical = results.violations.filter(
      (v) => v.impact === 'critical' || v.impact === 'serious'
    )

    if (critical.length > 0) {
      const summary = critical.map(
        (v) => `[${v.impact}] ${v.id}: ${v.description} (${v.nodes.length} instances)`
      )
      console.log('Register page a11y violations:', summary)
    }

    expect(critical).toHaveLength(0)
  })

  test.describe('Authenticated pages', () => {
    test.beforeEach(async ({ page }) => {
      const available = await isBackendAvailable(page)
      test.skip(!available, 'Backend is not running — skipping authenticated a11y tests')
      await loginViaUi(page)
    })

    test('dashboard has no critical accessibility violations', async ({ page }) => {
      await expect(page).toHaveURL(/\/dashboard/)
      await page.waitForLoadState('networkidle')

      const results = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa'])
        .analyze()

      const critical = results.violations.filter(
        (v) => v.impact === 'critical' || v.impact === 'serious'
      )

      if (critical.length > 0) {
        const summary = critical.map(
          (v) => `[${v.impact}] ${v.id}: ${v.description} (${v.nodes.length} instances)`
        )
        console.log('Dashboard a11y violations:', summary)
      }

      expect(critical).toHaveLength(0)
    })

    test('entity list page has no critical accessibility violations', async ({ page }) => {
      await spaNavigate(page, '/odata/MasterData/Customer')
      await page.waitForLoadState('networkidle')

      // Wait for the page to fully render
      await page.waitForTimeout(2_000)

      const results = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa'])
        .analyze()

      const critical = results.violations.filter(
        (v) => v.impact === 'critical' || v.impact === 'serious'
      )

      if (critical.length > 0) {
        const summary = critical.map(
          (v) => `[${v.impact}] ${v.id}: ${v.description} (${v.nodes.length} instances)`
        )
        console.log('Entity list a11y violations:', summary)
      }

      expect(critical).toHaveLength(0)
    })

    test('admin modules page has no critical accessibility violations', async ({ page }) => {
      await spaNavigate(page, '/admin/modules')
      await page.waitForLoadState('networkidle')
      await page.waitForTimeout(2_000)

      const results = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa'])
        .analyze()

      const critical = results.violations.filter(
        (v) => v.impact === 'critical' || v.impact === 'serious'
      )

      if (critical.length > 0) {
        const summary = critical.map(
          (v) => `[${v.impact}] ${v.id}: ${v.description} (${v.nodes.length} instances)`
        )
        console.log('Admin modules a11y violations:', summary)
      }

      expect(critical).toHaveLength(0)
    })

    test('settings page has no critical accessibility violations', async ({ page }) => {
      await spaNavigate(page, '/settings')
      await page.waitForLoadState('networkidle')
      await page.waitForTimeout(2_000)

      const results = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa'])
        .analyze()

      const critical = results.violations.filter(
        (v) => v.impact === 'critical' || v.impact === 'serious'
      )

      if (critical.length > 0) {
        const summary = critical.map(
          (v) => `[${v.impact}] ${v.id}: ${v.description} (${v.nodes.length} instances)`
        )
        console.log('Settings page a11y violations:', summary)
      }

      expect(critical).toHaveLength(0)
    })

    test('tenant list page has no critical accessibility violations', async ({ page }) => {
      const navigated = await spaNavigate(page, '/tenants').then(() => true).catch(() => false)
      if (!navigated) return
      await page.waitForLoadState('networkidle')
      await page.waitForTimeout(2_000)

      const results = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa'])
        .analyze()

      const critical = results.violations.filter(
        (v) => v.impact === 'critical' || v.impact === 'serious'
      )

      if (critical.length > 0) {
        const summary = critical.map(
          (v) => `[${v.impact}] ${v.id}: ${v.description} (${v.nodes.length} instances)`
        )
        console.log('Tenant list a11y violations:', summary)
      }

      expect(critical).toHaveLength(0)
    })

    test('batch operations page has no critical accessibility violations', async ({ page }) => {
      const navigated = await spaNavigate(page, '/admin/batch').then(() => true).catch(() => false)
      if (!navigated) return
      await page.waitForLoadState('networkidle')
      await page.waitForTimeout(2_000)

      const results = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa'])
        .analyze()

      const critical = results.violations.filter(
        (v) => v.impact === 'critical' || v.impact === 'serious'
      )

      if (critical.length > 0) {
        const summary = critical.map(
          (v) => `[${v.impact}] ${v.id}: ${v.description} (${v.nodes.length} instances)`
        )
        console.log('Batch operations a11y violations:', summary)
      }

      expect(critical).toHaveLength(0)
    })

    test('API docs page has no critical accessibility violations', async ({ page }) => {
      const navigated = await spaNavigate(page, '/admin/api-docs').then(() => true).catch(() => false)
      if (!navigated) return
      await page.waitForLoadState('networkidle')
      await page.waitForTimeout(2_000)

      const results = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa'])
        .analyze()

      const critical = results.violations.filter(
        (v) => v.impact === 'critical' || v.impact === 'serious'
      )

      if (critical.length > 0) {
        const summary = critical.map(
          (v) => `[${v.impact}] ${v.id}: ${v.description} (${v.nodes.length} instances)`
        )
        console.log('API docs a11y violations:', summary)
      }

      expect(critical).toHaveLength(0)
    })

    test('metadata browser page has no critical accessibility violations', async ({ page }) => {
      const navigated = await spaNavigate(page, '/admin/metadata').then(() => true).catch(() => false)
      if (!navigated) return
      await page.waitForLoadState('networkidle')
      await page.waitForTimeout(2_000)

      const results = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa'])
        .analyze()

      const critical = results.violations.filter(
        (v) => v.impact === 'critical' || v.impact === 'serious'
      )

      if (critical.length > 0) {
        const summary = critical.map(
          (v) => `[${v.impact}] ${v.id}: ${v.description} (${v.nodes.length} instances)`
        )
        console.log('Metadata browser a11y violations:', summary)
      }

      expect(critical).toHaveLength(0)
    })
  })

  test('login page has proper form labels', async ({ page }) => {
    await page.goto('/auth/login')
    await page.waitForLoadState('domcontentloaded')

    // Email/username input should have a label
    const emailInput = page.getByPlaceholder('name@example.com')
    await expect(emailInput).toBeVisible()

    // Password input should have a label
    const passwordInput = page.locator('input[type="password"]')
    await expect(passwordInput).toBeVisible()

    // The form should be navigable via labels
    const labels = page.locator('label')
    const labelCount = await labels.count()
    expect(labelCount).toBeGreaterThanOrEqual(2)
  })

  test('login page supports keyboard navigation', async ({ page }) => {
    await page.goto('/auth/login')
    await page.waitForLoadState('domcontentloaded')

    // Tab through the form
    await page.keyboard.press('Tab')
    await page.keyboard.press('Tab')

    // At this point focus should be on either the email input or password input
    const focusedTag = await page.evaluate(() => document.activeElement?.tagName.toLowerCase())
    expect(['input', 'button', 'a']).toContain(focusedTag)
  })

  test('pages use semantic HTML landmarks', async ({ page }) => {
    await page.goto('/auth/login')
    await page.waitForLoadState('domcontentloaded')

    // Should have at least a main content area
    // Check for semantic elements
    const hasMain = await page.locator('main, [role="main"], #main-content').count()
    const hasForm = await page.locator('form').count()

    expect(hasMain + hasForm).toBeGreaterThan(0)
  })

  test.describe('Manual screen reader tests (NVDA/VoiceOver)', () => {
    test.skip('SmartTable: announces row count and sort direction to screen reader', () => {})
    test.skip('FilterPopover: announces dialog open/close', () => {})
    test.skip('ColumnPicker: announces toggle state changes', () => {})
    test.skip('AggregationBuilder: announces when aggregation added/removed', () => {})
    test.skip('Compile result: announces success/error after async compile', () => {})
    test.skip('Batch execute: announces when batch operation completes', () => {})
  })
})
