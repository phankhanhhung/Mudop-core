import { ref, computed } from 'vue'
import type { Ref } from 'vue'
import type { AssociationMetadata, EntityMetadata } from '@/types/metadata'
import { useMetadataStore } from '@/stores/metadata'

export function useCompositionLoader(
  module: Ref<string>,
  metadata: Ref<EntityMetadata | null>
) {
  const metadataStore = useMetadataStore()
  const compositionMeta = ref<Map<string, EntityMetadata>>(new Map())

  const compositionAssociations = computed<AssociationMetadata[]>(() =>
    metadata.value?.associations.filter(
      (a) => a.isComposition && (a.cardinality === 'Many' || a.cardinality === 'OneOrMore')
    ) ?? []
  )

  async function loadCompositionMetadata(): Promise<void> {
    for (const comp of compositionAssociations.value) {
      const target = comp.targetEntity
      const lastDot = target.lastIndexOf('.')
      const targetModule = lastDot >= 0 ? target.substring(0, lastDot) : module.value
      const targetEntity = lastDot >= 0 ? target.substring(lastDot + 1) : target
      try {
        const meta = await metadataStore.fetchEntity(targetModule, targetEntity)
        compositionMeta.value.set(comp.name, meta)
      } catch {
        // Skip compositions with missing metadata
      }
    }
  }

  return { compositionAssociations, compositionMeta, loadCompositionMetadata }
}
