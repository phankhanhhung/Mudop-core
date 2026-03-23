<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import { PopoverRoot, PopoverTrigger, PopoverContent, PopoverPortal } from 'radix-vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Checkbox } from '@/components/ui/checkbox'
import { Select } from '@/components/ui/select'
import { Filter, X, Check } from 'lucide-vue-next'
import type { FilterCondition, FilterOperator } from '@/types/odata'
import type { FieldType, EnumValue } from '@/types/metadata'

interface Props {
  fieldName: string
  fieldType: FieldType
  enumValues?: EnumValue[]
  currentFilter?: FilterCondition | null
}

const props = withDefaults(defineProps<Props>(), {
  currentFilter: null
})

const emit = defineEmits<{
  apply: [filter: FilterCondition]
  'apply-between': [field: string, min: unknown, max: unknown, type: 'number' | 'date']
  clear: []
}>()

const isOpen = ref(false)
const firstInputRef = ref<HTMLElement | null>(null)

// -- Operator options per field type --

interface OperatorOption {
  value: string
  label: string
}

const stringOperators: OperatorOption[] = [
  { value: 'contains', label: 'Contains' },
  { value: 'startswith', label: 'Starts with' },
  { value: 'endswith', label: 'Ends with' },
  { value: 'eq', label: 'Equals' },
  { value: 'ne', label: 'Not equals' }
]

const numberOperators: OperatorOption[] = [
  { value: 'eq', label: 'Equals' },
  { value: 'ne', label: 'Not equals' },
  { value: 'gt', label: 'Greater than' },
  { value: 'lt', label: 'Less than' },
  { value: 'between', label: 'Between' }
]

const dateOperators: OperatorOption[] = [
  { value: 'eq', label: 'Equals' },
  { value: 'lt', label: 'Before' },
  { value: 'gt', label: 'After' },
  { value: 'between', label: 'Between' }
]

const operators = computed<OperatorOption[]>(() => {
  switch (props.fieldType) {
    case 'String':
      return stringOperators
    case 'Integer':
    case 'Decimal':
      return numberOperators
    case 'Date':
    case 'DateTime':
    case 'Timestamp':
    case 'Time':
      return dateOperators
    default:
      return []
  }
})

// -- State --

const selectedOperator = ref<string>('eq')
const filterValue = ref<string>('')
const filterValueMin = ref<string>('')
const filterValueMax = ref<string>('')
const booleanValue = ref<'true' | 'false' | 'any'>('any')
const selectedEnumValues = ref<Set<string>>(new Set())

const isBetween = computed(() => selectedOperator.value === 'between')

const showOperatorSelect = computed(() => {
  return (
    props.fieldType === 'String' ||
    props.fieldType === 'Integer' ||
    props.fieldType === 'Decimal' ||
    props.fieldType === 'Date' ||
    props.fieldType === 'DateTime' ||
    props.fieldType === 'Timestamp' ||
    props.fieldType === 'Time'
  )
})

const inputType = computed(() => {
  switch (props.fieldType) {
    case 'Integer':
    case 'Decimal':
      return 'number'
    case 'Date':
      return 'date'
    case 'DateTime':
    case 'Timestamp':
      return 'datetime-local'
    case 'Time':
      return 'time'
    default:
      return 'text'
  }
})

const displayFieldName = computed(() => {
  return props.fieldName.replace(/([A-Z])/g, ' $1').trim()
})

const canApply = computed(() => {
  if (props.fieldType === 'Boolean') {
    return booleanValue.value !== 'any'
  }
  if (props.fieldType === 'Enum') {
    return selectedEnumValues.value.size > 0
  }
  if (isBetween.value) {
    return filterValueMin.value !== '' || filterValueMax.value !== ''
  }
  return filterValue.value !== ''
})

// -- Initialize state from current filter --

function initializeFromCurrentFilter(): void {
  // Reset all state
  filterValue.value = ''
  filterValueMin.value = ''
  filterValueMax.value = ''
  booleanValue.value = 'any'
  selectedEnumValues.value = new Set()

  // Set default operator based on field type
  if (props.fieldType === 'String') {
    selectedOperator.value = 'contains'
  } else {
    selectedOperator.value = 'eq'
  }

  if (!props.currentFilter) return

  const filter = props.currentFilter
  selectedOperator.value = filter.operator
  filterValue.value = String(filter.value ?? '')

  if (props.fieldType === 'Boolean') {
    booleanValue.value = filter.value === true || filter.value === 'true' ? 'true' : 'false'
  }
}

// Re-initialize when popover opens and focus the first input
watch(isOpen, (open) => {
  if (open) {
    initializeFromCurrentFilter()
    nextTick(() => {
      firstInputRef.value?.focus()
    })
  }
})

// -- Enum helpers --

function toggleEnumValue(val: string): void {
  const next = new Set(selectedEnumValues.value)
  if (next.has(val)) {
    next.delete(val)
  } else {
    next.add(val)
  }
  selectedEnumValues.value = next
}

function isEnumSelected(val: string): boolean {
  return selectedEnumValues.value.has(val)
}

// -- Actions --

function handleApply(): void {
  if (props.fieldType === 'Boolean') {
    if (booleanValue.value === 'any') return
    emit('apply', {
      field: props.fieldName,
      operator: 'eq',
      value: booleanValue.value === 'true'
    })
    isOpen.value = false
    return
  }

  if (props.fieldType === 'Enum') {
    if (selectedEnumValues.value.size === 0) return
    // For single selection, emit eq; for multiple, emit the first as eq
    // The parent can handle multiple enum values via OR
    const values = Array.from(selectedEnumValues.value)
    if (values.length === 1) {
      emit('apply', {
        field: props.fieldName,
        operator: 'eq',
        value: values[0]
      })
    } else {
      // Emit as a comma-separated list; the filter builder will handle OR logic
      emit('apply', {
        field: props.fieldName,
        operator: 'eq',
        value: values.join(',')
      })
    }
    isOpen.value = false
    return
  }

  if (props.fieldType === 'UUID') {
    if (!filterValue.value) return
    emit('apply', {
      field: props.fieldName,
      operator: 'eq',
      value: filterValue.value
    })
    isOpen.value = false
    return
  }

  if (isBetween.value) {
    const betweenType =
      props.fieldType === 'Integer' || props.fieldType === 'Decimal' ? 'number' : 'date'
    emit(
      'apply-between',
      props.fieldName,
      filterValueMin.value || null,
      filterValueMax.value || null,
      betweenType
    )
    isOpen.value = false
    return
  }

  if (!filterValue.value) return

  let typedValue: unknown = filterValue.value
  if (props.fieldType === 'Integer') {
    typedValue = parseInt(filterValue.value, 10)
    if (isNaN(typedValue as number)) return
  } else if (props.fieldType === 'Decimal') {
    typedValue = parseFloat(filterValue.value)
    if (isNaN(typedValue as number)) return
  }

  emit('apply', {
    field: props.fieldName,
    operator: selectedOperator.value as FilterOperator,
    value: typedValue
  })
  isOpen.value = false
}

function handleClear(): void {
  emit('clear')
  isOpen.value = false
}

function handleKeydown(event: KeyboardEvent): void {
  if (event.key === 'Enter' && canApply.value) {
    handleApply()
  }
}
</script>

<template>
  <PopoverRoot v-model:open="isOpen">
    <PopoverTrigger as-child>
      <slot>
        <Button
          variant="ghost"
          size="icon"
          class="h-6 w-6 shrink-0"
          :class="currentFilter ? 'text-primary' : 'text-muted-foreground'"
        >
          <Filter class="h-3.5 w-3.5" />
        </Button>
      </slot>
    </PopoverTrigger>
    <PopoverPortal>
      <PopoverContent
        :side-offset="4"
        align="start"
        class="z-50 w-72 rounded-md border bg-background p-0 shadow-md outline-none data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95"
        role="dialog"
        aria-modal="true"
        :aria-label="'Filter: ' + displayFieldName"
        tabindex="0"
        @keydown="handleKeydown"
      >
        <!-- Header -->
        <div class="flex items-center justify-between border-b px-3 py-2">
          <span class="text-sm font-medium">Filter: {{ displayFieldName }}</span>
          <button
            class="rounded-sm p-0.5 hover:bg-muted transition-colors"
            aria-label="Close filter"
            @click="isOpen = false"
          >
            <X class="h-4 w-4 text-muted-foreground" aria-hidden="true" />
          </button>
        </div>

        <div class="p-3 space-y-3">
          <!-- Boolean filter -->
          <template v-if="fieldType === 'Boolean'">
            <div class="space-y-1.5">
              <label
                class="flex items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer"
                @click="booleanValue = 'true'"
              >
                <span
                  class="h-4 w-4 rounded-full border-2 flex items-center justify-center"
                  :class="
                    booleanValue === 'true'
                      ? 'border-primary bg-primary'
                      : 'border-muted-foreground'
                  "
                >
                  <span
                    v-if="booleanValue === 'true'"
                    class="h-1.5 w-1.5 rounded-full bg-primary-foreground"
                  />
                </span>
                Yes / True
              </label>
              <label
                class="flex items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer"
                @click="booleanValue = 'false'"
              >
                <span
                  class="h-4 w-4 rounded-full border-2 flex items-center justify-center"
                  :class="
                    booleanValue === 'false'
                      ? 'border-primary bg-primary'
                      : 'border-muted-foreground'
                  "
                >
                  <span
                    v-if="booleanValue === 'false'"
                    class="h-1.5 w-1.5 rounded-full bg-primary-foreground"
                  />
                </span>
                No / False
              </label>
              <label
                class="flex items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer"
                @click="booleanValue = 'any'"
              >
                <span
                  class="h-4 w-4 rounded-full border-2 flex items-center justify-center"
                  :class="
                    booleanValue === 'any'
                      ? 'border-primary bg-primary'
                      : 'border-muted-foreground'
                  "
                >
                  <span
                    v-if="booleanValue === 'any'"
                    class="h-1.5 w-1.5 rounded-full bg-primary-foreground"
                  />
                </span>
                Any
              </label>
            </div>
          </template>

          <!-- Enum filter -->
          <template v-else-if="fieldType === 'Enum' && enumValues">
            <div class="max-h-48 overflow-y-auto space-y-0.5">
              <button
                v-for="ev in enumValues"
                :key="String(ev.value)"
                class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent hover:text-accent-foreground cursor-pointer"
                @click="toggleEnumValue(String(ev.value))"
              >
                <Checkbox
                  :model-value="isEnumSelected(String(ev.value))"
                  class="pointer-events-none"
                />
                <span class="truncate">{{ ev.displayName ?? ev.name }}</span>
              </button>
            </div>
          </template>

          <!-- UUID filter -->
          <template v-else-if="fieldType === 'UUID'">
            <div class="space-y-1.5">
              <Label class="text-xs text-muted-foreground">UUID value</Label>
              <Input
                ref="firstInputRef"
                v-model="filterValue"
                type="text"
                placeholder="e.g. 550e8400-e29b-41d4-..."
                class="h-8 text-sm"
              />
            </div>
          </template>

          <!-- String / Number / Date filter -->
          <template v-else>
            <!-- Operator selector -->
            <div v-if="showOperatorSelect" class="space-y-1.5">
              <Label class="text-xs text-muted-foreground">Operator</Label>
              <Select
                :model-value="selectedOperator"
                class="h-8 text-sm"
                @update:model-value="selectedOperator = String($event)"
              >
                <option
                  v-for="op in operators"
                  :key="op.value"
                  :value="op.value"
                >
                  {{ op.label }}
                </option>
              </Select>
            </div>

            <!-- Value input(s) -->
            <div v-if="isBetween" class="space-y-1.5">
              <Label class="text-xs text-muted-foreground">Min</Label>
              <Input
                ref="firstInputRef"
                v-model="filterValueMin"
                :type="inputType"
                placeholder="Minimum"
                class="h-8 text-sm"
              />
              <Label class="text-xs text-muted-foreground">Max</Label>
              <Input
                v-model="filterValueMax"
                :type="inputType"
                placeholder="Maximum"
                class="h-8 text-sm"
              />
            </div>
            <div v-else class="space-y-1.5">
              <Label class="text-xs text-muted-foreground">Value</Label>
              <Input
                ref="firstInputRef"
                v-model="filterValue"
                :type="inputType"
                placeholder="Enter value..."
                class="h-8 text-sm"
              />
            </div>
          </template>
        </div>

        <!-- Footer actions -->
        <div class="flex items-center justify-between border-t px-3 py-2">
          <Button
            variant="ghost"
            size="sm"
            class="h-7 text-xs text-muted-foreground"
            @click="handleClear"
          >
            <X class="mr-1 h-3 w-3" />
            Clear
          </Button>
          <Button
            size="sm"
            class="h-7 text-xs"
            :disabled="!canApply"
            @click="handleApply"
          >
            <Check class="mr-1 h-3 w-3" />
            Apply
          </Button>
        </div>
      </PopoverContent>
    </PopoverPortal>
  </PopoverRoot>
</template>
