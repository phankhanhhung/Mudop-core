import { ref, watch, type Ref } from 'vue'
import { layoutService, type SavedLayout } from '@/services/layoutService'
import type { FormLayoutSettings } from '@/types/formLayout'

export function useFormLayout(
  namespace: Ref<string>,
  entityName: Ref<string>,
) {
  const layouts = ref<SavedLayout[]>([])
  const selectedLayoutId = ref<string | null>(null)
  const selectedLayout = ref<FormLayoutSettings | null>(null)
  const isLoading = ref(false)

  async function loadLayouts() {
    if (!namespace.value || !entityName.value) return
    isLoading.value = true
    try {
      layouts.value = await layoutService.listLayouts(namespace.value, entityName.value)
      // Auto-select the first layout if available
      if (layouts.value.length > 0 && !selectedLayoutId.value) {
        selectLayout(layouts.value[0].id)
      }
    } catch {
      layouts.value = []
    } finally {
      isLoading.value = false
    }
  }

  function selectLayout(id: string | null) {
    selectedLayoutId.value = id
    if (!id) {
      selectedLayout.value = null
      return
    }
    const found = layouts.value.find((l) => l.id === id)
    selectedLayout.value = found?.settings ?? null
  }

  function clearLayout() {
    selectedLayoutId.value = null
    selectedLayout.value = null
  }

  watch([namespace, entityName], () => {
    layouts.value = []
    selectedLayoutId.value = null
    selectedLayout.value = null
    loadLayouts()
  }, { immediate: true })

  return {
    layouts,
    selectedLayoutId,
    selectedLayout,
    isLoading,
    loadLayouts,
    selectLayout,
    clearLayout,
  }
}
