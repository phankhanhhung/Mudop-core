import { ref, computed } from 'vue'

export interface TemporalState {
  asOf: string | null
  validAt: string | null
  includeHistory: boolean
}

export function useTemporal() {
  const asOf = ref<string | null>(null)
  const validAt = ref<string | null>(null)
  const includeHistory = ref(false)

  const isActive = computed(() => !!asOf.value || !!validAt.value || includeHistory.value)

  function getQueryParams(): Record<string, string> {
    const params: Record<string, string> = {}
    if (asOf.value) params.asOf = asOf.value
    if (validAt.value) params.validAt = validAt.value
    if (includeHistory.value) params.includeHistory = 'true'
    return params
  }

  function reset() {
    asOf.value = null
    validAt.value = null
    includeHistory.value = false
  }

  return {
    asOf,
    validAt,
    includeHistory,
    isActive,
    getQueryParams,
    reset
  }
}
