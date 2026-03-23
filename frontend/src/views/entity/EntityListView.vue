<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useRoute, useRouter, RouterLink } from 'vue-router'
import { useBindingContext } from '@/odata/useBindingContext'
import { SmartFilter } from '@/odata/SmartFilter'
import { odataService } from '@/services/odataService'
import { useMetadata } from '@/composables/useMetadata'
import { useUrlState } from '@/composables/useUrlState'
import { useConfirmDialog } from '@/composables/useConfirmDialog'
import { useColumnConfig } from '@/composables/useColumnConfig'
import { useRowSelection } from '@/composables/useRowSelection'
import { useInlineEdit } from '@/composables/useInlineEdit'
import { useFilterBuilder } from '@/composables/useFilterBuilder'
import { useTemporal } from '@/composables/useTemporal'
import { useSavedViews } from '@/composables/useSavedViews'
import type { SavedView } from '@/composables/useSavedViews'
import { useUiStore } from '@/stores/ui'
import { useEntityRouteParams } from '@/composables/useEntityRouteParams'
import { useAssociationHelpers } from '@/composables/useAssociationHelpers'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import FlexibleColumnLayout from '@/components/layout/FlexibleColumnLayout.vue'
import DynamicPage from '@/components/layout/DynamicPage.vue'
import { useFcl } from '@/composables/useFcl'
import SmartTable from '@/components/smart/SmartTable.vue'
import TreeTable from '@/components/smart/TreeTable.vue'
import type { TreeNode } from '@/composables/useTreeTable'
import SmartFilterBar from '@/components/smart/SmartFilterBar.vue'
import DraftIndicator from '@/components/smart/DraftIndicator.vue'
import OverflowToolbar from '@/components/smart/OverflowToolbar.vue'
import type { ToolbarItem } from '@/components/smart/OverflowToolbar.vue'
import AnalyticsDashboard from '@/components/analytics/AnalyticsDashboard.vue'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Spinner } from '@/components/ui/spinner'
import { ConfirmDialog } from '@/components/common'
import ActionButtonGroup from '@/components/entity/ActionButtonGroup.vue'
import ChangeTracker from '@/components/entity/ChangeTracker.vue'
import TemporalToolbar from '@/components/entity/TemporalToolbar.vue'
import { Pencil, Trash2, BarChart3, Upload, FileDown, Plus, ListTree, List, Kanban, Network } from 'lucide-vue-next'
import SavedViewSwitcher from '@/components/entity/SavedViewSwitcher.vue'
import { exportToCsv, generateFilename, exportTemplate } from '@/utils/dataExport'
import { exportToXlsx } from '@/utils/excelExport'
import { useBulkActions } from '@/composables/useBulkActions'
import ImportDialog from '@/components/entity/ImportDialog.vue'
import ModuleExportDialog from '@/components/entity/ModuleExportDialog.vue'
import type { FilterCondition, SortOption } from '@/types/odata'
import { usePreferences } from '@/utils/preferences'
import { useI18n } from 'vue-i18n'
import type { ExpandBindingOptions } from '@/odata/types'
import { usePlugins } from '@/composables/usePlugins'
import PluginSlot from '@/components/entity/PluginSlot.vue'

const { t } = useI18n()
const { preferences } = usePreferences()
const route = useRoute()
const router = useRouter()
const uiStore = useUiStore()
const confirmDialog = useConfirmDialog()

const { module, entity, displayName: routeDisplayName } = useEntityRouteParams()

const { customViews: pluginCustomViews } = usePlugins(entity, module)
const showPluginView = ref<string | null>(null)

// URL state synchronization
const {
  initialPage,
  initialPageSize,
  initialSort,
  initialSearch,
  initialFilters,
  updateUrl,
  parseQuery
} = useUrlState()

// Load metadata
const {
  metadata,
  fields,
  keyFields,
  isLoading: metadataLoading,
  error: metadataError,
  load: loadMetadata
} = useMetadata({
  module: module.value,
  entity: entity.value,
  autoLoad: false
})

// ── SmartFilter integration ──
const smartFilter = new SmartFilter()
const smartFilterFields = computed(() => {
  if (!metadata.value) return []
  return smartFilter.generateFields(metadata.value)
})
// Expose SmartFilter fields for template/parent component use
defineExpose({ smartFilterFields })

// ── List binding via useBindingContext (replaces useOData for list operations) ──
const listBinding = useBindingContext<Record<string, unknown>>(
  entity.value,
  module.value,
  {
    $top: route.query.pageSize ? initialPageSize : preferences.value.pageSize,
    $orderby: initialSort,
    $filter: initialFilters,
    $search: initialSearch,
    autoLoad: false,
  }
)

// Destructure list binding return values
const {
  data,
  totalCount,
  currentPage,
  pageSize,
  isLoading: dataLoading,
  error: dataError,
  refresh,
  goToPage,
  setPageSize: bindingSetPageSize,
  setExpand: bindingSetExpand,
  setTemporal,
} = listBinding

// Local tracking refs for template bindings (the template reads these for display)
const sortOptions = ref<SortOption[]>([...initialSort])
const filters = ref<FilterCondition[]>([...initialFilters])
const search = ref(initialSearch)

// Combined error state
const error = computed(() => metadataError.value || dataError.value)

// Entity display name
const displayName = computed(() => metadata.value?.displayName || routeDisplayName.value)

// ── Phase 2: Column config ──
const entityKey = computed(() => `${module.value}.${entity.value}`)
const {
  visibleColumns,
} = useColumnConfig(entityKey.value, fields)

// ── Phase 2: Row selection ──
const {
  toggleRow,
  deselectAll
} = useRowSelection()

// ── Phase 2: Inline editing (used by SmartTable internally) ──
useInlineEdit({
  onSave: async (rowId, field, value) => {
    await odataService.update(module.value, entity.value, rowId, { [field]: value })
    await refresh()
  }
})

// ── Phase 3: Filter builder ──
const filterBuilder = useFilterBuilder({
  onFiltersChange: (newFilters: FilterCondition[]) => {
    // Update local tracking ref and push to binding (triggers load automatically)
    filters.value = [...newFilters]
    listBinding.filter(newFilters)
    notifyStateChange()
  }
})

// ── Temporal support ──
const temporal = useTemporal()
const isTemporal = computed(() => metadata.value?.isTemporal === true)
const temporalState = computed({
  get: () => ({
    asOf: temporal.asOf.value,
    validAt: temporal.validAt.value,
    includeHistory: temporal.includeHistory.value
  }),
  set: (val) => {
    temporal.asOf.value = val.asOf
    temporal.validAt.value = val.validAt
    temporal.includeHistory.value = val.includeHistory
  }
})

// ── Phase 3: Saved views ──
const savedViews = useSavedViews({ entityKey: entityKey })

// ── Flexible Column Layout ──
const fcl = useFcl('OneColumn')

const showAnalytics = ref(false)
const showTreeView = ref(false)

// Self-referencing association detection for tree view
const selfReferenceAssociation = computed(() => {
  if (!metadata.value) return null
  return metadata.value.associations.find(
    (a) => a.targetEntity === entity.value || a.targetEntity.endsWith('.' + entity.value)
  ) ?? null
})

const hasSelfReference = computed(() => selfReferenceAssociation.value !== null)

const selfReferenceFkField = computed(() => {
  if (!selfReferenceAssociation.value) return ''
  return selfReferenceAssociation.value.foreignKey || (selfReferenceAssociation.value.name + 'Id')
})

// Build tree data from flat data for TreeTable
const treeData = computed<TreeNode[]>(() => {
  if (!hasSelfReference.value || !showTreeView.value) return []
  const fkField = selfReferenceFkField.value
  const keyField = keyFields.value[0] ?? 'Id'
  const rows = data.value

  // Build a map of id -> row
  const map = new Map<string, TreeNode>()
  for (const row of rows) {
    const key = String(row[keyField] ?? row['ID'] ?? row['id'] ?? '')
    map.set(key, { ...row, children: [] })
  }

  // Build tree
  const roots: TreeNode[] = []
  for (const row of rows) {
    const key = String(row[keyField] ?? row['ID'] ?? row['id'] ?? '')
    const parentKey = row[fkField] ? String(row[fkField]) : null
    const node = map.get(key)!
    if (parentKey && map.has(parentKey)) {
      const parent = map.get(parentKey)!
      if (!parent.children) parent.children = []
      parent.children.push(node)
    } else {
      roots.push(node)
    }
  }

  return roots
})

// Detail panel state
const selectedId = ref<string | null>(null)
const detailData = ref<Record<string, unknown> | null>(null)
const detailLoading = ref(false)
const detailError = ref<string | null>(null)
const isDeleting = ref(false)

const { expandableAssociations, getFormattedValue, getAssociationLink, isAssociationField } = useAssociationHelpers(fields, metadata, detailData)

// Guard: when we programmatically change state (sort, filter, etc.), our notifyStateChange
// callback schedules a URL update. When that URL update fires, the route watcher would see
// a query change and try to reload — but the state is already current. This flag prevents that.
const pendingProgrammaticUrlUpdate = ref(false)
// Guard to prevent re-entrant URL sync when restoring state from URL (back/forward)
const syncingFromUrl = ref(false)

// Notify URL state synchronization after binding mutations
function notifyStateChange() {
  pendingProgrammaticUrlUpdate.value = true
  updateUrl({
    page: currentPage.value,
    pageSize: pageSize.value,
    sort: sortOptions.value,
    search: search.value,
    filters: filters.value,
  })
}

function setupExpand() {
  const assocs = expandableAssociations.value
  if (assocs.length > 0) {
    const expandMap: Record<string, ExpandBindingOptions | true> = {}
    for (const a of assocs) {
      expandMap[a.name] = true
    }
    bindingSetExpand(expandMap)
  }
}

// Initialize on mount
onMounted(async () => {
  await loadMetadata()

  // Singleton entities redirect to their detail view
  if (metadata.value?.isSingleton) {
    router.replace(`/odata/${module.value}/${entity.value}/_singleton`)
    return
  }

  setupExpand()

  // Set initial page from URL state
  if (initialPage > 1) {
    currentPage.value = initialPage
  }

  await refresh()

  // Load saved views from server and apply default if one exists
  await savedViews.loadViews()
  const defaultView = savedViews.defaultView.value
  if (defaultView) {
    handleSelectView(defaultView.id)
  }
})

// Reload when route params (module/entity) change
watch(
  [module, entity],
  async () => {
    selectedId.value = null
    detailData.value = null
    deselectAll()
    filterBuilder.clearAll()
    savedViews.selectView(null)
    await loadMetadata()
    setupExpand()
    await refresh()
    await savedViews.loadViews()
    // Apply default view if one exists (same as onMounted)
    const defaultView = savedViews.defaultView.value
    if (defaultView) {
      handleSelectView(defaultView.id)
    }
  }
)

// Sync state from URL when route query changes externally (e.g., browser back/forward).
// When we programmatically change state (sort, filter, etc.), our notifyStateChange
// callback schedules a URL update via updateUrl(). When that fires, the route changes and
// this watcher triggers — but we should skip it because the state is already current and
// load was already called by the binding mutation.
watch(
  () => route.query,
  async (newQuery) => {
    // Skip if this is our own URL update (from sort/filter/etc.)
    if (pendingProgrammaticUrlUpdate.value) {
      pendingProgrammaticUrlUpdate.value = false
      return
    }
    // Skip if we're already syncing (re-entrant guard)
    if (syncingFromUrl.value) return
    syncingFromUrl.value = true
    try {
      const urlState = parseQuery(newQuery)
      let stateChanged = false

      // Update page
      if (urlState.page !== currentPage.value) {
        currentPage.value = urlState.page
        stateChanged = true
      }

      // Update pageSize
      if (urlState.pageSize !== pageSize.value) {
        pageSize.value = urlState.pageSize
        stateChanged = true
      }

      // Update sort
      const currentSortStr = JSON.stringify(sortOptions.value)
      const urlSortStr = JSON.stringify(urlState.sort)
      if (currentSortStr !== urlSortStr) {
        sortOptions.value = urlState.sort
        stateChanged = true
      }

      // Update search
      if (urlState.search !== search.value) {
        search.value = urlState.search
        stateChanged = true
      }

      // Update filters (batch update to avoid cascade)
      const currentFilterStr = JSON.stringify(filters.value)
      const urlFilterStr = JSON.stringify(urlState.filters)
      if (currentFilterStr !== urlFilterStr) {
        // Update local ref and push to binding (triggers load automatically)
        filters.value = [...urlState.filters]
        await listBinding.filter(urlState.filters)
        // filter() handles load internally
      } else if (stateChanged) {
        // For sort/search/page changes, push to binding and refresh
        if (urlState.sort.length > 0) {
          await listBinding.sort(urlState.sort[0].field, urlState.sort[0].direction)
        }
        // Always push search to binding — search.value was already updated above,
        // so we compare against the binding's current search state, not the local ref
        if (urlState.search) {
          await listBinding.search(urlState.search)
        } else {
          await refresh()
        }
      }
    } finally {
      syncingFromUrl.value = false
    }
  }
)

// Load detail when selectedId changes
watch(selectedId, async (newId) => {
  if (!newId) {
    detailData.value = null
    detailError.value = null
    return
  }
  await loadDetail(newId)
})

async function loadDetail(id: string) {
  detailLoading.value = true
  detailError.value = null
  try {
    // Build expand options for detail view
    const expandOpts: Record<string, string> = {}
    const assocs = expandableAssociations.value
    if (assocs.length > 0) {
      expandOpts.$expand = assocs.map((a) => a.name).join(',')
    }
    detailData.value = await odataService.getById(module.value, entity.value, id, expandOpts)
  } catch (e) {
    detailError.value = e instanceof Error ? e.message : t('entity.failedToLoad')
  } finally {
    detailLoading.value = false
  }
}

// Navigation handlers
function handleView(id: string) {
  if (selectedId.value === id) {
    selectedId.value = null
    fcl.closeDetail()
  } else {
    selectedId.value = id
    fcl.navigateToDetail()
  }
}

function handleEdit(id: string) {
  router.push(`/odata/${module.value}/${entity.value}/${id}/edit`)
}

async function handleDelete(id: string) {
  const confirmed = await confirmDialog.confirm({
    title: t('entity.deleteRecord'),
    description: t('entity.deleteConfirm'),
    confirmLabel: t('common.delete'),
    variant: 'destructive'
  })
  if (!confirmed) return

  const wasSelected = selectedId.value === id
  isDeleting.value = true

  try {
    await odataService.delete(module.value, entity.value, id)
    await refresh()
    uiStore.success(t('entity.deleted'), t('entity.deletedSuccess'))
    if (wasSelected) {
      selectedId.value = null
    }
  } catch {
    uiStore.error(t('entity.deleteFailed'), t('entity.deleteFailedMessage'))
  } finally {
    isDeleting.value = false
  }
}

function handleDetailEdit() {
  if (selectedId.value) {
    handleEdit(selectedId.value)
  }
}

async function handleDetailDelete() {
  if (selectedId.value) {
    await handleDelete(selectedId.value)
  }
}

async function handleSearch(query: string) {
  search.value = query
  await listBinding.search(query)
  notifyStateChange()
}

async function handlePageChange(page: number) {
  await goToPage(page)
  notifyStateChange()
}

async function handlePageSizeChange(size: number) {
  await bindingSetPageSize(size)
  notifyStateChange()
}

// ── Export handler ──
function handleExportAll() {
  const exportFields = visibleColumns.value
    .map((col) => fields.value.find((f) => f.name === col.field))
    .filter((f): f is (typeof fields.value)[0] => f != null)

  exportToCsv({
    filename: generateFilename(entity.value),
    fields: exportFields,
    data: data.value
  })
}

function handleClearAllFilters() {
  filterBuilder.clearAll()
}

// ── Phase 3: Saved view handlers ──
async function handleSaveNewView(name: string) {
  try {
    const view = await savedViews.saveView(name, {
      filters: filters.value,
      sort: sortOptions.value,
      pageSize: pageSize.value,
      search: search.value,
    })
    savedViews.selectView(view.id)
    uiStore.success('View Saved', `"${name}" saved successfully`)
  } catch {
    uiStore.error('Save Failed', 'Failed to save view')
  }
}

async function handleUpdateView(viewId: string) {
  try {
    await savedViews.updateView(viewId, {
      filters: filters.value,
      sort: sortOptions.value,
      pageSize: pageSize.value,
      search: search.value,
    })
    uiStore.success('View Updated', 'View updated with current settings')
  } catch {
    uiStore.error('Update Failed', 'Failed to update view')
  }
}

async function handleDeleteView(viewId: string) {
  const view = savedViews.views.value.find((v: SavedView) => v.id === viewId)
  const confirmed = await confirmDialog.confirm({
    title: 'Delete View',
    description: `Delete saved view "${view?.name ?? viewId}"?`,
    confirmLabel: 'Delete',
    variant: 'destructive',
  })
  if (!confirmed) return
  try {
    await savedViews.deleteView(viewId)
    uiStore.success('View Deleted', 'Saved view removed')
  } catch {
    uiStore.error('Delete Failed', 'Failed to delete view')
  }
}

async function handleRenameView(viewId: string, name: string) {
  try {
    await savedViews.renameView(viewId, name)
    uiStore.success('View Renamed', `View renamed to "${name}"`)
  } catch {
    uiStore.error('Rename Failed', 'Failed to rename view')
  }
}

async function handleSetDefaultView(viewId: string | null) {
  try {
    await savedViews.setDefault(viewId)
    uiStore.success('Default Updated', viewId ? 'Default view set' : 'Default view cleared')
  } catch {
    uiStore.error('Update Failed', 'Failed to update default view')
  }
}

function handleSelectView(viewId: string | null) {
  savedViews.selectView(viewId)
  if (viewId) {
    const view = savedViews.views.value.find((v: SavedView) => v.id === viewId)
    if (view) {
      // Update local tracking refs
      sortOptions.value = [...view.sort]
      pageSize.value = view.pageSize
      search.value = view.search
      // Set binding state without triggering intermediate loads
      listBinding.setSort(view.sort)
      listBinding.setSearch(view.search)
      // Batch-replace filters — this triggers filter() → load() which runs
      // with the accumulated sort+search+filter state (single request)
      filterBuilder.replaceAll(view.filters)
      // notifyStateChange() is already called by onFiltersChange callback
    }
  } else {
    // Reset to defaults
    sortOptions.value = []
    search.value = ''
    // Set binding state without triggering intermediate loads
    listBinding.setSort([])
    listBinding.setSearch('')
    // Batch-replace filters — triggers single load with cleared state
    filterBuilder.replaceAll([])
    // notifyStateChange() is already called by onFiltersChange callback
  }
}

// ── Temporal apply handler ──
async function handleTemporalApply() {
  const params = temporal.getQueryParams()
  setTemporal(
    Object.keys(params).length > 0
      ? {
          asOf: params.asOf,
          validAt: params.validAt,
          includeHistory: params.includeHistory === 'true' ? true : undefined
        }
      : {}
  )
  await refresh()
}

// ── Import dialog ──
const isImportDialogOpen = ref(false)
const isExportDialogOpen = ref(false)

function handleOpenImport() {
  isImportDialogOpen.value = true
}

function handleImported(count: number) {
  uiStore.success('Import Complete', `${count} records imported successfully`)
  refresh()
}

function handleExportTemplate() {
  exportTemplate(entity.value, fields.value)
}

function handleSmartRowClick(_row: Record<string, unknown>, id: string) {
  handleView(id)
}

function handleSmartSort(field: string, direction: 'asc' | 'desc') {
  sortOptions.value = [{ field, direction }]
  listBinding.sort(field, direction)
  notifyStateChange()
}

function handleSmartExport(format: 'csv' | 'json' | 'xlsx') {
  if (format === 'csv') {
    handleExportAll()
  } else if (format === 'xlsx') {
    handleExportXlsx()
  }
}

function handleExportXlsx() {
  const exportFields = visibleColumns.value
    .map((col) => fields.value.find((f) => f.name === col.field))
    .filter((f): f is (typeof fields.value)[0] => f != null)

  exportToXlsx({
    filename: generateFilename(entity.value, 'xlsx'),
    fields: exportFields,
    data: data.value
  })
}

// ── Header toolbar items (OverflowToolbar) ──
const headerToolbarItems = computed<ToolbarItem[]>(() => {
  const items: ToolbarItem[] = []
  if (!metadata.value?.isAbstract) {
    items.push(
      { id: 'create', label: t('entity.newRecord'), icon: Plus, variant: 'default', priority: 10 },
      { id: 'sep1', label: '', separator: true },
      { id: 'export-template', label: 'Export Template', icon: FileDown, variant: 'outline', priority: 3 },
      { id: 'import', label: 'Import', icon: Upload, variant: 'outline', priority: 3 },
      { id: 'export', label: t('entity.exportModule'), icon: FileDown, variant: 'outline', priority: 3 },
      { id: 'sep2', label: '', separator: true },
    )
  }
  if (hasSelfReference.value) {
    items.push({
      id: 'tree-view',
      label: showTreeView.value ? 'List View' : 'Tree View',
      icon: showTreeView.value ? List : ListTree,
      variant: 'outline',
      priority: 2,
    })
  }
  items.push({ id: 'analytics', label: t('common.analytics'), icon: BarChart3, variant: 'outline', priority: 2 })
  items.push({ id: 'kanban', label: t('kanban.title'), icon: Kanban, variant: 'outline', priority: 2 })
  items.push({ id: 'erd', label: t('erd.title'), icon: Network, variant: 'outline', priority: 2 })
  // Plugin custom views
  for (const view of pluginCustomViews.value) {
    items.push({
      id: `plugin-view:${view.id}`,
      label: view.label,
      icon: view.icon,
      variant: 'outline',
      priority: 3,
    })
  }
  return items
})

function handleToolbarItemClick(id: string) {
  switch (id) {
    case 'create':
      router.push(`/odata/${module.value}/${entity.value}/new`)
      break
    case 'export-template':
      handleExportTemplate()
      break
    case 'import':
      handleOpenImport()
      break
    case 'export':
      isExportDialogOpen.value = true
      break
    case 'tree-view':
      showTreeView.value = !showTreeView.value
      break
    case 'analytics':
      showAnalytics.value = !showAnalytics.value
      break
    case 'kanban':
      router.push(`/odata/${module.value}/${entity.value}/kanban`)
      break
    case 'erd':
      router.push(`/erd?module=${module.value}`)
      break
    default:
      if (id.startsWith('plugin-view:')) {
        const viewId = id.slice('plugin-view:'.length)
        showPluginView.value = showPluginView.value === viewId ? null : viewId
      }
      break
  }
}

function handleSmartSelectionChange(selectedIds: string[]) {
  deselectAll()
  for (const id of selectedIds) {
    toggleRow(id)
  }
}

function handleSmartFilterChange(newFilters: FilterCondition[]) {
  filterBuilder.replaceAll(newFilters)
}

// ── Phase C: Bulk actions ──
const bulkActions = useBulkActions({
  module: module,
  entitySet: entity,
  onSuccess: () => refresh(),
})

async function handleBulkDelete(ids: string[]) {
  const confirmed = await confirmDialog.confirm({
    title: t('entity.deleteRecord'),
    description: `Delete ${ids.length} selected record${ids.length > 1 ? 's' : ''}? This cannot be undone.`,
    confirmLabel: t('common.delete'),
    variant: 'destructive'
  })
  if (!confirmed) return

  const result = await bulkActions.bulkDelete(ids)
  if (result.succeeded > 0) {
    // Clear detail panel if selected item was deleted
    if (selectedId.value && ids.includes(selectedId.value)) {
      selectedId.value = null
    }
    uiStore.success('Bulk Delete', `${result.succeeded} record${result.succeeded > 1 ? 's' : ''} deleted`)
  }
  if (result.failed > 0) {
    uiStore.error('Bulk Delete', `${result.failed} deletion${result.failed > 1 ? 's' : ''} failed`)
  }
}

function handleBulkExport(format: 'csv' | 'xlsx', ids: string[]) {
  const selectedData = data.value.filter((row) => {
    const id = String(row['Id'] ?? row['ID'] ?? row['id'] ?? '')
    return ids.includes(id)
  })

  const exportFields = visibleColumns.value
    .map((col) => fields.value.find((f) => f.name === col.field))
    .filter((f): f is (typeof fields.value)[0] => f != null)

  if (format === 'csv') {
    exportToCsv({
      filename: generateFilename(entity.value),
      fields: exportFields,
      data: selectedData
    })
  } else {
    exportToXlsx({
      filename: generateFilename(entity.value, 'xlsx'),
      fields: exportFields,
      data: selectedData
    })
  }
}

// ── Phase C: Row edit handler ──
async function handleRowSave(rowId: string, changes: Record<string, unknown>) {
  try {
    await odataService.update(module.value, entity.value, rowId, changes)
    await refresh()
    uiStore.success('Record Updated', 'Changes saved successfully')
  } catch {
    uiStore.error('Update Failed', 'Failed to save changes')
  }
}
</script>

<template>
  <DefaultLayout>
    <div v-if="metadataLoading" class="flex items-center justify-center py-12" role="status" aria-label="Loading entity list">
      <Spinner size="lg" />
    </div>

    <div v-else class="h-full">
    <FlexibleColumnLayout
      :layout="fcl.effectiveLayout.value"
      @layout-change="fcl.setLayout"
    >
      <!-- Begin column: Entity list with DynamicPage -->
      <template #begin>
        <DynamicPage initialCollapsed collapseOnContentFocus>
          <template #title>
            <div class="flex items-center gap-2">
              <h1 class="text-xl font-semibold truncate">{{ displayName }}</h1>
              <SavedViewSwitcher
                :views="savedViews.views.value"
                :current-view-id="savedViews.currentViewId.value"
                :default-view-id="savedViews.defaultView.value?.id ?? null"
                @select="handleSelectView"
                @save="handleSaveNewView"
                @update="handleUpdateView"
                @delete="handleDeleteView"
                @rename="handleRenameView"
                @set-default="handleSetDefaultView"
              />
            </div>
          </template>

          <template #headerActions>
            <OverflowToolbar
              :items="headerToolbarItems"
              class="flex-1"
              @item-click="handleToolbarItemClick"
            />
            <DraftIndicator :module="module" :entitySet="entity" />
            <ChangeTracker :module="module" :entitySet="entity" @refresh="refresh" />
          </template>

          <template #header>
            <div class="space-y-3">
              <TemporalToolbar
                v-if="isTemporal"
                v-model="temporalState"
                @apply="handleTemporalApply"
              />
              <SmartFilterBar
                v-if="metadata"
                :module="module"
                :entitySet="entity"
                :metadata="metadata"
                :activeFilters="filterBuilder.activeFilters.value"
                :searchQuery="search"
                @filter-change="handleSmartFilterChange"
                @search-change="handleSearch"
                @clear-all="handleClearAllFilters"
              />
            </div>
          </template>

          <!-- Main content: Tree View -->
          <TreeTable
            v-if="metadata && showTreeView && hasSelfReference"
            :treeData="treeData"
            :toggleField="selfReferenceFkField"
            :metadata="metadata"
            :title="displayName"
            :isLoading="dataLoading"
            :error="error"
            :sortField="sortOptions[0]?.field"
            :sortDirection="sortOptions[0]?.direction"
            @row-click="handleSmartRowClick"
            @sort="handleSmartSort"
            @refresh="refresh"
          />

          <!-- Main content: List View -->
          <SmartTable
            v-else-if="metadata"
            :module="module"
            :entitySet="entity"
            :metadata="metadata"
            :data="data"
            :totalCount="totalCount"
            :currentPage="currentPage"
            :pageSize="pageSize"
            :isLoading="dataLoading"
            :error="error"
            :selectionMode="'multi'"
            :title="displayName"
            :enableExport="true"
            :enableBulkActions="true"
            :enableRowEdit="true"
            :enableVirtualScroll="true"
            :sortField="sortOptions[0]?.field"
            :sortDirection="sortOptions[0]?.direction"
            :activeFilters="filterBuilder.activeFilters.value"
            @row-click="handleSmartRowClick"
            @sort="handleSmartSort"
            @page="handlePageChange"
            @page-size="handlePageSizeChange"
            @edit="handleEdit"
            @delete="handleDelete"
            @refresh="refresh"
            @export="handleSmartExport"
            @selection-change="handleSmartSelectionChange"
            @bulk-delete="handleBulkDelete"
            @bulk-export="handleBulkExport"
            @row-save="handleRowSave"
          />

          <AnalyticsDashboard
            v-if="showAnalytics && metadata"
            :module="module"
            :entity="entity"
            :entityMetadata="metadata"
            class="mt-4"
          />
        </DynamicPage>
      </template>

      <!-- Mid column: Detail panel -->
      <template #mid>
        <div v-if="selectedId" class="h-full overflow-y-auto">
          <Card class="m-4 rounded-lg">
            <CardHeader class="pb-3">
              <div class="flex items-center justify-between">
                <CardTitle class="text-lg">{{ $t('entity.details', { name: displayName }) }}</CardTitle>
              </div>
              <!-- Action buttons -->
              <div v-if="detailData" class="flex items-center gap-2 pt-2 flex-wrap">
                <ActionButtonGroup
                  v-if="metadata?.boundActions?.length || metadata?.boundFunctions?.length"
                  :actions="metadata?.boundActions ?? []"
                  :functions="metadata?.boundFunctions ?? []"
                  :module="module"
                  :entitySet="entity"
                  :entityId="selectedId!"
                />
                <Button variant="outline" size="sm" @click="handleDetailEdit">
                  <Pencil class="mr-2 h-3.5 w-3.5" />
                  {{ $t('common.edit') }}
                </Button>
                <Button
                  variant="destructive"
                  size="sm"
                  @click="handleDetailDelete"
                  :disabled="isDeleting"
                >
                  <Spinner v-if="isDeleting" size="sm" class="mr-2" />
                  <Trash2 v-else class="mr-2 h-3.5 w-3.5" />
                  {{ $t('common.delete') }}
                </Button>
              </div>
            </CardHeader>

            <CardContent>
              <!-- Loading -->
              <div v-if="detailLoading" class="flex items-center justify-center py-8" role="status" aria-label="Loading record">
                <Spinner />
              </div>

              <!-- Error -->
              <div v-else-if="detailError" class="text-center py-8">
                <p class="text-sm text-destructive">{{ detailError }}</p>
              </div>

              <!-- Detail fields -->
              <dl v-else-if="detailData" class="space-y-3">
                <div v-for="field in fields" :key="field.name" class="space-y-0.5">
                  <dt class="text-xs font-medium text-muted-foreground">
                    {{ field.displayName || field.name }}
                  </dt>
                  <dd>
                    <Badge
                      v-if="field.type === 'Boolean'"
                      :variant="detailData[field.name] ? 'default' : 'secondary'"
                    >
                      {{ getFormattedValue(field) }}
                    </Badge>
                    <RouterLink
                      v-else-if="isAssociationField(field) && getAssociationLink(field)"
                      :to="getAssociationLink(field)!"
                      class="text-sm text-primary hover:underline"
                    >
                      {{ getFormattedValue(field) }}
                    </RouterLink>
                    <span v-else class="text-sm break-all">
                      {{ getFormattedValue(field) }}
                    </span>
                  </dd>
                </div>
              </dl>
            </CardContent>
          </Card>
        </div>
      </template>
    </FlexibleColumnLayout>
    </div>

    <!-- Plugin custom views -->
    <div v-if="showPluginView" class="mt-4">
      <PluginSlot
        slot-type="custom-views"
        :entity-type="entity"
        :context="{ module, data }"
      />
    </div>

    <ImportDialog
      :open="isImportDialogOpen"
      :entityType="entity"
      :module="module"
      :fields="fields"
      @imported="handleImported"
      @close="isImportDialogOpen = false"
      @update:open="isImportDialogOpen = $event"
    />

    <ModuleExportDialog
      :open="isExportDialogOpen"
      :module="module"
      @close="isExportDialogOpen = false"
      @update:open="isExportDialogOpen = $event"
    />

    <ConfirmDialog
      :open="confirmDialog.isOpen.value"
      :title="confirmDialog.title.value"
      :description="confirmDialog.description.value"
      :confirm-label="confirmDialog.confirmLabel.value"
      :cancel-label="confirmDialog.cancelLabel.value"
      :variant="confirmDialog.variant.value"
      @confirm="confirmDialog.handleConfirm"
      @cancel="confirmDialog.handleCancel"
      @update:open="confirmDialog.isOpen.value = $event"
    />
  </DefaultLayout>
</template>
