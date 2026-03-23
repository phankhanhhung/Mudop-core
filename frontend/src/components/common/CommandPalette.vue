<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useMetadataStore } from '@/stores/metadata'
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
  X
} from 'lucide-vue-next'

const props = defineProps<{ open: boolean }>()
const emit = defineEmits<{ (e: 'close'): void }>()

const { t } = useI18n()
const router = useRouter()
const metadataStore = useMetadataStore()

const query = ref('')
const selectedIndex = ref(0)
const inputRef = ref<HTMLInputElement | null>(null)

interface CommandItem {
  id: string
  label: string
  group: string
  path: string
  icon: typeof LayoutDashboard
}

const pageItems = computed<CommandItem[]>(() => [
  { id: 'dashboard', label: t('sidebar.dashboard'), group: t('shortcuts.groups.pages'), path: '/dashboard', icon: LayoutDashboard },
  { id: 'tenants', label: t('sidebar.tenants'), group: t('shortcuts.groups.pages'), path: '/tenants', icon: Building2 },
  { id: 'modules', label: t('sidebar.modules'), group: t('shortcuts.groups.pages'), path: '/admin/modules', icon: Package },
  { id: 'metadata', label: t('sidebar.metadata'), group: t('shortcuts.groups.pages'), path: '/admin/metadata', icon: FileCode2 },
  { id: 'batch', label: t('sidebar.batch'), group: t('shortcuts.groups.pages'), path: '/admin/batch', icon: Layers },
  { id: 'actions', label: t('sidebar.actions'), group: t('shortcuts.groups.pages'), path: '/admin/actions', icon: Zap },
  { id: 'users', label: t('sidebar.users'), group: t('shortcuts.groups.pages'), path: '/admin/users', icon: Users },
  { id: 'sequences', label: t('sidebar.sequences'), group: t('shortcuts.groups.pages'), path: '/admin/sequences', icon: Hash },
  { id: 'roles', label: t('sidebar.roles'), group: t('shortcuts.groups.pages'), path: '/admin/roles', icon: Shield },
  { id: 'audit', label: t('sidebar.auditLog'), group: t('shortcuts.groups.pages'), path: '/admin/audit', icon: ScrollText },
  { id: 'settings', label: t('sidebar.settings'), group: t('shortcuts.groups.pages'), path: '/settings', icon: Settings }
])

const entityItems = computed<CommandItem[]>(() => {
  const items: CommandItem[] = []
  for (const mod of metadataStore.modules) {
    for (const service of mod.services) {
      for (const entity of service.entities) {
        items.push({
          id: `entity-${mod.name}-${entity.entityType}`,
          label: entity.name,
          group: t('shortcuts.groups.entities'),
          path: `/odata/${mod.name}/${entity.entityType}`,
          icon: Database
        })
      }
    }
  }
  return items
})

const allItems = computed(() => [...pageItems.value, ...entityItems.value])

const filteredItems = computed(() => {
  const q = query.value.toLowerCase().trim()
  if (!q) return allItems.value
  return allItems.value.filter((item) => item.label.toLowerCase().includes(q))
})

const groupedItems = computed(() => {
  const groups = new Map<string, CommandItem[]>()
  for (const item of filteredItems.value) {
    if (!groups.has(item.group)) {
      groups.set(item.group, [])
    }
    groups.get(item.group)!.push(item)
  }
  return groups
})

watch(
  () => props.open,
  (isOpen) => {
    if (isOpen) {
      query.value = ''
      selectedIndex.value = 0
      nextTick(() => inputRef.value?.focus())
    }
  }
)

watch(query, () => {
  selectedIndex.value = 0
})

function selectItem(item: CommandItem) {
  router.push(item.path)
  emit('close')
}

function handleKeydown(e: KeyboardEvent) {
  const total = filteredItems.value.length
  if (!total) return

  if (e.key === 'ArrowDown') {
    e.preventDefault()
    selectedIndex.value = (selectedIndex.value + 1) % total
  } else if (e.key === 'ArrowUp') {
    e.preventDefault()
    selectedIndex.value = (selectedIndex.value - 1 + total) % total
  } else if (e.key === 'Enter') {
    e.preventDefault()
    const item = filteredItems.value[selectedIndex.value]
    if (item) selectItem(item)
  }
}

function getFlatIndex(item: CommandItem): number {
  return filteredItems.value.indexOf(item)
}
</script>

<template>
  <Teleport to="body">
    <Transition name="fade">
      <div
        v-if="open"
        class="fixed inset-0 z-[100] flex items-start justify-center pt-[20vh]"
        @click.self="emit('close')"
        @keydown.escape="emit('close')"
      >
        <!-- Backdrop -->
        <div class="fixed inset-0 bg-black/50" aria-hidden="true" @click="emit('close')" />

        <!-- Dialog -->
        <div
          class="relative z-10 w-full max-w-lg overflow-hidden rounded-xl border bg-background shadow-2xl"
          role="dialog"
          :aria-label="t('shortcuts.commandPalette')"
          @keydown="handleKeydown"
        >
          <!-- Search input -->
          <div class="flex items-center border-b px-4">
            <Search class="h-4 w-4 shrink-0 text-muted-foreground" aria-hidden="true" />
            <input
              ref="inputRef"
              v-model="query"
              type="text"
              class="flex h-12 w-full bg-transparent px-3 text-sm outline-none placeholder:text-muted-foreground"
              :placeholder="t('shortcuts.searchPlaceholder')"
            />
            <button
              class="shrink-0 rounded-sm p-1 hover:bg-muted"
              :aria-label="t('common.cancel')"
              @click="emit('close')"
            >
              <X class="h-4 w-4" aria-hidden="true" />
            </button>
          </div>

          <!-- Results -->
          <div class="max-h-72 overflow-y-auto p-2">
            <template v-if="filteredItems.length === 0">
              <div class="px-4 py-8 text-center text-sm text-muted-foreground">
                {{ t('shortcuts.noResults') }}
              </div>
            </template>
            <template v-else>
              <template v-for="[group, items] in groupedItems" :key="group">
                <div class="px-2 py-1.5 text-xs font-semibold text-muted-foreground">
                  {{ group }}
                </div>
                <button
                  v-for="item in items"
                  :key="item.id"
                  class="flex w-full items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors"
                  :class="getFlatIndex(item) === selectedIndex ? 'bg-accent text-accent-foreground' : 'hover:bg-muted'"
                  @click="selectItem(item)"
                  @mouseenter="selectedIndex = getFlatIndex(item)"
                >
                  <component :is="item.icon" class="h-4 w-4 shrink-0 text-muted-foreground" aria-hidden="true" />
                  <span>{{ item.label }}</span>
                </button>
              </template>
            </template>
          </div>

          <!-- Footer hint -->
          <div class="flex items-center justify-between border-t px-4 py-2 text-xs text-muted-foreground">
            <span>{{ t('shortcuts.navigateHint') }}</span>
            <span>
              <kbd class="rounded border bg-muted px-1.5 py-0.5 font-mono text-[10px]">Esc</kbd>
              {{ t('shortcuts.toClose') }}
            </span>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.15s ease;
}
.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
