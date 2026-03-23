<script setup lang="ts">
import { computed, watch, ref, toRef } from 'vue'
import type { Component } from 'vue'
import type { EntityMetadata, AssociationMetadata, FieldMetadata } from '@/types/metadata'
import type { FormLayoutSettings } from '@/types/formLayout'
import { useSmartForm } from '@/composables/useSmartForm'
import FieldGroup from './FieldGroup.vue'
import SmartField from '@/components/smart/SmartField.vue'
import CompositionFormRows from '@/components/entity/CompositionFormRows.vue'
import { Button } from '@/components/ui/button'
import { Alert, AlertTitle, AlertDescription } from '@/components/ui/alert'
import { Spinner } from '@/components/ui/spinner'
import { Key, FileText, Link2, MoreHorizontal } from 'lucide-vue-next'

interface Props {
  // Data
  module: string
  entitySet: string
  metadata: EntityMetadata
  data?: Record<string, unknown>

  // Mode
  mode: 'edit' | 'create' | 'display'

  // State
  isLoading?: boolean
  error?: string | null
  serverErrors?: Record<string, string[]>

  // Configuration
  columns?: 1 | 2 | 3
  showActions?: boolean
  submitLabel?: string
  cancelLabel?: string

  // Associations for FK fields
  associations?: AssociationMetadata[]

  // Layout override from Form Designer
  layoutOverride?: FormLayoutSettings | null

  // Compositions
  compositions?: AssociationMetadata[]
  compositionMetadata?: Map<string, EntityMetadata>
  compositionData?: Record<string, Record<string, unknown>[]>
}

const props = withDefaults(defineProps<Props>(), {
  isLoading: false,
  error: null,
  showActions: true,
  columns: undefined,
  associations: () => [],
  compositions: () => [],
})

const emit = defineEmits<{
  submit: [data: Record<string, unknown>, compositionData?: Record<string, Record<string, unknown>[]>]
  cancel: []
  'field-change': [field: string, value: unknown]
  'dirty-change': [isDirty: boolean]
}>()

// Use the composable for form logic — pass layoutOverride as a ref for reactivity
const layoutRef = toRef(props, 'layoutOverride')
const {
  sections,
  fieldWidthMap,
  formData,
  isDirty,
  dirtyFields,
  fieldErrors,
  sectionErrors,
  updateField: updateFormField,
  validate,
  getSubmitData,
} = useSmartForm(props.metadata, props.mode, props.data, props.associations, layoutRef)

// Local composition data
const localCompositionData = ref<Record<string, Record<string, unknown>[]>>(
  props.compositionData ? { ...props.compositionData } : {}
)
const compositionSnapshot = ref<string>(JSON.stringify(props.compositionData ?? {}))

// Watch for external compositionData changes
watch(
  () => props.compositionData,
  (newData) => {
    if (newData) {
      localCompositionData.value = { ...newData }
      compositionSnapshot.value = JSON.stringify(newData)
    }
  },
  { deep: true }
)

// Track composition dirty state (additions, removals, or edits in child rows)
const isCompositionDirty = computed(() => {
  return JSON.stringify(localCompositionData.value) !== compositionSnapshot.value
})

// Merge fieldErrors with serverErrors
const mergedFieldErrors = computed<Record<string, string>>(() => {
  const errors = { ...fieldErrors.value }
  if (props.serverErrors) {
    for (const [fieldName, messages] of Object.entries(props.serverErrors)) {
      if (messages.length > 0) {
        errors[fieldName] = messages[0]
      }
    }
  }
  return errors
})

// Merged section error counts including server errors
const mergedSectionErrors = computed<Record<string, number>>(() => {
  // Start with base section errors from the composable
  const counts: Record<string, number> = { ...sectionErrors.value }
  // If there are additional server errors, recompute
  if (props.serverErrors && Object.keys(props.serverErrors).length > 0) {
    const errorFieldNames = new Set(Object.keys(mergedFieldErrors.value))
    for (const section of sections.value) {
      let count = 0
      for (const field of section.fields) {
        if (errorFieldNames.has(field.name)) count++
      }
      counts[section.id] = count
    }
  }
  return counts
})

// Watch dirty changes
watch(isDirty, (newValue) => {
  emit('dirty-change', newValue)
})

// Icon map
const iconMap: Record<string, Component> = {
  Key,
  FileText,
  Link2,
  MoreHorizontal,
}

function getSectionIcon(iconName?: string): Component | undefined {
  if (!iconName) return undefined
  return iconMap[iconName]
}

// Responsive grid class
const gridClass = computed(() => {
  const cols = props.layoutOverride?.columns ?? props.columns
  if (cols === 1) return 'grid grid-cols-1 gap-4'
  if (cols === 2) return 'grid grid-cols-1 md:grid-cols-2 gap-4'
  if (cols === 3) return 'grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4'
  // Auto: responsive default
  return 'grid grid-cols-1 md:grid-cols-2 gap-4'
})

// Get the effective width class for a field
function getFieldSpanClass(field: FieldMetadata): string {
  // Check layout override first
  const layoutWidth = fieldWidthMap.value.get(field.name)
  if (layoutWidth === 'full') return 'col-span-full'
  if (layoutWidth === 'half' || layoutWidth === 'third') return ''

  // Fallback heuristics (no layout or field not in layout)
  if (field.maxLength && field.maxLength > 200) return 'col-span-full'
  const annotations = field.annotations ?? {}
  if (annotations['@UI.MultiLineText'] || annotations['UI.MultiLineText']) return 'col-span-full'
  const lowerName = field.name.toLowerCase()
  if (lowerName.includes('description') || lowerName.includes('notes') || lowerName.includes('content')) return 'col-span-full'
  return ''
}

// Determine the effective mode for a field
// Key fields are readonly in edit mode, computed/readonly fields are always display
function getFieldMode(field: FieldMetadata): 'edit' | 'create' | 'display' {
  if (props.mode === 'display') return 'display'
  const keySet = new Set(props.metadata.keys ?? [])
  if (props.mode === 'edit' && keySet.has(field.name)) return 'display'
  if (field.isComputed || field.isReadOnly) return 'display'
  return props.mode
}

// Find association for a FK field
function findAssociation(field: FieldMetadata): AssociationMetadata | undefined {
  if (!props.associations) return undefined
  return props.associations.find(a => a.foreignKey === field.name)
}

// Handle field update
function handleFieldUpdate(fieldName: string, value: unknown) {
  updateFormField(fieldName, value)
  emit('field-change', fieldName, value)
}

// Handle composition update
function updateComposition(compositionName: string, rows: Record<string, unknown>[]) {
  localCompositionData.value = {
    ...localCompositionData.value,
    [compositionName]: rows,
  }
}

// Handle form submission
function handleSubmit() {
  const { valid } = validate()
  if (!valid) return

  const data = getSubmitData()
  const hasCompositions = Object.keys(localCompositionData.value).length > 0
  emit('submit', data, hasCompositions ? localCompositionData.value : undefined)
}
</script>

<template>
  <form @submit.prevent="handleSubmit" class="space-y-6">
    <!-- Error alert -->
    <Alert v-if="error" variant="destructive">
      <AlertTitle>Error</AlertTitle>
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <!-- Sections -->
    <FieldGroup
      v-for="section in sections"
      :key="section.id"
      :title="section.title"
      :description="section.description"
      :field-count="section.fields.length"
      :error-count="mergedSectionErrors[section.id]"
      :collapsed="section.collapsed"
      :icon="getSectionIcon(section.icon)"
    >
      <div :class="gridClass">
        <div
          v-for="field in section.fields"
          :key="field.name"
          class="relative"
          :class="getFieldSpanClass(field)"
        >
          <SmartField
            :field="field"
            :model-value="formData[field.name]"
            :mode="getFieldMode(field)"
            :module="module"
            :entity-set="entitySet"
            :association="findAssociation(field)"
            :error="mergedFieldErrors[field.name]"
            @update:model-value="handleFieldUpdate(field.name, $event)"
          />
          <!-- Dirty indicator -->
          <div
            v-if="dirtyFields.has(field.name)"
            class="absolute left-0 top-0 bottom-0 w-0.5 bg-amber-500 rounded-full"
          />
        </div>
      </div>
    </FieldGroup>

    <!-- Composition sections -->
    <FieldGroup
      v-for="comp in compositions"
      :key="comp.name"
      :title="comp.name"
      :field-count="(localCompositionData[comp.name] ?? []).length"
    >
      <CompositionFormRows
        v-if="compositionMetadata?.get(comp.name)"
        :association="comp"
        :child-metadata="compositionMetadata.get(comp.name)!"
        :parent-entity="entitySet"
        :model-value="localCompositionData[comp.name] ?? []"
        @update:model-value="updateComposition(comp.name, $event)"
      />
      <p
        v-else
        class="text-sm text-muted-foreground py-2"
      >
        No metadata available for {{ comp.name }}.
      </p>
    </FieldGroup>

    <!-- Form actions -->
    <div
      v-if="showActions && mode !== 'display'"
      class="flex justify-end gap-2 pt-4 border-t"
    >
      <Button type="button" variant="outline" @click="emit('cancel')">
        {{ cancelLabel || 'Cancel' }}
      </Button>
      <Button
        type="submit"
        :disabled="isLoading || (!isDirty && !isCompositionDirty && mode === 'edit')"
      >
        <Spinner v-if="isLoading" size="sm" class="mr-2" />
        {{ submitLabel || (mode === 'create' ? 'Create' : 'Save Changes') }}
      </Button>
    </div>
  </form>
</template>
