import { computed, ref, watch, isRef, type ComputedRef, type Ref } from 'vue'
import type { EntityMetadata, FieldMetadata, AssociationMetadata } from '@/types/metadata'
import type { FormLayoutSettings } from '@/types/formLayout'

/** System fields that should be excluded from form sections (case-insensitive) */
const SYSTEM_FIELDS_LOWER = new Set([
  'createdat', 'updatedat', 'createdby', 'updatedby',
  'tenantid', 'systemstart', 'systemend', 'version',
  'deletedat', 'isdeleted', 'deletedby',
])

export interface FormSection {
  id: string
  title: string
  description?: string
  icon?: string
  fields: FieldMetadata[]
  collapsed: boolean
}

export interface UseSmartFormReturn {
  sections: ComputedRef<FormSection[]>
  fieldWidthMap: ComputedRef<Map<string, 'full' | 'half' | 'third'>>
  formData: Ref<Record<string, unknown>>
  isDirty: ComputedRef<boolean>
  dirtyFields: ComputedRef<Set<string>>
  fieldErrors: ComputedRef<Record<string, string>>
  sectionErrors: ComputedRef<Record<string, number>>
  updateField: (field: string, value: unknown) => void
  resetForm: () => void
  validate: () => { valid: boolean; errors: Record<string, string> }
  getSubmitData: () => Record<string, unknown>
}

function isSystemField(fieldName: string): boolean {
  return SYSTEM_FIELDS_LOWER.has(fieldName.toLowerCase())
}

function buildForeignKeySet(associations?: AssociationMetadata[]): Set<string> {
  const fkSet = new Set<string>()
  if (!associations) return fkSet
  for (const assoc of associations) {
    if (assoc.foreignKey) {
      fkSet.add(assoc.foreignKey)
    }
  }
  return fkSet
}

function deepEqual(a: unknown, b: unknown): boolean {
  if (a === b) return true
  if (a == null && b == null) return true
  if (a == null || b == null) return a === b
  if (typeof a !== typeof b) return false
  if (typeof a === 'object') {
    return JSON.stringify(a) === JSON.stringify(b)
  }
  return false
}

function buildSectionsFromLayout(
  overrideLayout: FormLayoutSettings,
  allFields: FieldMetadata[],
  currentMode: 'create' | 'edit' | 'display',
): FormSection[] {
  const fieldMap = new Map(allFields.map((f) => [f.name, f]))
  return overrideLayout.sections
    .filter((s) => s.fields.some((f) => f.visible))
    .map((s): FormSection => ({
      id: s.id,
      title: s.title,
      description: undefined,
      icon: s.icon,
      collapsed: s.collapsed,
      fields: s.fields
        .filter((f) => f.visible)
        .map((f) => fieldMap.get(f.name))
        .filter((f): f is FieldMetadata => f !== undefined)
        .filter((f) => {
          // In create mode, hide computed/readonly/UUID-key fields
          if (currentMode === 'create') {
            if (f.isComputed || f.isReadOnly) return false
          }
          return true
        }),
    }))
}

export function useSmartForm(
  metadata: EntityMetadata,
  mode: 'edit' | 'create' | 'display',
  initialData?: Record<string, unknown>,
  associations?: AssociationMetadata[],
  layoutOverride?: Ref<FormLayoutSettings | null | undefined> | FormLayoutSettings | null,
): UseSmartFormReturn {
  const keySet = new Set(metadata.keys ?? [])
  const fkSet = buildForeignKeySet(associations)

  // Normalize layoutOverride to a reactive getter
  const getLayout = (): FormLayoutSettings | null | undefined =>
    isRef(layoutOverride) ? layoutOverride.value : layoutOverride

  // Initialize form data from initial data or defaults
  const buildInitialFormData = (): Record<string, unknown> => {
    const data: Record<string, unknown> = {}
    for (const field of metadata.fields) {
      if (initialData && field.name in initialData) {
        data[field.name] = initialData[field.name]
      } else {
        data[field.name] = field.defaultValue ?? null
      }
    }
    return data
  }

  const formData = ref<Record<string, unknown>>(buildInitialFormData())
  const snapshot = ref<Record<string, unknown>>({ ...buildInitialFormData() })

  // Track touched fields and whether a submit has been attempted
  const touchedFields = ref<Set<string>>(new Set())
  const submitAttempted = ref(false)

  // Build sections from metadata
  const sections = computed<FormSection[]>(() => {
    const activeLayout = getLayout()
    if (activeLayout) {
      return buildSectionsFromLayout(activeLayout, metadata.fields ?? [], mode)
    }
    const allFields = metadata.fields ?? []
    const result: FormSection[] = []
    const assigned = new Set<string>()

    // 1. Key Fields section
    const keyFields = allFields.filter(f => keySet.has(f.name))
    if (keyFields.length > 0) {
      const visibleKeyFields = mode === 'create'
        ? keyFields.filter(f => f.type !== 'UUID')
        : keyFields

      if (visibleKeyFields.length > 0) {
        result.push({
          id: 'keys',
          title: 'Key Fields',
          description: mode === 'create' ? 'UUID keys are auto-generated' : undefined,
          icon: 'Key',
          fields: visibleKeyFields,
          collapsed: mode === 'display',
        })
      }
      for (const f of keyFields) {
        assigned.add(f.name)
      }
    }

    // 2. General Information section
    const generalFields = allFields.filter(f => {
      if (assigned.has(f.name)) return false
      if (isSystemField(f.name)) return false
      if (fkSet.has(f.name)) return false
      return true
    })

    // Sort: required first, then optional
    const sortedGeneral = [...generalFields].sort((a, b) => {
      if (a.isRequired && !b.isRequired) return -1
      if (!a.isRequired && b.isRequired) return 1
      return 0
    })

    if (sortedGeneral.length > 0) {
      result.push({
        id: 'general',
        title: 'General Information',
        icon: 'FileText',
        fields: sortedGeneral,
        collapsed: false,
      })
      for (const f of sortedGeneral) {
        assigned.add(f.name)
      }
    }

    // 3. Associations section
    const associationFields = allFields.filter(f => {
      if (assigned.has(f.name)) return false
      if (isSystemField(f.name)) return false
      return fkSet.has(f.name)
    })

    if (associationFields.length > 0) {
      result.push({
        id: 'associations',
        title: 'Associations',
        icon: 'Link2',
        fields: associationFields,
        collapsed: false,
      })
      for (const f of associationFields) {
        assigned.add(f.name)
      }
    }

    // 4. Additional Details section - any remaining non-system fields
    const remainingFields = allFields.filter(f => {
      if (assigned.has(f.name)) return false
      if (isSystemField(f.name)) return false
      return true
    })

    if (remainingFields.length > 0) {
      result.push({
        id: 'additional',
        title: 'Additional Details',
        icon: 'MoreHorizontal',
        fields: remainingFields,
        collapsed: false,
      })
    }

    return result
  })

  // Dirty tracking
  const dirtyFields = computed<Set<string>>(() => {
    const dirty = new Set<string>()
    for (const field of metadata.fields) {
      const current = formData.value[field.name]
      const original = snapshot.value[field.name]
      if (!deepEqual(current, original)) {
        dirty.add(field.name)
      }
    }
    return dirty
  })

  const isDirty = computed(() => dirtyFields.value.size > 0)

  // Validation — raw errors (always computed, used internally for submit gating)
  const allFieldErrors = computed<Record<string, string>>(() => {
    if (mode === 'display') return {}
    const errors: Record<string, string> = {}
    for (const field of metadata.fields) {
      if (isSystemField(field.name)) continue
      // Skip UUID keys in create mode (auto-generated)
      if (mode === 'create' && keySet.has(field.name) && field.type === 'UUID') continue
      // Skip computed/readonly fields
      if (field.isComputed || field.isReadOnly) continue

      const value = formData.value[field.name]

      // Required check
      if (field.isRequired) {
        if (value === null || value === undefined || value === '') {
          const displayName = field.displayName || field.name
          errors[field.name] = `${displayName} is required`
          continue
        }
      }

      // MaxLength check for strings
      if (field.maxLength && typeof value === 'string' && value.length > field.maxLength) {
        const displayName = field.displayName || field.name
        errors[field.name] = `${displayName} exceeds maximum length of ${field.maxLength}`
      }
    }
    return errors
  })

  // Visible errors — only show for touched fields or after a submit attempt
  const fieldErrors = computed<Record<string, string>>(() => {
    if (submitAttempted.value) return allFieldErrors.value
    const errors: Record<string, string> = {}
    for (const [fieldName, message] of Object.entries(allFieldErrors.value)) {
      if (touchedFields.value.has(fieldName)) {
        errors[fieldName] = message
      }
    }
    return errors
  })

  // Section error counts
  const sectionErrors = computed<Record<string, number>>(() => {
    const counts: Record<string, number> = {}
    const errorFieldNames = new Set(Object.keys(fieldErrors.value))
    for (const section of sections.value) {
      let count = 0
      for (const field of section.fields) {
        if (errorFieldNames.has(field.name)) count++
      }
      counts[section.id] = count
    }
    return counts
  })

  function updateField(field: string, value: unknown) {
    formData.value = { ...formData.value, [field]: value }
    touchedFields.value.add(field)
  }

  function resetForm() {
    const initial = buildInitialFormData()
    formData.value = { ...initial }
    snapshot.value = { ...initial }
    touchedFields.value.clear()
    submitAttempted.value = false
  }

  function validate(): { valid: boolean; errors: Record<string, string> } {
    submitAttempted.value = true
    const errors = allFieldErrors.value
    return { valid: Object.keys(errors).length === 0, errors }
  }

  function getSubmitData(): Record<string, unknown> {
    const data: Record<string, unknown> = {}
    for (const field of metadata.fields) {
      // Skip system fields
      if (isSystemField(field.name)) continue
      // Skip computed fields
      if (field.isComputed) continue
      // Skip readonly fields in edit mode
      if (mode === 'edit' && field.isReadOnly) continue
      // Skip immutable fields in edit mode (set-once: editable on create only)
      if (mode === 'edit') {
        const annotations = field.annotations ?? {}
        if (annotations['Org.OData.Core.V1.Immutable'] || annotations['Core.Immutable']) continue
      }
      // Skip UUID key fields in create mode (auto-generated)
      if (mode === 'create' && keySet.has(field.name) && field.type === 'UUID') continue

      const value = formData.value[field.name]
      if (value !== undefined) {
        data[field.name] = value
      }
    }
    return data
  }

  // Watch for external initialData changes
  watch(
    () => initialData,
    (newData) => {
      if (newData) {
        const updated = buildInitialFormData()
        formData.value = { ...updated }
        snapshot.value = { ...updated }
      }
    },
    { deep: true }
  )

  // Field width map from layout override
  const fieldWidthMap = computed<Map<string, 'full' | 'half' | 'third'>>(() => {
    const activeLayout = getLayout()
    const map = new Map<string, 'full' | 'half' | 'third'>()
    if (!activeLayout) return map
    for (const section of activeLayout.sections) {
      for (const field of section.fields) {
        if (field.visible) map.set(field.name, field.width)
      }
    }
    return map
  })

  return {
    sections,
    fieldWidthMap,
    formData,
    isDirty,
    dirtyFields,
    fieldErrors,
    sectionErrors,
    updateField,
    resetForm,
    validate,
    getSubmitData,
  }
}
