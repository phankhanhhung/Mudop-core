<script setup lang="ts">
import { ref, computed, watch, onUnmounted } from 'vue'
import type { EntityMetadata } from '@/types/metadata'
import type { FilterCondition } from '@/types/odata'
import type { SmartFilterField } from '@/odata/types'
import { SmartFilter } from '@/odata/SmartFilter'
import { valueListProvider } from '@/odata/ValueListProvider'
import ValueHelpDialog from '@/components/smart/ValueHelpDialog.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Badge } from '@/components/ui/badge'
import { Search, X, SlidersHorizontal } from 'lucide-vue-next'

// ---------------------------------------------------------------------------
// Props & Emits
// ---------------------------------------------------------------------------

interface Props {
  module: string
  entitySet: string
  metadata: EntityMetadata
  activeFilters?: FilterCondition[]
  searchQuery?: string
  showSearch?: boolean
  maxVisibleFilters?: number
  compact?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  activeFilters: () => [],
  searchQuery: '',
  showSearch: true,
  maxVisibleFilters: 5,
  compact: false,
})

const emit = defineEmits<{
  'filter-change': [filters: FilterCondition[]]
  'search-change': [query: string]
  'clear-all': []
}>()

// ---------------------------------------------------------------------------
// Smart Filter instance
// ---------------------------------------------------------------------------

const smartFilter = new SmartFilter()

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

const localSearch = ref(props.searchQuery)
const localFilters = ref<Map<string, FilterCondition>>(new Map())
const showMoreFilters = ref(false)
const valueHelpOpen = ref(false)
const valueHelpTarget = ref('')
const valueHelpFieldName = ref('')
const associationLabels = ref<Map<string, string>>(new Map())

let debounceTimer: ReturnType<typeof setTimeout> | null = null

// ---------------------------------------------------------------------------
// Filter fields from metadata
// ---------------------------------------------------------------------------

const allFilterFields = computed<SmartFilterField[]>(() => {
  return smartFilter.generateFields(props.metadata)
})

const visibleFilterFields = computed<SmartFilterField[]>(() => {
  return allFilterFields.value.slice(0, props.maxVisibleFilters)
})

const hiddenFilterFields = computed<SmartFilterField[]>(() => {
  return allFilterFields.value.slice(props.maxVisibleFilters)
})

// ---------------------------------------------------------------------------
// Active filter chips
// ---------------------------------------------------------------------------

interface FilterChip {
  field: string
  label: string
  displayValue: string
}

const activeFilterChips = computed<FilterChip[]>(() => {
  const chips: FilterChip[] = []
  for (const [fieldName, condition] of localFilters.value) {
    const fieldDef = allFilterFields.value.find((f) => f.name === fieldName)
    const label = fieldDef?.label ?? fieldName

    let displayValue: string
    if (fieldDef?.widgetType === 'boolean') {
      displayValue = condition.value === true || condition.value === 'true' ? 'Yes' : 'No'
    } else if (fieldDef?.widgetType === 'enum' && fieldDef.enumValues) {
      const ev = fieldDef.enumValues.find(
        (e) => String(e.value) === String(condition.value)
      )
      displayValue = ev?.displayName ?? ev?.name ?? String(condition.value)
    } else if (fieldDef?.widgetType === 'association') {
      displayValue = associationLabels.value.get(fieldName) ?? String(condition.value)
    } else {
      displayValue = String(condition.value ?? '')
    }

    chips.push({ field: fieldName, label, displayValue })
  }
  return chips
})

const hasActiveFilters = computed(() => {
  return localFilters.value.size > 0 || localSearch.value.trim() !== ''
})

// ---------------------------------------------------------------------------
// Filter value accessors
// ---------------------------------------------------------------------------

function getFilterValue(fieldName: string): string {
  const condition = localFilters.value.get(fieldName)
  if (!condition) return ''
  return String(condition.value ?? '')
}

function getAssociationLabel(fieldName: string): string {
  return associationLabels.value.get(fieldName) ?? ''
}

// ---------------------------------------------------------------------------
// Filter manipulation
// ---------------------------------------------------------------------------

function setFilter(field: SmartFilterField, value: unknown): void {
  if (value === null || value === undefined || value === '') {
    localFilters.value.delete(field.name)
  } else {
    localFilters.value.set(field.name, {
      field: field.name,
      operator: field.defaultOperator,
      value,
    })
  }
  // Trigger reactivity
  localFilters.value = new Map(localFilters.value)
  emitFilterChange()
}

function clearFilter(fieldName: string): void {
  localFilters.value.delete(fieldName)
  associationLabels.value.delete(fieldName)
  // Trigger reactivity
  localFilters.value = new Map(localFilters.value)
  associationLabels.value = new Map(associationLabels.value)
  emitFilterChange()
}

function handleClearAll(): void {
  localFilters.value = new Map()
  associationLabels.value = new Map()
  localSearch.value = ''
  emit('clear-all')
  emit('filter-change', [])
  emit('search-change', '')
}

function emitFilterChange(): void {
  const conditions = Array.from(localFilters.value.values())
  emit('filter-change', conditions)
}

// ---------------------------------------------------------------------------
// Search with debounce
// ---------------------------------------------------------------------------

function debouncedSearch(): void {
  if (debounceTimer) clearTimeout(debounceTimer)
  debounceTimer = setTimeout(() => {
    emit('search-change', localSearch.value.trim())
  }, 300)
}

function clearSearch(): void {
  localSearch.value = ''
  emit('search-change', '')
}

// ---------------------------------------------------------------------------
// Value Help Dialog (for association filters)
// ---------------------------------------------------------------------------

function openValueHelp(field: SmartFilterField): void {
  if (!field.associationTarget) return
  valueHelpTarget.value = field.associationTarget
  valueHelpFieldName.value = field.name
  valueHelpOpen.value = true
}

function handleValueHelpSelect(selection: { key: string; label: string }): void {
  const fieldName = valueHelpFieldName.value
  const fieldDef = allFilterFields.value.find((f) => f.name === fieldName)
  if (!fieldDef) return

  // Store the label for display
  associationLabels.value.set(fieldName, selection.label)
  associationLabels.value = new Map(associationLabels.value)

  // Set the filter using the key
  setFilter(fieldDef, selection.key)
}

/**
 * For association filters, resolve display labels from the cached values.
 */
async function resolveAssociationLabelsFromFilters(): Promise<void> {
  for (const [fieldName, condition] of localFilters.value) {
    const fieldDef = allFilterFields.value.find((f) => f.name === fieldName)
    if (fieldDef?.widgetType !== 'association' || !fieldDef.associationTarget) continue

    const key = String(condition.value)
    if (!key) continue

    try {
      const entry = await valueListProvider.getValueByKey(
        { module: props.module, entitySet: fieldDef.associationTarget },
        key
      )
      if (entry) {
        associationLabels.value.set(fieldName, entry.label)
      }
    } catch {
      // Ignore — label will show the raw key
    }
  }
  associationLabels.value = new Map(associationLabels.value)
}

// ---------------------------------------------------------------------------
// Initialization
// ---------------------------------------------------------------------------

// Sync incoming activeFilters prop to local state
watch(
  () => props.activeFilters,
  (incoming) => {
    const newMap = new Map<string, FilterCondition>()
    for (const condition of incoming) {
      newMap.set(condition.field, condition)
    }
    localFilters.value = newMap
    resolveAssociationLabelsFromFilters()
  },
  { immediate: true }
)

watch(
  () => props.searchQuery,
  (val) => {
    localSearch.value = val
  }
)

// ---------------------------------------------------------------------------
// Cleanup
// ---------------------------------------------------------------------------

onUnmounted(() => {
  if (debounceTimer) clearTimeout(debounceTimer)
})

// ---------------------------------------------------------------------------
// Render helper for filter fields (shared between visible + hidden sections)
// ---------------------------------------------------------------------------

function handleTextInput(field: SmartFilterField, value: string | number): void {
  setFilter(field, String(value) || null)
}

function handleNumberInput(field: SmartFilterField, value: string | number): void {
  const str = String(value)
  setFilter(field, str === '' ? null : Number(str))
}

function handleBooleanChange(field: SmartFilterField, event: Event): void {
  const target = event.target as HTMLSelectElement
  setFilter(field, target.value === '' ? null : target.value === 'true')
}

function handleDateInput(field: SmartFilterField, value: string | number): void {
  setFilter(field, String(value) || null)
}

function handleEnumChange(field: SmartFilterField, event: Event): void {
  const target = event.target as HTMLSelectElement
  setFilter(field, target.value || null)
}

</script>

<template>
  <div class="space-y-3">
    <!-- Top row: Search + Filter fields + Actions -->
    <div class="flex flex-wrap items-end gap-3">
      <!-- Search -->
      <div v-if="showSearch" class="flex-1 min-w-[200px] max-w-sm">
        <Label class="text-xs text-muted-foreground mb-1">Search</Label>
        <div class="relative">
          <Search class="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input
            :modelValue="localSearch"
            placeholder="Search..."
            class="pl-9"
            @update:modelValue="(v: string | number) => { localSearch = String(v); debouncedSearch() }"
          />
          <button
            v-if="localSearch"
            class="absolute right-2.5 top-2.5 text-muted-foreground hover:text-foreground"
            @click="clearSearch"
          >
            <X class="h-4 w-4" />
          </button>
        </div>
      </div>

      <!-- Visible filter fields -->
      <div v-for="field in visibleFilterFields" :key="field.name" class="min-w-[150px]">
        <Label class="text-xs text-muted-foreground mb-1">{{ field.label }}</Label>

        <!-- Text filter -->
        <Input
          v-if="field.widgetType === 'text'"
          :modelValue="getFilterValue(field.name)"
          placeholder="Filter..."
          class="h-9"
          @update:modelValue="(v: string | number) => handleTextInput(field, v)"
        />

        <!-- Number / Decimal filter -->
        <Input
          v-else-if="field.widgetType === 'number' || field.widgetType === 'decimal'"
          type="number"
          :modelValue="getFilterValue(field.name)"
          class="h-9"
          @update:modelValue="(v: string | number) => handleNumberInput(field, v)"
        />

        <!-- Boolean filter -->
        <select
          v-else-if="field.widgetType === 'boolean'"
          :value="getFilterValue(field.name)"
          class="h-9 w-full rounded-md border border-input bg-background px-2 text-sm"
          @change="handleBooleanChange(field, $event)"
        >
          <option value="">All</option>
          <option value="true">Yes</option>
          <option value="false">No</option>
        </select>

        <!-- Date filter -->
        <Input
          v-else-if="field.widgetType === 'date' || field.widgetType === 'datetime'"
          type="date"
          :modelValue="getFilterValue(field.name)"
          class="h-9"
          @update:modelValue="(v: string | number) => handleDateInput(field, v)"
        />

        <!-- Enum filter -->
        <select
          v-else-if="field.widgetType === 'enum'"
          :value="getFilterValue(field.name)"
          class="h-9 w-full rounded-md border border-input bg-background px-2 text-sm"
          @change="handleEnumChange(field, $event)"
        >
          <option value="">All</option>
          <option
            v-for="ev in field.enumValues"
            :key="String(ev.value)"
            :value="String(ev.value)"
          >
            {{ ev.displayName || ev.name }}
          </option>
        </select>

        <!-- Association filter (value help) -->
        <div v-else-if="field.widgetType === 'association'" class="flex gap-1">
          <Input
            :modelValue="getAssociationLabel(field.name)"
            :readonly="true"
            placeholder="Select..."
            class="h-9 flex-1 cursor-pointer"
            @click="openValueHelp(field)"
          />
          <Button
            v-if="getFilterValue(field.name)"
            size="icon"
            variant="ghost"
            class="h-9 w-9"
            @click="clearFilter(field.name)"
          >
            <X class="h-3.5 w-3.5" />
          </Button>
        </div>

        <!-- UUID / fallback text filter -->
        <Input
          v-else
          :modelValue="getFilterValue(field.name)"
          placeholder="Filter..."
          class="h-9"
          @update:modelValue="(v: string | number) => handleTextInput(field, v)"
        />
      </div>

      <!-- More filters button (if overflow) -->
      <Button
        v-if="hiddenFilterFields.length > 0"
        variant="outline"
        size="sm"
        class="h-9"
        @click="showMoreFilters = !showMoreFilters"
      >
        <SlidersHorizontal class="h-4 w-4 mr-1" />
        More ({{ hiddenFilterFields.length }})
      </Button>

      <!-- Clear all -->
      <Button
        v-if="hasActiveFilters"
        variant="ghost"
        size="sm"
        class="h-9 text-destructive"
        @click="handleClearAll"
      >
        Clear All
      </Button>
    </div>

    <!-- Active filter chips -->
    <div v-if="activeFilterChips.length > 0" class="flex flex-wrap gap-2">
      <Badge
        v-for="chip in activeFilterChips"
        :key="chip.field"
        variant="secondary"
        class="gap-1 pr-1"
      >
        <span>{{ chip.label }}: {{ chip.displayValue }}</span>
        <button
          class="ml-1 rounded-full hover:bg-muted p-0.5"
          @click="clearFilter(chip.field)"
        >
          <X class="h-3 w-3" />
        </button>
      </Badge>
    </div>

    <!-- More filters panel (expanded) -->
    <div
      v-if="showMoreFilters && hiddenFilterFields.length > 0"
      class="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-3 p-4 border rounded-md bg-muted/30"
    >
      <div v-for="field in hiddenFilterFields" :key="field.name">
        <Label class="text-xs text-muted-foreground mb-1">{{ field.label }}</Label>

        <!-- Text filter -->
        <Input
          v-if="field.widgetType === 'text'"
          :modelValue="getFilterValue(field.name)"
          placeholder="Filter..."
          class="h-9"
          @update:modelValue="(v: string | number) => handleTextInput(field, v)"
        />

        <!-- Number / Decimal filter -->
        <Input
          v-else-if="field.widgetType === 'number' || field.widgetType === 'decimal'"
          type="number"
          :modelValue="getFilterValue(field.name)"
          class="h-9"
          @update:modelValue="(v: string | number) => handleNumberInput(field, v)"
        />

        <!-- Boolean filter -->
        <select
          v-else-if="field.widgetType === 'boolean'"
          :value="getFilterValue(field.name)"
          class="h-9 w-full rounded-md border border-input bg-background px-2 text-sm"
          @change="handleBooleanChange(field, $event)"
        >
          <option value="">All</option>
          <option value="true">Yes</option>
          <option value="false">No</option>
        </select>

        <!-- Date filter -->
        <Input
          v-else-if="field.widgetType === 'date' || field.widgetType === 'datetime'"
          type="date"
          :modelValue="getFilterValue(field.name)"
          class="h-9"
          @update:modelValue="(v: string | number) => handleDateInput(field, v)"
        />

        <!-- Enum filter -->
        <select
          v-else-if="field.widgetType === 'enum'"
          :value="getFilterValue(field.name)"
          class="h-9 w-full rounded-md border border-input bg-background px-2 text-sm"
          @change="handleEnumChange(field, $event)"
        >
          <option value="">All</option>
          <option
            v-for="ev in field.enumValues"
            :key="String(ev.value)"
            :value="String(ev.value)"
          >
            {{ ev.displayName || ev.name }}
          </option>
        </select>

        <!-- Association filter (value help) -->
        <div v-else-if="field.widgetType === 'association'" class="flex gap-1">
          <Input
            :modelValue="getAssociationLabel(field.name)"
            :readonly="true"
            placeholder="Select..."
            class="h-9 flex-1 cursor-pointer"
            @click="openValueHelp(field)"
          />
          <Button
            v-if="getFilterValue(field.name)"
            size="icon"
            variant="ghost"
            class="h-9 w-9"
            @click="clearFilter(field.name)"
          >
            <X class="h-3.5 w-3.5" />
          </Button>
        </div>

        <!-- UUID / fallback text filter -->
        <Input
          v-else
          :modelValue="getFilterValue(field.name)"
          placeholder="Filter..."
          class="h-9"
          @update:modelValue="(v: string | number) => handleTextInput(field, v)"
        />
      </div>
    </div>

    <!-- Value Help Dialog (for association filters) -->
    <ValueHelpDialog
      :open="valueHelpOpen"
      :module="module"
      :targetEntity="valueHelpTarget"
      @update:open="valueHelpOpen = $event"
      @select="handleValueHelpSelect"
    />
  </div>
</template>
