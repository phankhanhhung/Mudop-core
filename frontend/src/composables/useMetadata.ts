import { ref, computed, type Ref } from 'vue'
import { useMetadataStore } from '@/stores/metadata'
import type { EntityMetadata, FieldMetadata } from '@/types/metadata'

export interface UseMetadataOptions {
  module: string
  entity: string
  autoLoad?: boolean
}

export interface UseMetadataReturn {
  metadata: Ref<EntityMetadata | null>
  isLoading: Ref<boolean>
  error: Ref<string | null>
  fields: Ref<FieldMetadata[]>
  editableFields: Ref<FieldMetadata[]>
  requiredFields: Ref<FieldMetadata[]>
  keyFields: Ref<string[]>
  load: () => Promise<void>
  getField: (name: string) => FieldMetadata | undefined
  getFieldType: (name: string) => string | undefined
  isFieldRequired: (name: string) => boolean
  isFieldReadOnly: (name: string) => boolean
}

export function useMetadata(options: UseMetadataOptions): UseMetadataReturn {
  const { module, entity, autoLoad = true } = options
  const metadataStore = useMetadataStore()

  // State
  const metadata = ref<EntityMetadata | null>(null)
  const isLoading = ref(false)
  const error = ref<string | null>(null)

  // Computed
  const fields = computed(() => metadata.value?.fields ?? [])

  const editableFields = computed(() =>
    fields.value.filter((f) => !f.isReadOnly && !f.isComputed)
  )

  const requiredFields = computed(() =>
    fields.value.filter((f) => f.isRequired)
  )

  const keyFields = computed(() => metadata.value?.keys ?? [])

  // Actions
  async function load(): Promise<void> {
    // Check cache first
    const cached = metadataStore.getEntity(module, entity)
    if (cached) {
      metadata.value = cached
      return
    }

    isLoading.value = true
    error.value = null

    try {
      metadata.value = await metadataStore.fetchEntity(module, entity)
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to load metadata'
      throw e
    } finally {
      isLoading.value = false
    }
  }

  function getField(name: string): FieldMetadata | undefined {
    return fields.value.find((f) => f.name === name)
  }

  function getFieldType(name: string): string | undefined {
    return getField(name)?.type
  }

  function isFieldRequired(name: string): boolean {
    return getField(name)?.isRequired ?? false
  }

  function isFieldReadOnly(name: string): boolean {
    const field = getField(name)
    return (field?.isReadOnly || field?.isComputed) ?? false
  }

  // Auto-load on creation
  if (autoLoad) {
    load()
  }

  return {
    metadata,
    isLoading,
    error,
    fields,
    editableFields,
    requiredFields,
    keyFields,
    load,
    getField,
    getFieldType,
    isFieldRequired,
    isFieldReadOnly
  }
}
