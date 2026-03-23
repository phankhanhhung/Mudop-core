import { authenticatedTest as test, expect, isBackendAvailable, spaNavigate } from './fixtures'

/**
 * Tests that the layout does not produce flickering when scrolling.
 *
 * The main checks:
 * 1. The browser window should NOT have a vertical scrollbar (all scroll is internal)
 * 2. The main area uses internal scrolling via overflow-y-auto
 * 3. The DynamicPage header collapse/expand should be stable (no rapid toggling)
 */
test.describe('Scroll flicker prevention', () => {
  test('browser window should not have vertical scrollbar on dashboard', async ({ authenticatedPage: page }) => {
    // Dashboard uses DefaultLayout, so this tests the layout fix
    const hasScrollbar = await page.evaluate(() => {
      return document.documentElement.scrollHeight > document.documentElement.clientHeight
    })

    expect(hasScrollbar).toBe(false)
  })

  test('outer layout container prevents browser scroll', async ({ authenticatedPage: page }) => {
    // Check that the outermost layout div has overflow hidden and is viewport-height
    const layoutInfo = await page.evaluate(() => {
      // Find the root layout container (h-screen with overflow-hidden)
      const el = document.querySelector('[class*="h-screen"][class*="overflow-hidden"]')
      if (!el) {
        // Fallback: check all direct children of #app
        const appEl = document.getElementById('app')
        if (!appEl?.firstElementChild) return { found: false, details: 'no app root' }
        const style = window.getComputedStyle(appEl.firstElementChild)
        return {
          found: false,
          details: `app root: h=${style.height}, overflow=${style.overflow}, classes=${appEl.firstElementChild.className.substring(0, 100)}`
        }
      }
      const style = window.getComputedStyle(el)
      return {
        found: true,
        height: style.height,
        overflow: style.overflow,
        overflowY: style.overflowY
      }
    })

    expect(layoutInfo.found).toBe(true)
    if (layoutInfo.found) {
      expect(layoutInfo.overflow).toBe('hidden')
    }
  })

  test('main content area scrolls internally', async ({ authenticatedPage: page }) => {
    const mainInfo = await page.evaluate(() => {
      const main = document.querySelector('main#main-content')
      if (!main) return { found: false }
      const style = window.getComputedStyle(main)
      return {
        found: true,
        overflowY: style.overflowY,
        minHeight: style.minHeight,
        flex: style.flex
      }
    })

    expect(mainInfo.found).toBe(true)
    if (mainInfo.found) {
      expect(['auto', 'scroll']).toContain(mainInfo.overflowY)
    }
  })

  test('entity list does not produce layout flicker on scroll', async ({ authenticatedPage: page }) => {
    // Check if backend is available (needed for entity list data)
    const backendUp = await isBackendAvailable(page)
    test.skip(!backendUp, 'Backend not available')

    // Navigate to an entity list (try Core.Currencies as a common entity)
    await spaNavigate(page, '/odata/Core/Currencies')
    await page.waitForTimeout(2000)

    // Check that browser still has no scrollbar
    const hasScrollbar = await page.evaluate(() => {
      return document.documentElement.scrollHeight > document.documentElement.clientHeight
    })

    expect(hasScrollbar).toBe(false)

    // Find the DynamicPage content area and simulate scroll
    const dynamicPageContent = page.locator('.dynamic-page .overflow-y-auto').first()

    if (await dynamicPageContent.isVisible({ timeout: 3000 }).catch(() => false)) {
      // Record layout shift events
      const shifts = await page.evaluate(async () => {
        return new Promise<number>((resolve) => {
          let shiftCount = 0
          const observer = new PerformanceObserver((list) => {
            for (const entry of list.getEntries()) {
              if ((entry as any).value > 0.01) {
                shiftCount++
              }
            }
          })

          try {
            observer.observe({ type: 'layout-shift', buffered: false })
          } catch {
            // layout-shift may not be available in all browsers
            resolve(0)
            return
          }

          // Scroll the content area
          const contentArea = document.querySelector('.dynamic-page .overflow-y-auto')
          if (contentArea) {
            contentArea.scrollTop = 100
            setTimeout(() => {
              contentArea.scrollTop = 200
              setTimeout(() => {
                contentArea.scrollTop = 0
                setTimeout(() => {
                  observer.disconnect()
                  resolve(shiftCount)
                }, 500)
              }, 300)
            }, 300)
          } else {
            resolve(0)
          }
        })
      })

      // Should have minimal or no layout shifts (< 3 is acceptable)
      expect(shifts).toBeLessThan(3)
    }
  })
})
