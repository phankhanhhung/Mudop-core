<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useTenantStore } from '@/stores/tenant'
import { useConfirmDialog } from '@/composables/useConfirmDialog'
import { useClientPagination } from '@/composables/useClientPagination'
import { userService } from '@/services/userService'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import UserDialog from '@/components/admin/UserDialog.vue'
import UserRoleChips from '@/components/admin/UserRoleChips.vue'
import UserPermissionChips from '@/components/admin/UserPermissionChips.vue'
import { ConfirmDialog } from '@/components/common'
import { Card, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Spinner } from '@/components/ui/spinner'
import {
  Users,
  Plus,
  Search,
  Pencil,
  Trash2,
  AlertCircle,
  RefreshCw,
  ChevronLeft,
  ChevronRight,
  UserCheck,
  UserX,
  Shield,
  Mail,
  Eye
} from 'lucide-vue-next'
import type { TenantUser } from '@/types/user'

const { t } = useI18n()
const tenantStore = useTenantStore()
const confirmDialog = useConfirmDialog()

const tenantId = computed(() => tenantStore.currentTenant?.id || '')

const users = ref<TenantUser[]>([])
const isLoading = ref(false)
const error = ref<string | null>(null)
const searchQuery = ref('')
const statusFilter = ref<'all' | 'active' | 'inactive'>('all')

// Dialog state
const dialogOpen = ref(false)
const editingUser = ref<TenantUser | undefined>(undefined)

// Detail panel
const selectedUser = ref<TenantUser | null>(null)

// Stats
const stats = computed(() => {
  const total = users.value.length
  const active = users.value.filter(u => u.isActive).length
  const inactive = total - active
  const withRoles = users.value.filter(u => u.roles.length > 0).length
  return { total, active, inactive, withRoles }
})

const filteredUsers = computed(() => {
  let result = users.value

  // Status filter
  if (statusFilter.value === 'active') {
    result = result.filter(u => u.isActive)
  } else if (statusFilter.value === 'inactive') {
    result = result.filter(u => !u.isActive)
  }

  // Search filter
  const query = searchQuery.value.toLowerCase().trim()
  if (query) {
    result = result.filter(u =>
      u.username.toLowerCase().includes(query) ||
      u.email.toLowerCase().includes(query) ||
      (u.firstName && u.firstName.toLowerCase().includes(query)) ||
      (u.lastName && u.lastName.toLowerCase().includes(query)) ||
      u.roles.some(r => r.toLowerCase().includes(query)) ||
      (u.permissions && u.permissions.some(p => p.toLowerCase().includes(query)))
    )
  }

  return result
})

const { currentPage, pageSize, totalPages, paginatedItems: paginatedUsers, reset: resetPage } = useClientPagination(filteredUsers, 15)

// Reset page when filters change
watch([searchQuery, statusFilter], () => {
  resetPage()
})

async function fetchUsers() {
  if (!tenantId.value) return

  isLoading.value = true
  error.value = null
  try {
    users.value = await userService.listUsers(tenantId.value)
    // Refresh selectedUser with updated data from the new list
    if (selectedUser.value) {
      const updated = users.value.find(u => u.id === selectedUser.value!.id)
      selectedUser.value = updated || null
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('admin.users.failedToLoad')
  } finally {
    isLoading.value = false
  }
}

function openCreateDialog() {
  editingUser.value = undefined
  dialogOpen.value = true
}

function openEditDialog(user: TenantUser) {
  editingUser.value = user
  dialogOpen.value = true
}

function viewUser(user: TenantUser) {
  selectedUser.value = selectedUser.value?.id === user.id ? null : user
}

async function toggleUserStatus(user: TenantUser) {
  try {
    await userService.updateUser(tenantId.value, user.id, {
      isActive: !user.isActive
    })
    await fetchUsers()
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('admin.users.failedToUpdate')
  }
}

async function deleteUser(user: TenantUser) {
  const confirmed = await confirmDialog.confirm({
    title: t('admin.users.deactivateUser'),
    description: t('admin.users.deactivateConfirm', { name: user.username }),
    confirmLabel: t('admin.users.deactivate'),
    variant: 'destructive'
  })
  if (!confirmed) return

  try {
    await userService.deleteUser(tenantId.value, user.id)
    if (selectedUser.value?.id === user.id) {
      selectedUser.value = null
    }
    await fetchUsers()
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('admin.users.failedToDeactivate')
  }
}

function onUserSaved() {
  fetchUsers()
}

function onRolesUpdated() {
  fetchUsers()
}

function onPermissionsUpdated() {
  fetchUsers()
}

function displayName(user: TenantUser): string {
  const parts = [user.firstName, user.lastName].filter(Boolean)
  return parts.length > 0 ? parts.join(' ') : user.username
}

function getInitials(user: TenantUser): string {
  if (user.firstName && user.lastName) {
    return (user.firstName[0] + user.lastName[0]).toUpperCase()
  }
  return user.username.substring(0, 2).toUpperCase()
}

function getAvatarColor(user: TenantUser): string {
  // Generate a consistent color from username hash
  const colors = [
    'bg-blue-500', 'bg-emerald-500', 'bg-violet-500', 'bg-amber-500',
    'bg-rose-500', 'bg-cyan-500', 'bg-indigo-500', 'bg-teal-500',
    'bg-pink-500', 'bg-orange-500'
  ]
  let hash = 0
  for (const char of user.username) {
    hash = char.charCodeAt(0) + ((hash << 5) - hash)
  }
  return colors[Math.abs(hash) % colors.length]
}

onMounted(() => {
  if (tenantId.value) {
    fetchUsers()
  }
})
</script>

<template>
  <DefaultLayout>
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-3xl font-bold tracking-tight">{{ $t('admin.users.title') }}</h1>
          <p class="text-muted-foreground mt-1">
            {{ $t('admin.users.subtitle') }}
          </p>
        </div>
        <div class="flex gap-2">
          <Button variant="outline" size="sm" @click="fetchUsers" :disabled="isLoading || !tenantId">
            <Spinner v-if="isLoading" size="sm" class="mr-2" />
            <RefreshCw v-else class="mr-2 h-4 w-4" />
            {{ $t('common.refresh') }}
          </Button>
          <Button @click="openCreateDialog" :disabled="!tenantId">
            <Plus class="mr-2 h-4 w-4" />
            {{ $t('admin.users.addUser') }}
          </Button>
        </div>
      </div>

      <!-- No tenant selected -->
      <Alert v-if="!tenantId" variant="default">
        <AlertCircle class="h-4 w-4" />
        <AlertDescription>
          {{ $t('admin.users.selectTenantFirst') }}
        </AlertDescription>
      </Alert>

      <!-- Error -->
      <Alert v-if="error" variant="destructive">
        <AlertCircle class="h-4 w-4" />
        <AlertDescription>{{ error }}</AlertDescription>
      </Alert>

      <template v-if="tenantId">
        <!-- Stats Cards -->
        <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          <Card
            class="cursor-pointer transition-all hover:shadow-md"
            :class="statusFilter === 'all' ? 'ring-2 ring-primary' : ''"
            @click="statusFilter = 'all'"
          >
            <CardContent class="p-4">
              <div class="flex items-center justify-between">
                <div>
                  <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.users.stats.total') }}</p>
                  <p class="text-2xl font-bold mt-1">{{ stats.total }}</p>
                </div>
                <div class="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center">
                  <Users class="h-5 w-5 text-primary" />
                </div>
              </div>
            </CardContent>
          </Card>

          <Card
            class="cursor-pointer transition-all hover:shadow-md"
            :class="statusFilter === 'active' ? 'ring-2 ring-emerald-500' : ''"
            @click="statusFilter = statusFilter === 'active' ? 'all' : 'active'"
          >
            <CardContent class="p-4">
              <div class="flex items-center justify-between">
                <div>
                  <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.users.stats.active') }}</p>
                  <p class="text-2xl font-bold mt-1 text-emerald-600">{{ stats.active }}</p>
                </div>
                <div class="h-10 w-10 rounded-full bg-emerald-500/10 flex items-center justify-center">
                  <UserCheck class="h-5 w-5 text-emerald-500" />
                </div>
              </div>
            </CardContent>
          </Card>

          <Card
            class="cursor-pointer transition-all hover:shadow-md"
            :class="statusFilter === 'inactive' ? 'ring-2 ring-rose-500' : ''"
            @click="statusFilter = statusFilter === 'inactive' ? 'all' : 'inactive'"
          >
            <CardContent class="p-4">
              <div class="flex items-center justify-between">
                <div>
                  <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.users.stats.inactive') }}</p>
                  <p class="text-2xl font-bold mt-1 text-rose-600">{{ stats.inactive }}</p>
                </div>
                <div class="h-10 w-10 rounded-full bg-rose-500/10 flex items-center justify-center">
                  <UserX class="h-5 w-5 text-rose-500" />
                </div>
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardContent class="p-4">
              <div class="flex items-center justify-between">
                <div>
                  <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.users.stats.withRoles') }}</p>
                  <p class="text-2xl font-bold mt-1 text-violet-600">{{ stats.withRoles }}</p>
                </div>
                <div class="h-10 w-10 rounded-full bg-violet-500/10 flex items-center justify-center">
                  <Shield class="h-5 w-5 text-violet-500" />
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
              :placeholder="$t('admin.users.searchPlaceholder')"
              class="pl-9"
            />
          </div>
          <p class="text-sm text-muted-foreground">
            {{ $t('admin.users.showingCount', { count: filteredUsers.length, total: users.length }) }}
          </p>
        </div>

        <!-- Loading -->
        <div v-if="isLoading" class="flex flex-col items-center justify-center py-16">
          <Spinner size="lg" />
          <p class="text-muted-foreground mt-3 text-sm">{{ $t('admin.users.loading') }}</p>
        </div>

        <!-- Empty state -->
        <Card v-else-if="users.length === 0" class="border-dashed">
          <CardContent class="flex flex-col items-center justify-center py-16">
            <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
              <Users class="h-8 w-8 text-muted-foreground" />
            </div>
            <h3 class="text-lg font-semibold mb-1">{{ $t('admin.users.noUsers') }}</h3>
            <p class="text-muted-foreground text-sm mb-4 text-center max-w-sm">
              {{ $t('admin.users.noUsersDescription') }}
            </p>
            <Button @click="openCreateDialog">
              <Plus class="mr-2 h-4 w-4" />
              {{ $t('admin.users.createFirstUser') }}
            </Button>
          </CardContent>
        </Card>

        <!-- No results -->
        <Card v-else-if="filteredUsers.length === 0" class="border-dashed">
          <CardContent class="flex flex-col items-center justify-center py-12">
            <Search class="h-10 w-10 text-muted-foreground mb-3" />
            <h3 class="text-lg font-semibold mb-1">{{ $t('admin.users.noResults') }}</h3>
            <p class="text-muted-foreground text-sm">
              {{ $t('admin.users.noMatch', { query: searchQuery }) }}
            </p>
            <Button variant="outline" class="mt-3" @click="searchQuery = ''; statusFilter = 'all'">
              {{ $t('admin.users.clearFilters') }}
            </Button>
          </CardContent>
        </Card>

        <!-- User List -->
        <div v-else class="flex gap-6">
          <!-- User Table -->
          <Card class="flex-1 min-w-0">
            <CardContent class="p-0">
              <div class="divide-y">
                <div
                  v-for="user in paginatedUsers"
                  :key="user.id"
                  class="flex items-center gap-4 px-4 py-3 hover:bg-muted/50 transition-colors group"
                  :class="selectedUser?.id === user.id ? 'bg-muted/50' : ''"
                >
                  <!-- Avatar -->
                  <div
                    class="h-10 w-10 rounded-full flex items-center justify-center text-white text-sm font-medium shrink-0"
                    :class="[getAvatarColor(user), !user.isActive ? 'opacity-50' : '']"
                  >
                    {{ getInitials(user) }}
                  </div>

                  <!-- User Info -->
                  <div class="flex-1 min-w-0">
                    <div class="flex items-center gap-2">
                      <span class="font-medium truncate" :class="!user.isActive ? 'text-muted-foreground' : ''">
                        {{ displayName(user) }}
                      </span>
                      <Badge
                        v-if="!user.isActive"
                        variant="secondary"
                        class="text-[10px] px-1.5 py-0 bg-rose-100 text-rose-700 dark:bg-rose-900/30 dark:text-rose-400"
                      >
                        {{ $t('common.inactive') }}
                      </Badge>
                    </div>
                    <div class="flex items-center gap-3 mt-0.5">
                      <span class="text-sm text-muted-foreground truncate">@{{ user.username }}</span>
                      <span class="text-muted-foreground hidden sm:inline">·</span>
                      <span class="text-sm text-muted-foreground truncate hidden sm:inline">
                        <Mail class="h-3 w-3 inline-block mr-1 -mt-0.5" />{{ user.email }}
                      </span>
                    </div>
                  </div>

                  <!-- Roles & Permissions -->
                  <div class="hidden lg:flex items-center gap-2 shrink-0 max-w-[400px]">
                    <UserRoleChips
                      :user-id="user.id"
                      :tenant-id="tenantId"
                      :roles="user.roles"
                      editable
                      @updated="onRolesUpdated"
                    />
                    <UserPermissionChips
                      :user-id="user.id"
                      :tenant-id="tenantId"
                      :permissions="user.permissions ?? []"
                      editable
                      @updated="onPermissionsUpdated"
                    />
                  </div>

                  <!-- Actions -->
                  <div class="flex items-center gap-1 shrink-0 opacity-0 group-hover:opacity-100 transition-opacity">
                    <Button
                      variant="ghost"
                      size="sm"
                      class="h-8 w-8 p-0"
                      :title="$t('admin.users.viewDetails')"
                      @click="viewUser(user)"
                    >
                      <Eye class="h-4 w-4" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      class="h-8 w-8 p-0"
                      :title="$t('common.edit')"
                      @click="openEditDialog(user)"
                    >
                      <Pencil class="h-4 w-4" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      class="h-8 w-8 p-0 text-destructive hover:text-destructive"
                      :title="$t('admin.users.deactivateUser')"
                      @click="deleteUser(user)"
                    >
                      <Trash2 class="h-4 w-4" />
                    </Button>
                  </div>
                </div>
              </div>

              <!-- Pagination -->
              <div v-if="totalPages > 1" class="flex items-center justify-between px-4 py-3 border-t bg-muted/30">
                <p class="text-sm text-muted-foreground">
                  {{ $t('admin.users.showingUsers', {
                    start: (currentPage - 1) * pageSize + 1,
                    end: Math.min(currentPage * pageSize, filteredUsers.length),
                    total: filteredUsers.length
                  }) }}
                </p>
                <div class="flex items-center gap-1">
                  <Button
                    variant="outline"
                    size="sm"
                    class="h-8 w-8 p-0"
                    :disabled="currentPage <= 1"
                    @click="currentPage--"
                  >
                    <ChevronLeft class="h-4 w-4" />
                  </Button>
                  <template v-for="page in totalPages" :key="page">
                    <Button
                      v-if="page === 1 || page === totalPages || Math.abs(page - currentPage) <= 1"
                      :variant="page === currentPage ? 'default' : 'outline'"
                      size="sm"
                      class="h-8 w-8 p-0"
                      @click="currentPage = page"
                    >
                      {{ page }}
                    </Button>
                    <span
                      v-else-if="page === 2 && currentPage > 3 || page === totalPages - 1 && currentPage < totalPages - 2"
                      class="px-1 text-muted-foreground"
                    >...</span>
                  </template>
                  <Button
                    variant="outline"
                    size="sm"
                    class="h-8 w-8 p-0"
                    :disabled="currentPage >= totalPages"
                    @click="currentPage++"
                  >
                    <ChevronRight class="h-4 w-4" />
                  </Button>
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
            <Card v-if="selectedUser" class="w-80 shrink-0 self-start sticky top-20">
              <CardContent class="p-5">
                <!-- User profile header -->
                <div class="flex flex-col items-center text-center pb-4 border-b">
                  <div
                    class="h-16 w-16 rounded-full flex items-center justify-center text-white text-xl font-semibold mb-3"
                    :class="[getAvatarColor(selectedUser), !selectedUser.isActive ? 'opacity-50' : '']"
                  >
                    {{ getInitials(selectedUser) }}
                  </div>
                  <h3 class="font-semibold text-lg">{{ displayName(selectedUser) }}</h3>
                  <p class="text-sm text-muted-foreground">@{{ selectedUser.username }}</p>
                  <Badge
                    class="mt-2"
                    :variant="selectedUser.isActive ? 'default' : 'secondary'"
                    :class="selectedUser.isActive ? 'bg-emerald-600 hover:bg-emerald-700' : 'bg-rose-100 text-rose-700 dark:bg-rose-900/30 dark:text-rose-400'"
                  >
                    {{ selectedUser.isActive ? $t('common.active') : $t('common.inactive') }}
                  </Badge>
                </div>

                <!-- Details -->
                <div class="space-y-3 py-4 border-b">
                  <div>
                    <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider">{{ $t('admin.users.emailCol') }}</p>
                    <p class="text-sm mt-0.5 break-all">{{ selectedUser.email }}</p>
                  </div>
                  <div v-if="selectedUser.firstName || selectedUser.lastName">
                    <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider">{{ $t('admin.users.nameCol') }}</p>
                    <p class="text-sm mt-0.5">{{ [selectedUser.firstName, selectedUser.lastName].filter(Boolean).join(' ') }}</p>
                  </div>
                </div>

                <!-- Roles -->
                <div class="py-4 border-b">
                  <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider mb-2">{{ $t('admin.users.rolesCol') }}</p>
                  <UserRoleChips
                    :user-id="selectedUser.id"
                    :tenant-id="tenantId"
                    :roles="selectedUser.roles"
                    editable
                    @updated="onRolesUpdated"
                  />
                </div>

                <!-- Permissions -->
                <div class="py-4 border-b">
                  <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider mb-2">{{ $t('admin.users.permissionsCol') }}</p>
                  <UserPermissionChips
                    :user-id="selectedUser.id"
                    :tenant-id="tenantId"
                    :permissions="selectedUser.permissions ?? []"
                    editable
                    @updated="onPermissionsUpdated"
                  />
                </div>

                <!-- Actions -->
                <div class="flex flex-col gap-2 pt-4">
                  <Button variant="outline" size="sm" class="w-full justify-start" @click="openEditDialog(selectedUser)">
                    <Pencil class="mr-2 h-4 w-4" />
                    {{ $t('admin.users.editUser') }}
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    class="w-full justify-start"
                    @click="toggleUserStatus(selectedUser)"
                  >
                    <component :is="selectedUser.isActive ? UserX : UserCheck" class="mr-2 h-4 w-4" />
                    {{ selectedUser.isActive ? $t('admin.users.deactivateUser') : $t('admin.users.activateUser') }}
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    class="w-full justify-start text-destructive hover:text-destructive"
                    @click="deleteUser(selectedUser)"
                  >
                    <Trash2 class="mr-2 h-4 w-4" />
                    {{ $t('admin.users.deleteUser') }}
                  </Button>
                </div>
              </CardContent>
            </Card>
          </transition>
        </div>
      </template>
    </div>

    <!-- User create/edit dialog -->
    <UserDialog
      :open="dialogOpen"
      :user="editingUser"
      :tenant-id="tenantId"
      @update:open="dialogOpen = $event"
      @saved="onUserSaved"
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
