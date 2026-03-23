/**
 * BMMDL OData Framework Layer
 *
 * Framework-level OData integration inspired by SAP OpenUI5's OData Model.
 * Provides reactive data management, auto-batching, change tracking,
 * metadata-driven UI, and enterprise features.
 *
 * Architecture:
 *
 *   ┌─────────────────────────────────────────────────┐
 *   │                  Vue Components                  │
 *   │  (EntityListView, EntityDetailView, etc.)       │
 *   └──────────────────┬──────────────────────────────┘
 *                      │
 *   ┌──────────────────▼──────────────────────────────┐
 *   │              Composable Layer                    │
 *   │  useBindingContext  │  useEntityBinding          │
 *   │  useRelativeBinding │  useDeepOperation          │
 *   │  useTemporalBinding                              │
 *   └──────────────────┬──────────────────────────────┘
 *                      │
 *   ┌──────────────────▼──────────────────────────────┐
 *   │              ODataModel                          │
 *   │  bindList() │ bindEntity() │ bindProperty()     │
 *   │  submitChanges() │ resetChanges()               │
 *   │                                                  │
 *   │  ┌─────────────┐ ┌────────────┐ ┌────────────┐ │
 *   │  │ChangeTracker│ │BatchManager│ │EntityCache │ │
 *   │  │ dirty state │ │ auto-batch │ │ tag-based  │ │
 *   │  │ diff/patch  │ │ $auto grp  │ │ SWR cache  │ │
 *   │  └─────────────┘ └────────────┘ └────────────┘ │
 *   └──────────────────┬──────────────────────────────┘
 *                      │
 *   ┌──────────────────▼──────────────────────────────┐
 *   │           Supporting Modules                     │
 *   │  MetadataParser  │  SmartFilter                 │
 *   │  ETagManager     │  DraftManager                │
 *   │  NavigationBinding │ OptimisticUpdates          │
 *   │  TypedQueryBuilder │ RequestPipeline            │
 *   │  ODataDevTools                                   │
 *   └──────────────────┬──────────────────────────────┘
 *                      │
 *   ┌──────────────────▼──────────────────────────────┐
 *   │         Existing Service Layer                   │
 *   │  odataService.ts  │  metadataService.ts         │
 *   │  api.ts (axios)                                  │
 *   └─────────────────────────────────────────────────┘
 */

// ---------------------------------------------------------------------------
// Core Model
// ---------------------------------------------------------------------------
export { ODataModel } from './ODataModel'
export type {
  ODataModelConfig,
  ReactiveListBinding,
  ReactivePropertyBinding,
  ReactiveEntityContext,
} from './ODataModel'

// ---------------------------------------------------------------------------
// Change Tracking
// ---------------------------------------------------------------------------
export { ChangeTracker } from './ChangeTracker'
export type { TrackedEntity } from './ChangeTracker'

// ---------------------------------------------------------------------------
// Auto-Batching
// ---------------------------------------------------------------------------
export { BatchManager } from './BatchManager'

// ---------------------------------------------------------------------------
// Metadata Parser (CSDL)
// ---------------------------------------------------------------------------
export { MetadataParser } from './MetadataParser'

// ---------------------------------------------------------------------------
// ETag Manager
// ---------------------------------------------------------------------------
export { ETagManager } from './ETagManager'
export type { ConflictStrategy, ConflictInfo, ETagManagerConfig } from './ETagManager'

// ---------------------------------------------------------------------------
// Draft Manager
// ---------------------------------------------------------------------------
export { DraftManager } from './DraftManager'

// ---------------------------------------------------------------------------
// Declarative Binding Composables
// ---------------------------------------------------------------------------
export {
  useBindingContext,
  useEntityBinding,
  useRelativeBinding,
} from './useBindingContext'
export type {
  UseListBindingReturn,
  UseEntityBindingReturn,
  UseRelativeBindingReturn,
} from './useBindingContext'

// ---------------------------------------------------------------------------
// Smart Filter
// ---------------------------------------------------------------------------
export { SmartFilter } from './SmartFilter'

// ---------------------------------------------------------------------------
// Temporal Binding
// ---------------------------------------------------------------------------
export { useTemporalBinding } from './useTemporalBinding'
export type { UseTemporalBindingReturn, VersionEntry, VersionDiff } from './useTemporalBinding'

// ---------------------------------------------------------------------------
// Deep Operations
// ---------------------------------------------------------------------------
export { useDeepOperation } from './useDeepOperation'
export type { UseDeepOperationReturn, CompositionState } from './useDeepOperation'

// ---------------------------------------------------------------------------
// Navigation Binding
// ---------------------------------------------------------------------------
export { NavigationBinding } from './NavigationBinding'
export type { NavigationBindingState } from './NavigationBinding'

// ---------------------------------------------------------------------------
// Entity Cache
// ---------------------------------------------------------------------------
export { EntityCache } from './EntityCache'
export type { CacheGetResult, CacheSetOptions, CacheStats } from './EntityCache'

// ---------------------------------------------------------------------------
// Request Pipeline
// ---------------------------------------------------------------------------
export {
  RequestPipeline,
  timeoutMiddleware,
  dedupMiddleware,
  retryMiddleware,
  circuitBreakerMiddleware,
  priorityMiddleware,
  fetchMiddleware,
} from './RequestPipeline'

// ---------------------------------------------------------------------------
// Optimistic Updates
// ---------------------------------------------------------------------------
export { OptimisticUpdateManager } from './OptimisticUpdates'

// ---------------------------------------------------------------------------
// Type-Safe Query Builder
// ---------------------------------------------------------------------------
export { ODataQuery, FilterBuilder, FieldFilter, ExpandBuilder } from './TypedQueryBuilder'

// ---------------------------------------------------------------------------
// DevTools
// ---------------------------------------------------------------------------
export { ODataDevTools } from './ODataDevTools'
export type { DevToolsStats } from './ODataDevTools'

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------
export type {
  // Entity & Binding
  BoundEntity,
  PropertyBinding,
  ListBinding,
  ListQueryState,
  BindingContext,
  BindingOptions,
  ExpandBindingOptions,
  TemporalOptions,

  // Batch
  BatchGroupId,
  BatchGroup,
  PendingBatchRequest,

  // Change Tracking
  ChangeSet,
  PendingChange,

  // Draft
  DraftState,
  DraftConfig,
  DraftInstance,

  // CSDL
  CsdlSchema,
  CsdlEntityType,
  CsdlProperty,
  CsdlNavigationProperty,
  CsdlEnumType,
  CsdlEntityContainer,
  CsdlAnnotation,

  // Smart Filter
  SmartFilterWidgetType,
  SmartFilterField,

  // Pipeline
  PipelineMiddleware,
  PipelineRequest,
  PipelineResponse,
  PipelineRequestMetadata,
  RetryConfig,

  // Cache
  CacheEntry,
  CacheOptions,

  // Optimistic
  OptimisticOperation,

  // Navigation
  NavigationBindingOptions,

  // DevTools
  ODataDevToolsEntry,
} from './types'
