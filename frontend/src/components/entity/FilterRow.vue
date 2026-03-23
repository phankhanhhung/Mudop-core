<script setup lang="ts">
import { computed, watch } from 'vue'
import type { FieldMetadata, FieldType } from '@/types/metadata'
import type { FilterOperator } from '@/types/odata'
import { Select } from '@/components/ui/select'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import { X } from 'lucide-vue-next'

interface FilterRowValue {
  field: string
  operator: FilterOperator
  value: unknown
}

interface Props {
  fields: FieldMetadata[]
  modelValue: FilterRowValue
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:modelValue': [value: FilterRowValue]
  remove: []
}>()

// Operator definitions per field type
const operatorsByType: Record<string, { value: FilterOperator; label: string }[]> = {
  String: [
    { value: 'eq', label: 'equals' },
    { value: 'ne', label: 'not equals' },
    { value: 'contains', label: 'contains' },
    { value: 'startswith', label: 'starts with' },
    { value: 'endswith', label: 'ends with' }
  ],
  Integer: [
    { value: 'eq', label: '=' },
    { value: 'ne', label: '!=' },
    { value: 'gt', label: '>' },
    { value: 'ge', label: '>=' },
    { value: 'lt', label: '<' },
    { value: 'le', label: '<=' }
  ],
  Decimal: [
    { value: 'eq', label: '=' },
    { value: 'ne', label: '!=' },
    { value: 'gt', label: '>' },
    { value: 'ge', label: '>=' },
    { value: 'lt', label: '<' },
    { value: 'le', label: '<=' }
  ],
  Date: [
    { value: 'eq', label: '=' },
    { value: 'ne', label: '!=' },
    { value: 'gt', label: 'after' },
    { value: 'ge', label: 'on or after' },
    { value: 'lt', label: 'before' },
    { value: 'le', label: 'on or before' }
  ],
  DateTime: [
    { value: 'eq', label: '=' },
    { value: 'ne', label: '!=' },
    { value: 'gt', label: 'after' },
    { value: 'ge', label: 'on or after' },
    { value: 'lt', label: 'before' },
    { value: 'le', label: 'on or before' }
  ],
  Time: [
    { value: 'eq', label: '=' },
    { value: 'ne', label: '!=' },
    { value: 'gt', label: 'after' },
    { value: 'ge', label: 'on or after' },
    { value: 'lt', label: 'before' },
    { value: 'le', label: 'on or before' }
  ],
  Timestamp: [
    { value: 'eq', label: '=' },
    { value: 'ne', label: '!=' },
    { value: 'gt', label: 'after' },
    { value: 'ge', label: 'on or after' },
    { value: 'lt', label: 'before' },
    { value: 'le', label: 'on or before' }
  ],
  Boolean: [{ value: 'eq', label: 'equals' }],
  Enum: [{ value: 'eq', label: 'equals' }],
  UUID: [
    { value: 'eq', label: 'equals' },
    { value: 'ne', label: 'not equals' }
  ]
}

const selectedField = computed(() =>
  props.fields.find((f) => f.name === props.modelValue.field)
)

const selectedFieldType = computed<FieldType>(() => selectedField.value?.type ?? 'String')

const availableOperators = computed(() => {
  const type = selectedFieldType.value
  return operatorsByType[type] ?? operatorsByType.String
})

// Input type for the value field based on field type
const valueInputType = computed(() => {
  switch (selectedFieldType.value) {
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

const isBooleanField = computed(() => selectedFieldType.value === 'Boolean')
const isEnumField = computed(() => selectedFieldType.value === 'Enum')

function displayName(field: FieldMetadata): string {
  return field.displayName || field.name.replace(/([A-Z])/g, ' $1').trim()
}

function updateField(fieldName: string | number) {
  const newField = props.fields.find((f) => f.name === String(fieldName))
  const newType = newField?.type ?? 'String'
  const defaultOp = (operatorsByType[newType] ?? operatorsByType.String)[0]?.value ?? 'eq'
  emit('update:modelValue', {
    field: String(fieldName),
    operator: defaultOp,
    value: ''
  })
}

function updateOperator(op: string | number) {
  emit('update:modelValue', {
    ...props.modelValue,
    operator: String(op) as FilterOperator
  })
}

function updateValue(val: string | number) {
  let typedValue: unknown = val
  if (selectedFieldType.value === 'Integer') {
    typedValue = val === '' ? '' : parseInt(String(val), 10)
  } else if (selectedFieldType.value === 'Decimal') {
    typedValue = val === '' ? '' : parseFloat(String(val))
  }
  emit('update:modelValue', {
    ...props.modelValue,
    value: typedValue
  })
}

function updateBooleanValue(val: string | number) {
  emit('update:modelValue', {
    ...props.modelValue,
    value: String(val) === 'true'
  })
}

function updateEnumValue(val: string | number) {
  emit('update:modelValue', {
    ...props.modelValue,
    value: String(val)
  })
}

// When field type changes, reset operator if current one is not available
watch(availableOperators, (ops) => {
  const currentOp = props.modelValue.operator
  const isValid = ops.some((o) => o.value === currentOp)
  if (!isValid && ops.length > 0) {
    emit('update:modelValue', {
      ...props.modelValue,
      operator: ops[0].value,
      value: ''
    })
  }
})
</script>

<template>
  <div class="flex items-center gap-2">
    <!-- Field selector -->
    <Select
      :model-value="modelValue.field"
      placeholder="Select field..."
      class="min-w-[140px] flex-1"
      @update:model-value="updateField"
    >
      <option v-for="field in fields" :key="field.name" :value="field.name">
        {{ displayName(field) }}
      </option>
    </Select>

    <!-- Operator selector -->
    <Select
      :model-value="modelValue.operator"
      class="min-w-[110px]"
      @update:model-value="updateOperator"
    >
      <option v-for="op in availableOperators" :key="op.value" :value="op.value">
        {{ op.label }}
      </option>
    </Select>

    <!-- Value input: Boolean -->
    <Select
      v-if="isBooleanField"
      :model-value="String(modelValue.value)"
      class="min-w-[100px] flex-1"
      @update:model-value="updateBooleanValue"
    >
      <option value="true">true</option>
      <option value="false">false</option>
    </Select>

    <!-- Value input: Enum -->
    <Select
      v-else-if="isEnumField && selectedField?.enumValues?.length"
      :model-value="String(modelValue.value)"
      placeholder="Select value..."
      class="min-w-[100px] flex-1"
      @update:model-value="updateEnumValue"
    >
      <option
        v-for="ev in selectedField.enumValues"
        :key="ev.value"
        :value="ev.name"
      >
        {{ ev.displayName || ev.name }}
      </option>
    </Select>

    <!-- Value input: Standard types -->
    <Input
      v-else
      :model-value="modelValue.value as string | number"
      :type="valueInputType"
      placeholder="Value..."
      class="min-w-[100px] flex-1"
      @update:model-value="updateValue"
    />

    <!-- Remove button -->
    <Button
      variant="ghost"
      size="icon"
      class="h-8 w-8 shrink-0 text-muted-foreground hover:text-destructive"
      @click="emit('remove')"
    >
      <X class="h-4 w-4" />
    </Button>
  </div>
</template>
