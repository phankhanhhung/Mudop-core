<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import type { FieldMetadata, AssociationMetadata, EntityMetadata } from '@/types/metadata'
import type { FileReferenceInfo, UploadResult } from '@/types/file'
import { Button } from '@/components/ui/button'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Spinner } from '@/components/ui/spinner'
import { AlertCircle } from 'lucide-vue-next'
import EntityField from './EntityField.vue'
import FileReferenceField from './fields/FileReferenceField.vue'
import CompositionFormRows from './CompositionFormRows.vue'
import { validateFormData } from '@/utils/formValidator'
import { getFirstFieldError } from '@/utils/odataErrorParser'

interface Props {
  fields: FieldMetadata[]
  associations?: AssociationMetadata[]
  currentModule?: string
  initialData?: Record<string, unknown>
  mode?: 'create' | 'edit' | 'view'
  isLoading?: boolean
  error?: string | null
  currentEntity?: string
  entityId?: string
  compositions?: AssociationMetadata[]
  compositionMetadata?: Map<string, EntityMetadata>
  compositionData?: Record<string, Record<string, unknown>[]>
  /** Server-side field validation errors (field name -> error messages), from OData error response */
  serverErrors?: Record<string, string[]>
}

const props = withDefaults(defineProps<Props>(), {
  mode: 'create',
  isLoading: false
})

const emit = defineEmits<{
  submit: [data: Record<string, unknown>, compositionData: Record<string, Record<string, unknown>[]>]
  cancel: []
  'clear-server-error': [fieldName: string]
}>()

// Form data
const formData = ref<Record<string, unknown>>({})
const validationErrors = ref<Record<string, string>>({})
const localCompositionData = ref<Record<string, Record<string, unknown>[]>>({})

// Initialize form data
onMounted(() => {
  initializeForm()
})

watch(
  () => props.initialData,
  () => {
    initializeForm()
  }
)

watch(
  () => props.compositionData,
  (newVal) => {
    if (newVal) {
      localCompositionData.value = { ...newVal }
    }
  },
  { immediate: true }
)

// Convert default value string to proper type
function coerceDefaultValue(value: unknown, fieldType: string): unknown {
  if (value === undefined || value === null) return value
  const str = String(value)
  switch (fieldType) {
    case 'Boolean':
      return str.toLowerCase() === 'true'
    case 'Integer':
      return parseInt(str, 10) || 0
    case 'Decimal':
      return parseFloat(str) || 0
    default:
      return str
  }
}

function initializeForm() {
  const data: Record<string, unknown> = {}

  for (const field of props.fields) {
    // Skip read-only and computed fields in create mode
    if (props.mode === 'create' && (field.isReadOnly || field.isComputed)) {
      continue
    }

    // Use initial data or default value
    if (props.initialData && field.name in props.initialData) {
      data[field.name] = props.initialData[field.name]
    } else if (field.defaultValue !== undefined) {
      data[field.name] = coerceDefaultValue(field.defaultValue, field.type)
    } else {
      data[field.name] = null
    }
  }

  formData.value = data
}

// Editable fields (excluding read-only, computed, and FileReference expanded columns)
const editableFields = computed(() => {
  const base = props.mode === 'view'
    ? props.fields
    : props.fields.filter((f) => !f.isReadOnly && !f.isComputed)
  return base.filter(f => !fileRefFieldNames.value.has(f.name))
})

// Read-only in view mode
const isReadonly = computed(() => props.mode === 'view')

// FK association lookup: maps foreignKey field name → association
const fkAssociationMap = computed(() => {
  const map = new Map<string, AssociationMetadata>()
  for (const assoc of props.associations ?? []) {
    if (assoc.foreignKey) {
      map.set(assoc.foreignKey, assoc)
    }
  }
  return map
})

// FileReference detection: group 8 expanded columns into logical file fields
const FILE_REF_SUFFIXES = ['Provider', 'Bucket', 'Key', 'Size', 'MimeType', 'Checksum', 'UploadedAt', 'UploadedBy']

const fileReferenceGroups = computed(() => {
  const groups: { baseName: string; displayName: string; fieldNames: Set<string> }[] = []
  const fieldNames = new Set(props.fields.map(f => f.name))

  // For each field ending in 'Provider', check if companion fields exist
  for (const field of props.fields) {
    if (!field.name.endsWith('Provider')) continue
    const baseName = field.name.slice(0, -'Provider'.length)
    if (!baseName) continue

    // Check that at least Key and Bucket companions exist
    const hasKey = fieldNames.has(baseName + 'Key')
    const hasBucket = fieldNames.has(baseName + 'Bucket')
    if (!hasKey || !hasBucket) continue

    const groupFieldNames = new Set<string>()
    for (const suffix of FILE_REF_SUFFIXES) {
      const name = baseName + suffix
      if (fieldNames.has(name)) {
        groupFieldNames.add(name)
      }
    }

    groups.push({
      baseName,
      displayName: baseName.replace(/([A-Z])/g, ' $1').trim(),
      fieldNames: groupFieldNames
    })
  }
  return groups
})

// Set of all field names that belong to a FileReference group (to hide from regular form)
const fileRefFieldNames = computed(() => {
  const names = new Set<string>()
  for (const group of fileReferenceGroups.value) {
    for (const name of group.fieldNames) {
      names.add(name)
    }
  }
  return names
})

// Extract FileReferenceInfo from current form/initial data for a given base name
function getFileReferenceInfo(baseName: string): FileReferenceInfo {
  const data = props.initialData ?? formData.value
  return {
    provider: data[baseName + 'Provider'] as string | undefined,
    bucket: data[baseName + 'Bucket'] as string | undefined,
    key: data[baseName + 'Key'] as string | undefined,
    size: data[baseName + 'Size'] as number | undefined,
    mimeType: data[baseName + 'MimeType'] as string | undefined,
    checksum: data[baseName + 'Checksum'] as string | undefined,
    uploadedAt: data[baseName + 'UploadedAt'] as string | undefined,
    uploadedBy: data[baseName + 'UploadedBy'] as string | undefined
  }
}

function handleFileUploaded(baseName: string, result: UploadResult) {
  // Update form data with new file metadata
  formData.value[baseName + 'Provider'] = result.provider
  formData.value[baseName + 'Bucket'] = result.bucket
  formData.value[baseName + 'Key'] = result.key
  formData.value[baseName + 'Size'] = result.size
  formData.value[baseName + 'MimeType'] = result.mimeType
  formData.value[baseName + 'Checksum'] = result.checksum
  formData.value[baseName + 'UploadedAt'] = result.uploadedAt
}

function handleFileDeleted(baseName: string) {
  for (const suffix of FILE_REF_SUFFIXES) {
    formData.value[baseName + suffix] = null
  }
}

// Merged field errors: client-side validation errors + server-side field errors
const mergedFieldErrors = computed(() => {
  const merged: Record<string, string> = { ...validationErrors.value }
  if (props.serverErrors) {
    for (const field of editableFields.value) {
      if (!merged[field.name]) {
        const serverMsg = getFirstFieldError(props.serverErrors, field.name)
        if (serverMsg) {
          merged[field.name] = serverMsg
        }
      }
    }
  }
  return merged
})

// Update field value
function updateField(fieldName: string, value: unknown) {
  formData.value[fieldName] = value
  // Clear client-side validation error for this field
  delete validationErrors.value[fieldName]
  // Notify parent to clear server error for this field
  emit('clear-server-error', fieldName)
}

function updateCompositionRows(navName: string, rows: Record<string, unknown>[]) {
  localCompositionData.value = { ...localCompositionData.value, [navName]: rows }
}

// Submit handler
function handleSubmit() {
  // Validate
  const result = validateFormData(
    formData.value,
    editableFields.value,
    props.mode === 'edit' ? 'update' : 'create'
  )

  if (!result.success) {
    validationErrors.value = result.errors ?? {}
    return
  }

  emit('submit', result.data ?? formData.value, localCompositionData.value)
}

// Cancel handler
function handleCancel() {
  emit('cancel')
}
</script>

<template>
  <form @submit.prevent="handleSubmit" class="space-y-6">
    <!-- Error alert -->
    <Alert v-if="error" variant="destructive">
      <AlertCircle class="h-4 w-4" />
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <!-- Form fields -->
    <div class="grid gap-4 md:grid-cols-2">
      <div
        v-for="field in editableFields"
        :key="field.name"
        :class="{ 'md:col-span-2': field.maxLength && field.maxLength > 255 }"
      >
        <EntityField
          :field="field"
          :modelValue="formData[field.name]"
          :readonly="isReadonly"
          :association="fkAssociationMap.get(field.name) ?? null"
          :currentModule="currentModule"
          @update:modelValue="updateField(field.name, $event)"
        />
        <p v-if="mergedFieldErrors[field.name]" class="text-sm text-destructive mt-1">
          {{ mergedFieldErrors[field.name] }}
        </p>
      </div>
    </div>

    <!-- FileReference fields -->
    <div
      v-if="fileReferenceGroups.length > 0"
      class="grid gap-4 md:grid-cols-2"
    >
      <div
        v-for="group in fileReferenceGroups"
        :key="group.baseName"
        class="md:col-span-1"
      >
        <FileReferenceField
          :fieldName="group.baseName"
          :displayName="group.displayName"
          :module="currentModule ?? ''"
          :entitySet="currentEntity ?? ''"
          :entityId="entityId ?? ''"
          :fileInfo="getFileReferenceInfo(group.baseName)"
          :readonly="isReadonly"
          @uploaded="handleFileUploaded(group.baseName, $event)"
          @deleted="handleFileDeleted(group.baseName)"
        />
      </div>
    </div>

    <!-- Composition sections -->
    <div
      v-for="comp in compositions"
      :key="comp.name"
      class="border-t pt-4"
    >
      <CompositionFormRows
        v-if="compositionMetadata?.get(comp.name)"
        :association="comp"
        :childMetadata="compositionMetadata.get(comp.name)!"
        :parentEntity="currentEntity ?? ''"
        :modelValue="localCompositionData[comp.name] ?? []"
        @update:modelValue="updateCompositionRows(comp.name, $event)"
      />
    </div>

    <!-- Form actions -->
    <div v-if="mode !== 'view'" class="flex justify-end gap-2 pt-4 border-t">
      <Button type="button" variant="outline" @click="handleCancel" :disabled="isLoading">
        {{ $t('entity.form.cancel') }}
      </Button>
      <Button type="submit" :disabled="isLoading">
        <Spinner v-if="isLoading" size="sm" class="mr-2" />
        {{ mode === 'create' ? $t('entity.form.create') : $t('entity.form.saveChanges') }}
      </Button>
    </div>
  </form>
</template>
