<script setup lang="ts">
import { computed, watch } from 'vue'
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
import { Checkbox } from '@/components/ui/checkbox'
import { Select } from '@/components/ui/select'
import { cn } from '@/lib/utils'
import {
  ArrowUp,
  ArrowDown,
  X,
  Plus,
  Columns3,
  ArrowUpDown,
  Filter,
  Group,
  Search,
} from 'lucide-vue-next'
import { useP13nDialog } from '@/composables/useP13nDialog'
import type { P13nState } from '@/composables/useP13nDialog'
import { ref } from 'vue'

interface Props {
  availableColumns: { key: string; label: string }[]
  modelValue?: P13nState
  persistKey?: string
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:modelValue': [value: P13nState]
  apply: [value: P13nState]
}>()

const {
  state,
  draftState,
  isOpen,
  activeTab,
  open,
  close,
  apply: applyInternal,
  reset,
  moveColumn,
  toggleColumnVisibility,
  selectAllColumns,
  deselectAllColumns,
  addSortItem,
  removeSortItem,
  addFilterItem,
  removeFilterItem,
  addGroupItem,
  removeGroupItem,
} = useP13nDialog({
  availableColumns: props.availableColumns,
  initialState: props.modelValue,
  persistKey: props.persistKey,
})

// Sync external modelValue into state
watch(
  () => props.modelValue,
  (newVal) => {
    if (newVal) {
      state.value = JSON.parse(JSON.stringify(newVal))
    }
  },
  { deep: true }
)

// Emit on state change
watch(state, (newState) => {
  emit('update:modelValue', JSON.parse(JSON.stringify(newState)))
}, { deep: true })

function handleApply() {
  applyInternal()
  emit('apply', JSON.parse(JSON.stringify(state.value)))
}

function handleClose() {
  close()
}

function handleReset() {
  reset()
}

function onOpenChange(value: boolean) {
  if (!value) {
    close()
  }
}

// Column search
const columnSearch = ref('')

const filteredColumns = computed(() => {
  const query = columnSearch.value.toLowerCase().trim()
  if (!query) return draftState.value.columns
  return draftState.value.columns.filter(col =>
    col.label.toLowerCase().includes(query)
  )
})

// Helper to update sort/filter/group column selection
function onSortColumnChange(index: number, key: string) {
  const col = props.availableColumns.find(c => c.key === key)
  if (col) {
    draftState.value.sortItems[index].key = col.key
    draftState.value.sortItems[index].label = col.label
  }
}

function onFilterColumnChange(index: number, key: string) {
  const col = props.availableColumns.find(c => c.key === key)
  if (col) {
    draftState.value.filterItems[index].key = col.key
    draftState.value.filterItems[index].label = col.label
  }
}

function onGroupColumnChange(index: number, key: string) {
  const col = props.availableColumns.find(c => c.key === key)
  if (col) {
    draftState.value.groupItems[index].key = col.key
    draftState.value.groupItems[index].label = col.label
  }
}

const tabs = [
  { key: 'columns' as const, label: 'Columns', icon: Columns3 },
  { key: 'sort' as const, label: 'Sort', icon: ArrowUpDown },
  { key: 'filter' as const, label: 'Filter', icon: Filter },
  { key: 'group' as const, label: 'Group', icon: Group },
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

// Expose open method for parent usage
defineExpose({ open })
</script>

<template>
  <DialogRoot :open="isOpen" @update:open="onOpenChange">
    <DialogPortal>
      <DialogOverlay
        class="fixed inset-0 z-50 bg-black/50 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0"
      />
      <DialogContent
        class="fixed left-1/2 top-1/2 z-50 w-full max-w-2xl -translate-x-1/2 -translate-y-1/2 rounded-lg border bg-background shadow-lg duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[state=closed]:slide-out-to-left-1/2 data-[state=closed]:slide-out-to-top-[48%] data-[state=open]:slide-in-from-left-1/2 data-[state=open]:slide-in-from-top-[48%] flex flex-col"
        style="height: 70vh"
      >
        <!-- Header -->
        <div class="flex items-center justify-between px-6 pt-6 pb-4 border-b shrink-0">
          <div>
            <DialogTitle class="text-lg font-semibold text-foreground">
              Personalization
            </DialogTitle>
            <DialogDescription class="text-sm text-muted-foreground mt-0.5">
              Configure columns, sorting, filtering, and grouping
            </DialogDescription>
          </div>
          <button
            class="rounded-sm opacity-70 ring-offset-background transition-opacity hover:opacity-100 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
            @click="handleClose"
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
          <!-- Columns Tab -->
          <div v-if="activeTab === 'columns'" class="space-y-3">
            <!-- Search + Select All/Deselect All -->
            <div class="flex items-center gap-2">
              <div class="relative flex-1">
                <Search class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
                <Input
                  v-model="columnSearch"
                  placeholder="Search columns..."
                  class="pl-9"
                />
              </div>
              <Button variant="outline" size="sm" @click="selectAllColumns">
                Select All
              </Button>
              <Button variant="outline" size="sm" @click="deselectAllColumns">
                Deselect All
              </Button>
            </div>

            <!-- Column List -->
            <div class="space-y-1">
              <div
                v-for="(col, index) in filteredColumns"
                :key="col.key"
                :class="cn(
                  'flex items-center gap-3 rounded-md px-3 py-2 transition-colors',
                  'hover:bg-muted/50'
                )"
              >
                <Checkbox
                  :model-value="col.visible"
                  @update:model-value="toggleColumnVisibility(col.key)"
                />
                <span class="flex-1 text-sm" :class="{ 'text-muted-foreground': !col.visible }">
                  {{ col.label }}
                </span>
                <div class="flex items-center gap-1">
                  <Button
                    variant="ghost"
                    size="icon"
                    class="h-7 w-7"
                    :disabled="index === 0"
                    @click="moveColumn(draftState.columns.indexOf(col), 'up')"
                  >
                    <ArrowUp class="h-3.5 w-3.5" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    class="h-7 w-7"
                    :disabled="index === filteredColumns.length - 1"
                    @click="moveColumn(draftState.columns.indexOf(col), 'down')"
                  >
                    <ArrowDown class="h-3.5 w-3.5" />
                  </Button>
                </div>
              </div>
              <p
                v-if="filteredColumns.length === 0"
                class="text-sm text-muted-foreground text-center py-6"
              >
                No columns match your search.
              </p>
            </div>
          </div>

          <!-- Sort Tab -->
          <div v-if="activeTab === 'sort'" class="space-y-3">
            <Button variant="outline" size="sm" class="gap-1.5" @click="addSortItem">
              <Plus class="h-3.5 w-3.5" />
              Add Sort
            </Button>

            <div v-if="draftState.sortItems.length === 0" class="text-sm text-muted-foreground text-center py-8">
              No sort criteria defined. Click "Add Sort" to add one.
            </div>

            <div
              v-for="(item, index) in draftState.sortItems"
              :key="index"
              class="flex items-center gap-3 rounded-md border px-3 py-2.5"
            >
              <div class="flex-1">
                <Select
                  :model-value="item.key"
                  class="w-full"
                  @update:model-value="(val: string | number) => onSortColumnChange(index, String(val))"
                >
                  <option
                    v-for="col in availableColumns"
                    :key="col.key"
                    :value="col.key"
                  >
                    {{ col.label }}
                  </option>
                </Select>
              </div>
              <div class="w-40">
                <Select
                  :model-value="item.direction"
                  class="w-full"
                  @update:model-value="(val: string | number) => { item.direction = val as 'asc' | 'desc' }"
                >
                  <option value="asc">Ascending</option>
                  <option value="desc">Descending</option>
                </Select>
              </div>
              <Button
                variant="ghost"
                size="icon"
                class="h-8 w-8 shrink-0 text-muted-foreground hover:text-destructive"
                @click="removeSortItem(index)"
              >
                <X class="h-4 w-4" />
              </Button>
            </div>
          </div>

          <!-- Filter Tab -->
          <div v-if="activeTab === 'filter'" class="space-y-3">
            <Button variant="outline" size="sm" class="gap-1.5" @click="addFilterItem">
              <Plus class="h-3.5 w-3.5" />
              Add Filter
            </Button>

            <div v-if="draftState.filterItems.length === 0" class="text-sm text-muted-foreground text-center py-8">
              No filter criteria defined. Click "Add Filter" to add one.
            </div>

            <div
              v-for="(item, index) in draftState.filterItems"
              :key="index"
              class="flex items-center gap-2 rounded-md border px-3 py-2.5"
            >
              <div class="flex-1 min-w-0">
                <Select
                  :model-value="item.key"
                  class="w-full"
                  @update:model-value="(val: string | number) => onFilterColumnChange(index, String(val))"
                >
                  <option
                    v-for="col in availableColumns"
                    :key="col.key"
                    :value="col.key"
                  >
                    {{ col.label }}
                  </option>
                </Select>
              </div>
              <div class="w-36 shrink-0">
                <Select
                  :model-value="item.operator"
                  class="w-full"
                  @update:model-value="(val: string | number) => { item.operator = String(val) as typeof item.operator }"
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
              <div class="w-32 shrink-0">
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
                @click="removeFilterItem(index)"
              >
                <X class="h-4 w-4" />
              </Button>
            </div>
          </div>

          <!-- Group Tab -->
          <div v-if="activeTab === 'group'" class="space-y-3">
            <Button variant="outline" size="sm" class="gap-1.5" @click="addGroupItem">
              <Plus class="h-3.5 w-3.5" />
              Add Group
            </Button>

            <div v-if="draftState.groupItems.length === 0" class="text-sm text-muted-foreground text-center py-8">
              No grouping defined. Click "Add Group" to add one.
            </div>

            <div
              v-for="(item, index) in draftState.groupItems"
              :key="index"
              class="flex items-center gap-3 rounded-md border px-3 py-2.5"
            >
              <div class="flex-1">
                <Select
                  :model-value="item.key"
                  class="w-full"
                  @update:model-value="(val: string | number) => onGroupColumnChange(index, String(val))"
                >
                  <option
                    v-for="col in availableColumns"
                    :key="col.key"
                    :value="col.key"
                  >
                    {{ col.label }}
                  </option>
                </Select>
              </div>
              <label class="flex items-center gap-2 text-sm shrink-0 cursor-pointer">
                <Checkbox
                  :model-value="item.showSubtotals"
                  @update:model-value="item.showSubtotals = !item.showSubtotals"
                />
                Show Subtotals
              </label>
              <Button
                variant="ghost"
                size="icon"
                class="h-8 w-8 shrink-0 text-muted-foreground hover:text-destructive"
                @click="removeGroupItem(index)"
              >
                <X class="h-4 w-4" />
              </Button>
            </div>
          </div>
        </div>

        <!-- Footer -->
        <div class="flex items-center justify-between px-6 py-4 border-t shrink-0">
          <Button variant="outline" @click="handleReset">
            Reset
          </Button>
          <div class="flex gap-2">
            <Button variant="outline" @click="handleClose">
              Cancel
            </Button>
            <Button @click="handleApply">
              Apply
            </Button>
          </div>
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
