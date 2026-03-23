<script setup lang="ts">
import { ref, watch } from 'vue'
import {
  DialogRoot,
  DialogPortal,
  DialogOverlay,
  DialogContent,
  DialogTitle,
  DialogDescription,
} from 'radix-vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { cn } from '@/lib/utils'
import {
  ArrowUpDown,
  Filter,
  Group,
  X,
  Plus,
  Trash2,
} from 'lucide-vue-next'

interface Column {
  field: string
  label: string
  sortable?: boolean
  filterable?: boolean
  groupable?: boolean
}

interface SortSetting {
  field: string
  direction: 'asc' | 'desc'
}

interface FilterSetting {
  field: string
  operator: string
  value: string
}

interface ViewSettings {
  sort?: SortSetting
  filters: FilterSetting[]
  groupBy?: string
}

interface Props {
  open: boolean
  columns: Column[]
  currentSort?: SortSetting
  currentFilters?: FilterSetting[]
  currentGroupBy?: string
}

const props = withDefaults(defineProps<Props>(), {
  currentSort: undefined,
  currentFilters: () => [],
  currentGroupBy: undefined,
})

const emit = defineEmits<{
  'update:open': [value: boolean]
  apply: [settings: ViewSettings]
}>()

type TabKey = 'sort' | 'filter' | 'group'
const activeTab = ref<TabKey>('sort')

// Draft state
const draftSort = ref<SortSetting | null>(null)
const draftFilters = ref<FilterSetting[]>([])
const draftGroupBy = ref<string | undefined>(undefined)

// Initialize draft from props when dialog opens
watch(
  () => props.open,
  (isOpen) => {
    if (isOpen) {
      draftSort.value = props.currentSort ? { ...props.currentSort } : null
      draftFilters.value = props.currentFilters
        ? props.currentFilters.map((f) => ({ ...f }))
        : []
      draftGroupBy.value = props.currentGroupBy
    }
  }
)

// Derived column lists
function sortableColumns(): Column[] {
  return props.columns.filter((c) => c.sortable !== false)
}

function filterableColumns(): Column[] {
  return props.columns.filter((c) => c.filterable !== false)
}

function groupableColumns(): Column[] {
  return props.columns.filter((c) => c.groupable !== false)
}

// Filter management
function addFilter(): void {
  const firstCol = filterableColumns()[0]
  if (!firstCol) return
  draftFilters.value.push({
    field: firstCol.field,
    operator: 'contains',
    value: '',
  })
}

function removeFilter(index: number): void {
  draftFilters.value.splice(index, 1)
}

// Actions
function handleApply(): void {
  const settings: ViewSettings = {
    sort: draftSort.value ?? undefined,
    filters: draftFilters.value.filter((f) => f.value.trim() !== ''),
    groupBy: draftGroupBy.value,
  }
  emit('apply', settings)
  emit('update:open', false)
}

function handleCancel(): void {
  emit('update:open', false)
}

function onOpenChange(value: boolean): void {
  if (!value) {
    emit('update:open', false)
  }
}

const tabs: { key: TabKey; label: string; icon: typeof ArrowUpDown }[] = [
  { key: 'sort', label: 'Sort', icon: ArrowUpDown },
  { key: 'filter', label: 'Filter', icon: Filter },
  { key: 'group', label: 'Group', icon: Group },
]

const operatorLabels: Record<string, string> = {
  eq: 'Equals',
  ne: 'Not Equals',
  gt: 'Greater Than',
  lt: 'Less Than',
  ge: 'Greater or Equal',
  le: 'Less or Equal',
  contains: 'Contains',
  startswith: 'Starts With',
  endswith: 'Ends With',
}
</script>

<template>
  <DialogRoot :open="open" @update:open="onOpenChange">
    <DialogPortal>
      <DialogOverlay
        class="fixed inset-0 z-50 bg-black/50 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0"
      />
      <DialogContent
        class="fixed left-1/2 top-1/2 z-50 w-full max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-lg border bg-background shadow-lg duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[state=closed]:slide-out-to-left-1/2 data-[state=closed]:slide-out-to-top-[48%] data-[state=open]:slide-in-from-left-1/2 data-[state=open]:slide-in-from-top-[48%] flex flex-col"
        style="max-height: 70vh"
      >
        <!-- Header -->
        <div class="flex items-center justify-between px-6 pt-6 pb-4 border-b shrink-0">
          <div>
            <DialogTitle class="text-lg font-semibold text-foreground">
              View Settings
            </DialogTitle>
            <DialogDescription class="text-sm text-muted-foreground mt-0.5">
              Configure sorting, filtering, and grouping
            </DialogDescription>
          </div>
          <button
            class="rounded-sm opacity-70 ring-offset-background transition-opacity hover:opacity-100 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
            @click="handleCancel"
          >
            <X class="h-4 w-4" />
            <span class="sr-only">Close</span>
          </button>
        </div>

        <!-- Tab Bar -->
        <div class="flex border-b px-6 shrink-0">
          <button
            v-for="tab in tabs"
            :key="tab.key"
            :class="cn(
              'flex items-center gap-2 px-4 py-2.5 text-sm font-medium border-b-2 transition-colors -mb-px',
              activeTab === tab.key
                ? 'border-primary text-primary'
                : 'border-transparent text-muted-foreground hover:text-foreground hover:border-border'
            )"
            @click="activeTab = tab.key"
          >
            <component :is="tab.icon" class="h-4 w-4" />
            {{ tab.label }}
          </button>
        </div>

        <!-- Tab Content -->
        <div class="flex-1 overflow-y-auto px-6 py-4">
          <!-- Sort Tab -->
          <div v-if="activeTab === 'sort'" class="space-y-4">
            <p class="text-sm text-muted-foreground">
              Select a column and direction to sort by.
            </p>

            <div v-if="sortableColumns().length === 0" class="text-sm text-muted-foreground text-center py-6">
              No sortable columns available.
            </div>

            <template v-else>
              <div class="space-y-3">
                <div
                  v-for="col in sortableColumns()"
                  :key="col.field"
                  :class="cn(
                    'flex items-center gap-3 rounded-md px-3 py-2.5 border cursor-pointer transition-colors',
                    draftSort?.field === col.field
                      ? 'border-primary bg-primary/5'
                      : 'hover:bg-muted/50'
                  )"
                  @click="draftSort = { field: col.field, direction: draftSort?.field === col.field ? draftSort.direction : 'asc' }"
                >
                  <input
                    type="radio"
                    :checked="draftSort?.field === col.field"
                    name="sort-column"
                    class="h-4 w-4 border-input text-primary focus:ring-primary"
                    @change="draftSort = { field: col.field, direction: draftSort?.direction ?? 'asc' }"
                  />
                  <span class="flex-1 text-sm">{{ col.label }}</span>
                  <div
                    v-if="draftSort?.field === col.field"
                    class="flex gap-1"
                  >
                    <button
                      :class="cn(
                        'px-2 py-1 text-xs rounded transition-colors',
                        draftSort.direction === 'asc'
                          ? 'bg-primary text-primary-foreground'
                          : 'bg-muted text-muted-foreground hover:bg-muted/80'
                      )"
                      @click.stop="draftSort = { field: col.field, direction: 'asc' }"
                    >
                      Asc
                    </button>
                    <button
                      :class="cn(
                        'px-2 py-1 text-xs rounded transition-colors',
                        draftSort.direction === 'desc'
                          ? 'bg-primary text-primary-foreground'
                          : 'bg-muted text-muted-foreground hover:bg-muted/80'
                      )"
                      @click.stop="draftSort = { field: col.field, direction: 'desc' }"
                    >
                      Desc
                    </button>
                  </div>
                </div>
              </div>

              <Button
                v-if="draftSort"
                variant="ghost"
                size="sm"
                class="text-muted-foreground"
                @click="draftSort = null"
              >
                Clear Sort
              </Button>
            </template>
          </div>

          <!-- Filter Tab -->
          <div v-if="activeTab === 'filter'" class="space-y-3">
            <Button variant="outline" size="sm" class="gap-1.5" @click="addFilter">
              <Plus class="h-3.5 w-3.5" />
              Add Filter
            </Button>

            <div v-if="draftFilters.length === 0" class="text-sm text-muted-foreground text-center py-8">
              No filter criteria defined. Click "Add Filter" to add one.
            </div>

            <div
              v-for="(item, index) in draftFilters"
              :key="index"
              class="flex items-center gap-2 rounded-md border px-3 py-2.5"
            >
              <div class="flex-1 min-w-0">
                <Select
                  :model-value="item.field"
                  class="w-full"
                  @update:model-value="(val: string | number) => { item.field = String(val) }"
                >
                  <option
                    v-for="col in filterableColumns()"
                    :key="col.field"
                    :value="col.field"
                  >
                    {{ col.label }}
                  </option>
                </Select>
              </div>
              <div class="w-32 shrink-0">
                <Select
                  :model-value="item.operator"
                  class="w-full"
                  @update:model-value="(val: string | number) => { item.operator = String(val) }"
                >
                  <option
                    v-for="(label, op) in operatorLabels"
                    :key="op"
                    :value="op"
                  >
                    {{ label }}
                  </option>
                </Select>
              </div>
              <div class="w-28 shrink-0">
                <Input
                  :model-value="item.value"
                  placeholder="Value..."
                  @update:model-value="(val: string | number) => { item.value = String(val) }"
                />
              </div>
              <Button
                variant="ghost"
                size="icon"
                class="h-8 w-8 shrink-0 text-muted-foreground hover:text-destructive"
                @click="removeFilter(index)"
              >
                <Trash2 class="h-4 w-4" />
              </Button>
            </div>
          </div>

          <!-- Group Tab -->
          <div v-if="activeTab === 'group'" class="space-y-4">
            <p class="text-sm text-muted-foreground">
              Select a column to group rows by.
            </p>

            <div v-if="groupableColumns().length === 0" class="text-sm text-muted-foreground text-center py-6">
              No groupable columns available.
            </div>

            <div v-else class="space-y-1">
              <!-- None option -->
              <div
                :class="cn(
                  'flex items-center gap-3 rounded-md px-3 py-2.5 cursor-pointer transition-colors',
                  !draftGroupBy ? 'bg-primary/5' : 'hover:bg-muted/50'
                )"
                @click="draftGroupBy = undefined"
              >
                <input
                  type="radio"
                  :checked="!draftGroupBy"
                  name="group-column"
                  class="h-4 w-4 border-input text-primary focus:ring-primary"
                  @change="draftGroupBy = undefined"
                />
                <span class="text-sm text-muted-foreground italic">None</span>
              </div>

              <div
                v-for="col in groupableColumns()"
                :key="col.field"
                :class="cn(
                  'flex items-center gap-3 rounded-md px-3 py-2.5 cursor-pointer transition-colors',
                  draftGroupBy === col.field ? 'bg-primary/5' : 'hover:bg-muted/50'
                )"
                @click="draftGroupBy = col.field"
              >
                <input
                  type="radio"
                  :checked="draftGroupBy === col.field"
                  name="group-column"
                  class="h-4 w-4 border-input text-primary focus:ring-primary"
                  @change="draftGroupBy = col.field"
                />
                <span class="text-sm">{{ col.label }}</span>
              </div>
            </div>
          </div>
        </div>

        <!-- Footer -->
        <div class="flex items-center justify-end gap-2 px-6 py-4 border-t shrink-0">
          <Button variant="outline" @click="handleCancel">
            Cancel
          </Button>
          <Button @click="handleApply">
            OK
          </Button>
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
