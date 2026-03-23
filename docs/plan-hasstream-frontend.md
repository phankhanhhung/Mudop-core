# Entity-Level Media Streams (HasStream) + Frontend Updates

## Context
The OData v4 implementation is feature-complete except for entity-level media streams (`HasStream`). Additionally, the frontend has several gaps: a composition FK naming bug, unused nested expand support, and no file upload/download UI. This plan addresses both.

## Part 1: Entity-Level Media Streams (Backend)

### Design
- Use `@HasStream` annotation on entity (no grammar changes — parsed generically like `@OData.Singleton`)
- Store media content as `bytea` column in PostgreSQL (simple, no external storage dependency)
- Add 3 internal columns: `_media_content`, `_media_content_type`, `_media_etag`
- Entity-level `$value` endpoints: GET (download), PUT (upload), DELETE (remove)
- Entity responses enriched with `@odata.mediaReadLink`, `@odata.mediaContentType`, `@odata.mediaEtag`
- Internal `_media_*` columns never exposed in normal GET responses

### 1. `src/BMMDL.MetaModel/Structure/BmEntity.cs` — Add convenience properties
- `HasStream` property: `=> HasAnnotation("HasStream")`
- `MaxMediaSize` property: reads from `@HasStream(maxSize: N)`, default 10MB (10_485_760)
- Pattern: follows existing `IsTemporal`, `TenantScoped` properties

### 2. `src/BMMDL.CodeGen/PostgresDdlGenerator.cs` — Add media columns in DDL
- In `GenerateTable()`, after temporal columns block (line ~441) and before FK generation (line ~443):
  ```csharp
  if (entity.HasStream)
  {
      columns.Add("    _media_content BYTEA");
      columns.Add("    _media_content_type VARCHAR(255)");
      columns.Add("    _media_etag VARCHAR(64)");
  }
  ```
- All nullable (entity can exist before media is uploaded)

### 3. `src/BMMDL.Runtime.Api/Controllers/ODataMetadataController.cs` — CSDL + metadata DTO
- **CSDL**: In `GenerateEntityTypesForNamespace()` (line ~280), after creating `entityType` element:
  ```csharp
  if (entity.HasStream)
      entityType.Add(new XAttribute("HasStream", "true"));
  ```
- **JSON metadata**: In `GetEntityMetadata()` (line ~872), add `HasStream = entityDef.HasStream` to DTO construction

### 4. `src/BMMDL.Runtime.Api/Models/MetadataModels.cs` — Add HasStream to DTO
- Add to `EntityMetadataDto`:
  ```csharp
  [JsonPropertyName("hasStream")]
  public bool HasStream { get; init; }
  ```

### 5. `src/BMMDL.Runtime.Api/Controllers/DynamicEntityController.cs` — Entity-level $value endpoints

**5a. GET `{id:guid}/$value`** — Download media stream
- Route: `[HttpGet("{id:guid}/$value")]` (won't conflict with `{id:guid}/{property}/$value` — `$value` is a literal, matched before `{property}`)
- Query `_media_content` and `_media_content_type` from DB
- Return `File(bytes, contentType)` or 404 if no content
- Enforce tenant isolation

**5b. PUT `{id:guid}/$value`** — Upload/replace media stream
- Read `Request.Body` as bytes, `Request.ContentType` for MIME type
- Enforce `MaxMediaSize` (return 413 if exceeded)
- Generate ETag as SHA256 hash of content
- UPDATE `_media_content`, `_media_content_type`, `_media_etag` columns
- Return 204 No Content

**5c. DELETE `{id:guid}/$value`** — Remove media stream
- SET all three `_media_*` columns to NULL
- Return 204 No Content

**5d. Enrich entity responses** — In `GetById()` and `List()`
- After building response, if `entityDef.HasStream`:
  - Add `@odata.mediaReadLink` = `{host}/api/odata/{module}/{entity}/{id}/$value`
  - Add `@odata.mediaContentType` from `_media_content_type`
  - Add `@odata.mediaEtag` from `_media_etag`
  - Remove `_media_content`, `_media_content_type`, `_media_etag` from response dict

**5e. Strip media from Create/Update** — In `Create()`, `Update()`, `Replace()`
- If `entityDef.HasStream`, remove `_media_content`, `_media_content_type`, `_media_etag` from input data

**5f. Exclude `_media_content` from default SELECT**
- In `GetById` and `List`, when entity HasStream, add `_media_content` to exclusion list (or strip from results). Don't SELECT the potentially large bytea column in normal queries — only in `GetMediaStream`.

## Part 2: Frontend Updates

### 6. `frontend/src/components/entity/CompositionFormRows.vue` — Fix FK naming bug
- Line 28-32: Replace snake_case FK computation with PascalCase
- Before: `const snakeParent = ...replace(...).toLowerCase(); return \`${snakeParent}_id\``
- After: `return \`${props.parentEntity}Id\``
- Matches CompositionSection.vue (line 56) which correctly uses `${props.parentEntity}Id`

### 7. `frontend/src/types/metadata.ts` — Add hasStream
- Add `hasStream?: boolean` to `EntityMetadata` interface

### 8. `frontend/src/services/odataService.ts` — Media + $count methods
- `getMediaStream(module, entitySet, id)` — GET `$value`, returns `{ blob, contentType }`
- `uploadMediaStream(module, entitySet, id, file)` — PUT `$value` with file body + Content-Type
- `deleteMediaStream(module, entitySet, id)` — DELETE `$value`
- `count(module, entitySet, filter?)` — GET `$count`, returns parsed integer

### 9. `frontend/src/composables/useOData.ts` — Nested expand support
- Import `buildExpandString` from `odataQueryBuilder`
- Add `expandOptions` ref for structured expand: `Record<string, ExpandOptions | true>`
- In `buildQueryOptions()`: if `expandOptions` has entries, use `buildExpandString()` instead of simple `join(',')`
- Add `setExpandOptions()` to returned API
- Backward compatible: simple `expandFields` still works when `expandOptions` is empty

### 10. `frontend/src/components/entity/fields/MediaField.vue` — NEW: Media upload/download
- Props: `module`, `entitySet`, `entityId`, `mediaReadLink?`, `mediaContentType?`, `readonly?`
- Events: `upload(file)`, `delete()`
- Features:
  - Drag-and-drop upload zone (when no media or for replacement)
  - Image preview when content type is `image/*`
  - Download button via `mediaReadLink` (GET blob URL)
  - Delete button (with confirmation)
  - File type/size display
- Uses Tailwind CSS, lucide-vue-next icons (consistent with existing components)

### 11. `frontend/src/views/entity/EntityDetailView.vue` — Media section
- After field display, if `metadata?.hasStream`:
  - Show "Media Content" card section
  - Render `MediaField` component with `@odata.mediaReadLink` and `@odata.mediaContentType` from entity data
  - Wire `upload` event → `odataService.uploadMediaStream()` then reload
  - Wire `delete` event → `odataService.deleteMediaStream()` then reload

## Part 3: Tests

### 12. `src/BMMDL.Tests.New/OData/MediaStreamTests.cs` — NEW: E2E tests
- **HasStream_CsdlMetadata**: Verify `HasStream="true"` on EntityType in CSDL
- **MediaStream_Upload_Download**: PUT $value with bytes → GET $value → verify same content + Content-Type
- **MediaStream_EntityResponse_ContainsMediaAnnotations**: GET entity → verify `@odata.mediaReadLink`, `@odata.mediaContentType`
- **MediaStream_Delete**: DELETE $value → GET $value returns 404/204
- **MediaStream_MaxSize_Rejected**: PUT $value with oversized content → 413
- **MediaStream_ExcludedFromNormalGet**: GET entity → verify no `_media_content` key in response

Note: Tests need a HasStream entity. The WarehouseTest module's `TestWarehouse` entity can be used if `@HasStream` annotation is added to the test fixture, or a new test module with `@HasStream` entity can be created in the test setup.

## Implementation Sequence

1. **MetaModel**: BmEntity.cs (HasStream/MaxMediaSize properties)
2. **DDL**: PostgresDdlGenerator.cs (media columns)
3. **Metadata**: ODataMetadataController.cs + MetadataModels.cs (CSDL HasStream + DTO)
4. **Runtime**: DynamicEntityController.cs (3 $value endpoints + response enrichment + input stripping)
5. **Frontend bug fix**: CompositionFormRows.vue (FK naming)
6. **Frontend types**: metadata.ts (hasStream)
7. **Frontend services**: odataService.ts (media + $count methods)
8. **Frontend composable**: useOData.ts (nested expand)
9. **Frontend component**: MediaField.vue (new)
10. **Frontend view**: EntityDetailView.vue (media section)
11. **Tests**: MediaStreamTests.cs
12. **Build + verify**: `dotnet build && dotnet test`

## Verification
1. `dotnet build BMMDL.sln` — no build errors
2. `dotnet test src/BMMDL.Tests.New/BMMDL.Tests.New.csproj` — all existing + new tests pass
3. Frontend: `cd frontend && npm run build` — no build errors
4. Manual: compile a module with `@HasStream` entity, upload/download media via API, verify in frontend detail view
