<script setup lang="ts">
import { Select } from '@/components/ui/select'
import { Label } from '@/components/ui/label'
import type { FieldMetadata } from '@/types/metadata'

interface Props {
  field: FieldMetadata
  modelValue?: string | number | null
  readonly?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  readonly: false
})

const emit = defineEmits<{
  'update:modelValue': [value: string | number | null]
}>()

function handleChange(event: Event) {
  const target = event.target as HTMLSelectElement
  const value = target.value

  if (value === '') {
    emit('update:modelValue', null)
    return
  }

  // Preserve the original type
  const enumValue = props.field.enumValues?.find((e) => String(e.value) === value)
  emit('update:modelValue', enumValue?.value ?? value)
}
</script>

<template>
  <div class="space-y-2">
    <Label :for="field.name">
      {{ field.displayName || field.name }}
      <span v-if="field.isRequired" class="text-destructive">*</span>
    </Label>
    <Select
      :id="field.name"
      :modelValue="modelValue ?? ''"
      :disabled="readonly || field.isReadOnly"
      @change="handleChange"
    >
      <option v-if="!field.isRequired" value="">-- Select --</option>
      <option
        v-for="enumValue in field.enumValues"
        :key="String(enumValue.value)"
        :value="enumValue.value"
      >
        {{ enumValue.displayName || enumValue.name }}
      </option>
    </Select>
    <p v-if="field.description" class="text-xs text-muted-foreground">
      {{ field.description }}
    </p>
  </div>
</template>
