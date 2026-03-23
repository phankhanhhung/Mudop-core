import { ref, watch } from 'vue'

export interface RecentItem {
  module: string
  entity: string
  entityType: string
  id: string
  title: string
  visitedAt: string
}

const STORAGE_KEY = 'bmmdl_recent_items'
const MAX_ITEMS = 20

function loadFromStorage(): RecentItem[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return []
    const parsed = JSON.parse(raw) as RecentItem[]
    return Array.isArray(parsed) ? parsed.slice(0, MAX_ITEMS) : []
  } catch {
    return []
  }
}

function saveToStorage(items: RecentItem[]): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(items))
  } catch {
    // localStorage full or unavailable — silently ignore
  }
}

const recentItems = ref<RecentItem[]>(loadFromStorage())

// Persist whenever the list changes
watch(recentItems, (items) => {
  saveToStorage(items)
}, { deep: true })

export function useRecentItems() {
  function addRecentItem(
    module: string,
    entity: string,
    entityType: string,
    id: string,
    title: string
  ): void {
    const now = new Date().toISOString()

    // Remove existing duplicate (same module + entityType + id)
    const filtered = recentItems.value.filter(
      (item) =>
        !(item.module === module && item.entityType === entityType && item.id === id)
    )

    // Prepend the new item
    filtered.unshift({
      module,
      entity,
      entityType,
      id,
      title,
      visitedAt: now
    })

    // Cap at MAX_ITEMS
    recentItems.value = filtered.slice(0, MAX_ITEMS)
  }

  function clearRecentItems(): void {
    recentItems.value = []
    localStorage.removeItem(STORAGE_KEY)
  }

  return {
    recentItems,
    addRecentItem,
    clearRecentItems
  }
}
