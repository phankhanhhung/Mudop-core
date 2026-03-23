<script setup lang="ts">
import { computed } from 'vue'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import StepInput from '@/components/smart/StepInput.vue'
import type { FieldMetadata } from '@/types/metadata'

interface Props {
  field: FieldMetadata
  modelValue?: number | null
  readonly?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  readonly: false
})

const emit = defineEmits<{
  'update:modelValue': [value: number | null]
}>()

function handleInput(value: string | number) {
  if (value === '' || value === null || value === undefined) {
    emit('update:modelValue', null)
    return
  }

  const num = props.field.type === 'Integer'
    ? parseInt(String(value), 10)
    : parseFloat(String(value))

  emit('update:modelValue', isNaN(num) ? null : num)
}

// Calculate step for decimal fields
const step = props.field.type === 'Decimal' && props.field.scale
  ? Math.pow(10, -props.field.scale)
  : props.field.type === 'Integer' ? 1 : 'any'

// StepInput: use for Integer fields with min/max annotations
const minAnnotation = computed(() => {
  const v = props.field.annotations?.['Validation.Minimum'] ?? props.field.annotations?.['@Validation.Minimum']
  return typeof v === 'number' ? v : undefined
})

const maxAnnotation = computed(() => {
  const v = props.field.annotations?.['Validation.Maximum'] ?? props.field.annotations?.['@Validation.Maximum']
  return typeof v === 'number' ? v : undefined
})

const useStepInput = computed(() =>
  props.field.type === 'Integer' && (minAnnotation.value !== undefined || maxAnnotation.value !== undefined)
)

function handleStepChange(value: number) {
  emit('update:modelValue', value)
}
</script>

<template>
  <div class="space-y-2">
    <Label :for="field.name">
      {{ field.displayName || field.name }}
      <span v-if="field.isRequired" class="text-destructive">*</span>
    </Label>
    <StepInput
      v-if="useStepInput"
      :modelValue="modelValue ?? 0"
      :min="minAnnotation"
      :max="maxAnnotation"
      :step="1"
      :disabled="readonly || field.isReadOnly"
      :required="field.isRequired"
      @update:modelValue="handleStepChange"
    />
    <Input
      v-else
      :id="field.name"
      :modelValue="modelValue ?? ''"
      type="number"
      :placeholder="field.description"
      :disabled="readonly || field.isReadOnly"
      :step="step"
      @update:modelValue="handleInput"
    />
    <p v-if="field.description" class="text-xs text-muted-foreground">
      {{ field.description }}
    </p>
  </div>
</template>
