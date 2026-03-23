// OData query options
export interface ODataQueryOptions {
  $filter?: string
  $select?: string
  $expand?: string
  $orderby?: string
  $top?: number
  $skip?: number
  $count?: boolean
  $search?: string
  $apply?: string
  $compute?: string
  trackChanges?: boolean
  $deltatoken?: string
  asOf?: string
  validAt?: string
  includeHistory?: boolean
}

// OData filter operators
export type FilterOperator =
  | 'eq'
  | 'ne'
  | 'gt'
  | 'ge'
  | 'lt'
  | 'le'
  | 'contains'
  | 'startswith'
  | 'endswith'

export interface FilterCondition {
  field: string
  operator: FilterOperator
  value: unknown
}

export interface SortOption {
  field: string
  direction: 'asc' | 'desc'
}

// OData response format
export interface ODataResponse<T> {
  '@odata.context'?: string
  '@odata.count'?: number
  '@odata.nextLink'?: string
  '@odata.deltaLink'?: string
  value: T[]
}

export interface ODataSingleResponse<T> {
  '@odata.context'?: string
  data: T
}

// Write options for create/update/replace
export interface WriteOptions {
  ifMatch?: string
  prefer?: 'return=minimal' | 'return=representation'
}

// Batch request/response
export interface BatchRequest {
  id: string
  method: 'GET' | 'POST' | 'PATCH' | 'DELETE'
  url: string
  headers?: Record<string, string>
  body?: unknown
}

export interface BatchResponse {
  id: string
  status: number
  headers?: Record<string, string>
  body?: unknown
}
