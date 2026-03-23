<script setup lang="ts">
import { onMounted, onBeforeUnmount } from 'vue'
import type { EntityMetadata, FieldMetadata } from '@/types/metadata'
import type { SmartColumnConfig } from '@/composables/useSmartTable'
import { useRowEdit } from '@/composables/useRowEdit'
import SmartField from '@/components/smart/SmartField.vue'
import SmartCellDisplay from '@/components/smart/SmartCellDisplay.vue'
import { TableRow, TableCell } from '@/components/ui/table'
import { Button } from '@/components/ui/button'
import { Check, X } from 'lucide-vue-next'

interface Props {
  columns: SmartColumnConfig[]
  rowData: Record<string, unknown>
  rowId: string
  metadata: EntityMetadata
  module: string
  entitySet: string
  selectionMode?: 'none' | 'single' | 'multi'
}

const props = withDefaults(defineProps<Props>(), {
  selectionMode: 'none',
})

const emit = defineEmits<{
  save: [rowId: string, changes: Record<string, unknown>]
  cancel: [rowId: string]
}>()

const { editValues, dirtyFields, startEdit, updateField, isDirty, getChanges, cancelEdit } = useRowEdit()

// Start editing on mount
onMounted(() => {
  startEdit(props.rowId, props.rowData)
})

// Find FieldMetadata for a column
function getFieldMetadata(col: SmartColumnConfig): FieldMetadata | undefined {
  return props.metadata.fields.find((f) => f.name === col.field)
}

// Check if a column is editable (not key, not readonly, not computed)
function isEditable(col: SmartColumnConfig): boolean {
  if (col.isKey) return false
  const field = getFieldMetadata(col)
  if (!field) return false
  return !field.isReadOnly && !field.isComputed
}

function handleSave() {
  if (!isDirty()) return
  const changes = getChanges()
  emit('save', props.rowId, changes)
  cancelEdit()
}

function handleCancel() {
  cancelEdit()
  emit('cancel', props.rowId)
}

function handleKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') {
    handleCancel()
  } else if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
    handleSave()
  }
}

onMounted(() => {
  document.addEventListener('keydown', handleKeydown)
})

onBeforeUnmount(() => {
  document.removeEventListener('keydown', handleKeydown)
})
</script>

<template>
  <TableRow class="bg-muted/20 ring-1 ring-inset ring-primary/20">
    <!-- Selection cell placeholder -->
    <TableCell
      v-if="selectionMode !== 'none'"
      class="w-10"
    />

    <!-- Data cells -->
    <TableCell
      v-for="col in columns"
      :key="col.field"
      :style="{ textAlign: col.align }"
      :class="dirtyFields.has(col.field) ? 'border-l-2 border-l-amber-400' : ''"
    >
      <!-- Key / readonly fields: display mode -->
      <template v-if="!isEditable(col)">
        <SmartCellDisplay
          :column="col"
          :value="editValues[col.field]"
          :row="editValues"
        />
      </template>

      <!-- Editable fields: SmartField in edit mode -->
      <template v-else>
        <SmartField
          v-if="getFieldMetadata(col)"
          :field="getFieldMetadata(col)!"
          :modelValue="editValues[col.field]"
          mode="edit"
          :module="module"
          :entitySet="entitySet"
          :showLabel="false"
          @update:modelValue="updateField(col.field, $event)"
        />
      </template>
    </TableCell>

    <!-- Actions: Save / Cancel -->
    <TableCell class="text-right">
      <div class="flex justify-end gap-1">
        <Button
          variant="default"
          size="icon"
          class="h-7 w-7"
          title="Save (Ctrl+Enter)"
          :disabled="!isDirty()"
          @click="handleSave"
        >
          <Check class="h-3.5 w-3.5" />
        </Button>
        <Button
          variant="ghost"
          size="icon"
          class="h-7 w-7"
          title="Cancel (Escape)"
          @click="handleCancel"
        >
          <X class="h-3.5 w-3.5" />
        </Button>
      </div>
    </TableCell>
  </TableRow>
</template>
