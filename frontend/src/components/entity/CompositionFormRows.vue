<script setup lang="ts">
import { computed } from 'vue'
import type { FieldMetadata, AssociationMetadata, EntityMetadata } from '@/types/metadata'
import { Button } from '@/components/ui/button'
import EntityField from './EntityField.vue'
import { Plus, X } from 'lucide-vue-next'

interface Props {
  association: AssociationMetadata
  childMetadata: EntityMetadata
  parentEntity: string
  modelValue: Record<string, unknown>[]
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:modelValue': [value: Record<string, unknown>[]]
}>()

// System/audit fields to hide
const hiddenFieldPatterns = [
  'CreatedAt', 'UpdatedAt', 'DeletedAt', 'CreatedBy', 'UpdatedBy',
  'TenantId', 'IsDeleted', 'SystemStart', 'SystemEnd', 'Version'
]

// Compute the parent FK field name to hide
const parentFkField = computed(() => `${props.parentEntity}Id`)

// Editable fields: exclude key, parent FK, system/audit, read-only, computed
const editableFields = computed<FieldMetadata[]>(() => {
  if (!props.childMetadata) return []
  return props.childMetadata.fields.filter((f) => {
    if (f.isReadOnly || f.isComputed) return false
    if (hiddenFieldPatterns.some((p) => f.name === p)) return false
    if (f.name.toLowerCase() === parentFkField.value.toLowerCase()) return false
    if (f.name === 'ID' || f.name === 'Id') return false
    return true
  })
})

const displayName = computed(() => {
  return props.association.name.replace(/([A-Z])/g, ' $1').trim()
})

function addRow() {
  const newRow: Record<string, unknown> = {}
  for (const field of editableFields.value) {
    newRow[field.name] = field.defaultValue ?? null
  }
  emit('update:modelValue', [...props.modelValue, newRow])
}

function removeRow(index: number) {
  const updated = [...props.modelValue]
  updated.splice(index, 1)
  emit('update:modelValue', updated)
}

function updateField(rowIndex: number, fieldName: string, value: unknown) {
  const updated = [...props.modelValue]
  updated[rowIndex] = { ...updated[rowIndex], [fieldName]: value }
  emit('update:modelValue', updated)
}
</script>

<template>
  <div class="space-y-3">
    <div class="flex items-center justify-between">
      <h3 class="text-sm font-medium">{{ displayName }}</h3>
      <Button type="button" variant="outline" size="sm" @click="addRow">
        <Plus class="mr-1 h-3 w-3" />
        Add Row
      </Button>
    </div>

    <p v-if="modelValue.length === 0" class="text-sm text-muted-foreground py-2">
      No {{ displayName.toLowerCase() }} rows. Click "Add Row" to add one.
    </p>

    <div v-else class="space-y-4">
      <div
        v-for="(row, rowIdx) in modelValue"
        :key="rowIdx"
        class="border rounded-md p-4 relative"
      >
        <Button
          type="button"
          variant="ghost"
          size="sm"
          class="absolute top-2 right-2 h-6 w-6 p-0"
          @click="removeRow(rowIdx)"
        >
          <X class="h-4 w-4" />
        </Button>

        <div class="grid gap-3 md:grid-cols-2 pr-8">
          <div v-for="field in editableFields" :key="field.name">
            <EntityField
              :field="field"
              :modelValue="row[field.name]"
              @update:modelValue="updateField(rowIdx, field.name, $event)"
            />
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
