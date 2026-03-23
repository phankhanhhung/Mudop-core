import { describe, it, expect, beforeEach, vi } from 'vitest'

// Mock i18n (transitively imported by other modules)
vi.mock('@/i18n', () => ({
  default: {
    global: {
      locale: { value: 'en' },
      t: (key: string) => key
    }
  }
}))

// Mock odataService
const mockQuery = vi.fn()

vi.mock('@/services/odataService', () => ({
  odataService: {
    query: (...args: unknown[]) => mockQuery(...args)
  }
}))

import { useAggregation } from '../useAggregation'
import type { AggregationConfig, AggregationItem } from '@/types/aggregation'

const MODULE = 'test_module'
const ENTITY_SET = 'TestEntity'

function presetsKey(): string {
  return `bmmdl-aggregation-presets-${MODULE}-${ENTITY_SET}`
}

describe('useAggregation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    // Mock crypto.randomUUID if not available in test environment
    if (!globalThis.crypto?.randomUUID) {
      Object.defineProperty(globalThis, 'crypto', {
        value: {
          randomUUID: () => `test-uuid-${Date.now()}-${Math.random().toString(36).slice(2)}`
        },
        writable: true,
        configurable: true
      })
    }
  })

  describe('initial state', () => {
    it('has empty groupByFields', () => {
      const { config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      expect(config.value.groupByFields).toEqual([])
    })

    it('has empty aggregations', () => {
      const { config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      expect(config.value.aggregations).toEqual([])
    })

    it('has empty filter', () => {
      const { config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      expect(config.value.filter).toBe('')
    })

    it('has null results', () => {
      const { results } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      expect(results.value).toBeNull()
    })

    it('is not loading', () => {
      const { isLoading } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      expect(isLoading.value).toBe(false)
    })

    it('has null error', () => {
      const { error } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      expect(error.value).toBeNull()
    })

    it('has empty presets when localStorage is empty', () => {
      const { presets } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      expect(presets.value).toEqual([])
    })
  })

  describe('config manipulation', () => {
    it('sets group-by fields via config ref', () => {
      const { config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      config.value.groupByFields = ['Status', 'Category']
      expect(config.value.groupByFields).toEqual(['Status', 'Category'])
    })

    it('adds an aggregation item via config ref', () => {
      const { config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      const agg: AggregationItem = { id: '1', func: 'sum', field: 'Amount', alias: 'TotalAmount' }
      config.value.aggregations = [...config.value.aggregations, agg]
      expect(config.value.aggregations).toHaveLength(1)
      expect(config.value.aggregations[0]).toEqual(agg)
    })

    it('removes an aggregation by id via filter', () => {
      const { config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      const agg1: AggregationItem = { id: '1', func: 'sum', field: 'Amount', alias: 'TotalAmount' }
      const agg2: AggregationItem = { id: '2', func: 'avg', field: 'Price', alias: 'AvgPrice' }
      config.value.aggregations = [agg1, agg2]

      // Remove agg1
      config.value.aggregations = config.value.aggregations.filter(a => a.id !== '1')

      expect(config.value.aggregations).toHaveLength(1)
      expect(config.value.aggregations[0].id).toBe('2')
    })
  })

  describe('buildApplyString', () => {
    it('generates single group-by with single aggregation', () => {
      const { buildApplyString } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      const cfg: AggregationConfig = {
        groupByFields: ['Status'],
        aggregations: [{ id: '1', func: 'sum', field: 'Amount', alias: 'TotalAmount' }]
      }
      const result = buildApplyString(cfg)
      expect(result).toBe('groupby((Status),aggregate(Amount with sum as TotalAmount))')
    })

    it('generates multiple group-by fields', () => {
      const { buildApplyString } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      const cfg: AggregationConfig = {
        groupByFields: ['Status', 'Category'],
        aggregations: [{ id: '1', func: 'sum', field: 'Amount', alias: 'TotalAmount' }]
      }
      const result = buildApplyString(cfg)
      expect(result).toBe('groupby((Status,Category),aggregate(Amount with sum as TotalAmount))')
    })

    it('generates multiple aggregations', () => {
      const { buildApplyString } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      const cfg: AggregationConfig = {
        groupByFields: ['Status'],
        aggregations: [
          { id: '1', func: 'sum', field: 'Amount', alias: 'TotalAmount' },
          { id: '2', func: 'avg', field: 'Price', alias: 'AvgPrice' }
        ]
      }
      const result = buildApplyString(cfg)
      expect(result).toBe(
        'groupby((Status),aggregate(Amount with sum as TotalAmount,Price with avg as AvgPrice))'
      )
    })

    it('generates count aggregation using $count', () => {
      const { buildApplyString } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      const cfg: AggregationConfig = {
        groupByFields: ['Status'],
        aggregations: [{ id: '1', func: 'count', field: '', alias: 'Count' }]
      }
      const result = buildApplyString(cfg)
      expect(result).toBe('groupby((Status),aggregate($count as Count))')
    })

    it('generates countdistinct aggregation', () => {
      const { buildApplyString } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      const cfg: AggregationConfig = {
        groupByFields: ['Category'],
        aggregations: [{ id: '1', func: 'countdistinct', field: 'CustomerId', alias: 'UniqueCustomers' }]
      }
      const result = buildApplyString(cfg)
      expect(result).toBe(
        'groupby((Category),aggregate(CustomerId with countdistinct as UniqueCustomers))'
      )
    })

    it('prepends filter when present', () => {
      const { buildApplyString } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      const cfg: AggregationConfig = {
        groupByFields: ['Status'],
        aggregations: [{ id: '1', func: 'sum', field: 'Amount', alias: 'TotalAmount' }],
        filter: "Status eq 'Active'"
      }
      const result = buildApplyString(cfg)
      expect(result).toBe(
        "filter(Status eq 'Active')/groupby((Status),aggregate(Amount with sum as TotalAmount))"
      )
    })

    it('ignores empty/whitespace filter', () => {
      const { buildApplyString } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      const cfg: AggregationConfig = {
        groupByFields: ['Status'],
        aggregations: [{ id: '1', func: 'sum', field: 'Amount', alias: 's' }],
        filter: '   '
      }
      const result = buildApplyString(cfg)
      expect(result).toBe('groupby((Status),aggregate(Amount with sum as s))')
    })

    it('generates min and max aggregations', () => {
      const { buildApplyString } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      const cfg: AggregationConfig = {
        groupByFields: ['Category'],
        aggregations: [
          { id: '1', func: 'min', field: 'Price', alias: 'MinPrice' },
          { id: '2', func: 'max', field: 'Price', alias: 'MaxPrice' }
        ]
      }
      const result = buildApplyString(cfg)
      expect(result).toBe(
        'groupby((Category),aggregate(Price with min as MinPrice,Price with max as MaxPrice))'
      )
    })
  })

  describe('execute', () => {
    it('sets error when no group-by fields', async () => {
      const { execute, error, config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      config.value.aggregations = [{ id: '1', func: 'sum', field: 'Amount', alias: 'Total' }]
      // groupByFields is empty

      await execute()
      expect(error.value).toBe('Please select at least one group-by field')
      expect(mockQuery).not.toHaveBeenCalled()
    })

    it('sets error when no aggregations', async () => {
      const { execute, error, config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      config.value.groupByFields = ['Status']
      // aggregations is empty

      await execute()
      expect(error.value).toBe('Please add at least one aggregation')
      expect(mockQuery).not.toHaveBeenCalled()
    })

    it('sets error when non-count aggregation has no field', async () => {
      const { execute, error, config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      config.value.groupByFields = ['Status']
      config.value.aggregations = [{ id: '1', func: 'sum', field: '', alias: 'Total' }]

      await execute()
      expect(error.value).toBe('Please select a field for aggregation "Total"')
      expect(mockQuery).not.toHaveBeenCalled()
    })

    it('calls odataService.query with correct $apply parameter', async () => {
      mockQuery.mockResolvedValue({ value: [] })
      const { execute, config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      config.value.groupByFields = ['Status']
      config.value.aggregations = [{ id: '1', func: 'sum', field: 'Amount', alias: 'Total' }]

      await execute()

      expect(mockQuery).toHaveBeenCalledWith(MODULE, ENTITY_SET, {
        $apply: 'groupby((Status),aggregate(Amount with sum as Total))'
      })
    })

    it('sets isLoading during execution', async () => {
      let resolveQuery!: (v: unknown) => void
      mockQuery.mockReturnValue(new Promise(r => { resolveQuery = r }))

      const { execute, isLoading, config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      config.value.groupByFields = ['Status']
      config.value.aggregations = [{ id: '1', func: 'sum', field: 'Amount', alias: 'Total' }]

      const promise = execute()
      expect(isLoading.value).toBe(true)

      resolveQuery({ value: [] })
      await promise

      expect(isLoading.value).toBe(false)
    })

    it('parses response into labels and values', async () => {
      mockQuery.mockResolvedValue({
        value: [
          { Status: 'Active', Total: 100 },
          { Status: 'Inactive', Total: 50 }
        ]
      })

      const { execute, results, config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      config.value.groupByFields = ['Status']
      config.value.aggregations = [{ id: '1', func: 'sum', field: 'Amount', alias: 'Total' }]

      await execute()

      expect(results.value).not.toBeNull()
      expect(results.value!.labels).toEqual(['Active', 'Inactive'])
      expect(results.value!.values).toEqual([100, 50])
      expect(results.value!.series['Total']).toEqual([100, 50])
    })

    it('builds composite labels from multiple group-by fields', async () => {
      mockQuery.mockResolvedValue({
        value: [
          { Status: 'Active', Category: 'A', Total: 100 },
          { Status: 'Inactive', Category: 'B', Total: 50 }
        ]
      })

      const { execute, results, config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      config.value.groupByFields = ['Status', 'Category']
      config.value.aggregations = [{ id: '1', func: 'sum', field: 'Amount', alias: 'Total' }]

      await execute()

      expect(results.value!.labels).toEqual(['Active / A', 'Inactive / B'])
    })

    it('handles null group-by values as "(empty)"', async () => {
      mockQuery.mockResolvedValue({
        value: [
          { Status: null, Total: 30 }
        ]
      })

      const { execute, results, config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      config.value.groupByFields = ['Status']
      config.value.aggregations = [{ id: '1', func: 'sum', field: 'Amount', alias: 'Total' }]

      await execute()

      expect(results.value!.labels).toEqual(['(empty)'])
    })

    it('sets error on query failure', async () => {
      mockQuery.mockRejectedValue(new Error('Network error'))

      const { execute, error, results, config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      config.value.groupByFields = ['Status']
      config.value.aggregations = [{ id: '1', func: 'sum', field: 'Amount', alias: 'Total' }]

      await execute()

      expect(error.value).toBe('Network error')
      expect(results.value).toBeNull()
    })

    it('sets generic error message for non-Error throws', async () => {
      mockQuery.mockRejectedValue('something went wrong')

      const { execute, error, config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      config.value.groupByFields = ['Status']
      config.value.aggregations = [{ id: '1', func: 'sum', field: 'Amount', alias: 'Total' }]

      await execute()

      expect(error.value).toBe('Aggregation query failed')
    })
  })

  describe('summaryStats', () => {
    it('returns zeros when results are null', () => {
      const { summaryStats } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      expect(summaryStats.value).toEqual({
        totalGroups: 0,
        totalRecords: 0,
        primarySum: 0,
        primaryAvg: 0,
        primaryMin: 0,
        primaryMax: 0
      })
    })

    it('computes correct stats from results', async () => {
      mockQuery.mockResolvedValue({
        value: [
          { Status: 'Active', Total: 100 },
          { Status: 'Inactive', Total: 50 },
          { Status: 'Pending', Total: 150 }
        ]
      })

      const { execute, summaryStats, config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      config.value.groupByFields = ['Status']
      config.value.aggregations = [{ id: '1', func: 'sum', field: 'Amount', alias: 'Total' }]

      await execute()

      expect(summaryStats.value.totalGroups).toBe(3)
      expect(summaryStats.value.totalRecords).toBe(300) // sum of values
      expect(summaryStats.value.primarySum).toBe(300)
      expect(summaryStats.value.primaryAvg).toBe(100)
      expect(summaryStats.value.primaryMin).toBe(50)
      expect(summaryStats.value.primaryMax).toBe(150)
    })

    it('handles single result group', async () => {
      mockQuery.mockResolvedValue({
        value: [{ Status: 'Active', Total: 42 }]
      })

      const { execute, summaryStats, config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      config.value.groupByFields = ['Status']
      config.value.aggregations = [{ id: '1', func: 'sum', field: 'Amount', alias: 'Total' }]

      await execute()

      expect(summaryStats.value.totalGroups).toBe(1)
      expect(summaryStats.value.primarySum).toBe(42)
      expect(summaryStats.value.primaryAvg).toBe(42)
      expect(summaryStats.value.primaryMin).toBe(42)
      expect(summaryStats.value.primaryMax).toBe(42)
    })
  })

  describe('reset', () => {
    it('clears results and error', async () => {
      mockQuery.mockResolvedValue({
        value: [{ Status: 'Active', Total: 100 }]
      })

      const { execute, reset, results, error, config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      config.value.groupByFields = ['Status']
      config.value.aggregations = [{ id: '1', func: 'sum', field: 'Amount', alias: 'Total' }]

      await execute()
      expect(results.value).not.toBeNull()

      reset()
      expect(results.value).toBeNull()
      expect(error.value).toBeNull()
    })
  })

  describe('preset management', () => {
    it('saves a preset and persists to localStorage', () => {
      const { savePreset, presets, config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      config.value.groupByFields = ['Status']
      config.value.aggregations = [{ id: '1', func: 'sum', field: 'Amount', alias: 'Total' }]

      savePreset('My Preset', 'bar')

      expect(presets.value).toHaveLength(1)
      expect(presets.value[0].name).toBe('My Preset')
      expect(presets.value[0].chartType).toBe('bar')
      expect(presets.value[0].config.groupByFields).toEqual(['Status'])
      expect(presets.value[0].id).toBeTruthy()
      expect(presets.value[0].createdAt).toBeTruthy()

      // Verify localStorage
      const stored = localStorage.getItem(presetsKey())
      expect(stored).not.toBeNull()
      const parsed = JSON.parse(stored!)
      expect(parsed).toHaveLength(1)
      expect(parsed[0].name).toBe('My Preset')
    })

    it('loads a preset by id', () => {
      const { savePreset, loadPreset, presets, config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      config.value.groupByFields = ['Category']
      config.value.aggregations = [{ id: '1', func: 'avg', field: 'Price', alias: 'AvgPrice' }]

      savePreset('Preset A', 'line')
      const id = presets.value[0].id

      const loaded = loadPreset(id)
      expect(loaded).toBeDefined()
      expect(loaded!.name).toBe('Preset A')
      expect(loaded!.chartType).toBe('line')
      expect(loaded!.config.groupByFields).toEqual(['Category'])
    })

    it('returns undefined for non-existent preset id', () => {
      const { loadPreset } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      const loaded = loadPreset('non-existent-id')
      expect(loaded).toBeUndefined()
    })

    it('deletes a preset by id', () => {
      const { savePreset, deletePreset, presets, config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      config.value.groupByFields = ['Status']
      config.value.aggregations = [{ id: '1', func: 'sum', field: 'Amount', alias: 'Total' }]

      savePreset('First', 'bar')
      savePreset('Second', 'pie')
      expect(presets.value).toHaveLength(2)

      const firstId = presets.value[0].id
      deletePreset(firstId)

      expect(presets.value).toHaveLength(1)
      expect(presets.value[0].name).toBe('Second')

      // Verify localStorage updated
      const stored = JSON.parse(localStorage.getItem(presetsKey())!)
      expect(stored).toHaveLength(1)
      expect(stored[0].name).toBe('Second')
    })

    it('loads presets from localStorage on initialization', () => {
      const existingPresets = [
        {
          id: 'preset-1',
          name: 'Saved Preset',
          config: { groupByFields: ['Status'], aggregations: [], filter: '' },
          chartType: 'bar',
          createdAt: '2024-01-01T00:00:00Z'
        }
      ]
      localStorage.setItem(presetsKey(), JSON.stringify(existingPresets))

      const { presets } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      expect(presets.value).toHaveLength(1)
      expect(presets.value[0].name).toBe('Saved Preset')
    })

    it('handles corrupt localStorage for presets gracefully', () => {
      localStorage.setItem(presetsKey(), 'not-valid-json')
      const { presets } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      expect(presets.value).toEqual([])
    })

    it('saves preset config as a deep copy (not a reference)', () => {
      const { savePreset, presets, config } = useAggregation({ module: MODULE, entitySet: ENTITY_SET })
      config.value.groupByFields = ['Status']
      config.value.aggregations = [{ id: '1', func: 'sum', field: 'Amount', alias: 'Total' }]

      savePreset('Snapshot', 'bar')

      // Mutate config after saving
      config.value.groupByFields = ['Category']
      config.value.aggregations = []

      // Preset should still have original values
      expect(presets.value[0].config.groupByFields).toEqual(['Status'])
      expect(presets.value[0].config.aggregations).toHaveLength(1)
    })
  })
})
