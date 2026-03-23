import { computed } from 'vue'
import { useRoute } from 'vue-router'

function toDisplayName(name: string): string {
  // Split PascalCase into words: "SalesOrder" → "Sales Order"
  return name.replace(/([A-Z])/g, ' $1').trim()
}

export function useEntityRouteParams() {
  const route = useRoute()
  const module = computed(() => route.params.module as string)
  const entity = computed(() => route.params.entity as string)
  const entityId = computed(() => route.params.id as string | undefined)
  const displayName = computed(() => entity.value ? toDisplayName(entity.value) : '')
  return { module, entity, entityId, displayName }
}
