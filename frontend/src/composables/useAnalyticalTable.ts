import { ref, computed, type Ref, type ComputedRef } from 'vue'

export type AggregateFunction = 'sum' | 'avg' | 'count' | 'min' | 'max'

export interface GroupConfig {
  field: string
  label?: string
}

export interface AggregateConfig {
  field: string
  fn: AggregateFunction
  label?: string
}

export type AnalyticalRowType = 'data' | 'group-header' | 'subtotal' | 'grand-total'

export interface AnalyticalRow {
  type: AnalyticalRowType
  data: Record<string, unknown>
  key: string
  groupKey?: string
  groupValue?: unknown
  groupField?: string
  groupCount?: number
  depth?: number
  isExpanded?: boolean
  aggregates?: Record<string, number>
}

export interface UseAnalyticalTableOptions {
  data: Ref<Record<string, unknown>[]>
  groupBy: Ref<GroupConfig[]>
  aggregates: Ref<AggregateConfig[]>
  showSubtotals?: boolean
  showGrandTotal?: boolean
}

export interface UseAnalyticalTableReturn {
  analyticalRows: ComputedRef<AnalyticalRow[]>
  expandedGroups: Ref<Set<string>>
  toggleGroup: (groupKey: string) => void
  expandAllGroups: () => void
  collapseAllGroups: () => void
  grandTotals: ComputedRef<Record<string, number>>
  dataBarMax: ComputedRef<Record<string, number>>
}

function computeAggregate(values: number[], fn: AggregateFunction): number {
  if (values.length === 0) return 0
  switch (fn) {
    case 'sum': return values.reduce((a, b) => a + b, 0)
    case 'avg': return values.reduce((a, b) => a + b, 0) / values.length
    case 'count': return values.length
    case 'min': return Math.min(...values)
    case 'max': return Math.max(...values)
  }
}

export function useAnalyticalTable(options: UseAnalyticalTableOptions): UseAnalyticalTableReturn {
  const { data, groupBy, aggregates, showSubtotals = true, showGrandTotal = true } = options
  const expandedGroups = ref<Set<string>>(new Set())

  function collectGroupKeys(): string[] {
    const keys: string[] = []
    if (groupBy.value.length === 0) return keys
    const field = groupBy.value[0].field
    const groups = new Map<string, Record<string, unknown>[]>()
    for (const row of data.value) {
      const val = String(row[field] ?? 'Unknown')
      if (!groups.has(val)) groups.set(val, [])
      groups.get(val)!.push(row)
    }
    for (const [val] of groups) {
      keys.push(`group-${field}-${val}`)
    }
    return keys
  }

  // Auto-expand all groups initially
  const initKeys = collectGroupKeys()
  expandedGroups.value = new Set(initKeys)

  function computeAggregates(rows: Record<string, unknown>[]): Record<string, number> {
    const result: Record<string, number> = {}
    for (const agg of aggregates.value) {
      const values = rows
        .map(r => Number(r[agg.field]))
        .filter(v => !isNaN(v))
      result[agg.field] = computeAggregate(values, agg.fn)
    }
    return result
  }

  const grandTotals = computed<Record<string, number>>(() => {
    return computeAggregates(data.value)
  })

  const dataBarMax = computed<Record<string, number>>(() => {
    const result: Record<string, number> = {}
    for (const agg of aggregates.value) {
      const values = data.value
        .map(r => Math.abs(Number(r[agg.field]) || 0))
      result[agg.field] = values.length > 0 ? Math.max(...values) : 1
    }
    return result
  })

  const analyticalRows = computed<AnalyticalRow[]>(() => {
    const result: AnalyticalRow[] = []

    if (groupBy.value.length === 0) {
      for (let i = 0; i < data.value.length; i++) {
        result.push({
          type: 'data',
          data: data.value[i],
          key: `data-${i}`,
        })
      }
      if (showGrandTotal && data.value.length > 0) {
        result.push({
          type: 'grand-total',
          data: {},
          key: 'grand-total',
          aggregates: grandTotals.value,
        })
      }
      return result
    }

    // Single-level grouping (first groupBy field)
    const field = groupBy.value[0].field
    const label = groupBy.value[0].label || field
    const groups = new Map<string, Record<string, unknown>[]>()
    const groupOrder: string[] = []

    for (const row of data.value) {
      const val = String(row[field] ?? 'Unknown')
      if (!groups.has(val)) {
        groups.set(val, [])
        groupOrder.push(val)
      }
      groups.get(val)!.push(row)
    }

    for (const val of groupOrder) {
      const rows = groups.get(val)!
      const groupKey = `group-${field}-${val}`
      const isExpanded = expandedGroups.value.has(groupKey)

      result.push({
        type: 'group-header',
        data: { [field]: val },
        key: groupKey,
        groupKey,
        groupValue: val,
        groupField: field,
        groupCount: rows.length,
        depth: 0,
        isExpanded,
      })

      if (isExpanded) {
        for (let i = 0; i < rows.length; i++) {
          result.push({
            type: 'data',
            data: rows[i],
            key: `${groupKey}-data-${i}`,
            groupKey,
            depth: 1,
          })
        }

        if (showSubtotals) {
          result.push({
            type: 'subtotal',
            data: { [field]: `${label}: ${val}` },
            key: `${groupKey}-subtotal`,
            groupKey,
            groupValue: val,
            groupField: field,
            aggregates: computeAggregates(rows),
          })
        }
      }
    }

    if (showGrandTotal && data.value.length > 0) {
      result.push({
        type: 'grand-total',
        data: {},
        key: 'grand-total',
        aggregates: grandTotals.value,
      })
    }

    return result
  })

  function toggleGroup(groupKey: string) {
    const next = new Set(expandedGroups.value)
    if (next.has(groupKey)) {
      next.delete(groupKey)
    } else {
      next.add(groupKey)
    }
    expandedGroups.value = next
  }

  function expandAllGroups() {
    expandedGroups.value = new Set(collectGroupKeys())
  }

  function collapseAllGroups() {
    expandedGroups.value = new Set()
  }

  return {
    analyticalRows,
    expandedGroups,
    toggleGroup,
    expandAllGroups,
    collapseAllGroups,
    grandTotals,
    dataBarMax,
  }
}
