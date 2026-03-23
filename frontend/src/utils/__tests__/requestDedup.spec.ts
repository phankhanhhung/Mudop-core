import { describe, it, expect, beforeEach } from 'vitest'
import { dedup, cancelAllPending, cancelPending, createRequestScope } from '../requestDedup'

// The module keeps state in a module-level Map, so we need to isolate tests.
// cancelAllPending() clears the map between tests.

beforeEach(() => {
  cancelAllPending()
})

describe('dedup', () => {
  it('returns the same promise for concurrent identical requests', async () => {
    let callCount = 0
    const requestFn = (_signal: AbortSignal) => {
      callCount++
      return Promise.resolve('result')
    }

    const p1 = dedup('key1', requestFn)
    const p2 = dedup('key1', requestFn)

    expect(p1).toBe(p2)
    expect(callCount).toBe(1)

    const [r1, r2] = await Promise.all([p1, p2])
    expect(r1).toBe('result')
    expect(r2).toBe('result')
  })

  it('makes a new request after the first one completes', async () => {
    let callCount = 0
    const requestFn = (_signal: AbortSignal) => {
      callCount++
      return Promise.resolve(`result-${callCount}`)
    }

    const r1 = await dedup('key1', requestFn)
    expect(r1).toBe('result-1')
    expect(callCount).toBe(1)

    // After the first promise resolves, a new request should be made
    const r2 = await dedup('key1', requestFn)
    expect(r2).toBe('result-2')
    expect(callCount).toBe(2)
  })

  it('does not share promises between different keys', async () => {
    let callCount = 0
    const requestFn = (_signal: AbortSignal) => {
      callCount++
      return Promise.resolve(`result-${callCount}`)
    }

    const p1 = dedup('key1', requestFn)
    const p2 = dedup('key2', requestFn)

    expect(p1).not.toBe(p2)
    expect(callCount).toBe(2)

    const [r1, r2] = await Promise.all([p1, p2])
    expect(r1).toBe('result-1')
    expect(r2).toBe('result-2')
  })

  it('removes the entry from the map even when the request rejects', async () => {
    let callCount = 0
    const requestFn = (_signal: AbortSignal) => {
      callCount++
      return Promise.reject(new Error('fail'))
    }

    await expect(dedup('key1', requestFn)).rejects.toThrow('fail')
    expect(callCount).toBe(1)

    // After rejection, a new request should be made
    await expect(dedup('key1', requestFn)).rejects.toThrow('fail')
    expect(callCount).toBe(2)
  })

  it('passes an AbortSignal to the request function', async () => {
    let receivedSignal: AbortSignal | null = null
    const requestFn = (signal: AbortSignal) => {
      receivedSignal = signal
      return Promise.resolve('ok')
    }

    await dedup('key1', requestFn)
    expect(receivedSignal).toBeInstanceOf(AbortSignal)
    expect(receivedSignal!.aborted).toBe(false)
  })
})

describe('cancelAllPending', () => {
  it('aborts all in-flight requests', async () => {
    const signals: AbortSignal[] = []
    let resolve1!: (v: string) => void
    let resolve2!: (v: string) => void

    dedup('key1', (signal) => {
      signals.push(signal)
      return new Promise<string>((r) => { resolve1 = r })
    })
    dedup('key2', (signal) => {
      signals.push(signal)
      return new Promise<string>((r) => { resolve2 = r })
    })

    expect(signals).toHaveLength(2)
    expect(signals[0].aborted).toBe(false)
    expect(signals[1].aborted).toBe(false)

    cancelAllPending()

    expect(signals[0].aborted).toBe(true)
    expect(signals[1].aborted).toBe(true)

    // Clean up promises to avoid unhandled rejections
    resolve1('done')
    resolve2('done')
  })
})

describe('cancelPending', () => {
  it('aborts only the specified request', async () => {
    const signals: AbortSignal[] = []
    let resolve1!: (v: string) => void
    let resolve2!: (v: string) => void

    dedup('key1', (signal) => {
      signals.push(signal)
      return new Promise<string>((r) => { resolve1 = r })
    })
    dedup('key2', (signal) => {
      signals.push(signal)
      return new Promise<string>((r) => { resolve2 = r })
    })

    cancelPending('key1')

    expect(signals[0].aborted).toBe(true)
    expect(signals[1].aborted).toBe(false)

    resolve1('done')
    resolve2('done')
  })
})

describe('createRequestScope', () => {
  it('getSignal returns an AbortSignal', () => {
    const scope = createRequestScope()
    const signal = scope.getSignal()
    expect(signal).toBeInstanceOf(AbortSignal)
    expect(signal.aborted).toBe(false)
  })

  it('getSignal cancels the previous signal when called again', () => {
    const scope = createRequestScope()
    const signal1 = scope.getSignal()
    expect(signal1.aborted).toBe(false)

    const signal2 = scope.getSignal()
    expect(signal1.aborted).toBe(true)
    expect(signal2.aborted).toBe(false)
  })

  it('cancel aborts the current signal', () => {
    const scope = createRequestScope()
    const signal = scope.getSignal()
    expect(signal.aborted).toBe(false)

    scope.cancel()
    expect(signal.aborted).toBe(true)
  })

  it('cancel is a no-op if no signal was created', () => {
    const scope = createRequestScope()
    // Should not throw
    expect(() => scope.cancel()).not.toThrow()
  })

  it('cancel is a no-op after already being cancelled', () => {
    const scope = createRequestScope()
    scope.getSignal()
    scope.cancel()
    // Second cancel should not throw
    expect(() => scope.cancel()).not.toThrow()
  })
})
