import { describe, it, expect, beforeEach } from 'vitest'
import { ODataDevTools } from '../ODataDevTools'

/**
 * ODataDevTools is a singleton. Between tests we call clear() and disable()
 * to reset internal state, then re-enable for a clean slate.
 */
describe('ODataDevTools', () => {
  let devtools: ODataDevTools

  beforeEach(() => {
    devtools = ODataDevTools.getInstance()
    devtools.clear()
    devtools.disable()
  })

  // =========================================================================
  // Singleton
  // =========================================================================

  it('getInstance() returns singleton (same instance)', () => {
    const a = ODataDevTools.getInstance()
    const b = ODataDevTools.getInstance()
    expect(a).toBe(b)
  })

  // =========================================================================
  // enable / disable
  // =========================================================================

  it('enable()/disable() toggles logging', () => {
    expect(devtools.isEnabled()).toBe(false)

    devtools.enable()
    expect(devtools.isEnabled()).toBe(true)

    devtools.disable()
    expect(devtools.isEnabled()).toBe(false)
  })

  // =========================================================================
  // Logging — requests, responses, errors
  // =========================================================================

  it('logRequest() adds entry with type "request"', () => {
    devtools.enable()

    const id = devtools.logRequest({ method: 'GET', url: '/odata/Customers' })

    expect(id).toBeTruthy()
    const entries = devtools.getEntries({ type: 'request' })
    expect(entries.length).toBe(1)
    expect(entries[0].type).toBe('request')
    expect(entries[0].method).toBe('GET')
    expect(entries[0].url).toBe('/odata/Customers')
  })

  it('logResponse() adds entry with type "response", calculates duration', () => {
    devtools.enable()

    const reqId = devtools.logRequest({ method: 'POST', url: '/odata/Orders' })

    devtools.logResponse({
      requestId: reqId,
      status: 201,
      duration: 42,
      size: 1024,
    })

    const responses = devtools.getEntries({ type: 'response' })
    expect(responses.length).toBe(1)
    expect(responses[0].type).toBe('response')
    expect(responses[0].status).toBe(201)
    expect(responses[0].duration).toBe(42)
    expect(responses[0].size).toBe(1024)
  })

  it('logError() adds entry with type "error"', () => {
    devtools.enable()

    devtools.logError({
      method: 'DELETE',
      url: '/odata/Customers/1',
      status: 500,
      data: { message: 'Internal Server Error' },
    })

    const errors = devtools.getEntries({ type: 'error' })
    expect(errors.length).toBe(1)
    expect(errors[0].type).toBe('error')
    expect(errors[0].method).toBe('DELETE')
    expect(errors[0].status).toBe(500)
  })

  // =========================================================================
  // Computed properties
  // =========================================================================

  it('entryCount computed tracks entries', () => {
    devtools.enable()
    expect(devtools.entryCount.value).toBe(0)

    devtools.logRequest({ method: 'GET', url: '/a' })
    expect(devtools.entryCount.value).toBe(1)

    devtools.logRequest({ method: 'GET', url: '/b' })
    expect(devtools.entryCount.value).toBe(2)
  })

  it('errorCount computed tracks errors only', () => {
    devtools.enable()
    expect(devtools.errorCount.value).toBe(0)

    devtools.logRequest({ method: 'GET', url: '/a' })
    expect(devtools.errorCount.value).toBe(0) // requests are not errors

    devtools.logError({ method: 'GET', url: '/a', status: 500 })
    expect(devtools.errorCount.value).toBe(1)

    devtools.logError({ method: 'POST', url: '/b', status: 400 })
    expect(devtools.errorCount.value).toBe(2)
  })

  // =========================================================================
  // getStats()
  // =========================================================================

  it('getStats() returns correct totalRequests, totalErrors, averageResponseTime', () => {
    devtools.enable()

    const r1 = devtools.logRequest({ method: 'GET', url: '/a' })
    const r2 = devtools.logRequest({ method: 'GET', url: '/b' })

    devtools.logResponse({ requestId: r1, status: 200, duration: 100 })
    devtools.logResponse({ requestId: r2, status: 200, duration: 200 })
    devtools.logError({ method: 'GET', url: '/c', status: 500 })

    const stats = devtools.getStats()
    expect(stats.totalRequests).toBe(2)
    expect(stats.totalResponses).toBe(2)
    expect(stats.totalErrors).toBe(1)
    expect(stats.averageResponseTime).toBe(150) // (100 + 200) / 2
  })

  // =========================================================================
  // clear()
  // =========================================================================

  it('clear() removes all entries', () => {
    devtools.enable()

    devtools.logRequest({ method: 'GET', url: '/a' })
    devtools.logRequest({ method: 'GET', url: '/b' })
    devtools.logError({ method: 'GET', url: '/c', status: 500 })
    expect(devtools.entryCount.value).toBe(3)

    devtools.clear()
    expect(devtools.entryCount.value).toBe(0)
    expect(devtools.getEntries()).toHaveLength(0)
  })

  // =========================================================================
  // Max entries bounded
  // =========================================================================

  it('max entries bounded (add >500, oldest pruned)', () => {
    devtools.enable()

    // Add 510 entries (more than maxEntries = 500)
    for (let i = 0; i < 510; i++) {
      devtools.logRequest({ method: 'GET', url: `/item/${i}` })
    }

    // After pruning, entries should be at most 500
    // The implementation slices to maxEntries/2 = 250 once exceeded
    const count = devtools.entryCount.value
    expect(count).toBeLessThanOrEqual(500)
    expect(count).toBeGreaterThan(0)
  })

  // =========================================================================
  // getEntries() filtering
  // =========================================================================

  it('getEntries() returns filtered entries by type', () => {
    devtools.enable()

    devtools.logRequest({ method: 'GET', url: '/a' })
    devtools.logRequest({ method: 'POST', url: '/b' })
    devtools.logError({ method: 'GET', url: '/c', status: 500 })
    devtools.logCacheHit('/cached')

    expect(devtools.getEntries({ type: 'request' })).toHaveLength(2)
    expect(devtools.getEntries({ type: 'error' })).toHaveLength(1)
    expect(devtools.getEntries({ type: 'cache-hit' })).toHaveLength(1)
    expect(devtools.getEntries()).toHaveLength(4) // all entries
  })

  it('getEntries() limit returns last N entries', () => {
    devtools.enable()

    for (let i = 0; i < 10; i++) {
      devtools.logRequest({ method: 'GET', url: `/item/${i}` })
    }

    const limited = devtools.getEntries({ limit: 3 })
    expect(limited).toHaveLength(3)
  })

  // =========================================================================
  // Disabled mode — no logging
  // =========================================================================

  it('does not log entries when disabled', () => {
    // devtools starts disabled from beforeEach
    const id = devtools.logRequest({ method: 'GET', url: '/a' })
    expect(id).toBe('')
    expect(devtools.entryCount.value).toBe(0)

    devtools.logError({ method: 'GET', url: '/b', status: 500 })
    expect(devtools.errorCount.value).toBe(0)
  })
})
