import { ref, computed, type Ref } from 'vue'
import { odataService } from '@/services/odataService'
import type { AggregationConfig, AggregationResult, AggregationPreset, ChartType } from '@/types/aggregation'

export interface UseAggregationOptions {
  module: string
  entitySet: string
}

export interface UseAggregationReturn {
  config: Ref<AggregationConfig>
  results: Ref<AggregationResult | null>
  isLoading: Ref<boolean>
  error: Ref<string | null>
  buildApplyString: (cfg: AggregationConfig) => string
  execute: () => Promise<void>
  reset: () => void
  /** Summary stats derived from current results */
  summaryStats: Ref<SummaryStats>
  /** Preset management */
  presets: Ref<AggregationPreset[]>
  savePreset: (name: string, chartType: ChartType) => void
  loadPreset: (id: string) => AggregationPreset | undefined
  deletePreset: (id: string) => void
}

export interface SummaryStats {
  totalGroups: number
  totalRecords: number
  primarySum: number
  primaryAvg: number
  primaryMin: number
  primaryMax: number
}

function getPresetsStorageKey(module: string, entitySet: string): string {
  return `bmmdl-aggregation-presets-${module}-${entitySet}`
}

function loadPresetsFromStorage(module: string, entitySet: string): AggregationPreset[] {
  try {
    const raw = localStorage.getItem(getPresetsStorageKey(module, entitySet))
    return raw ? JSON.parse(raw) : []
  } catch {
    return []
  }
}

function savePresetsToStorage(module: string, entitySet: string, presets: AggregationPreset[]): void {
  localStorage.setItem(getPresetsStorageKey(module, entitySet), JSON.stringify(presets))
}

export function useAggregation(options: UseAggregationOptions): UseAggregationReturn {
  const { module, entitySet } = options

  const config = ref<AggregationConfig>({
    groupByFields: [],
    aggregations: [],
    filter: ''
  })

  const results = ref<AggregationResult | null>(null)
  const isLoading = ref(false)
  const error = ref<string | null>(null)

  // Presets
  const presets = ref<AggregationPreset[]>(loadPresetsFromStorage(module, entitySet))

  function buildApplyString(cfg: AggregationConfig): string {
    const { groupByFields, aggregations, filter } = cfg

    // Build aggregate expressions
    const aggregateExprs: string[] = []
    for (const agg of aggregations) {
      if (agg.func === 'count') {
        aggregateExprs.push(`$count as ${agg.alias}`)
      } else if (agg.func === 'countdistinct') {
        aggregateExprs.push(`${agg.field} with countdistinct as ${agg.alias}`)
      } else {
        aggregateExprs.push(`${agg.field} with ${agg.func} as ${agg.alias}`)
      }
    }

    const groupByPart = groupByFields.join(',')
    const aggregatePart = aggregateExprs.join(',')

    let applyString = `groupby((${groupByPart}),aggregate(${aggregatePart}))`

    // Prepend filter if present
    if (filter && filter.trim()) {
      applyString = `filter(${filter.trim()})/${applyString}`
    }

    return applyString
  }

  async function execute(): Promise<void> {
    const cfg = config.value

    if (cfg.groupByFields.length === 0) {
      error.value = 'Please select at least one group-by field'
      return
    }

    if (cfg.aggregations.length === 0) {
      error.value = 'Please add at least one aggregation'
      return
    }

    for (const agg of cfg.aggregations) {
      if (agg.func !== 'count' && !agg.field) {
        error.value = `Please select a field for aggregation "${agg.alias}"`
        return
      }
    }

    isLoading.value = true
    error.value = null
    results.value = null

    try {
      const applyString = buildApplyString(cfg)
      const response = await odataService.query<Record<string, unknown>>(module, entitySet, {
        $apply: applyString
      })

      const rawData = response.value || []
      const labels: string[] = []
      const series: Record<string, number[]> = {}

      // Initialize series arrays
      for (const agg of cfg.aggregations) {
        series[agg.alias] = []
      }

      for (const row of rawData) {
        // Build composite label from all group-by fields
        const labelParts: string[] = cfg.groupByFields.map(f => {
          const val = row[f]
          return val != null ? String(val) : '(empty)'
        })
        labels.push(labelParts.join(' / '))

        // Extract values for each aggregation
        for (const agg of cfg.aggregations) {
          const val = row[agg.alias]
          series[agg.alias].push(typeof val === 'number' ? val : Number(val) || 0)
        }
      }

      // Primary values = first aggregation's series (for chart compatibility)
      const primaryAlias = cfg.aggregations[0]?.alias || ''
      const values = series[primaryAlias] || []

      results.value = { labels, values, series, rawData }
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Aggregation query failed'
    } finally {
      isLoading.value = false
    }
  }

  function reset(): void {
    results.value = null
    error.value = null
  }

  // Summary stats
  const summaryStats = computed<SummaryStats>(() => {
    if (!results.value) {
      return { totalGroups: 0, totalRecords: 0, primarySum: 0, primaryAvg: 0, primaryMin: 0, primaryMax: 0 }
    }
    const vals = results.value.values
    const sum = vals.reduce((a, b) => a + b, 0)
    const avg = vals.length > 0 ? sum / vals.length : 0
    const min = vals.length > 0 ? Math.min(...vals) : 0
    const max = vals.length > 0 ? Math.max(...vals) : 0
    return {
      totalGroups: results.value.labels.length,
      totalRecords: sum,
      primarySum: sum,
      primaryAvg: Math.round(avg * 100) / 100,
      primaryMin: min,
      primaryMax: max
    }
  })

  // Preset management
  function savePreset(name: string, chartType: ChartType): void {
    const preset: AggregationPreset = {
      id: crypto.randomUUID(),
      name,
      config: JSON.parse(JSON.stringify(config.value)),
      chartType,
      createdAt: new Date().toISOString()
    }
    presets.value = [...presets.value, preset]
    savePresetsToStorage(module, entitySet, presets.value)
  }

  function loadPreset(id: string): AggregationPreset | undefined {
    return presets.value.find(p => p.id === id)
  }

  function deletePreset(id: string): void {
    presets.value = presets.value.filter(p => p.id !== id)
    savePresetsToStorage(module, entitySet, presets.value)
  }

  return {
    config,
    results,
    isLoading,
    error,
    buildApplyString,
    execute,
    reset,
    summaryStats,
    presets,
    savePreset,
    loadPreset,
    deletePreset
  }
}
