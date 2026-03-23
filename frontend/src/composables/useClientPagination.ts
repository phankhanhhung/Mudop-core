import { ref, computed } from 'vue'
import type { Ref } from 'vue'

export function useClientPagination<T>(items: Ref<T[]>, defaultPageSize = 20) {
  const currentPage = ref(1)
  const pageSize = ref(defaultPageSize)

  const totalPages = computed(() => Math.max(1, Math.ceil(items.value.length / pageSize.value)))
  const paginatedItems = computed(() => {
    const start = (currentPage.value - 1) * pageSize.value
    return items.value.slice(start, start + pageSize.value)
  })

  function setPage(page: number) {
    currentPage.value = Math.max(1, Math.min(page, totalPages.value || 1))
  }

  function reset() {
    currentPage.value = 1
  }

  return { currentPage, pageSize, totalPages, paginatedItems, setPage, reset }
}
