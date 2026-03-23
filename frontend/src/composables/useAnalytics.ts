import { ref, watch, type Ref } from 'vue'
import { odataService } from '@/services/odataService'
import { useMetadataStore } from '@/stores/metadata'
import type { FieldMetadata, EntityMetadata, FieldType } from '@/types/metadata'

export interface EnumDistributionItem {
  label: string
  value: number
}

export interface NumericFieldStats {
  avg: number
  sum: number
  min: number
  max: number
}

export interface TimeSeriesPoint {
  date: string
  count: number
}

export interface UseAnalyticsReturn {
  entityCount: Ref<number>
  enumDistributions: Ref<Map<string, EnumDistributionItem[]>>
  numericStats: Ref<Map<string, NumericFieldStats>>
  timeSeriesData: Ref<TimeSeriesPoint[]>
  isLoading: Ref<boolean>
  error: Ref<string | null>
  refresh: () => Promise<void>
}

// Simple TTL cache
interface CacheEntry<T> {
  data: T
  expiresAt: number
}

const CACHE_TTL = 5 * 60 * 1000 // 5 minutes
const analyticsCache = new Map<string, CacheEntry<unknown>>()

function getCached<T>(key: string): T | undefined {
  const entry = analyticsCache.get(key)
  if (!entry) return undefined
  if (Date.now() > entry.expiresAt) {
    analyticsCache.delete(key)
    return undefined
  }
  return entry.data as T
}

function setCache<T>(key: string, data: T): void {
  analyticsCache.set(key, { data, expiresAt: Date.now() + CACHE_TTL })
}

function isNumericType(type: FieldType): boolean {
  return type === 'Integer' || type === 'Decimal'
}

function isDateTimeType(type: FieldType): boolean {
  return type === 'DateTime' || type === 'Timestamp' || type === 'Date'
}

function isEnumType(field: FieldMetadata): boolean {
  return field.type === 'Enum' && !!field.enumValues && field.enumValues.length > 0
}

/**
 * Composable for fetching and processing analytics data for a given entity.
 * Uses OData $apply for server-side aggregation and caches results for 5 minutes.
 */
export function useAnalytics(
  module: Ref<string>,
  entity: Ref<string>
): UseAnalyticsReturn {
  const entityCount = ref(0)
  const enumDistributions = ref<Map<string, EnumDistributionItem[]>>(new Map())
  const numericStats = ref<Map<string, NumericFieldStats>>(new Map())
  const timeSeriesData = ref<TimeSeriesPoint[]>([])
  const isLoading = ref(false)
  const error = ref<string | null>(null)

  const metadataStore = useMetadataStore()

  async function getEntityMetadata(): Promise<EntityMetadata | null> {
    try {
      return await metadataStore.fetchEntity(module.value, entity.value)
    } catch {
      return null
    }
  }

  function findEntitySet(moduleName: string, entityName: string): string {
    const mod = metadataStore.getModule(moduleName)
    if (mod) {
      for (const svc of mod.services) {
        for (const es of svc.entities) {
          if (es.entityType === entityName) return es.entityType
        }
      }
    }
    // Fallback: use entity name directly
    return entityName
  }

  async function fetchEntityCount(entitySet: string): Promise<number> {
    const cacheKey = `count:${module.value}:${entitySet}`
    const cached = getCached<number>(cacheKey)
    if (cached !== undefined) return cached

    try {
      const count = await odataService.count(module.value, entitySet)
      setCache(cacheKey, count)
      return count
    } catch {
      return 0
    }
  }

  async function fetchEnumDistributions(
    entitySet: string,
    fields: FieldMetadata[]
  ): Promise<Map<string, EnumDistributionItem[]>> {
    const result = new Map<string, EnumDistributionItem[]>()
    const enumFields = fields.filter(isEnumType)

    for (const field of enumFields) {
      const cacheKey = `enum:${module.value}:${entitySet}:${field.name}`
      const cached = getCached<EnumDistributionItem[]>(cacheKey)
      if (cached) {
        result.set(field.name, cached)
        continue
      }

      try {
        const apply = `groupby((${field.name}),aggregate($count as Count))`
        const response = await odataService.query<Record<string, unknown>>(
          module.value,
          entitySet,
          { $apply: apply },
          { skipCache: true }
        )

        const items: EnumDistributionItem[] = (response.value || []).map((row) => ({
          label: row[field.name] != null ? String(row[field.name]) : '(empty)',
          value: typeof row['Count'] === 'number' ? row['Count'] : Number(row['Count']) || 0,
        }))

        result.set(field.name, items)
        setCache(cacheKey, items)
      } catch {
        // Skip failed field aggregations
      }
    }

    return result
  }

  async function fetchNumericStats(
    entitySet: string,
    fields: FieldMetadata[]
  ): Promise<Map<string, NumericFieldStats>> {
    const result = new Map<string, NumericFieldStats>()
    const numericFields = fields.filter(
      (f) => isNumericType(f.type) && !f.isComputed && !f.isReadOnly
    )

    if (numericFields.length === 0) return result

    // Build a single $apply query for all numeric fields at once
    const cacheKey = `numeric:${module.value}:${entitySet}`
    const cached = getCached<Map<string, NumericFieldStats>>(cacheKey)
    if (cached) return cached

    const aggParts: string[] = []
    for (const field of numericFields) {
      aggParts.push(`${field.name} with avg as ${field.name}_Avg`)
      aggParts.push(`${field.name} with sum as ${field.name}_Sum`)
      aggParts.push(`${field.name} with min as ${field.name}_Min`)
      aggParts.push(`${field.name} with max as ${field.name}_Max`)
    }

    try {
      const apply = `aggregate(${aggParts.join(',')})`
      const response = await odataService.query<Record<string, unknown>>(
        module.value,
        entitySet,
        { $apply: apply },
        { skipCache: true }
      )

      const row = response.value?.[0]
      if (row) {
        for (const field of numericFields) {
          const stats: NumericFieldStats = {
            avg: Number(row[`${field.name}_Avg`]) || 0,
            sum: Number(row[`${field.name}_Sum`]) || 0,
            min: Number(row[`${field.name}_Min`]) || 0,
            max: Number(row[`${field.name}_Max`]) || 0,
          }
          result.set(field.name, stats)
        }
      }

      setCache(cacheKey, result)
    } catch {
      // Ignore aggregate failures
    }

    return result
  }

  async function fetchTimeSeries(
    entitySet: string,
    fields: FieldMetadata[]
  ): Promise<TimeSeriesPoint[]> {
    // Find a datetime field (prefer CreatedAt, then any DateTime/Timestamp field)
    const dateField =
      fields.find((f) => f.name.toLowerCase() === 'createdat' && isDateTimeType(f.type)) ??
      fields.find((f) => isDateTimeType(f.type))

    if (!dateField) return []

    const cacheKey = `timeseries:${module.value}:${entitySet}:${dateField.name}`
    const cached = getCached<TimeSeriesPoint[]>(cacheKey)
    if (cached) return cached

    try {
      // Group by date (cast to date), aggregate count
      // Use $apply with date grouping
      const apply = `groupby((${dateField.name}),aggregate($count as Count))`
      const response = await odataService.query<Record<string, unknown>>(
        module.value,
        entitySet,
        {
          $apply: apply,
          $orderby: dateField.name,
          $top: 90, // Last ~90 data points
        },
        { skipCache: true }
      )

      const points: TimeSeriesPoint[] = (response.value || [])
        .filter((row) => row[dateField.name] != null)
        .map((row) => ({
          date: String(row[dateField.name]).slice(0, 10), // YYYY-MM-DD
          count: typeof row['Count'] === 'number' ? row['Count'] : Number(row['Count']) || 0,
        }))

      // Merge duplicates (same date)
      const merged = new Map<string, number>()
      for (const pt of points) {
        merged.set(pt.date, (merged.get(pt.date) ?? 0) + pt.count)
      }

      const result = Array.from(merged.entries())
        .map(([date, count]) => ({ date, count }))
        .sort((a, b) => a.date.localeCompare(b.date))

      setCache(cacheKey, result)
      return result
    } catch {
      return []
    }
  }

  async function refresh(): Promise<void> {
    if (!module.value || !entity.value) return

    isLoading.value = true
    error.value = null

    try {
      const metadata = await getEntityMetadata()
      if (!metadata) {
        error.value = 'Failed to load entity metadata'
        return
      }

      const entitySet = findEntitySet(module.value, entity.value)

      // Run all queries in parallel
      const [count, enums, numerics, timeSeries] = await Promise.all([
        fetchEntityCount(entitySet),
        fetchEnumDistributions(entitySet, metadata.fields),
        fetchNumericStats(entitySet, metadata.fields),
        fetchTimeSeries(entitySet, metadata.fields),
      ])

      entityCount.value = count
      enumDistributions.value = enums
      numericStats.value = numerics
      timeSeriesData.value = timeSeries
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Analytics query failed'
    } finally {
      isLoading.value = false
    }
  }

  // Auto-refresh when module/entity changes
  watch([module, entity], () => {
    if (module.value && entity.value) {
      void refresh()
    }
  }, { immediate: true })

  return {
    entityCount,
    enumDistributions,
    numericStats,
    timeSeriesData,
    isLoading,
    error,
    refresh,
  }
}
