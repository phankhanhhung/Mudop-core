/**
 * Request deduplication and cancellation utilities.
 *
 * - `dedup()` merges concurrent identical GET requests into one
 * - `cancellable()` wraps a request with AbortController support
 */

type PendingRequest<T> = {
  promise: Promise<T>
  abortController: AbortController
  refCount: number
}

const pendingRequests = new Map<string, PendingRequest<unknown>>()

/**
 * Deduplicate concurrent identical requests.
 * If the same key is requested while a previous request is still in-flight,
 * return the existing promise instead of making a new request.
 */
export function dedup<T>(key: string, requestFn: (signal: AbortSignal) => Promise<T>): Promise<T> {
  const existing = pendingRequests.get(key) as PendingRequest<T> | undefined
  if (existing) {
    existing.refCount++
    return wrapWithRefCount(key, existing)
  }

  const abortController = new AbortController()
  const promise = requestFn(abortController.signal)
  const entry: PendingRequest<T> = { promise, abortController, refCount: 1 }

  pendingRequests.set(key, entry)
  return wrapWithRefCount(key, entry)
}

function wrapWithRefCount<T>(key: string, entry: PendingRequest<T>): Promise<T> {
  return entry.promise.then(
    (result) => {
      entry.refCount--
      if (entry.refCount <= 0) {
        pendingRequests.delete(key)
      }
      return result
    },
    (err) => {
      entry.refCount--
      if (entry.refCount <= 0) {
        pendingRequests.delete(key)
      }
      throw err
    }
  )
}

/**
 * Cancel all pending deduped requests (useful on logout/tenant switch).
 */
export function cancelAllPending(): void {
  for (const [key, pending] of pendingRequests) {
    pending.abortController.abort()
    pendingRequests.delete(key)
  }
}

/**
 * Cancel a specific pending request by key.
 */
export function cancelPending(key: string): void {
  const pending = pendingRequests.get(key)
  if (pending) {
    pending.abortController.abort()
    pendingRequests.delete(key)
  }
}

/**
 * Creates a cancellable request scope — returns an abort function.
 * Useful in composables where you want to cancel previous requests
 * when new ones start (e.g., pagination, search).
 */
export function createRequestScope() {
  let currentController: AbortController | null = null

  return {
    /**
     * Get a fresh AbortSignal, cancelling any previous request.
     */
    getSignal(): AbortSignal {
      // Cancel previous in-flight request
      if (currentController) {
        currentController.abort()
      }
      currentController = new AbortController()
      return currentController.signal
    },

    /**
     * Cancel the current in-flight request without starting a new one.
     */
    cancel(): void {
      if (currentController) {
        currentController.abort()
        currentController = null
      }
    }
  }
}
