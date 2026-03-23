import { ref, watch, onMounted, onUnmounted } from 'vue'
import { dashboardService, type CacheStats, type EntityCount } from '@/services/dashboardService'
import { usePreferences, type AutoRefreshInterval } from '@/utils/preferences'

/** Convert the AutoRefreshInterval preference to milliseconds (0 = off) */
function intervalToMs(interval: AutoRefreshInterval): number {
  switch (interval) {
    case '30s': return 30_000
    case '1min': return 60_000
    case '5min': return 300_000
    case 'off':
    default: return 0
  }
}

export function useDashboard() {
  const isLoading = ref(false)
  const cacheStats = ref<CacheStats | null>(null)
  const entityCounts = ref<EntityCount[]>([])
  const recentActivity = ref<any[]>([])
  const error = ref<string | null>(null)
  let refreshInterval: ReturnType<typeof setInterval> | null = null

  const { preferences } = usePreferences()

  async function loadAll() {
    isLoading.value = true
    error.value = null
    try {
      const [stats, counts, activity] = await Promise.allSettled([
        dashboardService.getCacheStats(),
        dashboardService.getEntityCounts(),
        dashboardService.getRecentActivity()
      ])

      if (stats.status === 'fulfilled') cacheStats.value = stats.value
      if (counts.status === 'fulfilled') entityCounts.value = counts.value
      if (activity.status === 'fulfilled') recentActivity.value = activity.value
    } catch (e: any) {
      error.value = e?.message || 'Failed to load dashboard data'
    } finally {
      isLoading.value = false
    }
  }

  function startAutoRefresh(intervalMs: number) {
    stopAutoRefresh()
    if (intervalMs > 0) {
      refreshInterval = setInterval(loadAll, intervalMs)
    }
  }

  function stopAutoRefresh() {
    if (refreshInterval) {
      clearInterval(refreshInterval)
      refreshInterval = null
    }
  }

  // React to preference changes
  watch(
    () => preferences.value.autoRefreshInterval,
    (newInterval) => {
      startAutoRefresh(intervalToMs(newInterval))
    }
  )

  onMounted(() => {
    loadAll()
    startAutoRefresh(intervalToMs(preferences.value.autoRefreshInterval))
  })

  onUnmounted(() => {
    stopAutoRefresh()
  })

  return {
    isLoading,
    cacheStats,
    entityCounts,
    recentActivity,
    error,
    refresh: loadAll
  }
}
