<script setup lang="ts">
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import type { FieldMetadata } from '@/types/metadata'

interface Props {
  field: FieldMetadata
  modelValue?: boolean | null
  readonly?: boolean
}

defineProps<Props>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
}>()

function handleChange(value: boolean) {
  emit('update:modelValue', value)
}
</script>

<template>
  <div class="flex items-center space-x-2">
    <Checkbox
      :id="field.name"
      :modelValue="modelValue ?? false"
      :disabled="readonly || field.isReadOnly"
      @update:modelValue="handleChange"
    />
    <div>
      <Label :for="field.name" class="cursor-pointer">
        {{ field.displayName || field.name }}
        <span v-if="field.isRequired" class="text-destructive">*</span>
      </Label>
      <p v-if="field.description" class="text-xs text-muted-foreground">
        {{ field.description }}
      </p>
    </div>
  </div>
</template>
