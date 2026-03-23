# OData v4 Completion Plan for BMMDL

> **Created**: 2026-01-26
> **Updated**: 2026-02-06
> **Status**: ✅ COMPLETE — All OData v4 Core features implemented.

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Current State Analysis](#current-state-analysis)
3. [Gap Analysis](#gap-analysis)
4. [Implementation Phases](#implementation-phases)
   - [Phase 1: Core Foundation](#phase-1-core-foundation-critical)
   - [Phase 2: Relationship Management](#phase-2-relationship-management)
   - [Phase 3: Metadata Completeness](#phase-3-metadata-completeness)
   - [Phase 4: Advanced Features](#phase-4-advanced-features)
5. [New Files Structure](#new-files-structure)
6. [Timeline](#timeline)
7. [Acceptance Criteria](#acceptance-criteria)

---

## Executive Summary

This document tracks OData v4 implementation progress in the BMMDL platform. The implementation is now **near-complete** with comprehensive coverage of OData v4 Core features.

### Status Summary

| Category | Features | Status |
|----------|----------|--------|
| **Core CRUD** | Deep Insert, Deep Update, ETag | ✅ All Complete |
| **Operations** | Bound Actions, $batch, Async | ✅ All Complete |
| **Relationships** | $ref, Containment, Constraints | ✅ All Complete |
| **Metadata** | Actions/Functions, ReferentialConstraint, ContainsTarget | ✅ Complete |
| **Query Options** | $filter, $select, $expand, $levels, $apply, $compute, $search | ✅ All Complete |
| **Property Access** | $value (GET/PUT/DELETE) | ✅ Complete |
| **Delta & Prefer** | Delta responses ($deltatoken), Prefer header handling | ✅ All Complete |
| **Metadata Annotations** | Computed fields, Capability annotations, Singleton support | ✅ All Complete |

---

## Current State Analysis

### What's Working (✅)

| Feature | Status | Location |
|---------|--------|----------|
| Basic CRUD (GET/POST/PATCH/PUT/DELETE) | ✅ Complete | `Controllers/DynamicEntityController.cs` |
| $filter | ✅ Complete | `Runtime/DataAccess/FilterExpressionParser.cs` |
| $select | ✅ Complete | `Runtime/DataAccess/DynamicSqlBuilder.cs` |
| $orderby | ✅ Complete | `Runtime/DataAccess/FilterExpressionParser.cs` |
| $top / $skip | ✅ Complete | `Runtime/DataAccess/DynamicSqlBuilder.cs` |
| $count | ✅ Complete | `Controllers/DynamicEntityController.cs` |
| $expand (ManyToOne via JOIN, OneToMany via batch) | ✅ Complete | `Runtime/DataAccess/DynamicSqlBuilder.cs` |
| $expand with $levels (recursive) | ✅ Complete | `Handlers/RecursiveExpandHandler.cs` |
| $apply (aggregation) | ✅ Complete | `Runtime/DataAccess/ApplyExpressionParser.cs` |
| $search | ✅ Complete | `Runtime/DataAccess/SearchExpressionParser.cs` |
| $compute | ✅ Complete | `Controllers/DynamicEntityController.cs` |
| $value (property access) | ✅ Complete | `Controllers/DynamicEntityController.cs` (GET/PUT/DELETE) |
| OData v4 Headers | ✅ Complete | `Middleware/ODataHeaderMiddleware.cs` |
| OData v4 Response Format | ✅ Complete | `Models/ODataResponse.cs` |
| $metadata (CSDL XML) | ✅ Complete | `Controllers/ODataMetadataController.cs` |
| ReferentialConstraint in CSDL | ✅ Complete | `Controllers/ODataMetadataController.cs` |
| ContainsTarget in CSDL | ✅ Complete | `Controllers/ODataMetadataController.cs` |
| Deep Insert | ✅ Complete | `Handlers/DeepInsertHandler.cs` |
| Deep Update | ✅ Complete | `Handlers/DeepUpdateHandler.cs` |
| Composition Support | ✅ Complete | DDL FK generation, $expand, deep insert/update |
| ETag / Optimistic Concurrency | ✅ Complete | `Controllers/DynamicEntityController.cs` (If-Match, 412) |
| $batch | ✅ Complete | `Controllers/BatchController.cs` |
| Bound Actions/Functions | ✅ Complete | `Controllers/EntityActionController.cs` |
| Unbound Actions/Functions | ✅ Complete | `Controllers/ODataServiceController.cs` |
| $ref (relationship management) | ✅ Complete | `Controllers/EntityReferenceController.cs` |
| Containment Navigation | ✅ Complete | `Controllers/EntityActionController.cs` (contained entity CRUD) |
| Async Operations (202 Accepted) | ✅ Complete | `Controllers/AsyncOperationController.cs` |
| Temporal Queries | ⚠️ Extension | `asOf`, `validAt`, `includeHistory` |

**Additional Parsers** (sub-parsers used by FilterExpressionParser):
- `BMMDL.Runtime/DataAccess/Parsers/ArithmeticParser.cs`
- `BMMDL.Runtime/DataAccess/Parsers/FunctionParser.cs`
- `BMMDL.Runtime/DataAccess/Parsers/LambdaParser.cs`

### All Core Features Complete ✅

All previously missing items have been implemented (Feb 2026):
1. **Delta Responses** ✅ — `DeltaTokenService` wired into List, `@odata.deltaLink` in responses, HMAC-signed tokens
2. **Singleton Entities** ✅ — `@OData.Singleton` annotation, keyless GET/PATCH routing, `<Singleton>` in CSDL
3. **Capability Annotations** ✅ — OData vocabulary references, per-EntitySet Filter/Sort/Expand/Insert/Update/Delete annotations
4. **Prefer Header** ✅ — `return=minimal` (204 No Content), `odata.maxpagesize`, `Preference-Applied` header
5. **Computed/ReadOnly Fields** ✅ — `Org.OData.Core.V1.Computed` and `Immutable` annotations in CSDL, computed fields stripped from input

---

### ⚠️ BMMDL Extensions (Non-Standard OData Features)

The following features are **custom BMMDL extensions** and NOT part of OData v4 Core specification:

| Feature | Description | OData v4 Status |
|---------|-------------|----------------|
| **Temporal Queries** | `asOf`, `validAt`, `includeHistory` parameters | ❌ Not in OData v4 (SAP HANA style extension) |
| **Versions Endpoint** | `GET /{entity}/{id}/versions` | ❌ Custom extension |
| **Soft Delete** | `DELETE ?soft=true` parameter | ❌ Custom extension |
| **Module Routing** | `/api/odata/{module}/{entity}` | ❌ OData uses flat EntitySet names |
| **Temporal Strategies** | `InlineHistory`, `SeparateTables` | ❌ Custom enterprise feature |

> [!NOTE]
> These extensions provide valuable enterprise capabilities but clients should be aware they
> are not interoperable with standard OData v4 tooling without custom handling.

---

## Gap Analysis

### 1. Batch Operations ✅ COMPLETE

**Feature**: `POST /api/odata/$batch` endpoint for multi-operation requests

**Implementation**: `Controllers/BatchController.cs`, `Models/BatchModels.cs`

- Supports GET, POST, PATCH, DELETE operations within batch
- Dependency tracking via `DependsOn` (returns 424 on missing dependency)
- Per-item status and response body
- URL parsing for module/entity/ID extraction
- Query option support ($filter, $orderby, $select, $top, $skip)

---

### 2. Deep Insert (Nested Entity Creation) ✅ COMPLETE

**Feature**: POST with nested entities in request body

**Implemented**: Feb 2026 — Commits `b64bafb`, `1d3f016`

**Implementation**:
- `DeepInsertHandler.cs` detects nested objects/arrays in POST body
- Processes both `Associations` and `Compositions` from entity definition
- Auto-populates FK on child entities (derived as `parentEntityName + "Id"`)
- Executes within a single transaction (parent first, then children)
- Returns nested structure in response via `$expand`

---

### 3. Deep Update (Nested Entity Modification) ✅ COMPLETE

**Feature**: PATCH/PUT with nested entities

**Implemented**: Feb 2026 — Commits `b64bafb`, `1d3f016`

**Implementation**:
- `DeepUpdateHandler.cs` detects nested objects/arrays in PATCH body
- Determines operation per nested item: has ID → UPDATE, no ID → CREATE
- Processes both `Associations` and `Compositions` from entity definition
- Auto-populates FK on new child entities
- Executes within a single transaction

---

### 4. ETag / Optimistic Concurrency ✅ COMPLETE

**Feature**: Prevent lost updates with version tracking

**Implementation**: `Controllers/DynamicEntityController.cs`

- Weak ETag generation on entity retrieval (GetById, Create, Update, Replace)
- `If-Match` header validation on PATCH, PUT, DELETE
- Returns `412 Precondition Failed` on ETag mismatch

---

### 5. Bound Actions/Functions ✅ COMPLETE

**Feature**: POST/GET to invoke operations bound to specific entity instances

**Implementation**: `Controllers/EntityActionController.cs`

- `POST /{module}/{entity}/{id}/{actionName}` — Bound action invocation via `IActionExecutor`
- `GET /{module}/{entity}/{id}/{functionName}()` — Bound function invocation with inline params
- `POST /{module}/{entity}/{actionName}` — Collection-bound actions
- Route disambiguation between bound operations and navigation properties
- Entity instance injected into evaluation context
- Metadata includes `IsBound="true"` actions/functions (`ODataMetadataController`)

---

### 6. $ref for Relationship Management ✅ COMPLETE

**Feature**: Create/modify/delete relationships without modifying entities

**Implementation**: `Controllers/EntityReferenceController.cs`

- `POST /{entity}/{id}/{navProp}/$ref` — Create relationship (parses `@odata.id`)
- `PUT /{entity}/{id}/{navProp}/$ref` — Replace relationship (delegates to CreateRef)
- `DELETE /{entity}/{id}/{navProp}/$ref` — Remove relationship (nullifies FK)
- Validates parent entity exists before FK operation
- Extracts entity ID from OData URL format

---

### 7. Delta Responses ✅ COMPLETE

**Feature**: Track changes since last query for efficient sync

**Implementation**: `Services/DeltaTokenService.cs`, `Controllers/DynamicEntityController.cs`, `Models/ODataResponse.cs`

- HMAC-signed Base64 delta tokens encoding timestamp + entity + tenant
- `$deltatoken` query parameter filters records by `updated_at`/`created_at` since token
- `@odata.deltaLink` in collection responses when track-changes requested
- `ODataCollectionResponse<T>` includes `DeltaLink` property

---

### 8. Singleton Entities ✅ COMPLETE

**Feature**: Single-valued entity sets without key

**Implementation**: `Controllers/DynamicEntityController.cs`, `Controllers/ODataMetadataController.cs`

- `@OData.Singleton` annotation in BMMDL marks entities as singletons
- `<Singleton>` element in CSDL (instead of EntitySet)
- List action detects singletons and redirects to `GetSingletonInternal` (returns first record for tenant)
- `PatchSingleton` action handles PATCH without ID segment

---

### 9. Stream / Media Entities ⚠️ PARTIAL

**Property-level `$value`** — ✅ COMPLETE (`DynamicEntityController`):
- `GET /{entity}/{id}/{property}/$value` — Returns raw value (byte[] → octet-stream, string → text/plain)
- `PUT /{entity}/{id}/{property}/$value` — Writes raw body bytes to property
- `DELETE /{entity}/{id}/{property}/$value` — Sets property to null

**Entity-level media entities** — ❌ NOT IMPLEMENTED:
- No `HasStream="true"` attribute on EntityType in CSDL
- No entity-level `GET /{entity}/{id}/$value` for media entities (e.g., a `Document` entity where the entity itself is a file)
- No `@odata.mediaReadLink` / `@odata.mediaEditLink` in responses

**Impact**: Low — Property-level $value covers current needs. Entity-level media only needed if entities themselves represent files.

---

### 10. Async Operations ✅ COMPLETE

**Feature**: Long-running operations with async pattern

**Implementation**: `Controllers/AsyncOperationController.cs`

- `GET /$operations/{id}` — Monitor operation status
- `DELETE /$operations/{id}` — Cancel running operation
- Returns `202 Accepted` with `Retry-After` header while running
- Returns `200` with result on success, error details on failure
- Operation state tracking: Running, Succeeded, Failed

---

### 11. $levels for Recursive Expand ✅ COMPLETE

**Feature**: Expand nested relationships to arbitrary depth

**Implementation**: `Handlers/RecursiveExpandHandler.cs`

- Recursive expansion with configurable `$levels=N`
- Supports self-referential relationships (e.g., org trees)
- Batch fetch for both ManyToOne and OneToMany to avoid N+1 queries
- Depth limiting (max 10 levels) to prevent infinite loops

---

### 12. Containment Navigation ✅ COMPLETE

**Feature**: Nested resource routing for compositions

**Implementation**: `Controllers/EntityActionController.cs`, `Controllers/EntityNavigationController.cs`

- ✅ DDL: Auto FK column generation for compositions
- ✅ `$expand` for compositions (OneToMany via batch sub-queries)
- ✅ Deep Insert/Update handles composition children
- ✅ `GET /{parent}/{parentId}/{navProp}` — List contained entities
- ✅ `GET /{parent}/{parentId}/{navProp}/{childId}` — Get single contained entity
- ✅ `POST /{parent}/{parentId}/{navProp}` — Create contained entity with auto FK
- ✅ Metadata declares `ContainsTarget="true"` on composition navigation properties

---

### 13. Referential Constraints in Metadata ✅ COMPLETE

**Feature**: Declare FK relationships in CSDL

**Implementation**: `Controllers/ODataMetadataController.cs` — `GenerateNavigationProperty()`

- ✅ `<ReferentialConstraint>` elements on single-valued navigation properties (ManyToOne, OneToOne)
- ✅ Maps FK fields to referenced entity keys

---

### 14. Action/Function Imports in Metadata ✅ COMPLETE

**Feature**: Declare service operations in CSDL

```xml
<FunctionImport Name="GetProductsByRating" Function="Sales.GetProductsByRating" />
<ActionImport Name="ApproveOrder" Action="Sales.ApproveOrder" />
```

**Current State**:
- ✅ `ODataMetadataController` now generates `<Action>` and `<Function>` definitions
- ✅ `EntityContainer` includes `<ActionImport>` and `<FunctionImport>` elements
- ✅ Service document lists ActionImports and FunctionImports

**Implementation**: Jan 28, 2026 - Commit `33e17c2`

**Impact**: High - Metadata completeness

---

### 15. Capability Annotations ✅ COMPLETE

**Feature**: Semantic annotations in metadata

**Implementation**: `Controllers/ODataMetadataController.cs`

- Vocabulary references: `Org.OData.Core.V1`, `Org.OData.Capabilities.V1`
- Per-EntitySet annotations: FilterRestrictions, SortRestrictions, ExpandRestrictions, SearchRestrictions, InsertRestrictions, UpdateRestrictions, DeleteRestrictions
- `GenerateCapabilityAnnotations()` and `CreateCapabilityRecord()` helper methods

---

### 16. Prefer Header Handling ✅ COMPLETE

**Feature**: Client preferences for response format

**Implementation**: `Controllers/DynamicEntityController.cs`

- `ParsePreferHeader()` extracts `return=minimal`/`return=representation` and `odata.maxpagesize`/`maxpagesize`
- `ApplyPreferReturn()` returns 204 No Content with `Preference-Applied` header for `return=minimal`
- Wired into Create, Update, Replace actions
- `maxpagesize` applied to List pagination with `Preference-Applied` header

---

## Implementation Phases

---

## Phase 1: Core Foundation ✅ COMPLETE

**Priority**: 🔴 Critical — **All items complete**

### 1.1 ETag & Optimistic Concurrency ✅ COMPLETE

**Implementation**: `DynamicEntityController.cs`

- [x] Weak ETag generation on entity retrieval
- [x] ETag header in GET/PATCH/POST/PUT responses
- [x] If-Match header validation on PATCH/PUT/DELETE
- [x] 412 Precondition Failed on mismatch

---

### 1.2 Deep Insert (Nested Create) ✅ COMPLETE

| Aspect | Detail |
|--------|--------|
| **Status** | ✅ Implemented Feb 2026 |
| **Files** | `DynamicEntityController.cs`, `Handlers/DeepInsertHandler.cs` |
| **Commits** | `b64bafb`, `1d3f016` |

**Completed Tasks**:
- [x] Detect nested objects in POST body
- [x] Resolve navigation property types from MetaModel
- [x] Insert order: parent first, then children
- [x] Generate multi-INSERT transaction
- [x] Populate FK fields automatically (derived as `parentEntityName + "Id"`)
- [x] Support composition (cascade create)
- [x] Support association (link to existing)
- [x] Return nested structure in response

---

### 1.3 Deep Update (Nested Modify) ✅ COMPLETE

| Aspect | Detail |
|--------|--------|
| **Status** | ✅ Implemented Feb 2026 |
| **Files** | `DynamicEntityController.cs`, `Handlers/DeepUpdateHandler.cs` |
| **Commits** | `b64bafb`, `1d3f016` |

**Completed Tasks**:
- [x] Detect nested objects in PATCH body
- [x] Determine operation type per nested item:
  - Has ID → UPDATE
  - No ID → CREATE new
- [x] Generate multi-statement transaction
- [x] Auto-populate FK on new child entities
- [x] Process both associations and compositions

---

### 1.4 Bound Actions & Functions ✅ COMPLETE

**Implementation**: `Controllers/EntityActionController.cs`, `Controllers/ODataMetadataController.cs`

- [x] `POST /{module}/{entity}/{id}/{actionName}` — Bound action invocation
- [x] `GET /{module}/{entity}/{id}/{functionName}()` — Bound function invocation
- [x] `POST /{module}/{entity}/{actionName}` — Collection-bound actions
- [x] Entity instance injected into evaluation context
- [x] `IsBound="true"` actions/functions in CSDL metadata
- [x] Route disambiguation between bound operations and navigation properties

---

### 1.5 $batch Endpoint ✅ COMPLETE

**Implementation**: `Controllers/BatchController.cs`, `Models/BatchModels.cs`

- [x] `POST /api/odata/$batch` endpoint (JSON format)
- [x] Supports GET, POST, PATCH, DELETE within batch
- [x] Dependency tracking via `DependsOn` (424 on missing dependency)
- [x] Per-item status and response body
- [x] URL parsing for module/entity/ID extraction
- [x] Query option support within batch items

---

## Phase 2: Relationship Management ✅ COMPLETE

**Priority**: 🟠 High — **All items complete**

### 2.1 $ref Endpoint ✅ COMPLETE

**Implementation**: `Controllers/EntityReferenceController.cs`

- [x] `POST /{entity}/{id}/{navProp}/$ref` — Create relationship
- [x] `PUT /{entity}/{id}/{navProp}/$ref` — Replace relationship
- [x] `DELETE /{entity}/{id}/{navProp}/$ref` — Remove relationship (nullifies FK)
- [x] Parse `@odata.id` from request body
- [x] Validate parent entity exists

---

### 2.2 Containment Navigation ✅ COMPLETE

**Implementation**: `Controllers/EntityActionController.cs`, `Controllers/EntityNavigationController.cs`, `Controllers/ODataMetadataController.cs`

- [x] `ContainsTarget="true"` in CSDL for compositions
- [x] `GET /{parent}/{parentId}/{navProp}` — List contained entities
- [x] `GET /{parent}/{parentId}/{navProp}/{childId}` — Get single contained entity
- [x] `POST /{parent}/{parentId}/{navProp}` — Create contained entity with auto FK
- [x] Parent FK enforcement on child operations

---

### 2.3 Referential Constraints in Metadata ✅ COMPLETE

**Implementation**: `Controllers/ODataMetadataController.cs` — `GenerateNavigationProperty()`

- [x] `<ReferentialConstraint>` elements on single-valued navigation properties
- [x] Maps FK fields to referenced entity keys

---

## Phase 3: Metadata Completeness ✅ COMPLETE

**Priority**: 🟠 High — **All items complete**

### 3.1 Action/Function Imports ✅ COMPLETE

**Implementation**: `Controllers/ODataMetadataController.cs` — Commit `33e17c2`

- [x] `<Function>` and `<Action>` elements for bound and unbound operations
- [x] `<FunctionImport>` and `<ActionImport>` in EntityContainer
- [x] Parameters with Type and ReturnType
- [x] `IsBound="true"` for entity-bound operations
- [x] Service document includes action/function imports

---

### 3.2 Capability Annotations ✅ COMPLETE

**Implementation**: `ODataMetadataController.cs` — Feb 2026

- [x] Import OData vocabulary namespaces (Core.V1, Capabilities.V1)
- [x] Add Capabilities annotations per EntitySet (Filter, Sort, Expand, Search, Insert, Update, Delete restrictions)
- [x] Add Core annotations for computed fields (`Org.OData.Core.V1.Computed`)
- [x] Add Immutable annotation for keys (`Org.OData.Core.V1.Immutable`)

---

### 3.3 Computed/ReadOnly Fields ✅ COMPLETE

**Implementation**: `ODataMetadataController.cs`, `DynamicEntityController.cs` — Feb 2026

- [x] Detect computed fields from BmField.IsComputed / IsVirtual
- [x] Add `Org.OData.Core.V1.Computed` annotation in CSDL
- [x] Strip computed fields from POST/PATCH input data (`StripComputedFields`)
- [x] Return computed values in responses

---

## Phase 4: Advanced Features ✅ COMPLETE

**Priority**: 🟡 Medium — **All items complete**

### 4.1 Delta Responses ✅ COMPLETE

**Implementation**: `Services/DeltaTokenService.cs`, `Controllers/DynamicEntityController.cs`, `Models/ODataResponse.cs` — Feb 2026

- [x] Generate opaque delta tokens (HMAC-signed Base64 with timestamp + entity + tenant)
- [x] Parse `$deltatoken` query parameter
- [x] Query only changes since token timestamp (filter by `updated_at`/`created_at`)
- [x] Include `@odata.deltaLink` in collection responses
- [x] Support track-changes preference via `odata.track-changes`
- [x] `ODataCollectionResponse<T>.DeltaLink` property

---

### 4.2 Async Operations ✅ COMPLETE

**Implementation**: `Controllers/AsyncOperationController.cs`

- [x] `GET /$operations/{id}` — Monitor operation status (202 while running, 200 on complete)
- [x] `DELETE /$operations/{id}` — Cancel running operation
- [x] Retry-After header on pending operations
- [x] State tracking: Running, Succeeded, Failed

---

### 4.3 $levels Recursive Expand ✅ COMPLETE

**Implementation**: `Handlers/RecursiveExpandHandler.cs`

- [x] Recursive expansion with `$levels=N`
- [x] Self-referential relationship support (e.g., org trees)
- [x] Batch fetch for ManyToOne and OneToMany (avoids N+1)
- [x] Depth limiting (max 10 levels) prevents infinite loops

---

### 4.4 Singleton Entities ✅ COMPLETE

**Implementation**: `Controllers/DynamicEntityController.cs`, `Controllers/ODataMetadataController.cs` — Feb 2026

- [x] `@OData.Singleton` annotation recognized in MetaModel
- [x] Generate `<Singleton>` in CSDL (instead of EntitySet)
- [x] Route `GET /{singleton}` without ID → `GetSingletonInternal` (returns first record for tenant)
- [x] Route `PATCH /{singleton}` without ID → `PatchSingleton` action
- [x] Resolve singleton instance by tenant

---

### 4.5 Stream Properties ⚠️ PARTIAL

**Current State**:
- ✅ Property-level `$value`: `GET/PUT/DELETE /{entity}/{id}/{property}/$value` (`DynamicEntityController`)
- ❌ Entity-level media streams (`HasStream="true"`)
- ❌ `@odata.mediaReadLink` / `@odata.mediaEditLink`

**Remaining Tasks**:
- [ ] Add `HasStream="true"` on EntityType in CSDL
- [ ] Entity-level `$value` endpoint for media entities
- [ ] Return media link annotations in responses

---

### 4.6 Prefer Header Handling ✅ COMPLETE

**Implementation**: `Controllers/DynamicEntityController.cs` — Feb 2026

- [x] `ParsePreferHeader()` parses Prefer header values
- [x] Support `return=representation` (default behavior)
- [x] Support `return=minimal` → returns 204 No Content
- [x] Support `odata.maxpagesize` / `maxpagesize` for pagination
- [x] Support `odata.track-changes` for delta link generation
- [x] Set `Preference-Applied` response header

---

## Files Structure (Current)

```
src/BMMDL.Runtime.Api/
├── Controllers/
│   ├── DynamicEntityController.cs      # Core OData CRUD
│   ├── ODataMetadataController.cs      # $metadata and service document
│   ├── ODataServiceController.cs       # Unbound actions/functions
│   ├── BatchController.cs              # $batch endpoint
│   ├── EntityNavigationController.cs   # Navigation property access
│   ├── EntityReferenceController.cs    # $ref management
│   ├── EntityActionController.cs       # Bound actions/functions
│   ├── AsyncOperationController.cs     # Operation monitoring
│   └── ...
├── Handlers/
│   ├── DeepInsertHandler.cs            # ✅ Nested create logic
│   ├── DeepUpdateHandler.cs            # ✅ Nested update logic
│   └── RecursiveExpandHandler.cs       # $levels recursive expand
├── Models/
│   ├── ODataResponse.cs                # OData response wrappers
│   ├── MetadataModels.cs               # CSDL metadata models
│   └── BatchModels.cs                  # Batch request/response models
└── Middleware/
    └── ODataHeaderMiddleware.cs        # OData headers
```

### Additional Services

```
src/BMMDL.Runtime.Api/
├── Services/
│   └── DeltaTokenService.cs            # ✅ HMAC-signed delta tokens
└── Middleware/
    └── ODataHeaderMiddleware.cs        # ✅ OData response headers
```

---

## Timeline

```
┌─────────────────────────────────────────────────────────────────┐
│                    PHASE 1: Core (4-6 weeks)                    │
├─────────────────────────────────────────────────────────────────┤
│ Week 1-2: ETag + Deep Insert                                    │
│ Week 2-3: Deep Update + Bound Actions                           │
│ Week 4-6: $batch Endpoint                                       │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│              PHASE 2: Relationships (2-3 weeks)                 │
├─────────────────────────────────────────────────────────────────┤
│ Week 7: $ref Endpoint                                           │
│ Week 8: Containment Navigation                                  │
│ Week 8-9: Referential Constraints                               │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│              PHASE 3: Metadata (1-2 weeks)                      │
├─────────────────────────────────────────────────────────────────┤
│ Week 9-10: Action/Function Imports + Annotations                │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│              PHASE 4: Advanced (3-4 weeks)                      │
├─────────────────────────────────────────────────────────────────┤
│ Week 11-12: Delta + Async                                       │
│ Week 13: $levels + Singletons                                   │
│ Week 14: Streams + Prefer                                       │
└─────────────────────────────────────────────────────────────────┘

Total: ~14 weeks for complete OData v4 compliance
```

---

## Acceptance Criteria

### Phase 1 ✅ COMPLETE:
- [x] `PATCH` with `If-Match` header returns 412 if ETag doesn't match
- [x] `POST` with nested objects creates parent + children in 1 transaction (Deep Insert)
- [x] `PATCH` with nested objects updates/creates children appropriately (Deep Update)
- [x] `POST /Entity/{id}/action` works for bound actions
- [x] `GET /Entity/{id}/function()` works for bound functions
- [x] `POST /$batch` processes multiple operations
- [x] Dependency tracking in batch (DependsOn, 424 on failure)

### Phase 2 ✅ COMPLETE:
- [x] `POST /Entity/{id}/Nav/$ref` creates relationship
- [x] `DELETE /Entity/{id}/Nav/$ref` removes relationship
- [x] `GET /Parent/{parentId}/Children/{childId}` works for containment
- [x] `POST /Parent/{parentId}/Children` creates child with auto FK
- [x] CSDL has `<ReferentialConstraint>` elements
- [x] CSDL has `ContainsTarget="true"` on compositions

### Phase 3 ✅ COMPLETE:
- [x] CSDL has `<Function>` and `<Action>` elements (bound + unbound)
- [x] CSDL has `<FunctionImport>` and `<ActionImport>` in container
- [x] CSDL has capability annotations (Filter, Sort, Expand, Search, Insert, Update, Delete)
- [x] Computed fields marked with `Org.OData.Core.V1.Computed` in metadata
- [x] Computed fields stripped from POST/PATCH input data

### Phase 4 ✅ COMPLETE:
- [x] `$deltatoken` returns only changes since token
- [x] Response includes `@odata.deltaLink`
- [x] `Prefer: respond-async` returns 202 + Retry-After
- [x] `GET /$operations/{id}` returns operation status
- [x] `$expand=Children($levels=3)` works correctly
- [x] Circular reference detection prevents infinite loops (max 10 levels)
- [x] Singleton routes work without ID segment (`@OData.Singleton` annotation)
- [x] `GET /Entity/{id}/{property}/$value` returns raw property value
- [x] `PUT /Entity/{id}/{property}/$value` updates raw property value
- [x] `Prefer: return=minimal` returns 204 No Content with Preference-Applied

---

## Risk Assessment

| Risk | Likelihood | Impact | Status |
|------|------------|--------|--------|
| Deep Insert/Update complexity | High | High | ✅ Resolved — implemented and working |
| $batch parsing edge cases | Medium | Medium | ✅ Resolved — JSON batch format implemented |
| Performance regression | Medium | High | Ongoing — monitor with N+1 avoidance in batch expand |
| Breaking existing clients | Low | High | Ongoing — maintain backward compatibility |
| Temporal + Deep operations | High | Medium | ✅ Resolved — temporal strategies handled in controller |

---

## Dependencies on External Changes

All OData v4 Core features are now implemented. The only remaining gap is entity-level media streams (`HasStream`), which would require:

| Feature | Requires MetaModel Change? | Requires Grammar Change? |
|---------|---------------------------|-------------------------|
| Full Media Streams | Yes - IsStream flag | Optional - @Stream |

---

## Testing Strategy

### Unit Tests
- ETag generation/parsing
- Deep insert/update handlers
- Batch request parsing
- $levels expand SQL generation

### Integration Tests
- Full CRUD with ETags
- Nested create/update scenarios
- Batch with change sets
- Bound action invocation
- $ref operations

### E2E Tests
- Complete workflow scenarios
- Concurrent update handling
- Large batch operations
- Delta sync workflows

---

## References

- [OData v4.0 Specification](https://docs.oasis-open.org/odata/odata/v4.0/odata-v4.0-part1-protocol.html)
- [OData CSDL](https://docs.oasis-open.org/odata/odata-csdl-xml/v4.01/odata-csdl-xml-v4.01.html)
- [OData Vocabularies](https://github.com/oasis-tcs/odata-vocabularies)
- [OData Batch Processing](https://docs.oasis-open.org/odata/odata/v4.0/os/part1-protocol/odata-v4.0-os-part1-protocol.html#_Toc372793748)

---

## Implementation Log

### 2026-01-28: Unbound Operations & Metadata (Phase 3.1)

**Commit**: `33e17c2`

**Implemented Features**:
| Feature | Route | Status |
|---------|-------|--------|
| Unbound Action | `POST /api/odata/{service}/{action}` | ✅ |
| Unbound Function | `GET /api/odata/{service}/{function}()` | ✅ |
| ActionImport in CSDL | `<ActionImport>` elements | ✅ |
| FunctionImport in CSDL | `<FunctionImport>` elements | ✅ |
| Service Document | Includes action/function imports | ✅ |

**Files Added**:
- `src/BMMDL.Runtime.Api/Controllers/ODataServiceController.cs` - Unbound operation routing
- `src/BMMDL.Tests.New/OData/UnboundOperationTests.cs` - Integration tests

**Files Modified**:
- `src/BMMDL.Runtime.Api/Controllers/ODataMetadataController.cs`
  - Added `GenerateUnboundActions()`, `GenerateUnboundFunctions()`
  - Extended `GenerateEntityContainer()` with ActionImport/FunctionImport
  - Updated `GetServiceDocument()` to include imports

---

### 2026-01-29 — 2026-02-05: Frontend, Admin, Deep Insert/Update, Compositions

**Commits**: `126a44f` through `1d3f016`

**Implemented Features**:
| Feature | Status |
|---------|--------|
| Vue 3 frontend with TypeScript + Tailwind CSS | ✅ |
| Admin module management (compile & install from UI) | ✅ |
| Admin key authorization (`X-Admin-Key` header) | ✅ |
| Association picker for FK fields | ✅ |
| Entity name display instead of UUIDs for FK fields | ✅ |
| Deep Insert (`DeepInsertHandler`) | ✅ |
| Deep Update (`DeepUpdateHandler`) | ✅ |
| Composition support in frontend (detail view, inline forms) | ✅ |
| `$expand` for OneToMany compositions (LEFT JOIN) | ✅ |
| Composition FK generation in DDL | ✅ |
| Registry cardinality persistence (OneToMany/ManyToOne) | ✅ |
| `$metadata` with full EntityTypes, NavigationProperties, Actions, Functions | ✅ |

**Key Files Added**:
- `frontend/` — Complete Vue 3 SPA
- `src/BMMDL.Runtime.Api/Handlers/DeepInsertHandler.cs`
- `src/BMMDL.Runtime.Api/Handlers/DeepUpdateHandler.cs`
- `src/BMMDL.Runtime.Api/Models/MetadataModels.cs`
- `src/BMMDL.Registry.Api/Models/AdminModels.cs`
- `frontend/src/views/admin/AdminModulesView.vue`
- `frontend/src/components/entity/CompositionSection.vue`
- `frontend/src/components/entity/CompositionFormRows.vue`
- `frontend/src/utils/associationDisplay.ts`

**Key Files Modified**:
- `src/BMMDL.Runtime.Api/Controllers/DynamicEntityController.cs` — Deep insert/update integration, `$expand` support
- `src/BMMDL.Runtime/DataAccess/DynamicSqlBuilder.cs` — `$expand` via LEFT JOIN for associations/compositions
- `src/BMMDL.CodeGen/PostgresDdlGenerator.cs` — Composition FK column generation
- `src/BMMDL.Registry/Repositories/EfCoreMetaModelRepository.cs` — Cardinality persistence
- `src/BMMDL.Registry.Api/Services/AdminService.cs` — Module compile & install workflow

---

### 2026-02-06: Final 5 OData v4 Features (Phases 3-4 Completion)

**Implemented Features**:
| Feature | Implementation | Status |
|---------|---------------|--------|
| Computed/ReadOnly Fields | `Org.OData.Core.V1.Computed` + `Immutable` annotations; `StripComputedFields` | ✅ |
| Capability Annotations | Per-EntitySet vocabulary annotations in CSDL | ✅ |
| Prefer Header | `return=minimal` (204), `maxpagesize`, `Preference-Applied` | ✅ |
| Delta Responses | `DeltaTokenService` wired into List, `@odata.deltaLink` | ✅ |
| Singleton Entities | `@OData.Singleton` annotation, keyless GET/PATCH | ✅ |

**Files Modified**:
- `src/BMMDL.Runtime.Api/Controllers/ODataMetadataController.cs` — Computed/Immutable annotations, capability annotations, vocabulary references
- `src/BMMDL.Runtime.Api/Controllers/DynamicEntityController.cs` — StripComputedFields, ParsePreferHeader, ApplyPreferReturn, delta token wiring, GetSingletonInternal, PatchSingleton
- `src/BMMDL.Runtime.Api/Models/ODataResponse.cs` — DeltaLink property on ODataCollectionResponse

