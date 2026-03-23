<script setup lang="ts">
import { computed } from 'vue'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import type { FieldMetadata } from '@/types/metadata'

interface Props {
  field: FieldMetadata
  modelValue?: string | null
  readonly?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  readonly: false
})

const emit = defineEmits<{
  'update:modelValue': [value: string | null]
}>()

const inputType = computed(() => {
  switch (props.field.type) {
    case 'Date':
      return 'date'
    case 'Time':
      return 'time'
    case 'DateTime':
    case 'Timestamp':
      return 'datetime-local'
    default:
      return 'date'
  }
})

// Format value for input
const formattedValue = computed(() => {
  if (!props.modelValue) return ''

  if (inputType.value === 'datetime-local') {
    // Convert ISO string to datetime-local format
    const date = new Date(props.modelValue)
    if (isNaN(date.getTime())) return ''
    return date.toISOString().slice(0, 16)
  }

  return props.modelValue
})

function handleInput(value: string | number) {
  if (value === '' || value === null || value === undefined) {
    emit('update:modelValue', null)
    return
  }

  let result = String(value)

  // Convert datetime-local to ISO string
  if (inputType.value === 'datetime-local' && result) {
    const date = new Date(result)
    if (isNaN(date.getTime())) return
    result = date.toISOString()
  }

  emit('update:modelValue', result)
}
</script>

<template>
  <div class="space-y-2">
    <Label :for="field.name">
      {{ field.displayName || field.name }}
      <span v-if="field.isRequired" class="text-destructive">*</span>
    </Label>
    <Input
      :id="field.name"
      :modelValue="formattedValue"
      :type="inputType"
      :placeholder="field.description"
      :disabled="readonly || field.isReadOnly"
      @update:modelValue="handleInput"
    />
    <p v-if="field.description" class="text-xs text-muted-foreground">
      {{ field.description }}
    </p>
  </div>
</template>
