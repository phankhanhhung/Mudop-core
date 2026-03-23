<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useConfirmDialog } from '@/composables/useConfirmDialog'
import { useUiStore } from '@/stores/ui'
import api from '@/services/api'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Spinner } from '@/components/ui/spinner'
import { ConfirmDialog } from '@/components/common'
import {
  Users,
  Plus,
  Search,
  RefreshCw,
  Pencil,
  Power,
  PowerOff,
  AlertCircle,
  Calendar,
  Building2,
} from 'lucide-vue-next'
import { formatDate } from '@/utils/formatting'

// ── Types ────────────────────────────────────────────────────────────────────

interface PluginTenant {
  id: string
  code: string
  name: string
  description?: string
  subscriptionTier: string
  maxUsers: number
  isActive: boolean
  createdAt: string
  updatedAt?: string
}

const router = useRouter()
const uiStore = useUiStore()
const confirmDialog = useConfirmDialog()

// State
const tenants = ref<PluginTenant[]>([])
const isLoading = ref(false)
const error = ref<string | null>(null)
const actionLoading = ref<string | null>(null)
const searchQuery = ref('')

// Filtered tenants
const filteredTenants = computed(() => {
  if (!searchQuery.value.trim()) return tenants.value
  const q = searchQuery.value.toLowerCase()
  return tenants.value.filter((t: PluginTenant) =>
    t.code.toLowerCase().includes(q) ||
    t.name.toLowerCase().includes(q) ||
    t.subscriptionTier.toLowerCase().includes(q)
  )
})

// Stats
const stats = computed(() => ({
  total: tenants.value.length,
  active: tenants.value.filter((t: PluginTenant) => t.isActive).length,
  inactive: tenants.value.filter((t: PluginTenant) => !t.isActive).length,
}))

function getTierBadgeClass(tier: string): string {
  switch (tier.toLowerCase()) {
    case 'premium': return 'bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-400'
    case 'standard': return 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400'
    case 'free':
    default: return 'bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400'
  }
}

// API calls
async function fetchTenants() {
  isLoading.value = true
  error.value = null
  try {
    const response = await api.get<PluginTenant[]>('/tenants')
    tenants.value = response.data
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to load tenants'
  } finally {
    isLoading.value = false
  }
}

async function toggleTenantActive(tenant: PluginTenant) {
  const action = tenant.isActive ? 'deactivate' : 'activate'
  if (tenant.isActive) {
    const confirmed = await confirmDialog.confirm({
      title: 'Deactivate Tenant',
      description: `Are you sure you want to deactivate tenant "${tenant.name}"? Users will lose access.`,
      confirmLabel: 'Deactivate',
      variant: 'destructive',
    })
    if (!confirmed) return
  }

  actionLoading.value = tenant.id
  try {
    await api.post(`/tenants/${tenant.id}/${action}`)
    uiStore.success(
      `Tenant ${action === 'activate' ? 'Activated' : 'Deactivated'}`,
      `${tenant.name} has been ${action}d.`
    )
    await fetchTenants()
  } catch (e) {
    uiStore.error(`${action} Failed`, e instanceof Error ? e.message : `Failed to ${action} tenant`)
  } finally {
    actionLoading.value = null
  }
}

function navigateToCreate() {
  router.push('/admin/tenants/create')
}

function navigateToEdit(tenant: PluginTenant) {
  router.push(`/admin/tenants/${tenant.id}/edit`)
}

onMounted(() => {
  fetchTenants()
})
</script>

<template>
  <DefaultLayout>
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-3xl font-bold tracking-tight">Tenant Management</h1>
          <p class="text-muted-foreground mt-1">
            Create, configure, and manage tenants for multi-tenancy.
          </p>
        </div>
        <div class="flex gap-2">
          <Button variant="outline" size="sm" @click="fetchTenants" :disabled="isLoading">
            <Spinner v-if="isLoading" size="sm" class="mr-2" />
            <RefreshCw v-else class="mr-2 h-4 w-4" />
            Refresh
          </Button>
          <Button size="sm" @click="navigateToCreate">
            <Plus class="mr-2 h-4 w-4" />
            Create Tenant
          </Button>
        </div>
      </div>

      <!-- Error -->
      <Alert v-if="error" variant="destructive">
        <AlertCircle class="h-4 w-4" />
        <AlertDescription>{{ error }}</AlertDescription>
      </Alert>

      <!-- Stats -->
      <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">Total Tenants</p>
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
                <p class="text-sm font-medium text-muted-foreground">Active</p>
                <p class="text-2xl font-bold mt-1 text-emerald-600">{{ stats.active }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-emerald-500/10 flex items-center justify-center">
                <Power class="h-5 w-5 text-emerald-500" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">Inactive</p>
                <p class="text-2xl font-bold mt-1 text-gray-500">{{ stats.inactive }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-gray-500/10 flex items-center justify-center">
                <PowerOff class="h-5 w-5 text-gray-400" />
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      <!-- Tenant Table -->
      <Card>
        <CardHeader>
          <div class="flex items-center justify-between">
            <div class="flex items-center gap-2">
              <Users class="h-5 w-5" />
              <CardTitle>Tenants</CardTitle>
            </div>
            <!-- Search -->
            <div class="relative w-64">
              <Search class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                v-model="searchQuery"
                placeholder="Search tenants..."
                class="pl-9"
              />
            </div>
          </div>
        </CardHeader>
        <CardContent class="p-0">
          <!-- Loading -->
          <div v-if="isLoading" class="flex flex-col items-center justify-center py-16" role="status" aria-label="Loading tenants">
            <Spinner size="lg" />
            <p class="text-muted-foreground mt-3 text-sm">Loading tenants...</p>
          </div>

          <!-- Empty state -->
          <div v-else-if="filteredTenants.length === 0" class="px-6 pb-6">
            <Card class="border-dashed">
              <CardContent class="flex flex-col items-center justify-center py-16">
                <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
                  <Building2 class="h-8 w-8 text-muted-foreground" />
                </div>
                <h3 class="text-lg font-semibold mb-1">No tenants found</h3>
                <p class="text-muted-foreground text-sm mb-4 text-center max-w-sm">
                  {{ searchQuery ? 'No tenants match your search.' : 'Create your first tenant to get started.' }}
                </p>
                <Button v-if="!searchQuery" size="sm" @click="navigateToCreate">
                  <Plus class="mr-2 h-4 w-4" />
                  Create Tenant
                </Button>
              </CardContent>
            </Card>
          </div>

          <!-- Table -->
          <div v-else class="overflow-x-auto">
            <table class="w-full text-sm">
              <thead>
                <tr class="border-b bg-muted/50">
                  <th class="text-left font-medium text-muted-foreground px-5 py-3">Code</th>
                  <th class="text-left font-medium text-muted-foreground px-5 py-3">Name</th>
                  <th class="text-left font-medium text-muted-foreground px-5 py-3">Tier</th>
                  <th class="text-left font-medium text-muted-foreground px-5 py-3">Status</th>
                  <th class="text-left font-medium text-muted-foreground px-5 py-3">Max Users</th>
                  <th class="text-left font-medium text-muted-foreground px-5 py-3">Created</th>
                  <th class="text-right font-medium text-muted-foreground px-5 py-3">Actions</th>
                </tr>
              </thead>
              <tbody class="divide-y">
                <tr
                  v-for="tenant in filteredTenants"
                  :key="tenant.id"
                  class="hover:bg-muted/50 transition-colors"
                >
                  <td class="px-5 py-3 font-mono text-sm">{{ tenant.code }}</td>
                  <td class="px-5 py-3 font-medium">{{ tenant.name }}</td>
                  <td class="px-5 py-3">
                    <span
                      class="inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium capitalize"
                      :class="getTierBadgeClass(tenant.subscriptionTier)"
                    >
                      {{ tenant.subscriptionTier }}
                    </span>
                  </td>
                  <td class="px-5 py-3">
                    <Badge
                      :variant="tenant.isActive ? 'default' : 'secondary'"
                      :class="tenant.isActive ? 'bg-emerald-600 hover:bg-emerald-700 dark:bg-emerald-700 dark:hover:bg-emerald-600' : ''"
                    >
                      {{ tenant.isActive ? 'Active' : 'Inactive' }}
                    </Badge>
                  </td>
                  <td class="px-5 py-3 text-muted-foreground">{{ tenant.maxUsers }}</td>
                  <td class="px-5 py-3 text-muted-foreground">
                    <span class="flex items-center gap-1.5">
                      <Calendar class="h-3.5 w-3.5" />
                      {{ formatDate(tenant.createdAt) }}
                    </span>
                  </td>
                  <td class="px-5 py-3">
                    <div class="flex items-center justify-end gap-1">
                      <Spinner v-if="actionLoading === tenant.id" size="sm" />
                      <template v-else>
                        <Button
                          variant="ghost"
                          size="sm"
                          class="h-8 w-8 p-0"
                          title="Edit"
                          @click="navigateToEdit(tenant)"
                        >
                          <Pencil class="h-3.5 w-3.5" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          class="h-8 w-8 p-0"
                          :title="tenant.isActive ? 'Deactivate' : 'Activate'"
                          @click="toggleTenantActive(tenant)"
                        >
                          <PowerOff v-if="tenant.isActive" class="h-3.5 w-3.5 text-muted-foreground" />
                          <Power v-else class="h-3.5 w-3.5 text-emerald-500" />
                        </Button>
                      </template>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>

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
    </div>
  </DefaultLayout>
</template>
