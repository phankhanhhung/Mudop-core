import { useRoute, useRouter } from 'vue-router'
import type { LocationQuery } from 'vue-router'
import type { SortOption, FilterCondition } from '@/types/odata'

export interface UrlState {
  page: number
  pageSize: number
  sort: SortOption[]
  search: string
  filters: FilterCondition[]
}

const DEFAULT_PAGE = 1
const DEFAULT_PAGE_SIZE = 20
const DEBOUNCE_MS = 100

function parseIntSafe(value: unknown, fallback: number): number {
  if (typeof value !== 'string' || !value) return fallback
  const parsed = parseInt(value, 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

function parseSortFromUrl(value: unknown): SortOption[] {
  if (typeof value !== 'string' || !value) return []
  const parts = value.trim().split(/\s+/)
  if (parts.length < 1 || parts.length > 2) return []
  const field = parts[0]
  const direction = parts.length === 2 && (parts[1] === 'asc' || parts[1] === 'desc')
    ? parts[1]
    : 'asc'
  return [{ field, direction }]
}

function parseFiltersFromUrl(value: unknown): FilterCondition[] {
  if (typeof value !== 'string' || !value) return []
  try {
    const parsed = JSON.parse(value)
    if (!Array.isArray(parsed)) return []
    // Validate each filter has required fields
    return parsed.filter(
      (f: unknown) =>
        typeof f === 'object' &&
        f !== null &&
        'field' in f &&
        'operator' in f &&
        'value' in f &&
        typeof (f as FilterCondition).field === 'string' &&
        typeof (f as FilterCondition).operator === 'string'
    ) as FilterCondition[]
  } catch {
    return []
  }
}

function readStateFromQuery(query: LocationQuery): UrlState {
  return {
    page: parseIntSafe(query.page, DEFAULT_PAGE),
    pageSize: parseIntSafe(query.pageSize, DEFAULT_PAGE_SIZE),
    sort: parseSortFromUrl(query.sort),
    search: typeof query.search === 'string' ? query.search : '',
    filters: parseFiltersFromUrl(query.filter)
  }
}

export function useUrlState() {
  const route = useRoute()
  const router = useRouter()

  // Read initial state from URL
  const initialState = readStateFromQuery(route.query)

  let debounceTimer: ReturnType<typeof setTimeout> | null = null

  function updateUrl(state: Partial<UrlState>): void {
    if (debounceTimer) {
      clearTimeout(debounceTimer)
    }

    debounceTimer = setTimeout(() => {
      const query: Record<string, string> = {}

      const page = state.page ?? DEFAULT_PAGE
      if (page !== DEFAULT_PAGE) {
        query.page = String(page)
      }

      const pageSize = state.pageSize ?? DEFAULT_PAGE_SIZE
      if (pageSize !== DEFAULT_PAGE_SIZE) {
        query.pageSize = String(pageSize)
      }

      const sort = state.sort ?? []
      if (sort.length > 0) {
        query.sort = `${sort[0].field} ${sort[0].direction}`
      }

      const search = state.search ?? ''
      if (search) {
        query.search = search
      }

      const filters = state.filters ?? []
      if (filters.length > 0) {
        query.filter = JSON.stringify(filters)
      }

      router.replace({ query })
    }, DEBOUNCE_MS)
  }

  /**
   * Parse state from a given query object. Useful for watching route.query changes.
   */
  function parseQuery(query: LocationQuery): UrlState {
    return readStateFromQuery(query)
  }

  return {
    initialPage: initialState.page,
    initialPageSize: initialState.pageSize,
    initialSort: initialState.sort,
    initialSearch: initialState.search,
    initialFilters: initialState.filters,
    updateUrl,
    parseQuery
  }
}
