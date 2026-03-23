<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import type { FieldMetadata } from '@/types/metadata'
import type { FilterCondition, FilterOperator } from '@/types/odata'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { Badge } from '@/components/ui/badge'
import { Label } from '@/components/ui/label'
import { X, Plus, Filter, Code, Eye, RotateCcw } from 'lucide-vue-next'
import FilterRow from './FilterRow.vue'
import {
  type FilterGroup,
  buildAdvancedFilter,
  conditionsToGroup,
  validateRawFilter
} from '@/utils/filterGroupBuilder'

interface Props {
  fields: FieldMetadata[]
  filters: FilterCondition[]
  isOpen: boolean
}

const props = defineProps<Props>()

const emit = defineEmits<{
  apply: [filters: FilterCondition[]]
  close: []
}>()

// --- State ---

// Visual mode: array of filter groups
const filterGroups = ref<FilterGroup[]>([])

// Raw mode: raw OData $filter text
const rawFilterText = ref('')
const rawFilterError = ref<string | undefined>(undefined)

// Mode toggle: visual vs raw
const isRawMode = ref(false)

// --- Initialize from props ---
watch(
  () => props.filters,
  (newFilters) => {
    if (newFilters.length > 0 && filterGroups.value.length === 0) {
      filterGroups.value = [conditionsToGroup(newFilters)]
    }
  },
  { immediate: true }
)

watch(
  () => props.isOpen,
  (open) => {
    if (open && filterGroups.value.length === 0 && props.filters.length === 0) {
      // Start with one empty group containing one empty condition
      filterGroups.value = [createEmptyGroup()]
    }
  }
)

// --- Computed ---

// Preview string generated from the visual builder
const filterPreview = computed(() => {
  if (isRawMode.value) {
    return rawFilterText.value.trim()
  }
  return buildAdvancedFilter(filterGroups.value)
})

// Total number of valid conditions across all groups
const activeConditionCount = computed(() => {
  return filterGroups.value.reduce((count, group) => {
    return (
      count +
      group.conditions.filter(
        (c) => c.field && c.operator && c.value !== undefined && c.value !== ''
      ).length
    )
  }, 0)
})

// --- Methods ---

function createEmptyGroup(): FilterGroup {
  return {
    logic: 'and',
    conditions: [{ field: '', operator: 'eq' as FilterOperator, value: '' }]
  }
}

function addGroup() {
  filterGroups.value.push(createEmptyGroup())
}

function removeGroup(groupIndex: number) {
  filterGroups.value.splice(groupIndex, 1)
  if (filterGroups.value.length === 0) {
    filterGroups.value.push(createEmptyGroup())
  }
}

function toggleGroupLogic(groupIndex: number) {
  const group = filterGroups.value[groupIndex]
  group.logic = group.logic === 'and' ? 'or' : 'and'
}

function addCondition(groupIndex: number) {
  filterGroups.value[groupIndex].conditions.push({
    field: '',
    operator: 'eq' as FilterOperator,
    value: ''
  })
}

function removeCondition(groupIndex: number, conditionIndex: number) {
  const group = filterGroups.value[groupIndex]
  group.conditions.splice(conditionIndex, 1)
  if (group.conditions.length === 0) {
    // Remove empty group, or re-add an empty condition
    if (filterGroups.value.length > 1) {
      filterGroups.value.splice(groupIndex, 1)
    } else {
      group.conditions.push({ field: '', operator: 'eq' as FilterOperator, value: '' })
    }
  }
}

function updateCondition(
  groupIndex: number,
  conditionIndex: number,
  value: { field: string; operator: FilterOperator; value: unknown }
) {
  filterGroups.value[groupIndex].conditions[conditionIndex] = value
}

function handleApply() {
  if (isRawMode.value) {
    const validation = validateRawFilter(rawFilterText.value)
    if (!validation.valid) {
      rawFilterError.value = validation.error
      return
    }
    rawFilterError.value = undefined

    // In raw mode, emit a single special condition that the parent
    // can use as a raw $filter string. We use a synthetic FilterCondition
    // with a special marker.
    const rawText = rawFilterText.value.trim()
    if (rawText) {
      // Emit as a single condition with the raw filter in the value
      // The parent should check for this pattern and use the raw string
      emit('apply', [
        { field: '__raw__', operator: 'eq' as FilterOperator, value: rawText }
      ])
    } else {
      emit('apply', [])
    }
  } else {
    // Collect all valid conditions from all groups
    // For simplicity, flatten groups into conditions
    // The parent gets the filter string via the preview
    const allConditions: FilterCondition[] = []
    for (const group of filterGroups.value) {
      for (const condition of group.conditions) {
        if (condition.field && condition.operator && condition.value !== undefined && condition.value !== '') {
          allConditions.push({ ...condition })
        }
      }
    }
    emit('apply', allConditions)
  }
}

function handleReset() {
  filterGroups.value = [createEmptyGroup()]
  rawFilterText.value = ''
  rawFilterError.value = undefined
}

function handleClose() {
  emit('close')
}

function switchToVisual() {
  isRawMode.value = false
  rawFilterError.value = undefined
}

function switchToRaw() {
  // Pre-populate raw text from the current visual builder state
  const preview = buildAdvancedFilter(filterGroups.value)
  if (preview) {
    rawFilterText.value = preview
  }
  isRawMode.value = true
}

function onRawInput(val: string) {
  rawFilterText.value = val
  // Clear error on edit
  if (rawFilterError.value) {
    const validation = validateRawFilter(val)
    if (validation.valid) {
      rawFilterError.value = undefined
    }
  }
}
</script>

<template>
  <!-- Backdrop overlay -->
  <Teleport to="body">
    <Transition name="fade">
      <div
        v-if="isOpen"
        class="fixed inset-0 bg-black/30 z-40"
        @click="handleClose"
      />
    </Transition>

    <!-- Side panel -->
    <Transition name="slide">
      <div
        v-if="isOpen"
        class="fixed inset-y-0 right-0 w-full sm:w-96 bg-background border-l shadow-xl z-50 flex flex-col"
      >
        <!-- Header -->
        <div class="flex items-center justify-between px-4 py-3 border-b">
          <div class="flex items-center gap-2">
            <Filter class="h-5 w-5 text-muted-foreground" />
            <h2 class="text-lg font-semibold">Advanced Filter</h2>
            <Badge v-if="activeConditionCount > 0" variant="secondary" class="ml-1">
              {{ activeConditionCount }}
            </Badge>
          </div>
          <Button variant="ghost" size="icon" class="h-8 w-8" @click="handleClose">
            <X class="h-4 w-4" />
          </Button>
        </div>

        <!-- Mode toggle -->
        <div class="flex items-center gap-1 px-4 pt-3">
          <Button
            :variant="!isRawMode ? 'secondary' : 'ghost'"
            size="sm"
            class="gap-1.5 text-xs"
            @click="switchToVisual"
          >
            <Eye class="h-3.5 w-3.5" />
            Visual
          </Button>
          <Button
            :variant="isRawMode ? 'secondary' : 'ghost'"
            size="sm"
            class="gap-1.5 text-xs"
            @click="switchToRaw"
          >
            <Code class="h-3.5 w-3.5" />
            Raw OData
          </Button>
        </div>

        <!-- Scrollable content area -->
        <div class="flex-1 overflow-y-auto px-4 py-3 space-y-4">
          <!-- Raw mode -->
          <template v-if="isRawMode">
            <div class="space-y-2">
              <Label>$filter expression</Label>
              <Textarea
                :model-value="rawFilterText"
                placeholder="e.g. Name eq 'John' and Age gt 25"
                :rows="6"
                class="font-mono text-sm"
                @update:model-value="onRawInput"
              />
              <p v-if="rawFilterError" class="text-sm text-destructive">
                {{ rawFilterError }}
              </p>
              <p class="text-xs text-muted-foreground">
                Enter a raw OData $filter expression. Supports standard OData filter syntax
                including logical operators (and, or, not), comparison operators (eq, ne, gt, ge, lt, le),
                and string functions (contains, startswith, endswith).
              </p>
            </div>
          </template>

          <!-- Visual mode -->
          <template v-else>
            <div
              v-for="(group, groupIndex) in filterGroups"
              :key="groupIndex"
              class="rounded-lg border bg-muted/30 p-3 space-y-3"
            >
              <!-- Group header -->
              <div class="flex items-center justify-between">
                <div class="flex items-center gap-2">
                  <span class="text-xs font-medium text-muted-foreground uppercase tracking-wide">
                    Group {{ groupIndex + 1 }}
                  </span>
                  <button
                    class="inline-flex items-center rounded-md px-2 py-0.5 text-xs font-semibold transition-colors cursor-pointer"
                    :class="
                      group.logic === 'and'
                        ? 'bg-blue-100 text-blue-700 hover:bg-blue-200 dark:bg-blue-900/30 dark:text-blue-400'
                        : 'bg-amber-100 text-amber-700 hover:bg-amber-200 dark:bg-amber-900/30 dark:text-amber-400'
                    "
                    @click="toggleGroupLogic(groupIndex)"
                  >
                    {{ group.logic.toUpperCase() }}
                  </button>
                </div>
                <Button
                  v-if="filterGroups.length > 1"
                  variant="ghost"
                  size="icon"
                  class="h-6 w-6 text-muted-foreground hover:text-destructive"
                  @click="removeGroup(groupIndex)"
                >
                  <X class="h-3.5 w-3.5" />
                </Button>
              </div>

              <!-- Condition rows -->
              <div class="space-y-2">
                <FilterRow
                  v-for="(condition, condIndex) in group.conditions"
                  :key="condIndex"
                  :fields="fields"
                  :model-value="condition"
                  @update:model-value="updateCondition(groupIndex, condIndex, $event)"
                  @remove="removeCondition(groupIndex, condIndex)"
                />
              </div>

              <!-- Add condition button -->
              <Button
                variant="ghost"
                size="sm"
                class="gap-1.5 text-xs w-full justify-center border border-dashed border-muted-foreground/30 hover:border-muted-foreground/50"
                @click="addCondition(groupIndex)"
              >
                <Plus class="h-3.5 w-3.5" />
                Add condition
              </Button>
            </div>

            <!-- Add group button -->
            <Button
              variant="outline"
              size="sm"
              class="gap-1.5 text-xs w-full"
              @click="addGroup"
            >
              <Plus class="h-3.5 w-3.5" />
              Add filter group
            </Button>
          </template>

          <!-- Filter preview -->
          <div v-if="filterPreview" class="space-y-1.5">
            <Label class="text-xs text-muted-foreground">Filter preview</Label>
            <div
              class="rounded-md bg-muted px-3 py-2 font-mono text-xs text-foreground/80 break-all select-all"
            >
              $filter={{ filterPreview }}
            </div>
          </div>
        </div>

        <!-- Footer actions -->
        <div class="border-t px-4 py-3 flex items-center gap-2">
          <Button
            variant="default"
            size="sm"
            class="flex-1 gap-1.5"
            @click="handleApply"
          >
            <Filter class="h-4 w-4" />
            Apply Filter
          </Button>
          <Button
            variant="outline"
            size="sm"
            class="gap-1.5"
            @click="handleReset"
          >
            <RotateCcw class="h-4 w-4" />
            Reset
          </Button>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
/* Backdrop fade transition */
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s ease;
}
.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}

/* Panel slide transition */
.slide-enter-active,
.slide-leave-active {
  transition: transform 0.3s ease;
}
.slide-enter-from,
.slide-leave-to {
  transform: translateX(100%);
}
</style>
