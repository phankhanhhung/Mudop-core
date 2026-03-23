<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useUiStore } from '@/stores/ui'
import { useMetadataStore } from '@/stores/metadata'
import { usePluginRegistry, getPluginMenuItems } from '@/plugins'
import type { PluginManifest } from '@/services/pluginService'
import { cn } from '@/lib/utils'
import {
  LayoutDashboard,
  LayoutGrid,
  LayoutTemplate,
  Building2,
  Database,
  Settings,
  Package,
  ChevronLeft,
  ChevronRight,
  FileCode2,
  Layers,
  Zap,
  Users,
  Hash,
  Shield,
  Book,
  Palette,
  Blocks,
} from 'lucide-vue-next'

useI18n()
const route = useRoute()
const router = useRouter()
const uiStore = useUiStore()
const metadataStore = useMetadataStore()
const { loadManifest, manifest } = usePluginRegistry()

interface NavItem {
  nameKey: string
  path: string
  icon: typeof LayoutDashboard
  requiresTenant?: boolean
  isLiteral?: boolean  // If true, nameKey is used as literal label (not i18n key)
}

const mainNavItems: NavItem[] = [
  { nameKey: 'sidebar.launchpad', path: '/launchpad', icon: LayoutGrid },
  { nameKey: 'sidebar.dashboard', path: '/dashboard', icon: LayoutDashboard },
  { nameKey: 'sidebar.tenants', path: '/tenants', icon: Building2 }
]

const toolsNavItems: NavItem[] = [
  { nameKey: 'sidebar.showcase', path: '/showcase', icon: Palette },
  { nameKey: 'sidebar.advancedFields', path: '/showcase/advanced-fields', icon: Layers }
]

const coreAdminNavItems: NavItem[] = [
  { nameKey: 'sidebar.modules', path: '/admin/modules', icon: Package },
  { nameKey: 'sidebar.metadata', path: '/admin/metadata', icon: FileCode2 },
  { nameKey: 'sidebar.batch', path: '/admin/batch', icon: Layers },
  { nameKey: 'sidebar.actions', path: '/admin/actions', icon: Zap },
  { nameKey: 'sidebar.users', path: '/admin/users', icon: Users },
  { nameKey: 'sidebar.sequences', path: '/admin/sequences', icon: Hash },
  { nameKey: 'sidebar.roles', path: '/admin/roles', icon: Shield },
  { nameKey: 'sidebar.apiDocs', path: '/admin/api-docs', icon: Book },
  { nameKey: 'admin.formDesigner.title', path: '/admin/form-designer', icon: LayoutTemplate },
  { nameKey: 'sidebar.plugins', path: '/admin/plugins', icon: Blocks },
]

// Merge core admin items with plugin-contributed admin menu items
const adminNavItems = computed<NavItem[]>(() => {
  if (!manifest.value) return coreAdminNavItems
  const pluginItems = getPluginMenuItems(manifest.value as PluginManifest).filter(i => i.section === 'admin')
  const mapped: NavItem[] = pluginItems.map(item => ({
    nameKey: item.label,
    path: item.route,
    icon: Database, // Default icon for plugin menu items
    isLiteral: true,
  }))
  return [...coreAdminNavItems, ...mapped]
})

// Plugin-contributed main section items
const pluginMainNavItems = computed<NavItem[]>(() => {
  if (!manifest.value) return []
  const pluginItems = getPluginMenuItems(manifest.value as PluginManifest).filter(i => i.section === 'main')
  return pluginItems.map(item => ({
    nameKey: item.label,
    path: item.route,
    icon: Database,
    isLiteral: true,
  }))
})

// Plugin-contributed system section items
const systemNavItems = computed<NavItem[]>(() => {
  if (!manifest.value) return []
  const pluginItems = getPluginMenuItems(manifest.value as PluginManifest)
    .filter(i => i.section === 'system')
  return pluginItems.map(item => ({
    nameKey: item.label,
    path: item.route,
    icon: Database,
    isLiteral: true,
  }))
})

// All main items (core + plugin)
const allMainNavItems = computed<NavItem[]>(() => [
  ...mainNavItems,
  ...pluginMainNavItems.value,
])

// Load plugin manifest on mount (non-blocking)
onMounted(async () => {
  try {
    await loadManifest()
  } catch {
    // Plugin manifest is optional — continue without it
  }
})

const bottomNavItems: NavItem[] = [
  { nameKey: 'sidebar.settings', path: '/settings', icon: Settings }
]

// Generate entity nav items from metadata
const entityNavItems = computed(() => {
  if (!metadataStore.hasModules) {
    return []
  }

  const items: NavItem[] = []
  for (const module of metadataStore.modules) {
    for (const service of module.services) {
      for (const entity of service.entities) {
        items.push({
          nameKey: entity.name,
          path: `/odata/${module.name}/${entity.entityType}`,
          icon: Database
        })
      }
    }
  }
  return items
})

function isActive(path: string): boolean {
  return route.path === path || route.path.startsWith(path + '/')
}

function navigate(path: string) {
  router.push(path)
  uiStore.closeMobileSidebar()
}

const sidebarClass = computed(() =>
  cn(
    'fixed inset-y-0 left-0 z-50 flex flex-col border-r bg-background transition-all duration-300',
    uiStore.sidebarCollapsed ? 'w-16' : 'w-64',
    uiStore.sidebarMobileOpen
      ? 'translate-x-0'
      : '-translate-x-full lg:translate-x-0'
  )
)
</script>

<template>
  <aside :class="sidebarClass" role="complementary" aria-label="Sidebar">
    <!-- Logo -->
    <div class="flex h-16 items-center justify-between border-b px-4">
      <span v-if="!uiStore.sidebarCollapsed" class="text-xl font-bold">BMMDL</span>
      <span v-else class="text-xl font-bold">B</span>
    </div>

    <!-- Navigation -->
    <nav class="flex-1 overflow-y-auto p-2" :aria-label="$t('accessibility.mainNavigation')">
      <!-- Main navigation -->
      <div class="space-y-1" role="list">
        <button
          v-for="item in allMainNavItems"
          :key="item.path"
          role="listitem"
          :aria-label="uiStore.sidebarCollapsed ? (item.isLiteral ? item.nameKey : $t(item.nameKey)) : undefined"
          :title="uiStore.sidebarCollapsed ? (item.isLiteral ? item.nameKey : $t(item.nameKey)) : undefined"
          :aria-current="isActive(item.path) ? 'page' : undefined"
          :class="cn(
            'flex w-full items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors',
            isActive(item.path)
              ? 'bg-primary text-primary-foreground'
              : 'hover:bg-muted'
          )"
          @click="navigate(item.path)"
        >
          <component :is="item.icon" class="h-5 w-5 shrink-0" aria-hidden="true" />
          <span v-if="!uiStore.sidebarCollapsed">{{ item.isLiteral ? item.nameKey : $t(item.nameKey) }}</span>
        </button>
      </div>

      <!-- Admin navigation -->
      <div class="mt-6" role="group" :aria-label="$t('accessibility.adminNavigation')">
        <div
          v-if="!uiStore.sidebarCollapsed"
          class="px-3 py-2 text-xs font-semibold uppercase text-muted-foreground"
          aria-hidden="true"
        >
          {{ $t('sidebar.admin') }}
        </div>
        <div class="space-y-1" role="list">
          <button
            v-for="item in adminNavItems"
            :key="item.path"
            role="listitem"
            :aria-label="uiStore.sidebarCollapsed ? (item.isLiteral ? item.nameKey : $t(item.nameKey)) : undefined"
            :title="uiStore.sidebarCollapsed ? (item.isLiteral ? item.nameKey : $t(item.nameKey)) : undefined"
            :aria-current="isActive(item.path) ? 'page' : undefined"
            :class="cn(
              'flex w-full items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors',
              isActive(item.path)
                ? 'bg-primary text-primary-foreground'
                : 'hover:bg-muted'
            )"
            @click="navigate(item.path)"
          >
            <component :is="item.icon" class="h-5 w-5 shrink-0" aria-hidden="true" />
            <span v-if="!uiStore.sidebarCollapsed">{{ item.isLiteral ? item.nameKey : $t(item.nameKey) }}</span>
          </button>
        </div>
      </div>

      <!-- System section (plugin-contributed) -->
      <div v-if="systemNavItems.length > 0" class="mt-6" role="group" :aria-label="'System'">
        <div
          v-if="!uiStore.sidebarCollapsed"
          class="px-3 py-2 text-xs font-semibold uppercase text-muted-foreground"
          aria-hidden="true"
        >
          System
        </div>
        <div class="space-y-1" role="list">
          <button
            v-for="item in systemNavItems"
            :key="item.path"
            role="listitem"
            :aria-label="uiStore.sidebarCollapsed ? item.nameKey : undefined"
            :title="uiStore.sidebarCollapsed ? item.nameKey : undefined"
            :aria-current="isActive(item.path) ? 'page' : undefined"
            :class="cn(
              'flex w-full items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors',
              isActive(item.path)
                ? 'bg-primary text-primary-foreground'
                : 'hover:bg-muted'
            )"
            @click="navigate(item.path)"
          >
            <component :is="item.icon" class="h-5 w-5 shrink-0" aria-hidden="true" />
            <span v-if="!uiStore.sidebarCollapsed">{{ item.nameKey }}</span>
          </button>
        </div>
      </div>

      <!-- Tools navigation -->
      <div class="mt-6" role="group" :aria-label="'Tools'">
        <div
          v-if="!uiStore.sidebarCollapsed"
          class="px-3 py-2 text-xs font-semibold uppercase text-muted-foreground"
          aria-hidden="true"
        >
          Tools
        </div>
        <div class="space-y-1" role="list">
          <button
            v-for="item in toolsNavItems"
            :key="item.path"
            role="listitem"
            :aria-label="uiStore.sidebarCollapsed ? $t(item.nameKey) : undefined"
            :title="uiStore.sidebarCollapsed ? $t(item.nameKey) : undefined"
            :aria-current="isActive(item.path) ? 'page' : undefined"
            :class="cn(
              'flex w-full items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors',
              isActive(item.path)
                ? 'bg-primary text-primary-foreground'
                : 'hover:bg-muted'
            )"
            @click="navigate(item.path)"
          >
            <component :is="item.icon" class="h-5 w-5 shrink-0" aria-hidden="true" />
            <span v-if="!uiStore.sidebarCollapsed">{{ $t(item.nameKey) }}</span>
          </button>
        </div>
      </div>

      <!-- Entity navigation (when tenant selected) -->
      <div v-if="entityNavItems.length > 0" class="mt-6" role="group" :aria-label="$t('accessibility.entityNavigation')">
        <div
          v-if="!uiStore.sidebarCollapsed"
          class="px-3 py-2 text-xs font-semibold uppercase text-muted-foreground"
          aria-hidden="true"
        >
          {{ $t('sidebar.entities') }}
        </div>
        <div class="space-y-1" role="list">
          <button
            v-for="item in entityNavItems"
            :key="item.path"
            role="listitem"
            :aria-label="uiStore.sidebarCollapsed ? $t(item.nameKey) : undefined"
            :title="uiStore.sidebarCollapsed ? $t(item.nameKey) : undefined"
            :aria-current="isActive(item.path) ? 'page' : undefined"
            :class="cn(
              'flex w-full items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors',
              isActive(item.path)
                ? 'bg-primary text-primary-foreground'
                : 'hover:bg-muted'
            )"
            @click="navigate(item.path)"
          >
            <component :is="item.icon" class="h-5 w-5 shrink-0" aria-hidden="true" />
            <span v-if="!uiStore.sidebarCollapsed">{{ $t(item.nameKey) }}</span>
          </button>
        </div>
      </div>
    </nav>

    <!-- Bottom navigation -->
    <div class="border-t p-2">
      <div class="space-y-1">
        <button
          v-for="item in bottomNavItems"
          :key="item.path"
          :aria-label="uiStore.sidebarCollapsed ? (item.isLiteral ? item.nameKey : $t(item.nameKey)) : undefined"
          :title="uiStore.sidebarCollapsed ? (item.isLiteral ? item.nameKey : $t(item.nameKey)) : undefined"
          :aria-current="isActive(item.path) ? 'page' : undefined"
          :class="cn(
            'flex w-full items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors',
            isActive(item.path)
              ? 'bg-primary text-primary-foreground'
              : 'hover:bg-muted'
          )"
          @click="navigate(item.path)"
        >
          <component :is="item.icon" class="h-5 w-5 shrink-0" aria-hidden="true" />
          <span v-if="!uiStore.sidebarCollapsed">{{ $t(item.nameKey) }}</span>
        </button>
      </div>

      <!-- Collapse toggle (desktop) -->
      <button
        class="mt-2 hidden w-full items-center justify-center rounded-md p-2 hover:bg-muted lg:flex"
        :aria-label="uiStore.sidebarCollapsed ? $t('accessibility.expandSidebar') : $t('accessibility.collapseSidebar')"
        @click="uiStore.toggleSidebar"
      >
        <ChevronLeft v-if="!uiStore.sidebarCollapsed" class="h-5 w-5" aria-hidden="true" />
        <ChevronRight v-else class="h-5 w-5" aria-hidden="true" />
      </button>
    </div>
  </aside>
</template>
