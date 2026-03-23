<script setup lang="ts">
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
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

function handleInput(value: string | number) {
  emit('update:modelValue', value === '' ? null : String(value))
}

// Use textarea for longer strings
const useTextarea = props.field.maxLength && props.field.maxLength > 255
</script>

<template>
  <div class="space-y-2">
    <Label :for="field.name">
      {{ field.displayName || field.name }}
      <span v-if="field.isRequired" class="text-destructive">*</span>
    </Label>
    <Textarea
      v-if="useTextarea"
      :id="field.name"
      :modelValue="modelValue ?? ''"
      :placeholder="field.description"
      :disabled="readonly || field.isReadOnly"
      :maxlength="field.maxLength"
      :rows="3"
      @update:modelValue="handleInput"
    />
    <Input
      v-else
      :id="field.name"
      :modelValue="modelValue ?? ''"
      type="text"
      :placeholder="field.description"
      :disabled="readonly || field.isReadOnly"
      :maxlength="field.maxLength"
      @update:modelValue="handleInput"
    />
    <p v-if="field.description" class="text-xs text-muted-foreground">
      {{ field.description }}
    </p>
  </div>
</template>
