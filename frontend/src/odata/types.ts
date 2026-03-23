/**
 * Core types for the OData framework layer.
 * These extend the existing types with framework-level concepts.
 */

import type { FilterCondition, SortOption, BatchRequest, BatchResponse } from '@/types/odata'
import type { AssociationMetadata } from '@/types/metadata'

// ---------------------------------------------------------------------------
// Entity & Property Binding
// ---------------------------------------------------------------------------

/** Represents a bound entity instance with change tracking */
export interface BoundEntity<T = Record<string, unknown>> {
  /** The current data (reactive proxy) */
  data: T
  /** The original snapshot taken at bind-time */
  _original: T
  /** Entity set this instance belongs to */
  _entitySet: string
  /** Module context */
  _module: string
  /** Primary key value */
  _key: string
  /** Current ETag for optimistic concurrency */
  _etag?: string
  /** Whether this entity has unsaved changes */
  _dirty: boolean
  /** Set of field names that have been modified */
  _dirtyFields: Set<string>
  /** Whether this entity is a new (unsaved) instance */
  _isNew: boolean
  /** Draft state */
  _draftState?: DraftState
}

/** Property binding - connects a reactive ref to an entity property path */
export interface PropertyBinding<T = unknown> {
  /** The bound value (reactive) */
  value: T
  /** Full path (e.g., '/Customers(1)/name') */
  path: string
  /** The entity context this property belongs to */
  context: BindingContext
}

/** List binding - connects a reactive list to an entity set */
export interface ListBinding<T = Record<string, unknown>> {
  /** The bound data (reactive array) */
  data: T[]
  /** Total count from server */
  totalCount: number
  /** Whether more pages are available */
  hasMore: boolean
  /** The entity set path */
  path: string
  /** Current query state */
  queryState: ListQueryState
}

export interface ListQueryState {
  $filter?: string
  $select?: string
  $expand?: string
  $orderby?: string
  $top: number
  $skip: number
  $count: boolean
  $search?: string
  $apply?: string
}

// ---------------------------------------------------------------------------
// Binding Context
// ---------------------------------------------------------------------------

export interface BindingContext {
  /** Module name */
  module: string
  /** Entity set name */
  entitySet: string
  /** Entity key (undefined for list contexts) */
  key?: string
  /** Parent context for relative bindings */
  parent?: BindingContext
  /** Navigation property used to reach this context from parent */
  navigationProperty?: string
}

export interface BindingOptions {
  $select?: string[]
  $expand?: Record<string, ExpandBindingOptions | true>
  $filter?: FilterCondition[]
  $orderby?: SortOption[]
  $top?: number
  $skip?: number
  $search?: string
  $apply?: string
  /** Temporal query params */
  temporal?: TemporalOptions
}

export interface ExpandBindingOptions {
  $select?: string[]
  $filter?: FilterCondition[]
  $orderby?: string
  $top?: number
  $expand?: Record<string, ExpandBindingOptions | true>
}

export interface TemporalOptions {
  asOf?: string
  validAt?: string
  includeHistory?: boolean
}

// ---------------------------------------------------------------------------
// Batch Manager
// ---------------------------------------------------------------------------

export type BatchGroupId = '$auto' | '$direct' | string

export interface BatchGroup {
  id: BatchGroupId
  requests: PendingBatchRequest[]
  /** Timer handle for auto-flush */
  flushTimer?: ReturnType<typeof setTimeout>
}

export interface PendingBatchRequest {
  id: string
  request: BatchRequest
  resolve: (response: BatchResponse) => void
  reject: (error: Error) => void
  groupId: BatchGroupId
  createdAt: number
}

// ---------------------------------------------------------------------------
// Change Tracker
// ---------------------------------------------------------------------------

export interface ChangeSet {
  /** Entity key → set of changed field names */
  changes: Map<string, Set<string>>
  /** Entity key → pending create data */
  creates: Map<string, Record<string, unknown>>
  /** Entity keys pending deletion */
  deletes: Set<string>
}

export interface PendingChange {
  type: 'create' | 'update' | 'delete'
  entitySet: string
  module: string
  key?: string
  data?: Record<string, unknown>
  dirtyFields?: string[]
}

// ---------------------------------------------------------------------------
// Draft Handling
// ---------------------------------------------------------------------------

export type DraftState = 'new' | 'editing' | 'preparing' | 'active' | 'discarded'

export interface DraftConfig {
  /** Enable draft persistence */
  enabled: boolean
  /** Auto-save interval in ms (default: 30000) */
  autoSaveInterval?: number
  /** Draft timeout in ms (default: 1800000 = 30 min) */
  timeout?: number
}

export interface DraftInstance {
  /** Draft entity key */
  draftKey: string
  /** Original entity key (undefined for new drafts) */
  entityKey?: string
  /** Module + entity set context */
  module: string
  entitySet: string
  /** Current draft data */
  data: Record<string, unknown>
  /** Draft state */
  state: DraftState
  /** Last auto-save timestamp */
  lastSaved?: Date
  /** Draft creation timestamp */
  createdAt: Date
  /** Dirty fields since last save */
  dirtyFields: Set<string>
}

// ---------------------------------------------------------------------------
// CSDL Metadata Parser
// ---------------------------------------------------------------------------

export interface CsdlSchema {
  namespace: string
  entityTypes: Map<string, CsdlEntityType>
  enumTypes: Map<string, CsdlEnumType>
  entityContainer?: CsdlEntityContainer
  annotations: CsdlAnnotation[]
}

export interface CsdlEntityType {
  name: string
  key: string[]
  properties: CsdlProperty[]
  navigationProperties: CsdlNavigationProperty[]
  annotations: Record<string, unknown>
}

export interface CsdlProperty {
  name: string
  type: string
  nullable: boolean
  maxLength?: number
  precision?: number
  scale?: number
  defaultValue?: string
  annotations: Record<string, unknown>
}

export interface CsdlNavigationProperty {
  name: string
  type: string
  isCollection: boolean
  partner?: string
  referentialConstraints: Array<{ property: string; referencedProperty: string }>
  containsTarget: boolean
}

export interface CsdlEnumType {
  name: string
  members: Array<{ name: string; value: string | number }>
}

export interface CsdlEntityContainer {
  name: string
  entitySets: Array<{
    name: string
    entityType: string
    navigationPropertyBindings: Array<{ path: string; target: string }>
  }>
}

export interface CsdlAnnotation {
  target: string
  term: string
  value: unknown
}

// ---------------------------------------------------------------------------
// Smart Filter
// ---------------------------------------------------------------------------

export type SmartFilterWidgetType =
  | 'text'
  | 'number'
  | 'decimal'
  | 'boolean'
  | 'date'
  | 'datetime'
  | 'enum'
  | 'uuid'
  | 'association'

export interface SmartFilterField {
  name: string
  label: string
  widgetType: SmartFilterWidgetType
  /** Whether this field supports filtering based on metadata annotations */
  filterable: boolean
  /** Whether this field supports sorting */
  sortable: boolean
  /** Enum options if widgetType is 'enum' */
  enumValues?: Array<{ name: string; value: string | number; displayName?: string }>
  /** Association target for value help if widgetType is 'association' */
  associationTarget?: string
  /** Default filter operator for this widget type */
  defaultOperator: FilterOperator
}

export type FilterOperator = 'eq' | 'ne' | 'gt' | 'ge' | 'lt' | 'le' | 'contains' | 'startswith' | 'endswith'

// ---------------------------------------------------------------------------
// Request Pipeline
// ---------------------------------------------------------------------------

export type PipelineMiddleware = (
  request: PipelineRequest,
  next: () => Promise<PipelineResponse>
) => Promise<PipelineResponse>

export interface PipelineRequest {
  url: string
  method: 'GET' | 'POST' | 'PATCH' | 'PUT' | 'DELETE'
  headers: Record<string, string>
  body?: unknown
  signal?: AbortSignal
  metadata: PipelineRequestMetadata
}

export interface PipelineRequestMetadata {
  /** Request priority */
  priority: 'high' | 'normal' | 'low'
  /** Per-endpoint timeout in ms */
  timeout?: number
  /** Retry configuration */
  retry?: RetryConfig
  /** Batch group ID */
  batchGroupId?: BatchGroupId
  /** Whether to skip dedup */
  skipDedup?: boolean
  /** Whether to skip cache */
  skipCache?: boolean
}

export interface PipelineResponse {
  status: number
  headers: Record<string, string>
  data: unknown
}

export interface RetryConfig {
  /** Max retry attempts */
  maxRetries: number
  /** Base delay in ms */
  baseDelay: number
  /** Multiplier for exponential backoff */
  multiplier: number
  /** Status codes to retry on */
  retryOn: number[]
}

// ---------------------------------------------------------------------------
// Entity Cache
// ---------------------------------------------------------------------------

export interface CacheEntry<T = unknown> {
  data: T
  timestamp: number
  etag?: string
  tags: string[]
  /** Size estimate in bytes for memory management */
  size: number
}

export interface CacheOptions {
  /** TTL in milliseconds */
  ttl: number
  /** Maximum number of entries */
  maxEntries: number
  /** Enable stale-while-revalidate */
  staleWhileRevalidate: boolean
  /** SWR window in ms (how long stale data is acceptable) */
  swrWindow: number
}

// ---------------------------------------------------------------------------
// Optimistic Updates
// ---------------------------------------------------------------------------

export interface OptimisticOperation<T = unknown> {
  id: string
  type: 'create' | 'update' | 'delete'
  entitySet: string
  key?: string
  /** Data applied optimistically */
  optimisticData: T
  /** Previous data for rollback */
  previousData?: T
  /** The actual server promise */
  serverPromise: Promise<unknown>
  /** Operation status */
  status: 'pending' | 'confirmed' | 'rolledBack'
  timestamp: number
}

// ---------------------------------------------------------------------------
// Navigation Binding
// ---------------------------------------------------------------------------

export interface NavigationBindingOptions {
  /** Parent entity context */
  parentContext: BindingContext
  /** Navigation property name */
  navigationProperty: string
  /** Association metadata */
  association: AssociationMetadata
  /** Query options for the child collection */
  queryOptions?: BindingOptions
  /** Whether to lazy-load (only load when visible) */
  lazy?: boolean
  /** Page size for child collection */
  pageSize?: number
}

// ---------------------------------------------------------------------------
// DevTools
// ---------------------------------------------------------------------------

export interface ODataDevToolsEntry {
  id: string
  timestamp: number
  type: 'request' | 'response' | 'cache-hit' | 'cache-miss' | 'batch' | 'error'
  method?: string
  url?: string
  status?: number
  duration?: number
  size?: number
  batchGroupId?: string
  data?: unknown
}
