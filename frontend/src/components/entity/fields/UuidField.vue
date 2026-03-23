<script setup lang="ts">
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import type { FieldMetadata } from '@/types/metadata'

interface Props {
  field: FieldMetadata
  modelValue?: string | null
  readonly?: boolean
}

defineProps<Props>()

const emit = defineEmits<{
  'update:modelValue': [value: string | null]
}>()

function handleInput(value: string | number) {
  emit('update:modelValue', value === '' ? null : String(value))
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
      :modelValue="modelValue ?? ''"
      type="text"
      :placeholder="field.description || 'xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx'"
      :disabled="readonly || field.isReadOnly"
      pattern="[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"
      @update:modelValue="handleInput"
    />
    <p v-if="field.description" class="text-xs text-muted-foreground">
      {{ field.description }}
    </p>
  </div>
</template>
