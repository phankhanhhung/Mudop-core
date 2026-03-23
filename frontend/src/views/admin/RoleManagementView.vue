<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useConfirmDialog } from '@/composables/useConfirmDialog'
import { roleService } from '@/services/roleService'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import RoleDialog from '@/components/admin/RoleDialog.vue'
import PermissionMatrix from '@/components/admin/PermissionMatrix.vue'
import { ConfirmDialog } from '@/components/common'
import { Card, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Spinner } from '@/components/ui/spinner'
import {
  Shield,
  Plus,
  Search,
  Pencil,
  Trash2,
  AlertCircle,
  RefreshCw,
  KeyRound,
  Lock,
  ShieldCheck,
  ChevronRight,
  X
} from 'lucide-vue-next'
import type { Role } from '@/types/role'

const { t } = useI18n()
const confirmDialog = useConfirmDialog()

const roles = ref<Role[]>([])
const isLoading = ref(false)
const error = ref<string | null>(null)
const searchQuery = ref('')

// Dialog state
const dialogOpen = ref(false)
const editingRole = ref<Role | undefined>(undefined)

// Permission panel state
const selectedRoleId = ref<string | null>(null)

// Stats
const stats = computed(() => {
  const total = roles.value.length
  const system = roles.value.filter(r => r.IsSystem).length
  const custom = total - system
  return { total, system, custom }
})

const filteredRoles = computed(() => {
  const query = searchQuery.value.toLowerCase().trim()
  if (!query) return roles.value
  return roles.value.filter(r =>
    r.Name.toLowerCase().includes(query) ||
    (r.Description && r.Description.toLowerCase().includes(query))
  )
})

const selectedRole = computed(() =>
  roles.value.find(r => r.Id === selectedRoleId.value)
)

function getRoleIcon(role: Role): typeof Shield {
  if (role.IsSystem) return Lock
  return Shield
}

function getRoleColor(role: Role): string {
  if (role.IsSystem) return 'bg-amber-500'
  const colors = ['bg-blue-500', 'bg-emerald-500', 'bg-violet-500', 'bg-rose-500', 'bg-cyan-500', 'bg-indigo-500']
  let hash = 0
  for (const char of role.Name) {
    hash = char.charCodeAt(0) + ((hash << 5) - hash)
  }
  return colors[Math.abs(hash) % colors.length]
}

async function fetchRoles() {
  isLoading.value = true
  error.value = null
  try {
    roles.value = await roleService.listRoles()
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('admin.roles.failedToLoad')
  } finally {
    isLoading.value = false
  }
}

function openCreateDialog() {
  editingRole.value = undefined
  dialogOpen.value = true
}

function openEditDialog(role: Role) {
  editingRole.value = role
  dialogOpen.value = true
}

function selectRole(role: Role) {
  selectedRoleId.value = selectedRoleId.value === role.Id ? null : role.Id
}

async function deleteRole(role: Role) {
  const confirmed = await confirmDialog.confirm({
    title: t('admin.roles.deleteRole'),
    description: t('admin.roles.deleteConfirm', { name: role.Name }),
    confirmLabel: t('common.delete'),
    variant: 'destructive'
  })
  if (!confirmed) return

  try {
    await roleService.deleteRole(role.Id)
    if (selectedRoleId.value === role.Id) {
      selectedRoleId.value = null
    }
    await fetchRoles()
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('admin.roles.failedToDelete')
  }
}

function onRoleSaved() {
  fetchRoles()
}

onMounted(() => {
  fetchRoles()
})
</script>

<template>
  <DefaultLayout>
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-3xl font-bold tracking-tight">{{ $t('admin.roles.title') }}</h1>
          <p class="text-muted-foreground mt-1">
            {{ $t('admin.roles.subtitle') }}
          </p>
        </div>
        <div class="flex gap-2">
          <Button variant="outline" size="sm" @click="fetchRoles" :disabled="isLoading">
            <Spinner v-if="isLoading" size="sm" class="mr-2" />
            <RefreshCw v-else class="mr-2 h-4 w-4" />
            {{ $t('common.refresh') }}
          </Button>
          <Button @click="openCreateDialog">
            <Plus class="mr-2 h-4 w-4" />
            {{ $t('admin.roles.createRole') }}
          </Button>
        </div>
      </div>

      <!-- Error -->
      <Alert v-if="error" variant="destructive">
        <AlertCircle class="h-4 w-4" />
        <AlertDescription>{{ error }}</AlertDescription>
      </Alert>

      <!-- Stats Cards -->
      <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <Card>
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.roles.stats.total') }}</p>
                <p class="text-2xl font-bold mt-1">{{ stats.total }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center">
                <Shield class="h-5 w-5 text-primary" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.roles.stats.system') }}</p>
                <p class="text-2xl font-bold mt-1 text-amber-600">{{ stats.system }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-amber-500/10 flex items-center justify-center">
                <Lock class="h-5 w-5 text-amber-500" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.roles.stats.custom') }}</p>
                <p class="text-2xl font-bold mt-1 text-emerald-600">{{ stats.custom }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-emerald-500/10 flex items-center justify-center">
                <ShieldCheck class="h-5 w-5 text-emerald-500" />
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      <div class="flex gap-6">
        <!-- Left panel: Role list -->
        <div class="w-80 shrink-0">
          <!-- Search -->
          <div class="relative mb-3">
            <Search class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
            <Input
              v-model="searchQuery"
              :placeholder="$t('admin.roles.searchPlaceholder')"
              class="pl-9"
            />
          </div>

          <!-- Loading -->
          <div v-if="isLoading" class="flex flex-col items-center justify-center py-16">
            <Spinner size="lg" />
            <p class="text-muted-foreground mt-3 text-sm">{{ $t('admin.roles.loading') }}</p>
          </div>

          <!-- Empty state -->
          <Card v-else-if="roles.length === 0" class="border-dashed">
            <CardContent class="flex flex-col items-center justify-center py-12">
              <div class="h-14 w-14 rounded-full bg-muted flex items-center justify-center mb-3">
                <Shield class="h-7 w-7 text-muted-foreground" />
              </div>
              <h3 class="font-semibold mb-1">{{ $t('admin.roles.noRoles') }}</h3>
              <p class="text-muted-foreground text-sm mb-3 text-center">{{ $t('admin.roles.noRolesDescription') }}</p>
              <Button size="sm" @click="openCreateDialog">
                <Plus class="mr-2 h-4 w-4" />
                {{ $t('admin.roles.createFirstRole') }}
              </Button>
            </CardContent>
          </Card>

          <!-- No results -->
          <Card v-else-if="filteredRoles.length === 0" class="border-dashed">
            <CardContent class="flex flex-col items-center justify-center py-8">
              <Search class="h-8 w-8 text-muted-foreground mb-2" />
              <p class="text-sm text-muted-foreground">{{ $t('admin.roles.noMatch', { query: searchQuery }) }}</p>
              <Button variant="outline" size="sm" class="mt-2" @click="searchQuery = ''">
                {{ $t('admin.roles.clearSearch') }}
              </Button>
            </CardContent>
          </Card>

          <!-- Role list -->
          <div v-else class="space-y-2">
            <button
              v-for="role in filteredRoles"
              :key="role.Id"
              class="w-full text-left rounded-lg border p-3 transition-all hover:shadow-sm group"
              :class="selectedRoleId === role.Id
                ? 'border-primary bg-primary/5 shadow-sm ring-1 ring-primary/20'
                : 'border-border hover:border-muted-foreground/30'"
              @click="selectRole(role)"
            >
              <div class="flex items-center gap-3">
                <!-- Role icon -->
                <div
                  class="h-9 w-9 rounded-lg flex items-center justify-center text-white shrink-0"
                  :class="getRoleColor(role)"
                >
                  <component :is="getRoleIcon(role)" class="h-4 w-4" />
                </div>

                <!-- Role info -->
                <div class="flex-1 min-w-0">
                  <div class="flex items-center gap-2">
                    <span class="font-medium text-sm truncate">{{ role.Name }}</span>
                    <Badge
                      v-if="role.IsSystem"
                      variant="outline"
                      class="text-[10px] px-1.5 py-0 border-amber-300 text-amber-700 dark:border-amber-700 dark:text-amber-400 shrink-0"
                    >
                      {{ $t('common.system') }}
                    </Badge>
                  </div>
                  <p v-if="role.Description" class="text-xs text-muted-foreground mt-0.5 truncate">
                    {{ role.Description }}
                  </p>
                  <p v-else class="text-xs text-muted-foreground/50 mt-0.5 italic">
                    {{ $t('admin.roles.noDescription') }}
                  </p>
                </div>

                <!-- Arrow indicator -->
                <ChevronRight
                  class="h-4 w-4 text-muted-foreground shrink-0 transition-transform"
                  :class="selectedRoleId === role.Id ? 'text-primary rotate-0' : 'opacity-0 group-hover:opacity-50'"
                />
              </div>

              <!-- Action buttons (shown on hover) -->
              <div class="flex items-center gap-1 mt-2 pt-2 border-t opacity-0 group-hover:opacity-100 transition-opacity">
                <Button
                  variant="ghost"
                  size="sm"
                  class="h-7 text-xs gap-1.5 flex-1"
                  @click.stop="openEditDialog(role)"
                >
                  <Pencil class="h-3 w-3" />
                  {{ $t('common.edit') }}
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  class="h-7 text-xs gap-1.5 flex-1"
                  :class="role.IsSystem ? 'text-muted-foreground cursor-not-allowed' : 'text-destructive hover:text-destructive'"
                  :disabled="role.IsSystem"
                  :title="role.IsSystem ? $t('admin.roles.systemRole') : undefined"
                  @click.stop="deleteRole(role)"
                >
                  <Trash2 class="h-3 w-3" />
                  {{ $t('common.delete') }}
                </Button>
              </div>
            </button>
          </div>
        </div>

        <!-- Right panel: Permission matrix -->
        <Card class="flex-1 min-w-0 self-start sticky top-20">
          <CardContent class="p-0">
            <!-- Panel header -->
            <div class="flex items-center justify-between px-5 py-4 border-b">
              <div class="flex items-center gap-3">
                <div class="h-9 w-9 rounded-lg bg-primary/10 flex items-center justify-center">
                  <KeyRound class="h-4 w-4 text-primary" />
                </div>
                <div>
                  <h3 class="font-semibold text-sm">
                    {{ selectedRole ? $t('admin.roles.permissionsFor', { name: selectedRole.Name }) : $t('admin.roles.permissions') }}
                  </h3>
                  <p class="text-xs text-muted-foreground">
                    {{ selectedRole
                      ? $t('admin.roles.toggleHint')
                      : $t('admin.roles.selectRoleHint')
                    }}
                  </p>
                </div>
              </div>
              <Button
                v-if="selectedRole"
                variant="ghost"
                size="sm"
                class="h-8 w-8 p-0"
                @click="selectedRoleId = null"
              >
                <X class="h-4 w-4" />
              </Button>
            </div>

            <!-- Empty state -->
            <div v-if="!selectedRoleId" class="flex flex-col items-center justify-center py-20 text-muted-foreground">
              <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
                <KeyRound class="h-8 w-8 text-muted-foreground/50" />
              </div>
              <h3 class="font-medium mb-1">{{ $t('admin.roles.selectRoleToView') }}</h3>
              <p class="text-sm text-center max-w-xs">{{ $t('admin.roles.selectRoleDescription') }}</p>
            </div>

            <!-- Permission matrix -->
            <div v-else class="p-5">
              <PermissionMatrix
                :key="selectedRoleId"
                :role-id="selectedRoleId"
              />
            </div>
          </CardContent>
        </Card>
      </div>
    </div>

    <!-- Role create/edit dialog -->
    <RoleDialog
      :open="dialogOpen"
      :role="editingRole"
      @update:open="dialogOpen = $event"
      @saved="onRoleSaved"
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
