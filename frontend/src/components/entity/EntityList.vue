<script setup lang="ts">
import { ref, watch } from 'vue'
import type { FieldMetadata, AssociationMetadata } from '@/types/metadata'
import type { SortOption, FilterCondition } from '@/types/odata'
import type { ColumnConfig } from '@/composables/useColumnConfig'
import type { CellEditState } from '@/composables/useInlineEdit'
import type { SavedView } from '@/composables/useSavedViews'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Plus, Search, RefreshCw, AlertCircle, Download, Filter } from 'lucide-vue-next'
import EntityTable from './EntityTable.vue'
import EntityPagination from './EntityPagination.vue'
import ColumnPicker from './ColumnPicker.vue'
import BulkActionBar from './BulkActionBar.vue'
import FilterChips from './FilterChips.vue'
import AdvancedFilterPanel from './AdvancedFilterPanel.vue'
import SavedViewSwitcher from './SavedViewSwitcher.vue'

interface Props {
  title: string
  data: Record<string, unknown>[]
  fields: FieldMetadata[]
  keyField: string
  totalCount: number
  currentPage: number
  pageSize: number
  totalPages: number
  isLoading?: boolean
  error?: string | null
  sortOptions?: SortOption[]
  filters?: FilterCondition[]
  search?: string
  selectedId?: string | null
  associations?: AssociationMetadata[]
  // Phase 2 props
  columns?: ColumnConfig[]
  selectionEnabled?: boolean
  selectedRowIds?: Set<string>
  isAllSelected?: boolean
  isIndeterminate?: boolean
  selectedCount?: number
  editingCell?: CellEditState | null
  isCellSaving?: boolean
  cellSaveError?: string | null
  // Phase 3 props
  activeFilters?: FilterCondition[]
  savedViews?: SavedView[]
  currentViewId?: string | null
  defaultViewId?: string | null
  isAdvancedFilterOpen?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  isLoading: false,
  sortOptions: () => [],
  filters: () => [],
  search: '',
  selectedId: null,
  associations: () => [],
  columns: undefined,
  selectionEnabled: false,
  selectedRowIds: () => new Set<string>(),
  isAllSelected: false,
  isIndeterminate: false,
  selectedCount: 0,
  editingCell: null,
  isCellSaving: false,
  cellSaveError: null,
  activeFilters: () => [],
  savedViews: () => [],
  currentViewId: null,
  defaultViewId: null,
  isAdvancedFilterOpen: false
})

const emit = defineEmits<{
  // Existing
  create: []
  view: [id: string]
  edit: [id: string]
  delete: [id: string]
  refresh: []
  sort: [field: string]
  search: [query: string]
  filter: [filters: FilterCondition[]]
  'update:currentPage': [page: number]
  'update:pageSize': [size: number]
  // Phase 2: Column config
  'toggle-column': [field: string]
  'show-all-columns': []
  'hide-all-columns': []
  'reset-columns': []
  'resize-column': [field: string, width: number]
  'reorder-column': [fromIndex: number, toIndex: number]
  // Phase 2: Row selection
  'toggle-row': [id: string]
  'toggle-all': []
  'delete-selected': []
  'export-selected': []
  'deselect-all': []
  // Phase 2: Inline editing
  'start-cell-edit': [rowId: string, field: string, value: unknown]
  'update-cell-value': [value: unknown]
  'commit-cell-edit': []
  'cancel-cell-edit': []
  // Phase 2: Export all
  'export-all': []
  // Phase 3: Filter events
  'apply-filter': [filter: FilterCondition]
  'apply-between-filter': [field: string, min: unknown, max: unknown, type: 'number' | 'date']
  'clear-filter': [field: string]
  'clear-all-filters': []
  'apply-advanced-filters': [filters: FilterCondition[]]
  'toggle-advanced-filter': []
  // Phase 3: Saved views
  'select-view': [id: string | null]
  'save-view': [name: string]
  'update-view': [id: string]
  'delete-view': [id: string]
  'rename-view': [id: string, name: string]
  'set-default-view': [id: string | null]
}>()

const searchQuery = ref(props.search ?? '')

// Sync search box when parent changes search externally (saved view, URL restore)
let syncingFromProp = false
watch(() => props.search, (val) => {
  const newVal = val ?? ''
  if (newVal !== searchQuery.value) {
    syncingFromProp = true
    searchQuery.value = newVal
  }
})

// Debounced search — only emit when user types (not when syncing from prop)
let searchTimeout: ReturnType<typeof setTimeout> | null = null

watch(searchQuery, (newQuery) => {
  if (syncingFromProp) {
    syncingFromProp = false
    return
  }
  if (searchTimeout) {
    clearTimeout(searchTimeout)
  }
  searchTimeout = setTimeout(() => {
    emit('search', newQuery)
  }, 300)
})

function handleSort(field: string) {
  emit('sort', field)
}

function handleRefresh() {
  emit('refresh')
}
</script>

<template>
  <Card>
    <CardHeader>
      <div class="flex flex-col gap-3">
        <div class="flex items-center justify-between">
          <CardTitle>{{ title }}</CardTitle>
          <!-- Primary actions: always visible -->
          <div class="flex items-center gap-2">
            <!-- Refresh -->
            <Button variant="outline" size="icon" @click="handleRefresh" :disabled="isLoading">
              <RefreshCw :class="['h-4 w-4', isLoading && 'animate-spin']" />
            </Button>

            <!-- Create -->
            <Button @click="emit('create')">
              <Plus class="mr-1 sm:mr-2 h-4 w-4" />
              <span class="hidden sm:inline">Create</span>
            </Button>
          </div>
        </div>

        <!-- Search + secondary toolbar -->
        <div class="flex flex-col sm:flex-row items-stretch sm:items-center gap-2">
          <!-- Search -->
          <div class="relative flex-1 sm:flex-initial sm:min-w-0">
            <Search class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
            <Input
              v-model="searchQuery"
              type="search"
              placeholder="Search..."
              class="pl-9 w-full sm:w-64"
            />
          </div>

          <!-- Secondary actions row -->
          <div class="flex items-center gap-2 flex-wrap">
            <!-- Saved View Switcher -->
            <SavedViewSwitcher
              v-if="savedViews.length > 0 || currentViewId != null"
              :views="savedViews"
              :currentViewId="currentViewId"
              :defaultViewId="defaultViewId"
              @select="emit('select-view', $event)"
              @save="emit('save-view', $event)"
              @update="emit('update-view', $event)"
              @delete="emit('delete-view', $event)"
              @rename="(id: string, name: string) => emit('rename-view', id, name)"
              @set-default="emit('set-default-view', $event)"
            />

            <!-- Advanced Filter toggle -->
            <Button
              variant="outline"
              size="sm"
              class="gap-1.5"
              @click="emit('toggle-advanced-filter')"
            >
              <Filter class="h-4 w-4" />
              <span class="hidden sm:inline">Filter</span>
            </Button>

            <!-- Column picker - hidden on small screens -->
            <div class="hidden md:block">
              <ColumnPicker
                v-if="columns"
                :columns="columns"
                :totalFields="fields.length"
                @toggle="emit('toggle-column', $event)"
                @show-all="emit('show-all-columns')"
                @hide-all="emit('hide-all-columns')"
                @reset="emit('reset-columns')"
              />
            </div>

            <!-- Export All - hidden on small screens -->
            <Button variant="outline" size="sm" class="hidden md:inline-flex" @click="emit('export-all')">
              <Download class="mr-1.5 h-4 w-4" />
              Export
            </Button>
          </div>
        </div>
      </div>
    </CardHeader>

    <CardContent>
      <!-- Error alert -->
      <Alert v-if="error" variant="destructive" class="mb-4">
        <AlertCircle class="h-4 w-4" />
        <AlertDescription>{{ error }}</AlertDescription>
      </Alert>

      <!-- Filter chips -->
      <FilterChips
        v-if="activeFilters.length > 0"
        :filters="activeFilters"
        :fields="fields"
        class="mb-3"
        @remove="emit('clear-filter', $event)"
        @clear-all="emit('clear-all-filters')"
      />

      <!-- Bulk action bar -->
      <BulkActionBar
        :selectedCount="selectedCount"
        class="mb-3"
        @delete-selected="emit('delete-selected')"
        @export-selected="emit('export-selected')"
        @deselect-all="emit('deselect-all')"
      />

      <!-- Table -->
      <EntityTable
        :data="data"
        :fields="fields"
        :keyField="keyField"
        :isLoading="isLoading"
        :sortOptions="sortOptions"
        :selectedId="selectedId"
        :associations="associations"
        :columns="columns"
        :selectionEnabled="selectionEnabled"
        :selectedRowIds="selectedRowIds"
        :isAllSelected="isAllSelected"
        :isIndeterminate="isIndeterminate"
        :editingCell="editingCell"
        :isCellSaving="isCellSaving"
        :cellSaveError="cellSaveError"
        :activeFilters="activeFilters"
        @sort="handleSort"
        @view="emit('view', $event)"
        @edit="emit('edit', $event)"
        @delete="emit('delete', $event)"
        @toggle-row="emit('toggle-row', $event)"
        @toggle-all="emit('toggle-all')"
        @start-cell-edit="(rowId: string, field: string, value: unknown) => emit('start-cell-edit', rowId, field, value)"
        @update-cell-value="emit('update-cell-value', $event)"
        @commit-cell-edit="emit('commit-cell-edit')"
        @cancel-cell-edit="emit('cancel-cell-edit')"
        @resize-column="(field: string, width: number) => emit('resize-column', field, width)"
        @reorder-column="(from: number, to: number) => emit('reorder-column', from, to)"
        @apply-filter="emit('apply-filter', $event)"
        @apply-between-filter="(field: string, min: unknown, max: unknown, type: 'number' | 'date') => emit('apply-between-filter', field, min, max, type)"
        @clear-filter="emit('clear-filter', $event)"
      />

      <!-- Pagination -->
      <EntityPagination
        :currentPage="currentPage"
        :totalPages="totalPages"
        :pageSize="pageSize"
        :totalCount="totalCount"
        @update:currentPage="emit('update:currentPage', $event)"
        @update:pageSize="emit('update:pageSize', $event)"
      />
    </CardContent>
  </Card>

  <!-- Advanced Filter Panel (side panel, teleported to body) -->
  <AdvancedFilterPanel
    :fields="fields"
    :filters="activeFilters"
    :isOpen="isAdvancedFilterOpen"
    @apply="emit('apply-advanced-filters', $event)"
    @close="emit('toggle-advanced-filter')"
  />
</template>
