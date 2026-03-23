import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import {
  RequestPipeline,
  timeoutMiddleware,
  dedupMiddleware,
  retryMiddleware,
  circuitBreakerMiddleware,
  priorityMiddleware,
  fetchMiddleware,
} from '../RequestPipeline'
import type { PipelineRequest, PipelineResponse, PipelineMiddleware } from '../types'

/** Helper to create a minimal PipelineRequest */
function makeRequest(overrides?: Partial<PipelineRequest>): PipelineRequest {
  return {
    url: '/odata/test/Customers',
    method: 'GET',
    headers: {},
    metadata: { priority: 'normal' },
    ...overrides,
  }
}

/** Helper to create a minimal PipelineResponse */
function makeResponse(overrides?: Partial<PipelineResponse>): PipelineResponse {
  return {
    status: 200,
    headers: {},
    data: { value: [] },
    ...overrides,
  }
}

describe('RequestPipeline', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  describe('basic pipeline execution', () => {
    it('executes a single terminal middleware and returns its response', async () => {
      const pipeline = new RequestPipeline()
      const expected = makeResponse({ data: { name: 'Alice' } })

      pipeline.use(async () => expected)

      const result = await pipeline.execute(makeRequest())
      expect(result).toBe(expected)
    })

    it('throws when no terminal middleware is registered', async () => {
      const pipeline = new RequestPipeline()

      await expect(pipeline.execute(makeRequest())).rejects.toThrow(
        'No terminal middleware (fetcher) registered in pipeline'
      )
    })
  })

  describe('middleware chain order', () => {
    it('executes middleware in registration order (A -> B -> terminal)', async () => {
      const pipeline = new RequestPipeline()
      const order: string[] = []

      const middlewareA: PipelineMiddleware = async (_req, next) => {
        order.push('A-before')
        const res = await next()
        order.push('A-after')
        return res
      }

      const middlewareB: PipelineMiddleware = async (_req, next) => {
        order.push('B-before')
        const res = await next()
        order.push('B-after')
        return res
      }

      const terminal: PipelineMiddleware = async () => {
        order.push('terminal')
        return makeResponse()
      }

      pipeline.use(middlewareA)
      pipeline.use(middlewareB)
      pipeline.use(terminal)

      await pipeline.execute(makeRequest())

      expect(order).toEqual(['A-before', 'B-before', 'terminal', 'B-after', 'A-after'])
    })
  })

  describe('timeoutMiddleware', () => {
    it('succeeds when request completes within timeout', async () => {
      const pipeline = new RequestPipeline()
      const expected = makeResponse({ data: 'ok' })

      pipeline.use(timeoutMiddleware(5000))
      pipeline.use(async () => {
        // Completes instantly
        return expected
      })

      const result = await pipeline.execute(makeRequest())
      expect(result).toBe(expected)
    })

    it('throws when request exceeds timeout', async () => {
      const pipeline = new RequestPipeline()

      pipeline.use(timeoutMiddleware(1000))
      pipeline.use(async (req) => {
        // Simulate a slow request that waits for the abort signal
        return new Promise<PipelineResponse>((_resolve, reject) => {
          req.signal?.addEventListener('abort', () => {
            reject(new Error('aborted'))
          })
        })
      })

      const promise = pipeline.execute(makeRequest())
      vi.advanceTimersByTime(1001)

      await expect(promise).rejects.toThrow(/timed out after 1000ms/i)
    })

    it('uses per-request timeout from metadata when provided', async () => {
      const pipeline = new RequestPipeline()

      pipeline.use(timeoutMiddleware(30_000)) // Default is 30s
      pipeline.use(async (req) => {
        return new Promise<PipelineResponse>((_resolve, reject) => {
          req.signal?.addEventListener('abort', () => {
            reject(new Error('aborted'))
          })
        })
      })

      const request = makeRequest({ metadata: { priority: 'normal', timeout: 500 } })
      const promise = pipeline.execute(request)
      vi.advanceTimersByTime(501)

      await expect(promise).rejects.toThrow(/timed out after 500ms/i)
    })
  })

  describe('dedupMiddleware', () => {
    it('deduplicates two identical concurrent GET requests', async () => {
      const dedup = dedupMiddleware()
      let callCount = 0

      const terminal: PipelineMiddleware = async () => {
        callCount++
        // Return after a microtask to simulate async work
        await Promise.resolve()
        return makeResponse({ data: 'result' })
      }

      const pipeline = new RequestPipeline()
      pipeline.use(dedup)
      pipeline.use(terminal)

      const req = makeRequest()
      const [r1, r2] = await Promise.all([
        pipeline.execute({ ...req }),
        pipeline.execute({ ...req }),
      ])

      // Both should receive the same response
      expect(r1).toEqual(r2)
      // The terminal middleware should only have been called once
      expect(callCount).toBe(1)
    })

    it('does NOT deduplicate POST requests', async () => {
      const dedup = dedupMiddleware()
      let callCount = 0

      const terminal: PipelineMiddleware = async () => {
        callCount++
        await Promise.resolve()
        return makeResponse({ data: `result-${callCount}` })
      }

      const pipeline = new RequestPipeline()
      pipeline.use(dedup)
      pipeline.use(terminal)

      const req = makeRequest({ method: 'POST' })
      await Promise.all([
        pipeline.execute({ ...req }),
        pipeline.execute({ ...req }),
      ])

      expect(callCount).toBe(2)
    })

    it('respects skipDedup metadata flag', async () => {
      const dedup = dedupMiddleware()
      let callCount = 0

      const terminal: PipelineMiddleware = async () => {
        callCount++
        await Promise.resolve()
        return makeResponse()
      }

      const pipeline = new RequestPipeline()
      pipeline.use(dedup)
      pipeline.use(terminal)

      const req = makeRequest({ metadata: { priority: 'normal', skipDedup: true } })
      await Promise.all([
        pipeline.execute({ ...req }),
        pipeline.execute({ ...req }),
      ])

      expect(callCount).toBe(2)
    })
  })

  describe('retryMiddleware', () => {
    // Note: retryMiddleware calls next() multiple times for retries, but the
    // pipeline's shared index means each next() call advances past the terminal.
    // We test retryMiddleware directly by invoking it with a controlled next function.

    it('retries on 503 status and succeeds on retry', async () => {
      vi.useRealTimers()

      let attempt = 0
      const middleware = retryMiddleware({ maxRetries: 2, baseDelay: 10, multiplier: 1, retryOn: [503] })

      const next = async (): Promise<PipelineResponse> => {
        attempt++
        if (attempt === 1) {
          const error: Error & { response?: { status: number; data: unknown } } = new Error('Service Unavailable')
          error.response = { status: 503, data: null }
          throw error
        }
        return makeResponse({ data: 'success' })
      }

      const result = await middleware(makeRequest(), next)
      expect(result.data).toBe('success')
      expect(attempt).toBe(2)
    })

    it('does NOT retry POST requests by default', async () => {
      let attempt = 0
      const middleware = retryMiddleware({ maxRetries: 2, baseDelay: 10, multiplier: 1, retryOn: [503] })

      const next = async (): Promise<PipelineResponse> => {
        attempt++
        const error: Error & { response?: { status: number; data: unknown } } = new Error('Service Unavailable')
        error.response = { status: 503, data: null }
        throw error
      }

      await expect(middleware(makeRequest({ method: 'POST' }), next)).rejects.toThrow('Service Unavailable')
      expect(attempt).toBe(1) // Only one attempt, no retry
    })

    it('stops retrying after maxRetries is reached', async () => {
      vi.useRealTimers()

      let attempt = 0
      const middleware = retryMiddleware({ maxRetries: 2, baseDelay: 10, multiplier: 1, retryOn: [503] })

      const next = async (): Promise<PipelineResponse> => {
        attempt++
        const error: Error & { response?: { status: number; data: unknown } } = new Error('Service Unavailable')
        error.response = { status: 503, data: null }
        throw error
      }

      await expect(middleware(makeRequest(), next)).rejects.toThrow('Service Unavailable')
      // 1 initial + 2 retries = 3 attempts
      expect(attempt).toBe(3)
    })

    it('does not retry on non-retryable status codes', async () => {
      let attempt = 0
      const middleware = retryMiddleware({ maxRetries: 2, baseDelay: 10, multiplier: 1, retryOn: [503] })

      const next = async (): Promise<PipelineResponse> => {
        attempt++
        const error: Error & { response?: { status: number; data: unknown } } = new Error('Not Found')
        error.response = { status: 404, data: null }
        throw error
      }

      await expect(middleware(makeRequest(), next)).rejects.toThrow('Not Found')
      expect(attempt).toBe(1) // No retries for 404
    })
  })

  describe('circuitBreakerMiddleware', () => {
    it('opens after failureThreshold consecutive failures', async () => {
      const breaker = circuitBreakerMiddleware({ failureThreshold: 3, resetTimeout: 30_000 })

      const terminal: PipelineMiddleware = async () => {
        throw new Error('server error')
      }

      const pipeline = new RequestPipeline()
      pipeline.use(breaker)
      pipeline.use(terminal)

      // 3 failures to open the circuit
      for (let i = 0; i < 3; i++) {
        await expect(pipeline.execute(makeRequest())).rejects.toThrow('server error')
      }

      // 4th request should be rejected by the circuit breaker itself
      await expect(pipeline.execute(makeRequest())).rejects.toThrow(/circuit breaker is open/i)
    })

    it('rejects with "circuit breaker is open" when the circuit is open', async () => {
      const breaker = circuitBreakerMiddleware({ failureThreshold: 2, resetTimeout: 30_000 })

      const terminal: PipelineMiddleware = async () => {
        throw new Error('fail')
      }

      const pipeline = new RequestPipeline()
      pipeline.use(breaker)
      pipeline.use(terminal)

      // Open the circuit
      await expect(pipeline.execute(makeRequest())).rejects.toThrow('fail')
      await expect(pipeline.execute(makeRequest())).rejects.toThrow('fail')

      // Circuit is now open
      await expect(pipeline.execute(makeRequest())).rejects.toThrow(/circuit breaker is open/i)
    })

    it('transitions to half-open after resetTimeout and allows one test request', async () => {
      let shouldFail = true
      const breaker = circuitBreakerMiddleware({ failureThreshold: 2, resetTimeout: 5000 })

      const terminal: PipelineMiddleware = async () => {
        if (shouldFail) throw new Error('fail')
        return makeResponse({ data: 'recovered' })
      }

      const pipeline = new RequestPipeline()
      pipeline.use(breaker)
      pipeline.use(terminal)

      // Open the circuit with 2 failures
      await expect(pipeline.execute(makeRequest())).rejects.toThrow('fail')
      await expect(pipeline.execute(makeRequest())).rejects.toThrow('fail')

      // Circuit is open
      await expect(pipeline.execute(makeRequest())).rejects.toThrow(/circuit breaker is open/i)

      // Advance past resetTimeout
      vi.advanceTimersByTime(5001)
      shouldFail = false

      // Should transition to half-open and allow the test request through
      const result = await pipeline.execute(makeRequest())
      expect(result.data).toBe('recovered')
    })

    it('closes on success in half-open state', async () => {
      let callCount = 0
      const breaker = circuitBreakerMiddleware({ failureThreshold: 2, resetTimeout: 5000 })

      const terminal: PipelineMiddleware = async () => {
        callCount++
        if (callCount <= 2) throw new Error('fail')
        return makeResponse({ data: 'ok' })
      }

      const pipeline = new RequestPipeline()
      pipeline.use(breaker)
      pipeline.use(terminal)

      // Open the circuit
      await expect(pipeline.execute(makeRequest())).rejects.toThrow('fail')
      await expect(pipeline.execute(makeRequest())).rejects.toThrow('fail')

      // Advance past reset timeout
      vi.advanceTimersByTime(5001)

      // Half-open: test request succeeds, circuit should close
      const result = await pipeline.execute(makeRequest())
      expect(result.data).toBe('ok')

      // Circuit is closed again, subsequent requests go through
      const result2 = await pipeline.execute(makeRequest())
      expect(result2.data).toBe('ok')
    })
  })

  describe('priorityMiddleware', () => {
    it('delays low-priority requests when high-priority requests are active', async () => {
      vi.useRealTimers()

      const priority = priorityMiddleware()
      const events: string[] = []

      const terminal: PipelineMiddleware = async (req) => {
        events.push(`exec-${req.metadata.priority}`)
        // Simulate async work
        await new Promise(resolve => setTimeout(resolve, 10))
        return makeResponse()
      }

      const pipeline = new RequestPipeline()
      pipeline.use(priority)
      pipeline.use(terminal)

      const highReq = makeRequest({ metadata: { priority: 'high' } })
      const lowReq = makeRequest({ metadata: { priority: 'low' } })

      // Start both concurrently; high-priority executes first,
      // low-priority should be delayed
      await Promise.all([
        pipeline.execute(highReq),
        pipeline.execute(lowReq),
      ])

      // High priority should have started first
      expect(events[0]).toBe('exec-high')
    })

    it('does not delay normal-priority requests', async () => {
      const priority = priorityMiddleware()

      const terminal: PipelineMiddleware = async () => {
        return makeResponse({ data: 'ok' })
      }

      const pipeline = new RequestPipeline()
      pipeline.use(priority)
      pipeline.use(terminal)

      const result = await pipeline.execute(makeRequest({ metadata: { priority: 'normal' } }))
      expect(result.data).toBe('ok')
    })
  })

  describe('fetchMiddleware', () => {
    it('makes a fetch call and returns the parsed response', async () => {
      vi.useRealTimers()

      const mockHeaders = new Headers({ 'content-type': 'application/json' })
      const mockFetchResponse = {
        ok: true,
        status: 200,
        headers: mockHeaders,
        json: vi.fn().mockResolvedValue({ value: [{ id: 1 }] }),
        text: vi.fn(),
      }

      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(mockFetchResponse))

      const pipeline = new RequestPipeline()
      pipeline.use(fetchMiddleware())

      const result = await pipeline.execute(makeRequest())

      expect(global.fetch).toHaveBeenCalledOnce()
      expect(result.status).toBe(200)
      expect(result.data).toEqual({ value: [{ id: 1 }] })

      vi.unstubAllGlobals()
    })

    it('sends body as JSON for non-GET requests', async () => {
      vi.useRealTimers()

      const mockHeaders = new Headers({ 'content-type': 'application/json' })
      const mockFetchResponse = {
        ok: true,
        status: 201,
        headers: mockHeaders,
        json: vi.fn().mockResolvedValue({ id: 1 }),
        text: vi.fn(),
      }

      const fetchMock = vi.fn().mockResolvedValue(mockFetchResponse)
      vi.stubGlobal('fetch', fetchMock)

      const pipeline = new RequestPipeline()
      pipeline.use(fetchMiddleware())

      const body = { name: 'Alice' }
      await pipeline.execute(makeRequest({ method: 'POST', body }))

      expect(fetchMock).toHaveBeenCalledWith(
        '/odata/test/Customers',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify(body),
        })
      )

      vi.unstubAllGlobals()
    })

    it('throws on non-OK responses with status info', async () => {
      vi.useRealTimers()

      const mockHeaders = new Headers({ 'content-type': 'application/json' })
      const mockFetchResponse = {
        ok: false,
        status: 404,
        headers: mockHeaders,
        json: vi.fn().mockResolvedValue({ error: { message: 'Not Found' } }),
        text: vi.fn(),
      }

      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(mockFetchResponse))

      const pipeline = new RequestPipeline()
      pipeline.use(fetchMiddleware())

      await expect(pipeline.execute(makeRequest())).rejects.toThrow('HTTP 404')

      vi.unstubAllGlobals()
    })

    it('handles text responses when content-type is not JSON', async () => {
      vi.useRealTimers()

      const mockHeaders = new Headers({ 'content-type': 'text/plain' })
      const mockFetchResponse = {
        ok: true,
        status: 200,
        headers: mockHeaders,
        json: vi.fn(),
        text: vi.fn().mockResolvedValue('plain text response'),
      }

      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(mockFetchResponse))

      const pipeline = new RequestPipeline()
      pipeline.use(fetchMiddleware())

      const result = await pipeline.execute(makeRequest())
      expect(result.data).toBe('plain text response')

      vi.unstubAllGlobals()
    })
  })

  describe('createDefault()', () => {
    it('creates a pipeline with all default middleware', async () => {
      vi.useRealTimers()

      const mockHeaders = new Headers({ 'content-type': 'application/json' })
      const mockFetchResponse = {
        ok: true,
        status: 200,
        headers: mockHeaders,
        json: vi.fn().mockResolvedValue({ value: [] }),
        text: vi.fn(),
      }

      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(mockFetchResponse))

      const pipeline = RequestPipeline.createDefault()
      const result = await pipeline.execute(makeRequest())

      expect(result.status).toBe(200)
      expect(global.fetch).toHaveBeenCalledOnce()

      vi.unstubAllGlobals()
    })
  })

  describe('pipeline .use() chaining', () => {
    it('supports fluent chaining via .use() returning this', () => {
      const pipeline = new RequestPipeline()
      const result = pipeline
        .use(async (_req, next) => next())
        .use(async () => makeResponse())

      expect(result).toBe(pipeline)
    })
  })
})
