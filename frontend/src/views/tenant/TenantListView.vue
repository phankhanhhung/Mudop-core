<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useTenant } from '@/composables/useTenant'
import { useTenantStore } from '@/stores/tenant'
import { useConfirmDialog } from '@/composables/useConfirmDialog'
import { useUiStore } from '@/stores/ui'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import TenantDialog from '@/components/admin/TenantDialog.vue'
import { ConfirmDialog } from '@/components/common'
import { Card, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Spinner } from '@/components/ui/spinner'
import {
  Building2,
  Plus,
  Search,
  Pencil,
  Trash2,
  AlertCircle,
  RefreshCw,
  Check,
  ChevronRight,
  X,
  Calendar,
  Package,
  Database,
  Eye,
  LogIn
} from 'lucide-vue-next'
import type { Tenant } from '@/types/tenant'
import { formatDate } from '@/utils/formatting'

const { t } = useI18n()
const { tenants, currentTenant, isLoading, error, fetchTenants, selectTenant } = useTenant()
const tenantStore = useTenantStore()
const confirmDialog = useConfirmDialog()
const uiStore = useUiStore()

const searchQuery = ref('')

// Dialog state
const dialogOpen = ref(false)
const editingTenant = ref<Tenant | undefined>(undefined)

// Detail panel
const selectedTenant = ref<Tenant | null>(null)

// Stats
const stats = computed(() => {
  const total = tenants.value.length
  const activeName = currentTenant.value?.name || null
  return { total, activeName }
})

const filteredTenants = computed(() => {
  const query = searchQuery.value.toLowerCase().trim()
  if (!query) return tenants.value
  return tenants.value.filter(t =>
    t.name.toLowerCase().includes(query) ||
    t.code.toLowerCase().includes(query) ||
    (t.description && t.description.toLowerCase().includes(query))
  )
})

// Reset search when needed
watch(searchQuery, () => {
  // Nothing extra needed currently, but keeps reactivity clean
})

function isSelected(tenant: Tenant): boolean {
  return currentTenant.value?.id === tenant.id
}

function openCreateDialog() {
  editingTenant.value = undefined
  dialogOpen.value = true
}

function openEditDialog(tenant: Tenant) {
  editingTenant.value = tenant
  dialogOpen.value = true
}

function viewTenant(tenant: Tenant) {
  selectedTenant.value = selectedTenant.value?.id === tenant.id ? null : tenant
}

function handleSelect(tenant: Tenant) {
  selectTenant(tenant)
  uiStore.success(
    t('tenant.selected'),
    t('tenant.selectedMessage', { name: tenant.name })
  )
}

async function deleteTenant(tenant: Tenant) {
  const confirmed = await confirmDialog.confirm({
    title: t('tenant.deleteTenant'),
    description: t('tenant.deleteConfirm', { name: tenant.name }),
    confirmLabel: t('common.delete'),
    variant: 'destructive'
  })
  if (!confirmed) return

  try {
    await tenantStore.deleteTenant(tenant.id)
    if (selectedTenant.value?.id === tenant.id) {
      selectedTenant.value = null
    }
    uiStore.success(t('tenant.deleted'), t('tenant.deletedMessage', { name: tenant.name }))
  } catch (e) {
    uiStore.error(
      t('tenant.failedToDelete'),
      e instanceof Error ? e.message : t('tenant.failedToDelete')
    )
  }
}

function onTenantSaved(tenant: Tenant) {
  fetchTenants()
  // If creating, auto-select
  if (!editingTenant.value) {
    selectTenant(tenant)
    uiStore.success(t('tenant.create.success'), t('tenant.create.successMessage', { name: tenant.name }))
  } else {
    uiStore.success(t('tenant.updated'), t('tenant.updatedMessage', { name: tenant.name }))
    // Refresh selected tenant detail panel
    if (selectedTenant.value?.id === tenant.id) {
      selectedTenant.value = tenant
    }
  }
}

function getTenantColor(tenant: Tenant): string {
  const colors = [
    'bg-blue-500', 'bg-emerald-500', 'bg-violet-500', 'bg-amber-500',
    'bg-rose-500', 'bg-cyan-500', 'bg-indigo-500', 'bg-teal-500',
    'bg-pink-500', 'bg-orange-500'
  ]
  let hash = 0
  for (const char of tenant.code) {
    hash = char.charCodeAt(0) + ((hash << 5) - hash)
  }
  return colors[Math.abs(hash) % colors.length]
}

function getTenantInitials(tenant: Tenant): string {
  const words = tenant.name.trim().split(/\s+/)
  if (words.length >= 2) {
    return (words[0][0] + words[1][0]).toUpperCase()
  }
  return tenant.name.substring(0, 2).toUpperCase()
}

onMounted(async () => {
  await fetchTenants()
})
</script>

<template>
  <DefaultLayout>
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-3xl font-bold tracking-tight">{{ $t('tenant.title') }}</h1>
          <p class="text-muted-foreground mt-1">
            {{ $t('tenant.subtitle') }}
          </p>
        </div>
        <div class="flex gap-2">
          <Button variant="outline" size="sm" @click="fetchTenants" :disabled="isLoading">
            <Spinner v-if="isLoading" size="sm" class="mr-2" />
            <RefreshCw v-else class="mr-2 h-4 w-4" />
            {{ $t('common.refresh') }}
          </Button>
          <Button @click="openCreateDialog">
            <Plus class="mr-2 h-4 w-4" />
            {{ $t('tenant.createTenant') }}
          </Button>
        </div>
      </div>

      <!-- Error -->
      <Alert v-if="error" variant="destructive">
        <AlertCircle class="h-4 w-4" />
        <AlertDescription>{{ error }}</AlertDescription>
      </Alert>

      <!-- Stats Cards -->
      <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('tenant.stats.total') }}</p>
                <p class="text-2xl font-bold mt-1">{{ stats.total }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center">
                <Building2 class="h-5 w-5 text-primary" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('tenant.stats.active') }}</p>
                <p class="text-2xl font-bold mt-1 truncate max-w-[160px]" :class="stats.activeName ? 'text-emerald-600' : 'text-muted-foreground'">
                  {{ stats.activeName || $t('common.none') }}
                </p>
              </div>
              <div class="h-10 w-10 rounded-full bg-emerald-500/10 flex items-center justify-center">
                <Check class="h-5 w-5 text-emerald-500" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('tenant.stats.modules') }}</p>
                <p class="text-2xl font-bold mt-1 text-violet-600">--</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-violet-500/10 flex items-center justify-center">
                <Package class="h-5 w-5 text-violet-500" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('tenant.stats.entityTypes') }}</p>
                <p class="text-2xl font-bold mt-1 text-cyan-600">--</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-cyan-500/10 flex items-center justify-center">
                <Database class="h-5 w-5 text-cyan-500" />
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      <!-- Search Bar -->
      <div class="flex items-center gap-3">
        <div class="relative flex-1 max-w-sm">
          <Search class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
          <Input
            v-model="searchQuery"
            :placeholder="$t('tenant.searchPlaceholder')"
            class="pl-9"
          />
        </div>
        <p class="text-sm text-muted-foreground">
          {{ $t('tenant.showingCount', { count: filteredTenants.length, total: tenants.length }) }}
        </p>
      </div>

      <!-- Loading -->
      <div v-if="isLoading" class="flex flex-col items-center justify-center py-16">
        <Spinner size="lg" />
        <p class="text-muted-foreground mt-3 text-sm">{{ $t('tenant.loading') }}</p>
      </div>

      <!-- Empty state -->
      <Card v-else-if="tenants.length === 0" class="border-dashed">
        <CardContent class="flex flex-col items-center justify-center py-16">
          <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
            <Building2 class="h-8 w-8 text-muted-foreground" />
          </div>
          <h3 class="text-lg font-semibold mb-1">{{ $t('tenant.noTenants') }}</h3>
          <p class="text-muted-foreground text-sm mb-4 text-center max-w-sm">
            {{ $t('tenant.noTenantsDescription') }}
          </p>
          <Button @click="openCreateDialog">
            <Plus class="mr-2 h-4 w-4" />
            {{ $t('tenant.createFirstTenant') }}
          </Button>
        </CardContent>
      </Card>

      <!-- No search results -->
      <Card v-else-if="filteredTenants.length === 0" class="border-dashed">
        <CardContent class="flex flex-col items-center justify-center py-12">
          <Search class="h-10 w-10 text-muted-foreground mb-3" />
          <h3 class="text-lg font-semibold mb-1">{{ $t('tenant.noResults') }}</h3>
          <p class="text-muted-foreground text-sm">
            {{ $t('tenant.noMatch', { query: searchQuery }) }}
          </p>
          <Button variant="outline" class="mt-3" @click="searchQuery = ''">
            {{ $t('tenant.clearSearch') }}
          </Button>
        </CardContent>
      </Card>

      <!-- Tenant List with Detail Panel -->
      <div v-else class="flex gap-6">
        <!-- Tenant List -->
        <Card class="flex-1 min-w-0">
          <CardContent class="p-0">
            <div class="divide-y">
              <div
                v-for="tenant in filteredTenants"
                :key="tenant.id"
                class="flex items-center gap-4 px-4 py-3 hover:bg-muted/50 transition-colors group cursor-pointer"
                :class="[
                  selectedTenant?.id === tenant.id ? 'bg-muted/50' : '',
                  isSelected(tenant) ? 'border-l-4 border-l-emerald-500 pl-3' : ''
                ]"
                @click="viewTenant(tenant)"
              >
                <!-- Tenant icon -->
                <div
                  class="h-10 w-10 rounded-lg flex items-center justify-center text-white text-sm font-medium shrink-0"
                  :class="getTenantColor(tenant)"
                >
                  {{ getTenantInitials(tenant) }}
                </div>

                <!-- Tenant info -->
                <div class="flex-1 min-w-0">
                  <div class="flex items-center gap-2">
                    <span class="font-medium truncate">{{ tenant.name }}</span>
                    <Badge
                      v-if="isSelected(tenant)"
                      variant="default"
                      class="bg-emerald-600 hover:bg-emerald-700 shrink-0"
                    >
                      <Check class="mr-1 h-3 w-3" />
                      {{ $t('tenant.activeTenant') }}
                    </Badge>
                  </div>
                  <div class="flex items-center gap-3 mt-0.5">
                    <span class="text-sm text-muted-foreground font-mono">{{ tenant.code }}</span>
                    <span v-if="tenant.description" class="text-muted-foreground hidden sm:inline">&middot;</span>
                    <span v-if="tenant.description" class="text-sm text-muted-foreground truncate hidden sm:inline">
                      {{ tenant.description }}
                    </span>
                  </div>
                </div>

                <!-- Date -->
                <div class="hidden md:flex items-center text-sm text-muted-foreground shrink-0">
                  <Calendar class="h-3.5 w-3.5 mr-1.5" />
                  {{ tenant.createdAt ? formatDate(tenant.createdAt) : '--' }}
                </div>

                <!-- Actions -->
                <div class="flex items-center gap-1 shrink-0 opacity-0 group-hover:opacity-100 transition-opacity">
                  <Button
                    v-if="!isSelected(tenant)"
                    variant="ghost"
                    size="sm"
                    class="h-8 w-8 p-0 text-emerald-600 hover:text-emerald-700"
                    :title="$t('tenant.selectTenant')"
                    @click.stop="handleSelect(tenant)"
                  >
                    <LogIn class="h-4 w-4" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    class="h-8 w-8 p-0"
                    :title="$t('tenant.viewDetails')"
                    @click.stop="viewTenant(tenant)"
                  >
                    <Eye class="h-4 w-4" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    class="h-8 w-8 p-0"
                    :title="$t('common.edit')"
                    @click.stop="openEditDialog(tenant)"
                  >
                    <Pencil class="h-4 w-4" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    class="h-8 w-8 p-0 text-destructive hover:text-destructive"
                    :title="$t('tenant.deleteTenant')"
                    @click.stop="deleteTenant(tenant)"
                  >
                    <Trash2 class="h-4 w-4" />
                  </Button>
                </div>

                <!-- Chevron -->
                <ChevronRight
                  class="h-4 w-4 text-muted-foreground shrink-0 transition-transform"
                  :class="selectedTenant?.id === tenant.id ? 'text-primary rotate-90' : 'opacity-0 group-hover:opacity-50'"
                />
              </div>
            </div>
          </CardContent>
        </Card>

        <!-- Detail Panel -->
        <transition
          enter-active-class="transition-all duration-200 ease-out"
          leave-active-class="transition-all duration-150 ease-in"
          enter-from-class="opacity-0 translate-x-4"
          enter-to-class="opacity-100 translate-x-0"
          leave-from-class="opacity-100 translate-x-0"
          leave-to-class="opacity-0 translate-x-4"
        >
          <Card v-if="selectedTenant" class="w-80 shrink-0 self-start sticky top-20">
            <CardContent class="p-5">
              <!-- Close button -->
              <div class="flex justify-end mb-2">
                <Button
                  variant="ghost"
                  size="sm"
                  class="h-8 w-8 p-0"
                  @click="selectedTenant = null"
                >
                  <X class="h-4 w-4" />
                </Button>
              </div>

              <!-- Tenant profile header -->
              <div class="flex flex-col items-center text-center pb-4 border-b">
                <div
                  class="h-16 w-16 rounded-lg flex items-center justify-center text-white text-xl font-semibold mb-3"
                  :class="getTenantColor(selectedTenant)"
                >
                  {{ getTenantInitials(selectedTenant) }}
                </div>
                <h3 class="font-semibold text-lg">{{ selectedTenant.name }}</h3>
                <p class="text-sm text-muted-foreground font-mono mt-0.5">{{ selectedTenant.code }}</p>
                <Badge
                  class="mt-2"
                  :variant="isSelected(selectedTenant) ? 'default' : 'secondary'"
                  :class="isSelected(selectedTenant) ? 'bg-emerald-600 hover:bg-emerald-700' : ''"
                >
                  {{ isSelected(selectedTenant) ? $t('tenant.activeTenant') : $t('tenant.inactiveTenant') }}
                </Badge>
              </div>

              <!-- Details -->
              <div class="space-y-3 py-4 border-b">
                <div>
                  <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider">{{ $t('common.description') }}</p>
                  <p class="text-sm mt-0.5" :class="selectedTenant.description ? '' : 'text-muted-foreground italic'">
                    {{ selectedTenant.description || $t('common.noDescription') }}
                  </p>
                </div>
                <div>
                  <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider">{{ $t('tenant.tenantId') }}</p>
                  <p class="text-sm mt-0.5 font-mono text-muted-foreground break-all">{{ selectedTenant.id }}</p>
                </div>
                <div v-if="selectedTenant.createdAt">
                  <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider">{{ $t('common.created') }}</p>
                  <p class="text-sm mt-0.5 flex items-center gap-1.5">
                    <Calendar class="h-3.5 w-3.5 text-muted-foreground" />
                    {{ formatDate(selectedTenant.createdAt) }}
                  </p>
                </div>
              </div>

              <!-- Quick Actions -->
              <div class="flex flex-col gap-2 pt-4">
                <Button
                  v-if="!isSelected(selectedTenant)"
                  size="sm"
                  class="w-full justify-start bg-emerald-600 hover:bg-emerald-700"
                  @click="handleSelect(selectedTenant)"
                >
                  <LogIn class="mr-2 h-4 w-4" />
                  {{ $t('tenant.selectTenant') }}
                </Button>
                <Button
                  v-else
                  variant="outline"
                  size="sm"
                  class="w-full justify-start text-emerald-600 border-emerald-200 dark:border-emerald-800"
                  disabled
                >
                  <Check class="mr-2 h-4 w-4" />
                  {{ $t('tenant.currentlySelected') }}
                </Button>
                <Button variant="outline" size="sm" class="w-full justify-start" @click="openEditDialog(selectedTenant)">
                  <Pencil class="mr-2 h-4 w-4" />
                  {{ $t('tenant.editTenant') }}
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  class="w-full justify-start text-destructive hover:text-destructive"
                  @click="deleteTenant(selectedTenant)"
                >
                  <Trash2 class="mr-2 h-4 w-4" />
                  {{ $t('tenant.deleteTenant') }}
                </Button>
              </div>
            </CardContent>
          </Card>
        </transition>
      </div>
    </div>

    <!-- Tenant create/edit dialog -->
    <TenantDialog
      :open="dialogOpen"
      :tenant="editingTenant"
      @update:open="dialogOpen = $event"
      @saved="onTenantSaved"
    />

    <!-- Confirm dialog -->
    <ConfirmDialog
      :open="confirmDialog.isOpen.value"
      :title="confirmDialog.title.value"
      :description="confirmDialog.description.value"
      :confirm-label="confirmDialog.confirmLabel.value"
      :cancel-label="confirmDialog.cancelLabel.value"
      :variant="confirmDialog.variant.value"
      @confirm="confirmDialog.handleConfirm"
      @cancel="confirmDialog.handleCancel"
      @update:open="confirmDialog.isOpen.value = $event"
    />
  </DefaultLayout>
</template>
