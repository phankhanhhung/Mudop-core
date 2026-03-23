import { ref, computed, onUnmounted, type Ref, type ComputedRef } from 'vue'
import { odataService } from '@/services/odataService'
import type { ODataQueryOptions } from '@/types/odata'

export interface DeltaChange {
  id: string
  type: 'added' | 'modified' | 'removed'
  data: Record<string, unknown>
  timestamp: Date
}

export interface UseDeltaTrackingOptions {
  module: string
  entitySet: string
  pollInterval?: number
}

export interface UseDeltaTrackingReturn {
  isTracking: Ref<boolean>
  deltaLink: Ref<string | null>
  changes: Ref<DeltaChange[]>
  isPolling: Ref<boolean>
  error: Ref<string | null>
  changeCount: ComputedRef<number>
  startTracking: () => Promise<void>
  stopTracking: () => void
  poll: () => Promise<void>
  clearChanges: () => void
  applyChanges: () => DeltaChange[]
}

export function useDeltaTracking(options: UseDeltaTrackingOptions): UseDeltaTrackingReturn {
  const { module, entitySet, pollInterval = 30000 } = options

  const isTracking = ref(false)
  const deltaLink = ref<string | null>(null)
  const changes = ref<DeltaChange[]>([])
  const isPolling = ref(false)
  const error = ref<string | null>(null)

  let intervalId: ReturnType<typeof setInterval> | null = null

  const changeCount = computed(() => changes.value.length)

  /**
   * Extract the delta token value from a full @odata.deltaLink URL.
   * The link typically looks like: ...?$deltatoken=abc123
   */
  function extractDeltaToken(link: string): string | null {
    try {
      const url = new URL(link, window.location.origin)
      return url.searchParams.get('$deltatoken')
    } catch {
      // If the link is just a token string, return as-is
      const match = link.match(/\$deltatoken=([^&]+)/)
      return match ? match[1] : link
    }
  }

  /**
   * Start change tracking by making an initial query with trackChanges=true.
   * Stores the @odata.deltaLink from the response and begins automatic polling.
   */
  async function startTracking(): Promise<void> {
    error.value = null
    changes.value = []
    deltaLink.value = null

    try {
      const queryOptions: ODataQueryOptions = {
        trackChanges: true,
        $count: true
      }

      const response = await odataService.queryDelta<Record<string, unknown>>(
        module,
        entitySet,
        queryOptions
      )

      const link = response['@odata.deltaLink']
      if (link) {
        deltaLink.value = link
        isTracking.value = true
        startPolling()
      } else {
        error.value = 'Server did not return a delta link. Change tracking may not be supported for this entity.'
      }
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to start change tracking'
    }
  }

  /**
   * Stop change tracking, clear polling interval, and reset state.
   */
  function stopTracking(): void {
    stopPolling()
    isTracking.value = false
    deltaLink.value = null
    changes.value = []
    error.value = null
  }

  /**
   * Poll for delta changes using the stored deltaLink.
   * Parses the response to identify added, modified, and removed items.
   */
  async function poll(): Promise<void> {
    if (!deltaLink.value) return

    isPolling.value = true
    error.value = null

    try {
      const token = extractDeltaToken(deltaLink.value)
      if (!token) {
        error.value = 'Invalid delta link'
        return
      }

      const queryOptions: ODataQueryOptions = {
        $deltatoken: token
      }

      const response = await odataService.queryDelta<Record<string, unknown>>(
        module,
        entitySet,
        queryOptions
      )

      // Update the delta link for the next poll
      const newLink = response['@odata.deltaLink']
      if (newLink) {
        deltaLink.value = newLink
      }

      // Parse the response items to identify changes
      const now = new Date()
      const items = response.value || []

      for (const item of items) {
        const removed = item['@removed'] as Record<string, unknown> | undefined
        const itemId = String(item['Id'] ?? item['ID'] ?? item['id'] ?? '')

        if (removed) {
          changes.value.push({
            id: itemId,
            type: 'removed',
            data: { ...item },
            timestamp: now
          })
        } else {
          // Determine if the entity was added or modified.
          // Items without @removed are either new or updated. We check for an
          // existing tracked ID to decide, but by default OData delta responses
          // do not distinguish between add and modify at protocol level.
          // We rely on a simple heuristic: if we've seen this ID before in a
          // previous delta, it's modified; otherwise it's added.
          const existingChange = changes.value.find(
            (c) => c.id === itemId && c.type !== 'removed'
          )
          const changeType: 'added' | 'modified' = existingChange ? 'modified' : 'added'

          changes.value.push({
            id: itemId,
            type: changeType,
            data: { ...item },
            timestamp: now
          })
        }
      }
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to poll for changes'
    } finally {
      isPolling.value = false
    }
  }

  /**
   * Clear all tracked changes.
   */
  function clearChanges(): void {
    changes.value = []
  }

  /**
   * Return the current changes and clear the list.
   */
  function applyChanges(): DeltaChange[] {
    const currentChanges = [...changes.value]
    changes.value = []
    return currentChanges
  }

  function startPolling(): void {
    stopPolling()
    intervalId = setInterval(() => {
      poll()
    }, pollInterval)
  }

  function stopPolling(): void {
    if (intervalId !== null) {
      clearInterval(intervalId)
      intervalId = null
    }
  }

  // Clean up polling on component unmount
  onUnmounted(() => {
    stopPolling()
  })

  return {
    isTracking,
    deltaLink,
    changes,
    isPolling,
    error,
    changeCount,
    startTracking,
    stopTracking,
    poll,
    clearChanges,
    applyChanges
  }
}
