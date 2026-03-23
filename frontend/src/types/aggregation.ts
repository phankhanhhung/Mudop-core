export type AggregateFunction = 'sum' | 'avg' | 'min' | 'max' | 'count' | 'countdistinct'

export type ChartType = 'bar' | 'line' | 'pie' | 'doughnut' | 'area'

export interface AggregationItem {
  id: string
  func: AggregateFunction
  field: string // empty for 'count'
  alias: string
}

export interface AggregationConfig {
  groupByFields: string[]
  aggregations: AggregationItem[]
  filter?: string // optional pre-filter before aggregation
}

/** @deprecated Use the multi-aggregation config. Kept for internal compat in composable. */
export interface LegacyAggregationConfig {
  groupByField: string
  aggregateFunction: AggregateFunction
  aggregateField: string // empty for 'count'
}

export interface AggregationResult {
  labels: string[]
  values: number[]
  /** All numeric series keyed by alias */
  series: Record<string, number[]>
  rawData: Record<string, unknown>[]
}

export interface AggregationPreset {
  id: string
  name: string
  config: AggregationConfig
  chartType: ChartType
  createdAt: string
}
