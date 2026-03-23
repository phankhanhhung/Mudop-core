import { describe, it, expect, beforeEach, vi } from 'vitest'
import type { BatchResponse } from '@/types/odata'

vi.mock('@/services/odataService', () => ({
  odataService: {
    batch: vi.fn(),
  },
}))

import { BatchManager } from '../BatchManager'
import { odataService } from '@/services/odataService'

const mockedBatch = vi.mocked(odataService.batch)


describe('BatchManager', () => {
  let manager: BatchManager

  beforeEach(() => {
    manager = new BatchManager('testModule')
    mockedBatch.mockReset()
  })

  describe('$direct group', () => {
    it('sends immediately via batch with a single request', async () => {
      const response: BatchResponse = { id: expect.any(String), status: 200, body: { value: [] } }
      mockedBatch.mockResolvedValueOnce([response])

      const promise = manager.enqueue({ method: 'GET', url: 'Customers' }, '$direct')

      // Should have been called immediately (no microtask delay needed)
      expect(mockedBatch).toHaveBeenCalledTimes(1)
      expect(mockedBatch).toHaveBeenCalledWith('testModule', [
        expect.objectContaining({ method: 'GET', url: 'Customers' }),
      ])

      const result = await promise
      expect(result.status).toBe(200)
    })
  })

  describe('$auto group', () => {
    it('batches requests in the same microtask', async () => {
      mockedBatch.mockImplementation(async (_module, requests) => {
        return requests.map((r, i) => ({
          id: r.id,
          status: 200,
          body: { index: i },
        }))
      })

      const p1 = manager.enqueue({ method: 'GET', url: 'Customers' })
      const p2 = manager.enqueue({ method: 'GET', url: 'Orders' })

      // Not yet submitted — still in microtask queue
      expect(mockedBatch).not.toHaveBeenCalled()
      expect(manager.queuedCount.value).toBe(2)

      // Flush the microtask that triggers submitBatch, plus await
      // the resulting async work (submitBatch itself is async)
      const [r1, r2] = await Promise.all([p1, p2])

      // Now the batch should have been submitted with both requests
      expect(mockedBatch).toHaveBeenCalledTimes(1)
      const calledRequests = mockedBatch.mock.calls[0][1]
      expect(calledRequests).toHaveLength(2)

      expect(r1.status).toBe(200)
      expect(r2.status).toBe(200)
    })

    it('multiple $auto requests within same tick are batched together', async () => {
      mockedBatch.mockImplementation(async (_module, requests) => {
        return requests.map(r => ({
          id: r.id,
          status: 200,
          body: {},
        }))
      })

      // Enqueue three requests in same synchronous block
      const p1 = manager.enqueue({ method: 'GET', url: 'A' })
      const p2 = manager.enqueue({ method: 'GET', url: 'B' })
      const p3 = manager.enqueue({ method: 'GET', url: 'C' })

      await Promise.all([p1, p2, p3])

      // Single batch call with all three
      expect(mockedBatch).toHaveBeenCalledTimes(1)
      expect(mockedBatch.mock.calls[0][1]).toHaveLength(3)
    })
  })

  describe('custom group (submitBatch)', () => {
    it('flushes only the specified group', async () => {
      mockedBatch.mockImplementation(async (_module, requests) => {
        return requests.map(r => ({
          id: r.id,
          status: 200,
          body: {},
        }))
      })

      const p1 = manager.enqueue({ method: 'PATCH', url: 'Customers/1', body: { name: 'New' } }, 'saveAll')
      const p2 = manager.enqueue({ method: 'PATCH', url: 'Orders/2', body: { total: 100 } }, 'saveAll')

      // Not yet submitted
      expect(mockedBatch).not.toHaveBeenCalled()

      const responses = await manager.submitBatch('saveAll')

      expect(mockedBatch).toHaveBeenCalledTimes(1)
      expect(mockedBatch.mock.calls[0][1]).toHaveLength(2)
      expect(responses).toHaveLength(2)

      const [r1, r2] = await Promise.all([p1, p2])
      expect(r1.status).toBe(200)
      expect(r2.status).toBe(200)
    })
  })

  describe('queuedCount', () => {
    it('tracks pending requests', async () => {
      mockedBatch.mockImplementation(async (_module, requests) => {
        return requests.map(r => ({ id: r.id, status: 200, body: {} }))
      })

      expect(manager.queuedCount.value).toBe(0)

      manager.enqueue({ method: 'GET', url: 'A' }, 'myGroup')
      expect(manager.queuedCount.value).toBe(1)

      manager.enqueue({ method: 'GET', url: 'B' }, 'myGroup')
      expect(manager.queuedCount.value).toBe(2)

      await manager.submitBatch('myGroup')
      expect(manager.queuedCount.value).toBe(0)
    })
  })

  describe('isSubmitting', () => {
    it('is true during batch submission', async () => {
      let isSubmittingDuringBatch = false
      mockedBatch.mockImplementation(async (_module, requests) => {
        isSubmittingDuringBatch = manager.isSubmitting.value
        return requests.map(r => ({ id: r.id, status: 200, body: {} }))
      })

      manager.enqueue({ method: 'GET', url: 'A' }, 'group1')
      expect(manager.isSubmitting.value).toBe(false)

      await manager.submitBatch('group1')

      expect(isSubmittingDuringBatch).toBe(true)
      expect(manager.isSubmitting.value).toBe(false)
    })
  })

  describe('successful batch', () => {
    it('resolves individual request promises', async () => {
      mockedBatch.mockImplementation(async (_module, requests) => {
        return requests.map(r => ({
          id: r.id,
          status: 200,
          body: { resolved: true },
        }))
      })

      const p1 = manager.enqueue({ method: 'GET', url: 'Customers' }, 'g1')
      const p2 = manager.enqueue({ method: 'GET', url: 'Orders' }, 'g1')

      await manager.submitBatch('g1')

      const r1 = await p1
      const r2 = await p2
      expect(r1.status).toBe(200)
      expect(r1.body).toEqual({ resolved: true })
      expect(r2.status).toBe(200)
    })
  })

  describe('failed batch', () => {
    it('rejects individual request promises', async () => {
      mockedBatch.mockRejectedValueOnce(new Error('Network failure'))

      const p1 = manager.enqueue({ method: 'GET', url: 'Customers' }, 'g1')
      const p2 = manager.enqueue({ method: 'GET', url: 'Orders' }, 'g1')

      await expect(manager.submitBatch('g1')).rejects.toThrow('Network failure')

      await expect(p1).rejects.toThrow('Network failure')
      await expect(p2).rejects.toThrow('Network failure')
    })
  })

  describe('error ref', () => {
    it('is set on batch failure', async () => {
      mockedBatch.mockRejectedValueOnce(new Error('Server error'))

      // Capture the enqueued promise to handle its rejection
      const enqueuePromise = manager.enqueue({ method: 'GET', url: 'X' }, 'failGroup')

      expect(manager.error.value).toBeNull()

      try {
        await manager.submitBatch('failGroup')
      } catch {
        // expected — submitBatch rethrows
      }

      // Also consume the enqueued promise rejection to avoid unhandled rejection
      try {
        await enqueuePromise
      } catch {
        // expected — individual request is also rejected
      }

      expect(manager.error.value).toBe('Server error')
    })
  })

  describe('empty group submit', () => {
    it('is a no-op and returns empty array', async () => {
      const result = await manager.submitBatch('nonExistent')
      expect(result).toEqual([])
      expect(mockedBatch).not.toHaveBeenCalled()
    })
  })
})
