<script setup lang="ts">
import { computed } from 'vue'
import type { FieldMetadata, AssociationMetadata } from '@/types/metadata'
import StringField from './fields/StringField.vue'
import NumberField from './fields/NumberField.vue'
import BooleanField from './fields/BooleanField.vue'
import DateField from './fields/DateField.vue'
import EnumField from './fields/EnumField.vue'
import UuidField from './fields/UuidField.vue'
import AssociationField from './fields/AssociationField.vue'

interface Props {
  field: FieldMetadata
  modelValue?: unknown
  readonly?: boolean
  association?: AssociationMetadata | null
  currentModule?: string
}

const props = withDefaults(defineProps<Props>(), {
  readonly: false,
  association: null,
  currentModule: ''
})

const emit = defineEmits<{
  'update:modelValue': [value: unknown]
}>()

const fieldComponent = computed(() => {
  // FK fields get the association picker
  if (props.association && props.currentModule) {
    return AssociationField
  }

  switch (props.field.type) {
    case 'String':
      return StringField
    case 'Integer':
    case 'Decimal':
      return NumberField
    case 'Boolean':
      return BooleanField
    case 'Date':
    case 'Time':
    case 'DateTime':
    case 'Timestamp':
      return DateField
    case 'Enum':
      return EnumField
    case 'UUID':
      return UuidField
    default:
      return StringField
  }
})

const extraProps = computed(() => {
  if (props.association && props.currentModule) {
    return { association: props.association, currentModule: props.currentModule }
  }
  return {}
})

function handleUpdate(value: unknown) {
  emit('update:modelValue', value)
}
</script>

<template>
  <component
    :is="fieldComponent"
    :field="field"
    :modelValue="modelValue as any"
    :readonly="readonly"
    v-bind="extraProps"
    @update:modelValue="handleUpdate"
  />
</template>
