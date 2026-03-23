import { computed } from 'vue'
import type { ComputedRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'

export function useEntityNavigation(
  module: ComputedRef<string>,
  entity: ComputedRef<string>,
  entityId?: ComputedRef<string>
) {
  const route = useRoute()
  const router = useRouter()

  const parentFk = computed(() => route.query.parentFk as string | undefined)
  const parentId = computed(() => route.query.parentId as string | undefined)
  const parentEntity = computed(() => route.query.parentEntity as string | undefined)
  const parentModule = computed(() => route.query.parentModule as string | undefined)

  function goBack() {
    if (parentEntity.value && parentId.value) {
      const mod = parentModule.value || module.value
      router.push(`/odata/${mod}/${parentEntity.value}/${parentId.value}?_t=${Date.now()}`)
    } else if (entityId?.value) {
      router.push(`/odata/${module.value}/${entity.value}/${entityId.value}`)
    } else {
      router.push(`/odata/${module.value}/${entity.value}`)
    }
  }

  function goToList() {
    if (parentEntity.value && parentId.value) {
      const mod = parentModule.value || module.value
      router.push(`/odata/${mod}/${parentEntity.value}/${parentId.value}?_t=${Date.now()}`)
    } else {
      router.push(`/odata/${module.value}/${entity.value}`)
    }
  }

  return { parentFk, parentId, parentEntity, parentModule, goBack, goToList }
}
