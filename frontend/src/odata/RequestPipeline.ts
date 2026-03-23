/**
 * Request Pipeline — middleware-based request processing chain.
 *
 * Provides:
 * - Middleware chain: auth → tenant → dedup → cache → retry → timeout
 * - Per-endpoint timeout configuration
 * - Exponential backoff retry
 * - Circuit breaker pattern
 * - Request priority (high/normal/low)
 *
 * Usage:
 *   const pipeline = new RequestPipeline()
 *   pipeline.use(authMiddleware)
 *   pipeline.use(retryMiddleware({ maxRetries: 3 }))
 *   pipeline.use(circuitBreakerMiddleware())
 *
 *   const response = await pipeline.execute({
 *     url: '/odata/myapp/Customers',
 *     method: 'GET',
 *     headers: {},
 *     metadata: { priority: 'normal' }
 *   })
 */

import type {
  PipelineMiddleware,
  PipelineRequest,
  PipelineResponse,
  RetryConfig,
} from './types'

// ---------------------------------------------------------------------------
// Pipeline
// ---------------------------------------------------------------------------

export class RequestPipeline {
  private middlewares: PipelineMiddleware[] = []

  /**
   * Add a middleware to the pipeline.
   * Middlewares execute in order of registration.
   */
  use(middleware: PipelineMiddleware): this {
    this.middlewares.push(middleware)
    return this
  }

  /**
   * Execute a request through the middleware pipeline.
   */
  async execute(request: PipelineRequest): Promise<PipelineResponse> {
    let index = 0

    const next = async (): Promise<PipelineResponse> => {
      if (index < this.middlewares.length) {
        const middleware = this.middlewares[index++]
        return middleware(request, next)
      }
      throw new Error('No terminal middleware (fetcher) registered in pipeline')
    }

    return next()
  }

  /**
   * Create a default pipeline with standard middleware.
   */
  static createDefault(): RequestPipeline {
    const pipeline = new RequestPipeline()
    pipeline.use(timeoutMiddleware())
    pipeline.use(dedupMiddleware())
    pipeline.use(retryMiddleware())
    pipeline.use(circuitBreakerMiddleware())
    pipeline.use(fetchMiddleware())
    return pipeline
  }
}

// ---------------------------------------------------------------------------
// Built-in Middleware
// ---------------------------------------------------------------------------

/**
 * Timeout middleware — aborts requests that exceed the configured timeout.
 */
export function timeoutMiddleware(defaultTimeout = 30_000): PipelineMiddleware {
  return async (request, next) => {
    const timeout = request.metadata.timeout ?? defaultTimeout
    const controller = new AbortController()
    let didTimeout = false

    // Combine with existing signal
    const originalSignal = request.signal
    if (originalSignal) {
      originalSignal.addEventListener('abort', () => controller.abort())
    }
    request.signal = controller.signal

    const timeoutId = setTimeout(() => {
      didTimeout = true
      controller.abort()
    }, timeout)

    try {
      const response = await next()
      clearTimeout(timeoutId)
      return response
    } catch (e) {
      clearTimeout(timeoutId)
      if (didTimeout) {
        throw new Error(`Request timed out after ${timeout}ms: ${request.method} ${request.url}`)
      }
      throw e
    }
  }
}

/**
 * Dedup middleware — merges concurrent identical GET requests.
 */
export function dedupMiddleware(): PipelineMiddleware {
  const pending = new Map<string, Promise<PipelineResponse>>()

  return async (request, next) => {
    if (request.method !== 'GET' || request.metadata.skipDedup) {
      return next()
    }

    const key = `${request.method}:${request.url}`
    const existing = pending.get(key)
    if (existing) {
      return existing
    }

    const promise = next().finally(() => {
      pending.delete(key)
    })

    pending.set(key, promise)
    return promise
  }
}

/**
 * Retry middleware — retries failed requests with exponential backoff.
 */
export function retryMiddleware(config?: Partial<RetryConfig>): PipelineMiddleware {
  const retryConfig: RetryConfig = {
    maxRetries: config?.maxRetries ?? 2,
    baseDelay: config?.baseDelay ?? 1000,
    multiplier: config?.multiplier ?? 2,
    retryOn: config?.retryOn ?? [408, 429, 500, 502, 503, 504],
  }

  return async (request, next) => {
    // Don't retry non-idempotent methods unless configured
    if (request.method === 'POST' && !request.metadata.retry) {
      return next()
    }

    let lastError: Error | undefined
    for (let attempt = 0; attempt <= retryConfig.maxRetries; attempt++) {
      try {
        return await next()
      } catch (e) {
        lastError = e instanceof Error ? e : new Error(String(e))

        // Check if we should retry
        const status = (e as { response?: { status?: number } })?.response?.status
        const shouldRetry = status ? retryConfig.retryOn.includes(status) : false

        if (!shouldRetry || attempt >= retryConfig.maxRetries) {
          throw e
        }

        // Exponential backoff
        const delay = retryConfig.baseDelay * Math.pow(retryConfig.multiplier, attempt)
        await sleep(delay)
      }
    }

    throw lastError ?? new Error('Retry failed')
  }
}

/**
 * Circuit breaker middleware — stops requests after consecutive failures.
 */
export function circuitBreakerMiddleware(
  options?: { failureThreshold?: number; resetTimeout?: number }
): PipelineMiddleware {
  const threshold = options?.failureThreshold ?? 5
  const resetTimeout = options?.resetTimeout ?? 30_000

  let failures = 0
  let state: 'closed' | 'open' | 'half-open' = 'closed'
  let lastFailure = 0

  return async (_request, next) => {
    // Check circuit state
    if (state === 'open') {
      if (Date.now() - lastFailure > resetTimeout) {
        state = 'half-open' // Allow one test request
      } else {
        throw new Error(`Circuit breaker is open. Service unavailable. (${failures} consecutive failures)`)
      }
    }

    try {
      const response = await next()

      // Success — reset circuit
      if (state === 'half-open') {
        state = 'closed'
      }
      failures = 0
      return response
    } catch (e) {
      failures++
      lastFailure = Date.now()

      if (failures >= threshold) {
        state = 'open'
      }

      throw e
    }
  }
}

/**
 * Priority middleware — reorders requests based on priority.
 * High priority requests skip any queuing.
 * Low priority requests are delayed when high/normal requests are active.
 */
export function priorityMiddleware(): PipelineMiddleware {
  let activeHighPriority = 0

  return async (request, next) => {
    if (request.metadata.priority === 'high') {
      activeHighPriority++
      try {
        return await next()
      } finally {
        activeHighPriority--
      }
    }

    if (request.metadata.priority === 'low' && activeHighPriority > 0) {
      // Delay low-priority requests while high-priority are active
      await sleep(100)
    }

    return next()
  }
}

/**
 * Terminal fetch middleware — actually makes the HTTP request.
 * This should be the last middleware in the chain.
 */
export function fetchMiddleware(): PipelineMiddleware {
  return async (request) => {
    const init: RequestInit = {
      method: request.method,
      headers: request.headers,
      signal: request.signal,
    }

    if (request.body && request.method !== 'GET') {
      init.body = JSON.stringify(request.body)
      if (!request.headers['Content-Type']) {
        request.headers['Content-Type'] = 'application/json'
      }
    }

    const response = await fetch(request.url, init)

    const responseHeaders: Record<string, string> = {}
    response.headers.forEach((value, key) => {
      responseHeaders[key] = value
    })

    let data: unknown
    const contentType = response.headers.get('content-type')
    if (contentType?.includes('application/json')) {
      data = await response.json()
    } else {
      data = await response.text()
    }

    if (!response.ok) {
      const error: Error & { response?: { status: number; data: unknown } } = new Error(
        `HTTP ${response.status}: ${request.method} ${request.url}`
      )
      error.response = { status: response.status, data }
      throw error
    }

    return {
      status: response.status,
      headers: responseHeaders,
      data,
    }
  }
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function sleep(ms: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, ms))
}
