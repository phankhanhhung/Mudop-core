<script setup lang="ts">
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { useMetadataStore } from '@/stores/metadata'
import { useDashboard } from '@/composables/useDashboard'
import { useRecentItems } from '@/composables/useRecentItems'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import AppTile from '@/components/shell/AppTile.vue'
import GenericTile from '@/components/shell/GenericTile.vue'
import NumericContent from '@/components/shell/NumericContent.vue'
import LaunchpadSection from '@/components/shell/LaunchpadSection.vue'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import {
  Search,
  Database,
  Package,
  FileCode2,
  Layers,
  Zap,
  Users,
  Hash,
  Shield,
  ScrollText,
  Book,
  Plus,
  Clock,
  LayoutGrid,
  Settings2
} from 'lucide-vue-next'

const { t } = useI18n()
const authStore = useAuthStore()
const metadataStore = useMetadataStore()
const { isLoading, entityCounts } = useDashboard()
const { recentItems } = useRecentItems()

const searchQuery = ref('')
const myAppsCollapsed = ref(false)
const adminCollapsed = ref(false)
const quickCreateCollapsed = ref(false)
const recentCollapsed = ref(false)

// Time-of-day greeting
const greeting = computed(() => {
  const hour = new Date().getHours()
  if (hour < 12) return t('shell.launchpad.greetingMorning')
  if (hour < 18) return t('shell.launchpad.greetingAfternoon')
  return t('shell.launchpad.greetingEvening')
})

// Entity tiles from metadata
interface EntityTileInfo {
  name: string
  entityType: string
  module: string
  path: string
  count: number
}

const entityTiles = computed<EntityTileInfo[]>(() => {
  const tiles: EntityTileInfo[] = []
  for (const mod of metadataStore.modules) {
    for (const service of mod.services) {
      for (const entity of service.entities) {
        const countEntry = entityCounts.value.find(
          (ec) => ec.module === mod.name && ec.entityType === entity.entityType
        )
        tiles.push({
          name: entity.name,
          entityType: entity.entityType,
          module: mod.name,
          path: `/odata/${mod.name}/${entity.entityType}`,
          count: countEntry?.count ?? 0
        })
      }
    }
  }
  return tiles
})

// Admin tiles
interface AdminTileInfo {
  titleKey: string
  subtitleKey: string
  path: string
  icon: typeof Package
  color: string
}

const adminTiles: AdminTileInfo[] = [
  { titleKey: 'shell.launchpad.admin.modules', subtitleKey: 'shell.launchpad.admin.modulesDesc', path: '/admin/modules', icon: Package, color: 'blue' },
  { titleKey: 'shell.launchpad.admin.metadata', subtitleKey: 'shell.launchpad.admin.metadataDesc', path: '/admin/metadata', icon: FileCode2, color: 'indigo' },
  { titleKey: 'shell.launchpad.admin.batch', subtitleKey: 'shell.launchpad.admin.batchDesc', path: '/admin/batch', icon: Layers, color: 'violet' },
  { titleKey: 'shell.launchpad.admin.actions', subtitleKey: 'shell.launchpad.admin.actionsDesc', path: '/admin/actions', icon: Zap, color: 'amber' },
  { titleKey: 'shell.launchpad.admin.users', subtitleKey: 'shell.launchpad.admin.usersDesc', path: '/admin/users', icon: Users, color: 'emerald' },
  { titleKey: 'shell.launchpad.admin.sequences', subtitleKey: 'shell.launchpad.admin.sequencesDesc', path: '/admin/sequences', icon: Hash, color: 'cyan' },
  { titleKey: 'shell.launchpad.admin.roles', subtitleKey: 'shell.launchpad.admin.rolesDesc', path: '/admin/roles', icon: Shield, color: 'rose' },
  { titleKey: 'shell.launchpad.admin.audit', subtitleKey: 'shell.launchpad.admin.auditDesc', path: '/admin/audit', icon: ScrollText, color: 'orange' },
  { titleKey: 'shell.launchpad.admin.apiDocs', subtitleKey: 'shell.launchpad.admin.apiDocsDesc', path: '/admin/api-docs', icon: Book, color: 'teal' }
]

// Quick create tiles (entity + /new)
const quickCreateTiles = computed(() =>
  entityTiles.value.map((et) => ({
    name: et.name,
    path: `${et.path}/new`,
    module: et.module,
    entityType: et.entityType
  }))
)

// Recent items (max 10)
const recentTiles = computed(() => recentItems.value.slice(0, 10))

// Search filter
const lowerQuery = computed(() => searchQuery.value.toLowerCase().trim())

function matchesSearch(label: string): boolean {
  if (!lowerQuery.value) return true
  return label.toLowerCase().includes(lowerQuery.value)
}

const filteredEntityTiles = computed(() =>
  entityTiles.value.filter((t) => matchesSearch(t.name))
)

const filteredAdminTiles = computed(() =>
  adminTiles.filter((t) => matchesSearch(t.titleKey))
)

const filteredQuickCreateTiles = computed(() =>
  quickCreateTiles.value.filter((t) => matchesSearch(t.name))
)

const filteredRecentTiles = computed(() =>
  recentTiles.value.filter((t) => matchesSearch(t.title))
)

// Color cycling for entity tiles
const entityColors = ['primary', 'emerald', 'violet', 'cyan', 'amber', 'blue', 'indigo', 'rose', 'orange', 'teal']
function getEntityColor(index: number): string {
  return entityColors[index % entityColors.length]
}
</script>

<template>
  <DefaultLayout>
    <div class="space-y-8">
      <!-- Welcome banner -->
      <div class="rounded-xl bg-gradient-to-r from-primary/10 via-primary/5 to-transparent border p-6 lg:p-8">
        <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
          <div>
            <h1 class="text-2xl lg:text-3xl font-bold tracking-tight text-foreground">
              {{ greeting }}, {{ authStore.displayName }}
            </h1>
            <p class="text-muted-foreground mt-1">{{ t('shell.launchpad.subtitle') }}</p>
          </div>
          <Spinner v-if="isLoading" size="sm" />
        </div>
      </div>

      <!-- Search box -->
      <div class="relative max-w-md">
        <Search class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" aria-hidden="true" />
        <Input
          v-model="searchQuery"
          class="pl-10"
          :placeholder="t('shell.launchpad.searchPlaceholder')"
          type="search"
        />
      </div>

      <!-- My Apps section -->
      <LaunchpadSection
        v-if="filteredEntityTiles.length > 0"
        :title="t('shell.launchpad.myApps')"
        :subtitle="t('shell.launchpad.myAppsSubtitle')"
        :icon="Database"
        :count="filteredEntityTiles.length"
        v-model:collapsed="myAppsCollapsed"
      >
        <template
          v-for="(tile, idx) in filteredEntityTiles"
          :key="`entity-${tile.module}-${tile.entityType}`"
        >
          <!-- Use GenericTile with KPI for entities that have data -->
          <GenericTile
            v-if="tile.count > 0"
            :title="tile.name"
            :subtitle="tile.module"
            :to="tile.path"
          >
            <NumericContent
              :value="tile.count"
              :valueColor="tile.count > 0 ? 'Good' : 'Neutral'"
              :indicator="tile.count > 10 ? 'Up' : 'None'"
            />
          </GenericTile>
          <!-- Fallback to AppTile for empty entities -->
          <AppTile
            v-else
            :title="tile.name"
            :subtitle="tile.module"
            :icon="Database"
            :to="tile.path"
            :color="getEntityColor(idx)"
          />
        </template>
      </LaunchpadSection>

      <!-- Admin section -->
      <LaunchpadSection
        v-if="filteredAdminTiles.length > 0"
        :title="t('shell.launchpad.administration')"
        :subtitle="t('shell.launchpad.administrationSubtitle')"
        :icon="Settings2"
        :count="filteredAdminTiles.length"
        v-model:collapsed="adminCollapsed"
      >
        <AppTile
          v-for="tile in filteredAdminTiles"
          :key="tile.path"
          :title="t(tile.titleKey)"
          :subtitle="t(tile.subtitleKey)"
          :icon="tile.icon"
          :to="tile.path"
          :color="tile.color"
        />
      </LaunchpadSection>

      <!-- Quick Create section -->
      <LaunchpadSection
        v-if="filteredQuickCreateTiles.length > 0"
        :title="t('shell.launchpad.quickCreate')"
        :subtitle="t('shell.launchpad.quickCreateSubtitle')"
        :icon="Plus"
        :count="filteredQuickCreateTiles.length"
        v-model:collapsed="quickCreateCollapsed"
      >
        <AppTile
          v-for="(tile, idx) in filteredQuickCreateTiles"
          :key="`create-${tile.module}-${tile.entityType}`"
          :title="tile.name"
          :subtitle="t('shell.launchpad.createNew')"
          :icon="Plus"
          :to="tile.path"
          :color="getEntityColor(idx)"
        />
      </LaunchpadSection>

      <!-- Recent section -->
      <LaunchpadSection
        v-if="filteredRecentTiles.length > 0"
        :title="t('shell.launchpad.recent')"
        :subtitle="t('shell.launchpad.recentSubtitle')"
        :icon="Clock"
        :count="filteredRecentTiles.length"
        v-model:collapsed="recentCollapsed"
      >
        <AppTile
          v-for="item in filteredRecentTiles"
          :key="`recent-${item.module}-${item.entityType}-${item.id}`"
          :title="item.title"
          :subtitle="`${item.entity} - ${item.id.substring(0, 8)}`"
          :icon="Clock"
          :to="`/odata/${item.module}/${item.entityType}/${item.id}`"
          color="primary"
        />
      </LaunchpadSection>

      <!-- Empty state when search yields nothing -->
      <div
        v-if="searchQuery && filteredEntityTiles.length === 0 && filteredAdminTiles.length === 0 && filteredQuickCreateTiles.length === 0 && filteredRecentTiles.length === 0"
        class="flex flex-col items-center py-16 text-center"
      >
        <LayoutGrid class="h-12 w-12 text-muted-foreground/50 mb-4" />
        <h3 class="text-lg font-medium text-muted-foreground">
          {{ t('shell.launchpad.noResults') }}
        </h3>
        <p class="text-sm text-muted-foreground mt-1">
          {{ t('shell.launchpad.noResultsHint') }}
        </p>
      </div>
    </div>
  </DefaultLayout>
</template>
