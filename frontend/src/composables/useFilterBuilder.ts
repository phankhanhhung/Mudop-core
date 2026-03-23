import { ref, computed, type Ref, type ComputedRef } from 'vue'
import type { FilterCondition } from '@/types/odata'

export interface UseFilterBuilderOptions {
  onFiltersChange: (filters: FilterCondition[]) => void
}

export interface UseFilterBuilderReturn {
  activeFilters: Ref<FilterCondition[]>
  getFilterForField: (fieldName: string) => FilterCondition | null
  applyFilter: (filter: FilterCondition) => void
  applyBetweenFilter: (field: string, min: unknown, max: unknown, type: 'number' | 'date') => void
  removeFilter: (fieldName: string) => void
  clearAll: () => void
  replaceAll: (filters: FilterCondition[]) => void
  hasActiveFilter: (fieldName: string) => boolean
  activeFilterCount: ComputedRef<number>
}

export function useFilterBuilder(options: UseFilterBuilderOptions): UseFilterBuilderReturn {
  const activeFilters = ref<FilterCondition[]>([])

  const activeFilterCount = computed(() => {
    // Count unique fields (a "between" filter uses two conditions for one field)
    const fields = new Set(activeFilters.value.map((f) => f.field))
    return fields.size
  })

  function notifyChange(): void {
    options.onFiltersChange([...activeFilters.value])
  }

  function getFilterForField(fieldName: string): FilterCondition | null {
    return activeFilters.value.find((f) => f.field === fieldName) ?? null
  }

  function applyFilter(filter: FilterCondition): void {
    // Remove any existing conditions for this field, then add the new one
    activeFilters.value = activeFilters.value.filter((f) => f.field !== filter.field)
    activeFilters.value.push(filter)
    notifyChange()
  }

  function applyBetweenFilter(
    field: string,
    min: unknown,
    max: unknown,
    _type: 'number' | 'date'
  ): void {
    // Remove any existing conditions for this field
    activeFilters.value = activeFilters.value.filter((f) => f.field !== field)

    // Add two conditions: ge for min and le for max
    if (min != null && min !== '') {
      activeFilters.value.push({ field, operator: 'ge', value: min })
    }
    if (max != null && max !== '') {
      activeFilters.value.push({ field, operator: 'le', value: max })
    }

    notifyChange()
  }

  function removeFilter(fieldName: string): void {
    activeFilters.value = activeFilters.value.filter((f) => f.field !== fieldName)
    notifyChange()
  }

  function clearAll(): void {
    activeFilters.value = []
    notifyChange()
  }

  // Batch-replace all filters with a single notification (avoids cascade)
  function replaceAll(filters: FilterCondition[]): void {
    activeFilters.value = [...filters]
    notifyChange()
  }

  function hasActiveFilter(fieldName: string): boolean {
    return activeFilters.value.some((f) => f.field === fieldName)
  }

  return {
    activeFilters,
    getFilterForField,
    applyFilter,
    applyBetweenFilter,
    removeFilter,
    clearAll,
    replaceAll,
    hasActiveFilter,
    activeFilterCount
  }
}
