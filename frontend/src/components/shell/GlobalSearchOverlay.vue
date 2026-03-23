<script setup lang="ts">
import { ref, computed, watch, nextTick, type Component } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useMetadataStore } from '@/stores/metadata'
import { odataService } from '@/services/odataService'
import { Badge } from '@/components/ui/badge'
import {
  LayoutDashboard,
  Building2,
  Package,
  FileCode2,
  Layers,
  Zap,
  Users,
  Hash,
  Shield,
  ScrollText,
  Settings,
  Database,
  Search,
  X,
  Clock,
  Book,
  FileSearch
} from 'lucide-vue-next'

const props = defineProps<{ open: boolean }>()
const emit = defineEmits<{ (e: 'close'): void }>()

const { t } = useI18n()
const router = useRouter()
const metadataStore = useMetadataStore()

const query = ref('')
const selectedIndex = ref(0)
const inputRef = ref<HTMLInputElement | null>(null)
const isSearchingRecords = ref(false)
const recordResults = ref<SearchResult[]>([])
let debounceTimer: ReturnType<typeof setTimeout> | null = null

// Recent searches in localStorage
const RECENT_KEY = 'bmmdl_recent_searches'
const MAX_RECENT = 20

interface SearchResult {
  id: string
  label: string
  sublabel: string
  group: string
  path: string
  icon: Component
  category: 'page' | 'entity' | 'record' | 'admin' | 'recent'
}

function loadRecentSearches(): string[] {
  try {
    const raw = localStorage.getItem(RECENT_KEY)
    if (!raw) return []
    const parsed = JSON.parse(raw) as string[]
    return Array.isArray(parsed) ? parsed.slice(0, MAX_RECENT) : []
  } catch {
    return []
  }
}

function saveRecentSearch(term: string): void {
  if (!term.trim()) return
  try {
    const recent = loadRecentSearches().filter((s) => s !== term)
    recent.unshift(term)
    localStorage.setItem(RECENT_KEY, JSON.stringify(recent.slice(0, MAX_RECENT)))
  } catch {
    // Silently ignore
  }
}

// Static page results
const pageResults = computed<SearchResult[]>(() => [
  { id: 'p-dashboard', label: t('sidebar.dashboard'), sublabel: '/dashboard', group: t('shell.search.pages'), path: '/dashboard', icon: LayoutDashboard, category: 'page' },
  { id: 'p-tenants', label: t('sidebar.tenants'), sublabel: '/tenants', group: t('shell.search.pages'), path: '/tenants', icon: Building2, category: 'page' },
  { id: 'p-launchpad', label: t('shell.launchpad.title'), sublabel: '/launchpad', group: t('shell.search.pages'), path: '/launchpad', icon: LayoutDashboard, category: 'page' },
  { id: 'p-settings', label: t('sidebar.settings'), sublabel: '/settings', group: t('shell.search.pages'), path: '/settings', icon: Settings, category: 'page' }
])

// Admin results
const adminResults = computed<SearchResult[]>(() => [
  { id: 'a-modules', label: t('sidebar.modules'), sublabel: t('shell.search.adminArea'), group: t('shell.search.admin'), path: '/admin/modules', icon: Package, category: 'admin' },
  { id: 'a-metadata', label: t('sidebar.metadata'), sublabel: t('shell.search.adminArea'), group: t('shell.search.admin'), path: '/admin/metadata', icon: FileCode2, category: 'admin' },
  { id: 'a-batch', label: t('sidebar.batch'), sublabel: t('shell.search.adminArea'), group: t('shell.search.admin'), path: '/admin/batch', icon: Layers, category: 'admin' },
  { id: 'a-actions', label: t('sidebar.actions'), sublabel: t('shell.search.adminArea'), group: t('shell.search.admin'), path: '/admin/actions', icon: Zap, category: 'admin' },
  { id: 'a-users', label: t('sidebar.users'), sublabel: t('shell.search.adminArea'), group: t('shell.search.admin'), path: '/admin/users', icon: Users, category: 'admin' },
  { id: 'a-sequences', label: t('sidebar.sequences'), sublabel: t('shell.search.adminArea'), group: t('shell.search.admin'), path: '/admin/sequences', icon: Hash, category: 'admin' },
  { id: 'a-roles', label: t('sidebar.roles'), sublabel: t('shell.search.adminArea'), group: t('shell.search.admin'), path: '/admin/roles', icon: Shield, category: 'admin' },
  { id: 'a-audit', label: t('sidebar.auditLog'), sublabel: t('shell.search.adminArea'), group: t('shell.search.admin'), path: '/admin/audit', icon: ScrollText, category: 'admin' },
  { id: 'a-api-docs', label: t('sidebar.apiDocs'), sublabel: t('shell.search.adminArea'), group: t('shell.search.admin'), path: '/admin/api-docs', icon: Book, category: 'admin' }
])

// Entity type results
const entityResults = computed<SearchResult[]>(() => {
  const items: SearchResult[] = []
  for (const mod of metadataStore.modules) {
    for (const service of mod.services) {
      for (const entity of service.entities) {
        items.push({
          id: `e-${mod.name}-${entity.entityType}`,
          label: entity.name,
          sublabel: `${mod.name} / ${entity.entityType}`,
          group: t('shell.search.entities'),
          path: `/odata/${mod.name}/${entity.entityType}`,
          icon: Database,
          category: 'entity'
        })
      }
    }
  }
  return items
})

// Parse "in:EntityType" scoped search
const scopeMatch = computed(() => {
  const match = query.value.match(/^in:(\S+)\s*(.*)/i)
  if (!match) return null
  return { entityScope: match[1], searchTerm: match[2] }
})

// Debounced OData $search for entity records
watch(query, (newQuery) => {
  if (debounceTimer) clearTimeout(debounceTimer)
  recordResults.value = []

  const trimmed = newQuery.trim()
  if (trimmed.length < 2) {
    isSearchingRecords.value = false
    return
  }

  debounceTimer = setTimeout(async () => {
    await searchRecords(trimmed)
  }, 300)
})

async function searchRecords(term: string): Promise<void> {
  isSearchingRecords.value = true
  const results: SearchResult[] = []

  try {
    // If scoped search (in:EntityType), search only that entity
    if (scopeMatch.value) {
      const { entityScope, searchTerm } = scopeMatch.value
      if (!searchTerm.trim()) {
        isSearchingRecords.value = false
        return
      }
      const entity = entityResults.value.find(
        (e) => e.label.toLowerCase() === entityScope.toLowerCase()
      )
      if (entity) {
        const moduleName = entity.sublabel.split(' / ')[0]
        const entityType = entity.sublabel.split(' / ')[1]
        try {
          const response = await odataService.query<Record<string, unknown>>(
            moduleName,
            entityType,
            { $search: searchTerm.trim(), $top: 5, $count: false }
          )
          for (const item of response.value) {
            const itemId = String(item['Id'] ?? item['ID'] ?? item['id'] ?? '')
            const displayName = String(
              item['Name'] ?? item['name'] ?? item['Title'] ?? item['title'] ?? itemId
            )
            results.push({
              id: `r-${moduleName}-${entityType}-${itemId}`,
              label: displayName,
              sublabel: `${entity.label} - ${itemId.substring(0, 8)}`,
              group: t('shell.search.records'),
              path: `/odata/${moduleName}/${entityType}/${itemId}`,
              icon: FileSearch,
              category: 'record'
            })
          }
        } catch {
          // Silently ignore search errors for individual entities
        }
      }
    } else {
      // Search across all entities (limited)
      const entitiesToSearch = entityResults.value.slice(0, 5)
      const promises = entitiesToSearch.map(async (entity) => {
        const moduleName = entity.sublabel.split(' / ')[0]
        const entityType = entity.sublabel.split(' / ')[1]
        try {
          const response = await odataService.query<Record<string, unknown>>(
            moduleName,
            entityType,
            { $search: term, $top: 3, $count: false }
          )
          for (const item of response.value) {
            const itemId = String(item['Id'] ?? item['ID'] ?? item['id'] ?? '')
            const displayName = String(
              item['Name'] ?? item['name'] ?? item['Title'] ?? item['title'] ?? itemId
            )
            results.push({
              id: `r-${moduleName}-${entityType}-${itemId}`,
              label: displayName,
              sublabel: `${entity.label} - ${itemId.substring(0, 8)}`,
              group: t('shell.search.records'),
              path: `/odata/${moduleName}/${entityType}/${itemId}`,
              icon: FileSearch,
              category: 'record'
            })
          }
        } catch {
          // Silently ignore search errors
        }
      })
      await Promise.allSettled(promises)
    }
  } finally {
    isSearchingRecords.value = false
    recordResults.value = results
  }
}

// All static results combined
const allStaticResults = computed(() => [
  ...pageResults.value,
  ...entityResults.value,
  ...adminResults.value
])

// Filtered results
const filteredResults = computed<SearchResult[]>(() => {
  const q = query.value.toLowerCase().trim()

  // If scoped search, only show record results
  if (scopeMatch.value) {
    return recordResults.value
  }

  if (!q) {
    // Show recent searches as suggestions
    const recent = loadRecentSearches()
    if (recent.length > 0) {
      return recent.slice(0, 5).map((term, idx) => ({
        id: `recent-${idx}`,
        label: term,
        sublabel: t('shell.search.recentSearch'),
        group: t('shell.search.recentSearches'),
        path: '',
        icon: Clock,
        category: 'recent' as const
      }))
    }
    return allStaticResults.value
  }

  const staticFiltered = allStaticResults.value.filter(
    (item) =>
      item.label.toLowerCase().includes(q) ||
      item.sublabel.toLowerCase().includes(q)
  )

  return [...staticFiltered, ...recordResults.value]
})

// Grouped results
const groupedResults = computed(() => {
  const groups = new Map<string, SearchResult[]>()
  for (const item of filteredResults.value) {
    if (!groups.has(item.group)) {
      groups.set(item.group, [])
    }
    groups.get(item.group)!.push(item)
  }
  return groups
})

function getCategoryVariant(category: string): 'default' | 'secondary' | 'outline' | 'destructive' {
  switch (category) {
    case 'page': return 'default'
    case 'entity': return 'secondary'
    case 'admin': return 'outline'
    case 'record': return 'destructive'
    default: return 'secondary'
  }
}

// Reset on open/close
watch(
  () => props.open,
  (isOpen) => {
    if (isOpen) {
      query.value = ''
      selectedIndex.value = 0
      recordResults.value = []
      nextTick(() => inputRef.value?.focus())
    }
  }
)

watch(query, () => {
  selectedIndex.value = 0
})

function selectItem(item: SearchResult): void {
  if (item.category === 'recent') {
    // Use the recent search term as new query
    query.value = item.label
    return
  }
  saveRecentSearch(query.value)
  router.push(item.path)
  emit('close')
}

function handleKeydown(e: KeyboardEvent): void {
  const total = filteredResults.value.length
  if (!total) return

  if (e.key === 'ArrowDown') {
    e.preventDefault()
    selectedIndex.value = (selectedIndex.value + 1) % total
  } else if (e.key === 'ArrowUp') {
    e.preventDefault()
    selectedIndex.value = (selectedIndex.value - 1 + total) % total
  } else if (e.key === 'Enter') {
    e.preventDefault()
    const item = filteredResults.value[selectedIndex.value]
    if (item) selectItem(item)
  }
}

function getFlatIndex(item: SearchResult): number {
  return filteredResults.value.indexOf(item)
}
</script>

<template>
  <Teleport to="body">
    <Transition name="search-fade">
      <div
        v-if="open"
        class="fixed inset-0 z-[100] flex items-start justify-center pt-[15vh]"
        @click.self="emit('close')"
        @keydown.escape="emit('close')"
      >
        <!-- Backdrop -->
        <div class="fixed inset-0 bg-black/50 backdrop-blur-sm" aria-hidden="true" @click="emit('close')" />

        <!-- Dialog -->
        <div
          class="relative z-10 w-full max-w-2xl overflow-hidden rounded-xl border bg-background shadow-2xl"
          role="dialog"
          :aria-label="t('shell.search.title')"
          @keydown="handleKeydown"
        >
          <!-- Search input -->
          <div class="flex items-center border-b px-4">
            <Search class="h-4 w-4 shrink-0 text-muted-foreground" aria-hidden="true" />
            <input
              ref="inputRef"
              v-model="query"
              type="text"
              class="flex h-14 w-full bg-transparent px-3 text-sm outline-none placeholder:text-muted-foreground"
              :placeholder="t('shell.search.placeholder')"
            />
            <div v-if="isSearchingRecords" class="shrink-0 mr-2">
              <div class="h-4 w-4 animate-spin rounded-full border-2 border-muted-foreground border-t-transparent" />
            </div>
            <button
              class="shrink-0 rounded-sm p-1 hover:bg-muted"
              :aria-label="t('shell.search.close')"
              @click="emit('close')"
            >
              <X class="h-4 w-4" aria-hidden="true" />
            </button>
          </div>

          <!-- Scope hint -->
          <div v-if="!scopeMatch && entityResults.length > 0" class="border-b px-4 py-1.5 text-xs text-muted-foreground">
            {{ t('shell.search.scopeHint') }}
          </div>

          <!-- Results -->
          <div class="max-h-96 overflow-y-auto p-2">
            <template v-if="filteredResults.length === 0 && !isSearchingRecords">
              <div class="px-4 py-8 text-center text-sm text-muted-foreground">
                {{ t('shell.search.noResults') }}
              </div>
            </template>
            <template v-else>
              <template v-for="[group, items] in groupedResults" :key="group">
                <div class="px-2 py-1.5 text-xs font-semibold text-muted-foreground">
                  {{ group }}
                </div>
                <button
                  v-for="(item, itemIdx) in items"
                  :key="item.id"
                  :class="[
                    'flex w-full items-center gap-3 rounded-md px-3 py-2.5 text-sm transition-colors',
                    getFlatIndex(item) === selectedIndex ? 'bg-accent text-accent-foreground' : 'hover:bg-muted'
                  ]"
                  :style="{ animationDelay: `${itemIdx * 30}ms` }"
                  class="animate-in fade-in-0"
                  @click="selectItem(item)"
                  @mouseenter="selectedIndex = getFlatIndex(item)"
                >
                  <component :is="item.icon" class="h-4 w-4 shrink-0 text-muted-foreground" aria-hidden="true" />
                  <div class="flex-1 min-w-0 text-left">
                    <span class="font-medium">{{ item.label }}</span>
                    <span class="ml-2 text-xs text-muted-foreground">{{ item.sublabel }}</span>
                  </div>
                  <Badge
                    :variant="getCategoryVariant(item.category)"
                    class="shrink-0 text-[10px] px-1.5 py-0"
                  >
                    {{ item.category }}
                  </Badge>
                </button>
              </template>
            </template>

            <!-- Searching indicator -->
            <div v-if="isSearchingRecords && recordResults.length === 0" class="px-4 py-4 text-center text-xs text-muted-foreground">
              {{ t('shell.search.searchingRecords') }}
            </div>
          </div>

          <!-- Footer hint -->
          <div class="flex items-center justify-between border-t px-4 py-2 text-xs text-muted-foreground">
            <div class="flex items-center gap-3">
              <span>
                <kbd class="rounded border bg-muted px-1.5 py-0.5 font-mono text-[10px]">&uarr;&darr;</kbd>
                {{ t('shell.search.navigate') }}
              </span>
              <span>
                <kbd class="rounded border bg-muted px-1.5 py-0.5 font-mono text-[10px]">&crarr;</kbd>
                {{ t('shell.search.select') }}
              </span>
            </div>
            <span>
              <kbd class="rounded border bg-muted px-1.5 py-0.5 font-mono text-[10px]">Esc</kbd>
              {{ t('shell.search.close') }}
            </span>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.search-fade-enter-active,
.search-fade-leave-active {
  transition: opacity 0.15s ease;
}
.search-fade-enter-from,
.search-fade-leave-to {
  opacity: 0;
}

.animate-in {
  animation: fadeSlideIn 0.15s ease-out both;
}

@keyframes fadeSlideIn {
  from {
    opacity: 0;
    transform: translateY(-4px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
</style>
