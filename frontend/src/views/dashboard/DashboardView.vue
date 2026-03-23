<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { useTenantStore } from '@/stores/tenant'
import { useMetadataStore } from '@/stores/metadata'
import { useUiStore } from '@/stores/ui'
import { useDashboard } from '@/composables/useDashboard'
import { useDashboardLayout } from '@/composables/useDashboardLayout'
import { useConfirmDialog } from '@/composables/useConfirmDialog'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import KpiCardEnhanced from '@/components/analytics/KpiCardEnhanced.vue'
import DashboardGrid from '@/components/dashboard/DashboardGrid.vue'
import DashboardConfigurator from '@/components/dashboard/DashboardConfigurator.vue'
import WidgetCatalog from '@/components/dashboard/WidgetCatalog.vue'
import ConfirmDialog from '@/components/common/ConfirmDialog.vue'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { Spinner } from '@/components/ui/spinner'
import {
  Building2,
  Database,
  FolderOpen,
  RefreshCw,
  ArrowRight,
  Layers,
  Rocket,
  Package
} from 'lucide-vue-next'
import { formatInteger } from '@/utils/formatting'
import type { DashboardData } from '@/types/dashboard'

const { t } = useI18n()
const router = useRouter()
const authStore = useAuthStore()
const tenantStore = useTenantStore()
const metadataStore = useMetadataStore()
const uiStore = useUiStore()
const confirmDialog = useConfirmDialog()
const { isLoading, cacheStats, entityCounts, recentActivity, refresh } = useDashboard()

const totalRecords = computed(() =>
  entityCounts.value.reduce((sum, e) => sum + e.count, 0)
)

// Dashboard layout builder
const {
  layout,
  isDirty,
  isSaving,
  reorderWidgets,
  removeWidget,
  addWidget,
  saveLayout,
  resetToDefault,
  setColumns,
  loadLayout,
} = useDashboardLayout()

const editMode = ref(false)
const catalogOpen = ref(false)

// Build DashboardData for widgets
const dashboardData = computed<DashboardData>(() => ({
  cacheStats: cacheStats.value,
  entityCounts: entityCounts.value,
  recentActivity: recentActivity.value as unknown[],
  isLoading: isLoading.value,
}))

// Load layout when tenant becomes available
watch(
  () => tenantStore.hasTenant,
  (hasTenant) => {
    if (hasTenant) loadLayout()
  },
  { immediate: true }
)

async function handleSave() {
  try {
    await saveLayout()
    uiStore.success(t('dashboard.builder.savedSuccess'))
  } catch {
    uiStore.error(t('dashboard.builder.saveError'))
  }
}

async function handleReset() {
  const confirmed = await confirmDialog.confirm({
    title: t('dashboard.builder.reset'),
    description: t('dashboard.builder.resetConfirm'),
    variant: 'destructive',
    confirmLabel: t('dashboard.builder.reset'),
  })
  if (confirmed) await resetToDefault()
}
</script>

<template>
  <DefaultLayout>
    <div class="space-y-6">
      <!-- Welcome Header -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h1 class="text-3xl font-bold tracking-tight">{{ $t('dashboard.welcome', { name: authStore.displayName }) }}</h1>
          <p class="text-muted-foreground mt-1">{{ $t('dashboard.subtitle') }}</p>
        </div>
        <div class="flex items-center gap-3">
          <!-- Current tenant badge -->
          <Badge
            v-if="tenantStore.hasTenant"
            variant="outline"
            class="px-3 py-1.5 text-sm gap-1.5 cursor-pointer hover:bg-accent transition-colors"
            @click="router.push('/tenants')"
          >
            <Building2 class="h-3.5 w-3.5" />
            {{ tenantStore.currentTenantName }}
          </Badge>
          <Button variant="outline" size="sm" @click="refresh" :disabled="isLoading">
            <Spinner v-if="isLoading" size="sm" class="mr-2" />
            <RefreshCw v-else class="h-4 w-4 mr-2" />
            {{ $t('common.refresh') }}
          </Button>
        </div>
      </div>

      <!-- Getting Started - No Tenant Selected -->
      <Card v-if="!tenantStore.hasTenant" class="border-dashed border-2 bg-muted/30">
        <CardContent class="py-10">
          <div class="flex flex-col items-center text-center max-w-lg mx-auto">
            <div class="h-16 w-16 rounded-2xl bg-primary/10 flex items-center justify-center mb-5">
              <Rocket class="h-8 w-8 text-primary" />
            </div>
            <h2 class="text-xl font-semibold mb-2">{{ $t('dashboard.gettingStarted.title') }}</h2>
            <p class="text-muted-foreground text-sm mb-6">{{ $t('dashboard.gettingStarted.subtitle') }}</p>

            <!-- Steps -->
            <div class="grid grid-cols-1 sm:grid-cols-3 gap-4 w-full mb-6">
              <div class="flex flex-col items-center p-4 rounded-lg bg-background border">
                <div class="h-8 w-8 rounded-full bg-primary/10 flex items-center justify-center mb-2">
                  <span class="text-sm font-bold text-primary">1</span>
                </div>
                <h3 class="text-sm font-medium mb-1">{{ $t('dashboard.gettingStarted.step1Title') }}</h3>
                <p class="text-xs text-muted-foreground text-center">{{ $t('dashboard.gettingStarted.step1') }}</p>
              </div>
              <div class="flex flex-col items-center p-4 rounded-lg bg-background border">
                <div class="h-8 w-8 rounded-full bg-primary/10 flex items-center justify-center mb-2">
                  <span class="text-sm font-bold text-primary">2</span>
                </div>
                <h3 class="text-sm font-medium mb-1">{{ $t('dashboard.gettingStarted.step2Title') }}</h3>
                <p class="text-xs text-muted-foreground text-center">{{ $t('dashboard.gettingStarted.step2') }}</p>
              </div>
              <div class="flex flex-col items-center p-4 rounded-lg bg-background border">
                <div class="h-8 w-8 rounded-full bg-primary/10 flex items-center justify-center mb-2">
                  <span class="text-sm font-bold text-primary">3</span>
                </div>
                <h3 class="text-sm font-medium mb-1">{{ $t('dashboard.gettingStarted.step3Title') }}</h3>
                <p class="text-xs text-muted-foreground text-center">{{ $t('dashboard.gettingStarted.step3') }}</p>
              </div>
            </div>

            <Button size="lg" @click="router.push('/tenants')">
              <Building2 class="mr-2 h-4 w-4" />
              {{ $t('dashboard.gettingStarted.button') }}
              <ArrowRight class="ml-2 h-4 w-4" />
            </Button>
          </div>
        </CardContent>
      </Card>

      <!-- No Modules Loaded Callout -->
      <Card v-if="tenantStore.hasTenant && !metadataStore.hasModules && !isLoading" class="border-dashed border-2 bg-amber-50/50 dark:bg-amber-950/20 border-amber-300 dark:border-amber-800">
        <CardContent class="py-8">
          <div class="flex flex-col sm:flex-row items-center gap-5">
            <div class="h-14 w-14 rounded-2xl bg-amber-500/10 flex items-center justify-center shrink-0">
              <Package class="h-7 w-7 text-amber-500" />
            </div>
            <div class="flex-1 text-center sm:text-left">
              <h3 class="text-lg font-semibold mb-1">{{ $t('dashboard.gettingStarted.noModulesTitle') }}</h3>
              <p class="text-sm text-muted-foreground">{{ $t('dashboard.gettingStarted.noModulesSubtitle') }}</p>
            </div>
            <Button @click="router.push('/admin/modules')" class="shrink-0">
              <FolderOpen class="mr-2 h-4 w-4" />
              {{ $t('dashboard.gettingStarted.noModulesButton') }}
              <ArrowRight class="ml-2 h-4 w-4" />
            </Button>
          </div>
        </CardContent>
      </Card>

      <!-- KPI Stats Row -->
      <div v-if="tenantStore.hasTenant" class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <KpiCardEnhanced
          :title="$t('dashboard.currentTenant')"
          :value="tenantStore.currentTenantName"
          :description="$t('dashboard.tenantsAvailable', { count: tenantStore.tenants.length })"
          :icon="Building2"
          color="primary"
        />
        <KpiCardEnhanced
          :title="$t('dashboard.modules')"
          :value="metadataStore.modules.length"
          :description="$t('dashboard.loadedModules')"
          :icon="FolderOpen"
          color="emerald"
          :target="10"
        />
        <KpiCardEnhanced
          :title="$t('dashboard.entities')"
          :value="formatInteger(totalRecords)"
          :description="$t('dashboard.totalRecords')"
          :icon="Database"
          color="violet"
          :trend="totalRecords > 0 ? 'up' : 'neutral'"
          :trendValue="totalRecords > 0 ? `${totalRecords} total` : ''"
        />
        <KpiCardEnhanced
          :title="$t('dashboard.entityTypes')"
          :value="cacheStats?.entityCount || 0"
          :description="$t('dashboard.definedInSchema')"
          :icon="Layers"
          color="cyan"
        />
      </div>

      <!-- Dashboard Configurator toolbar (when tenant selected) -->
      <div v-if="tenantStore.hasTenant" class="flex justify-end">
        <DashboardConfigurator
          :edit-mode="editMode"
          :is-dirty="isDirty"
          :is-saving="isSaving"
          :columns="layout?.columns ?? 3"
          @update:edit-mode="editMode = $event"
          @add-widget="catalogOpen = true"
          @save="handleSave"
          @reset="handleReset"
          @set-columns="setColumns"
        />
      </div>

      <!-- Main Content Grid (when tenant selected) -->
      <DashboardGrid
        v-if="tenantStore.hasTenant && layout"
        :widgets="layout.widgets"
        :columns="layout.columns"
        :edit-mode="editMode"
        :dashboard-data="dashboardData"
        @reorder="reorderWidgets"
        @remove="removeWidget"
      />

      <!-- Widget Catalog -->
      <WidgetCatalog
        :open="catalogOpen"
        @close="catalogOpen = false"
        @add="(type) => { addWidget(type); catalogOpen = false }"
      />

      <ConfirmDialog
        :open="confirmDialog.isOpen.value"
        :title="confirmDialog.title.value"
        :description="confirmDialog.description.value"
        :confirm-label="confirmDialog.confirmLabel.value"
        :cancel-label="confirmDialog.cancelLabel.value"
        :variant="confirmDialog.variant.value"
        @confirm="confirmDialog.handleConfirm"
        @cancel="confirmDialog.handleCancel"
      />
    </div>
  </DefaultLayout>
</template>
