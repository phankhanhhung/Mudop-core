# BMMDL Frontend Roadmap

> Updated: 2026-02-19
> Previous version: 2026-02-14 (8-phase plan, Phases 1–6 now complete)

**Goal**: Evolve the BMMDL frontend from a feature-complete enterprise application into an intelligent low-code platform.

**Stack**: Vue 3 + TypeScript + Pinia + Vue Router + Tailwind CSS + shadcn-vue (radix-vue) + Axios + Zod + Monaco Editor + Chart.js + @tanstack/vue-virtual + vue-i18n + SignalR + SheetJS

---

## Current State — What's Done

The frontend has grown from a functional prototype to a **production-grade enterprise application**. The original Phases 1–6 are substantially complete:

### Core Platform (Phase 1–3 — Complete)

| Feature | Implementation | Tests |
|---------|---------------|-------|
| **ETag / Optimistic Concurrency** | `ETagManager` with 4 conflict strategies (fail, refresh-retry, force-overwrite, manual). Auto `If-Match` on all mutations. 412 conflict resolution dialog. | Unit tests |
| **Confirmation Dialog** | Radix-vue `ConfirmDialog` + `useConfirmDialog` composable. Destructive/default variants. | — |
| **Structured Error Handling** | `odataErrorParser.ts` — OData v4 + simple format parsing, field-level errors, network/timeout detection. | — |
| **URL State Sync** | `useUrlState.ts` — page, pageSize, sort, search, filters synced to URL query params with 100ms debounce. | — |
| **Breadcrumbs** | Route-aware `Breadcrumbs.vue` with i18n, semantic HTML, ARIA. | — |
| **Data Grid** | SmartTable with column visibility (`ColumnPicker`), resize (`useColumnResize`), drag-reorder (`useColumnDragReorder`), virtual scrolling (@tanstack/vue-virtual), multi-select (`useRowSelection`), bulk actions (`BulkActionBar`), inline editing (`EditableCell`). All persisted per-entity in localStorage. | — |
| **Data Export** | CSV (`dataExport.ts`, RFC 4180 + BOM) and Excel (`excelExport.ts`, SheetJS). | — |
| **Visual Filter Builder** | `FilterPopover` per-column, `FilterRow` with type-aware operators, `FilterChips` removable display, `AdvancedFilterPanel` with AND/OR groups, `SmartFilterBar`. | — |
| **Saved Views** | `useSavedViews` + `SavedViewSwitcher` — named filter+sort+columns+pageSize per entity in localStorage. | — |

### OData v4 Feature Completeness (Phase 4 — Complete)

| Feature | Implementation | Tests |
|---------|---------------|-------|
| **$metadata Browser** | `MetadataParser` (CSDL XML → TypeScript), `MetadataBrowserView` with tree navigation + detail panel. | Unit tests |
| **$apply Aggregation** | `useAggregation`, `AggregationBuilder`, `AggregationChart` (Chart.js), `EntityAggregationView`. | Unit tests |
| **$batch Operations** | `BatchManager` (auto/direct/manual flush modes), `BatchOperationsView`, `BatchItemForm`, `BatchQueueList`. | Unit tests |
| **Bound/Unbound Actions** | `ActionButtonGroup` (overflow dropdown), `ActionDialog` (parameter input + result display), `ServiceActionsView`. | — |
| **$ref Relationship Manager** | `ReferenceManager`, `refService`, `ManyToManySection`. | — |
| **Delta/Change Tracking** | `useDeltaTracking`, `ChangeTracker`, `ChangeLogPanel`. | Unit tests |

### Admin & Developer Experience (Phase 5 — Complete)

| Feature | Implementation | Tests |
|---------|---------------|-------|
| **BMMDL Code Editor** | Monaco Editor with custom BMMDL language (syntax highlighting, themes, autocomplete). Toolbar: word wrap, minimap, format, copy, fullscreen. | — |
| **Migration Diff Viewer** | `SchemaDiffViewer` — side-by-side DDL diff. | — |
| **User Management** | `UserManagementView`, `UserDialog`, `UserRoleChips`, `UserPermissionChips`, `userService`. | E2E tests |
| **Role Management** | `RoleManagementView`, `RoleDialog`, `PermissionMatrix`, `roleService`. | E2E tests |
| **Audit Log** | `AuditLogView`, `AuditTimeline`, `AuditFilterBar`, `auditService`. | — |
| **System Dashboard** | `DashboardView` with `SystemHealthWidget`, `EntityCountWidget`, `RecentActivityWidget`, `QuickLinksWidget`, `KpiCard`. | E2E tests |
| **Module Dependency Graph** | Force-directed SVG graph with zoom/pan/drag, tooltips, color-coded edges. | — |

### Enterprise Polish (Phase 6 — Complete)

| Feature | Implementation | Tests |
|---------|---------------|-------|
| **i18n** | `vue-i18n` with EN + DE locales (868 keys each). RTL direction switching. Used across 49 files. | — |
| **Accessibility** | ARIA labels across 60+ files. Radix-vue for dialog/focus-trap. Semantic HTML. Screen reader focus management on route change. | E2E (axe-core) |
| **Performance** | Virtual scrolling, `requestDedup.ts`, `queryCache.ts`, route-level lazy loading, service worker (Workbox). | Unit tests |
| **File Upload** | `FileReferenceField` — drag-drop, image preview, size validation, download. `UploadCollection` (multi-file). `fileService`. | — |
| **Temporal Data UI** | `TemporalToolbar` (as-of/valid-at pickers), `VersionHistory`, `VersionDiff`, `useTemporal`. | — |

### Bonus Features (not in original roadmap)

| Feature | Implementation |
|---------|---------------|
| **Global Search** (Ctrl+K) | `GlobalSearchOverlay` — pages, entities, live OData `$search` across entity types, recent searches. |
| **Keyboard Shortcuts** | `useKeyboardShortcuts` + `ShortcutOverlay` — 10 shortcuts across navigation/general/actions. |
| **Draft / Auto-save** | `useDraft` + `DraftManager` + `DraftIndicator` — automatic draft persistence for create/edit flows. |
| **SAP Fiori Components** | 40+ Smart Components: `SmartTable`, `SmartForm`, `SmartFilterBar`, `SmartField`, `AnalyticalTable`, `TreeTable`, `ProcessFlow`, `Timeline`, `Wizard`, `GanttChart`, `PlanningCalendar`, etc. |
| **Flexible Column Layout** | `FlexibleColumnLayout` + `FclLayout` — master/detail/detail-detail responsive layout. |
| **Launchpad** | `LaunchpadView` with `AppTile`, `GenericTile`, `LaunchpadSection` — SAP-style app launcher. |
| **Onboarding** | `useOnboarding` — first-run stepper logic + what's-new version tracking. |
| **PWA** | Vite PWA plugin with Workbox caching (CacheFirst for assets, NetworkFirst for API). |
| **OAuth / Social Login** | `useOAuth`, `SocialLoginButtons`, `ProviderLinkSection`, OAuth callback route. |
| **SignalR Real-time** | `useSignalR` — entity change notifications, tenant/entity group subscription, auto-reconnect. |
| **Personalization Dialog** | `P13nDialog` — columns, sorting, filtering, grouping configuration. |

### Testing Inventory

| Type | Specs | Cases | Coverage Focus |
|------|-------|-------|----------------|
| **Unit (Vitest)** | 22 | ~980 | OData layer, composables, utils, stores |
| **E2E (Playwright)** | 13 | ~125 | Auth, CRUD, admin, filtering, accessibility |
| **Component** | 0 | 0 | (gap) |

---

## Remaining Gaps — Quality Hardening

> Minor issues left over from Phases 1–6 that should be cleaned up before advancing.

### QH-1. Replace Remaining `window.confirm()` Calls

3 production flows still use native `confirm()` instead of the `useConfirmDialog` composable:

| File | Flow | Fix |
|------|------|-----|
| `views/entity/EntityCreateView.vue:225` | Unsaved draft navigation guard | Use `useConfirmDialog` |
| `views/entity/EntityEditView.vue:314` | Unsaved draft navigation guard | Use `useConfirmDialog` |
| `components/entity/fields/MediaField.vue:107` | Delete media content | Use `useConfirmDialog` |

**Effort**: Small (1–2 hours)

### QH-2. Complete i18n Locale Coverage

- EN and DE are complete (868 keys each, fully synced).
- FR and AR locale files are declared in `i18n.ts` but **do not exist** — lazy loading will fail silently.
- RTL infrastructure exists (`applyLocaleDirection()`) but is untested without AR translations.

**Tasks**:
1. Create `locales/fr.json` with all 868 keys
2. Create `locales/ar.json` with all 868 keys
3. Test RTL layout with Arabic locale
4. Add locale switcher E2E tests

**Effort**: Medium (translation + RTL testing)

### QH-3. WCAG 2.1 AA Audit

ARIA is broadly applied (60+ files, Radix-vue for dialogs), but no formal WCAG audit has been done.

**Tasks**:
1. Run axe-core audit across all views (extend existing `accessibility.spec.ts`)
2. Fix color contrast violations
3. Verify full keyboard navigation for SmartTable, FilterPopover, ColumnPicker, AggregationBuilder
4. Add `aria-live` announcements for async operations (batch submit, compile, import)
5. Test with screen reader (NVDA/VoiceOver)

**Effort**: Medium (~1 week)

### QH-4. PWA Hardening

PWA infrastructure exists but uses placeholder assets.

**Tasks**:
1. Design and add proper app icons (192px + 512px PNG, maskable variant)
2. Create offline fallback page
3. Add install prompt UI (use `usePwa` composable)
4. Test service worker update flow
5. Validate `manifest.json` against PWA checklist

**Effort**: Small (2–3 days)

### QH-5. Component Test Layer

Zero component-level rendering tests exist. All 22 unit specs cover composables/utils/OData layer. Vue components (160+) have no render tests.

**Tasks**:
1. Add Vue Test Utils + @testing-library/vue test infrastructure
2. Write component tests for critical entity components: `EntityForm`, `EntityTable`, `CompositionSection`, `FilterPopover`, `ActionDialog`
3. Write component tests for smart components: `SmartTable`, `SmartFilterBar`, `SmartForm`
4. Target: 30–50 component specs covering core user flows

**Effort**: Large (~2 weeks)

### QH-6. Onboarding First-Run Experience

`useOnboarding` composable exists with step logic, but no visible onboarding UI (welcome modal, guided tour) is wired to it.

**Tasks**:
1. Create `OnboardingDialog.vue` with 4-step welcome wizard
2. Create `WhatsNewModal.vue` content for version 1.0
3. Wire to `useOnboarding` composable on first login

**Effort**: Small (2–3 days)

---

## Phase A — Advanced Platform Features

> Transform BMMDL from a CRUD application into a true low-code platform.

### A1. Form Layout Designer

Drag-and-drop form layout editor per entity. Currently `SmartForm` renders fields from server metadata in a fixed layout. The designer would allow users to:

- Arrange fields into tab groups and sections
- Set conditional field visibility (show/hide based on other field values)
- Configure field widths (full/half/third)
- Persist custom layouts per entity per tenant (server-side via user preferences API)

**Depends on**: `SmartForm.vue`, `useSmartForm.ts`, `sortablejs` (already in dependencies via `@vueuse/integrations`)

**New files**: `views/admin/FormDesignerView.vue`, `components/admin/FormCanvas.vue`, `components/admin/FieldPalette.vue`, `services/layoutService.ts`

**Effort**: Large (~2 weeks)

### A2. Dashboard Builder

`DashboardGrid.vue` is currently a stub (8 lines, fixed 3-column grid with a slot). Replace with a configurable widget-based dashboard.

**Features**:
- Widget catalog: entity count cards, charts from `$apply`, recent records, quick action shortcuts, system health, KPIs
- Drag-to-reorder widget positions
- Add/remove widgets
- Per-user dashboard configuration persisted server-side
- Reuse existing `KpiCard`, `EntityCountWidget`, `SystemHealthWidget`, `AggregationChart`

**Depends on**: `DashboardView.vue`, `useDashboard.ts`, Chart.js, sortablejs

**New files**: `components/dashboard/DashboardConfigurator.vue`, `components/dashboard/WidgetCatalog.vue`, `services/dashboardLayoutService.ts`

**Effort**: Medium (~1 week)

### A3. Kanban Board View

Kanban column view for entities with enum status fields. Currently `ProcessFlow` is a linear horizontal diagram — no drag-between-columns board exists.

**Features**:
- Auto-detect entities with enum fields suitable as status columns
- Card display with configurable title/subtitle fields
- Drag cards between columns to update status (PATCH with ETag)
- Card count per column
- Filter and search within kanban view
- Link to entity detail on card click

**New files**: `views/entity/EntityKanbanView.vue`, `components/entity/KanbanBoard.vue`, `components/entity/KanbanColumn.vue`, `components/entity/KanbanCard.vue`, `composables/useKanban.ts`

**Effort**: Medium (~1 week)

### A4. Entity Relationship Diagram

`ModuleDependencyGraph` shows module-level dependencies (force-directed SVG). A separate ERD is needed for entity-level relationships.

**Features**:
- Auto-generated from entity metadata (associations, compositions, cardinalities)
- Interactive: click entity node to navigate to list view
- Visual distinction between association (dashed) and composition (solid)
- Cardinality labels on edges ([0..1], [1..*], etc.)
- Zoom/pan, fit-to-screen
- Filter by module/namespace

**Depends on**: `metadataService.ts`, `MetadataParser` (already parses NavigationProperty + ReferentialConstraint)

**New files**: `views/admin/EntityDiagramView.vue`, `components/admin/EntityRelationshipGraph.vue`

**Effort**: Medium (~1 week)

### A5. Plugin and Extension System

Enable per-entity custom views and action hooks without modifying core code.

**Features**:
- Plugin registry: `registerPlugin({ entityType, views, actions, columns })`
- Custom column renderers per field type
- Custom detail view sections (slot-based)
- Action hooks: before/after CRUD operations (client-side)
- Theme extension beyond light/dark (custom CSS variables)

**New files**: `lib/plugins/PluginRegistry.ts`, `lib/plugins/types.ts`, `components/entity/PluginSlot.vue`

**Effort**: Large (~2 weeks)

---

## Phase B — Intelligence & Integration

> Make BMMDL a connected, intelligent enterprise platform.

### B1. AI-Assisted BMMDL Authoring

Enhance the Monaco editor (already has BMMDL language support) with LLM-powered features.

**Features**:
- Inline code suggestions (ghost text) using an LLM API
- Natural language to BMMDL: describe an entity in plain text, generate DSL code
- Schema review assistant: suggest indexes, normalization, missing relationships
- Compilation error explanation: human-readable fix suggestions alongside error markers

**Depends on**: `BmmdlCodeEditor.vue`, `lib/monaco/bmmdl-completion.ts`

**New files**: `services/aiService.ts`, `components/admin/AiAssistPanel.vue`, `lib/monaco/bmmdl-ai-completion.ts`

**Effort**: Large (~2–3 weeks)

### B2. Natural Language Query

Chat-style interface that translates natural language into OData queries.

**Features**:
- Chat input with conversation history
- "Show me all customers with orders above $1000 this month" → `$filter` + `$expand` query
- Query preview before execution
- Pin queries for reuse (integrate with Saved Views)
- Context-aware: knows available entities, fields, and types from metadata

**New files**: `views/entity/NaturalQueryView.vue`, `components/entity/ChatQueryInput.vue`, `services/nlQueryService.ts`

**Effort**: Large (~2 weeks)

### B3. Import/Export Wizard Enhancement

`ImportDialog` and CSV import/export already work. Enhance into a full wizard experience.

**Tasks**:
1. Multi-step wizard using existing `Wizard` component
2. Column-to-field mapping UI with drag-drop or dropdown mapping
3. Data preview with validation errors highlighted per row
4. Bulk data seeding with progress indicator
5. Full module export (schema + data) as downloadable archive
6. Cross-tenant data migration tool

**Depends on**: `ImportDialog.vue`, `dataImport.ts`, `dataExport.ts`, `excelExport.ts`, `Wizard.vue`

**Effort**: Medium (~1 week)

### B4. External Integration Hub

Webhook and REST API connector management.

**Features**:
- Webhook configuration per entity (on create/update/delete events)
- REST API connector: map external endpoints to BMMDL entities
- Scheduled sync jobs with conflict resolution
- Integration health dashboard with execution logs
- Test webhook button (send sample payload)

**Depends on**: Backend event system (Events P3 outbox + P6 integration events — already complete)

**New files**: `views/admin/IntegrationHubView.vue`, `components/admin/WebhookEditor.vue`, `components/admin/ConnectorEditor.vue`, `services/integrationService.ts`

**Effort**: Large (~2 weeks)

### B5. Reporting & Print

Report template designer with PDF generation.

**Features**:
- Report templates: list, detail, summary layouts
- Template editor: select fields, grouping, sorting, header/footer
- PDF generation from entity data (client-side via jsPDF or server-side)
- Print-optimized CSS for browser print
- Scheduled report delivery (requires backend support)
- Embeddable report links for external sharing

**New files**: `views/admin/ReportDesignerView.vue`, `components/admin/ReportTemplate.vue`, `services/reportService.ts`, `utils/pdfGenerator.ts`

**Effort**: Large (~2 weeks)

### B6. Collaboration

Record-level discussion and coordination.

**Features**:
- Comments/activity feed on entity detail view (reuse `FeedList` component)
- @mention users with notification delivery (via SignalR, already integrated)
- Record locking indicator ("User X is editing" — via SignalR presence)
- Change request workflow: propose edits → review → approve/reject

**Depends on**: `FeedList.vue`, `useSignalR.ts`, `NotificationCenter.vue`

**New files**: `components/entity/CommentSection.vue`, `components/entity/RecordLockBadge.vue`, `services/commentService.ts`, `composables/useRecordLock.ts`

**Effort**: Large (~2 weeks)

---

## Implementation Priority

| Sprint | Items | Effort | Value |
|--------|-------|--------|-------|
| 1 | QH-1 (confirm), QH-4 (PWA), QH-6 (onboarding) | ~3 days | Quick quality wins |
| 2 | A2 (Dashboard Builder) | ~1 week | Immediate visual impact |
| 3 | A3 (Kanban Board) | ~1 week | New interaction paradigm |
| 4 | A4 (Entity Diagram) | ~1 week | Developer productivity |
| 5–6 | QH-3 (WCAG audit) + QH-2 (i18n FR/AR) | ~2 weeks | Enterprise compliance |
| 7–8 | QH-5 (Component tests) | ~2 weeks | Engineering confidence |
| 9–10 | A1 (Form Layout Designer) | ~2 weeks | Low-code capability |
| 11–12 | A5 (Plugin System) | ~2 weeks | Extensibility |
| 13–14 | B3 (Import Wizard) + B1 (AI Authoring) | ~3 weeks | Intelligence features |
| 15–16 | B2 (NL Query) + B6 (Collaboration) | ~4 weeks | User delight |
| 17–18 | B4 (Integration Hub) + B5 (Reporting) | ~4 weeks | Enterprise integration |

---

## Recommended New Libraries

| Need | Library | Rationale |
|------|---------|-----------|
| Drag-and-drop layouts | `sortablejs` via `@vueuse/integrations` | Already in dependencies, lightweight |
| PDF generation | `jsPDF` + `jspdf-autotable` | Client-side, no server dependency |
| Rich text (comments) | TipTap | Vue-native, extensible, headless |
| ERD rendering | Custom SVG (extend `ModuleDependencyGraph` pattern) | No new dependency needed |
| AI streaming | `@microsoft/fetch-event-source` | SSE for LLM streaming responses |

Libraries already integrated: Monaco Editor, Chart.js + vue-chartjs, @tanstack/vue-virtual, vue-i18n, @microsoft/signalr, SheetJS (xlsx), papaparse, @axe-core/playwright.

---

## Metrics & Success Criteria

| Metric | Current | Target |
|--------|---------|--------|
| Unit test specs | 22 | 70+ |
| E2E test specs | 13 | 25+ |
| Component test specs | 0 | 50+ |
| i18n locales | 2 (EN, DE) | 4 (+ FR, AR) |
| WCAG 2.1 AA violations | Unknown | 0 critical, 0 serious |
| Lighthouse PWA score | Partial | 100 |
| Vue components | ~160 | ~180 (new views/features) |
