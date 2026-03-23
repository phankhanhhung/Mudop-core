<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { roleService } from '@/services/roleService'
import { Badge } from '@/components/ui/badge'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Spinner } from '@/components/ui/spinner'
import { AlertCircle, ShieldCheck, Check, Minus } from 'lucide-vue-next'
import type { Permission, RolePermission } from '@/types/role'

interface Props {
  roleId: string
}

const props = defineProps<Props>()
const { t } = useI18n()

const permissions = ref<Permission[]>([])
const rolePermissions = ref<RolePermission[]>([])
const isLoading = ref(false)
const error = ref<string | null>(null)
const togglingIds = ref<Set<string>>(new Set())

// Map permissionId -> RolePermission record for quick lookup
const assignedMap = computed(() => {
  const map = new Map<string, RolePermission>()
  for (const rp of rolePermissions.value) {
    map.set(rp.PermissionId, rp)
  }
  return map
})

// Group permissions by Resource
const groupedPermissions = computed(() => {
  const groups = new Map<string, Permission[]>()
  for (const p of permissions.value) {
    const key = p.Resource
    if (!groups.has(key)) {
      groups.set(key, [])
    }
    groups.get(key)!.push(p)
  }
  return Array.from(groups.entries()).sort(([a], [b]) => a.localeCompare(b))
})

// Collect all unique actions across all permissions
const allActions = computed(() => {
  const actions = new Set<string>()
  for (const p of permissions.value) {
    actions.add(p.ActionType)
  }
  const preferred = ['Read', 'Create', 'Update', 'Delete']
  const sorted = Array.from(actions).sort((a, b) => {
    const ai = preferred.indexOf(a)
    const bi = preferred.indexOf(b)
    if (ai !== -1 && bi !== -1) return ai - bi
    if (ai !== -1) return -1
    if (bi !== -1) return 1
    return a.localeCompare(b)
  })
  return sorted
})

// Stats
const assignedCount = computed(() => rolePermissions.value.length)
const totalCount = computed(() => permissions.value.length)

const actionColors: Record<string, string> = {
  Read: 'text-blue-600 dark:text-blue-400',
  Create: 'text-emerald-600 dark:text-emerald-400',
  Update: 'text-amber-600 dark:text-amber-400',
  Delete: 'text-rose-600 dark:text-rose-400'
}

const actionBgColors: Record<string, string> = {
  Read: 'bg-blue-50 dark:bg-blue-950/30',
  Create: 'bg-emerald-50 dark:bg-emerald-950/30',
  Update: 'bg-amber-50 dark:bg-amber-950/30',
  Delete: 'bg-rose-50 dark:bg-rose-950/30'
}

function getActionColor(action: string): string {
  return actionColors[action] || 'text-primary'
}

function getActionBgColor(action: string): string {
  return actionBgColors[action] || 'bg-primary/5'
}

function findPermission(resource: string, action: string): Permission | undefined {
  return permissions.value.find(p => p.Resource === resource && p.ActionType === action)
}

function isAssigned(permissionId: string): boolean {
  return assignedMap.value.has(permissionId)
}

function isToggling(permissionId: string): boolean {
  return togglingIds.value.has(permissionId)
}

// Count assigned permissions per resource
function getResourceAssignedCount(resource: string): number {
  const perms = permissions.value.filter(p => p.Resource === resource)
  return perms.filter(p => isAssigned(p.Id)).length
}

function getResourceTotalCount(resource: string): number {
  return permissions.value.filter(p => p.Resource === resource).length
}

async function togglePermission(resource: string, action: string) {
  const permission = findPermission(resource, action)
  if (!permission) return

  const permId = permission.Id
  togglingIds.value.add(permId)
  error.value = null

  try {
    const existing = assignedMap.value.get(permId)
    if (existing) {
      await roleService.removePermission(existing.Id)
      rolePermissions.value = rolePermissions.value.filter(rp => rp.Id !== existing.Id)
    } else {
      const rp = await roleService.assignPermission(props.roleId, permId)
      rolePermissions.value.push(rp)
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('admin.roles.failedToToggle')
  } finally {
    togglingIds.value.delete(permId)
  }
}

async function fetchData() {
  isLoading.value = true
  error.value = null
  try {
    const [perms, rolePerms] = await Promise.all([
      roleService.listPermissions(),
      roleService.getRolePermissions(props.roleId)
    ])
    permissions.value = perms
    rolePermissions.value = rolePerms
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('admin.roles.failedToLoadPermissions')
  } finally {
    isLoading.value = false
  }
}

watch(() => props.roleId, () => {
  if (props.roleId) {
    fetchData()
  }
})

onMounted(() => {
  if (props.roleId) {
    fetchData()
  }
})
</script>

<template>
  <div>
    <!-- Loading -->
    <div v-if="isLoading" class="flex flex-col items-center justify-center py-12">
      <Spinner size="lg" />
      <p class="text-muted-foreground mt-3 text-sm">{{ t('admin.roles.loadingPermissions') }}</p>
    </div>

    <!-- Error -->
    <Alert v-else-if="error" variant="destructive" class="mb-4">
      <AlertCircle class="h-4 w-4" />
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <!-- No permissions -->
    <div v-else-if="permissions.length === 0" class="flex flex-col items-center justify-center py-12 text-muted-foreground">
      <div class="h-14 w-14 rounded-full bg-muted flex items-center justify-center mb-3">
        <ShieldCheck class="h-7 w-7 text-muted-foreground/50" />
      </div>
      <h3 class="font-medium mb-1">{{ t('admin.roles.noPermissions') }}</h3>
      <p class="text-sm text-center max-w-xs">{{ t('admin.roles.noPermissionsDescription') }}</p>
    </div>

    <!-- Permission matrix -->
    <div v-else>
      <!-- Summary bar -->
      <div class="flex items-center justify-between mb-4 pb-3 border-b">
        <div class="flex items-center gap-2">
          <Badge variant="outline" class="gap-1">
            <Check class="h-3 w-3" />
            {{ assignedCount }} / {{ totalCount }}
          </Badge>
          <span class="text-xs text-muted-foreground">{{ t('admin.roles.permissionsAssigned') }}</span>
        </div>
        <div class="flex items-center gap-2">
          <template v-for="action in allActions" :key="action">
            <span class="text-xs font-medium px-2 py-0.5 rounded-full" :class="[getActionColor(action), getActionBgColor(action)]">
              {{ action }}
            </span>
          </template>
        </div>
      </div>

      <!-- Resource rows -->
      <div class="space-y-2">
        <div
          v-for="[resource, _perms] in groupedPermissions"
          :key="resource"
          class="rounded-lg border p-3 hover:bg-muted/30 transition-colors"
        >
          <div class="flex items-center justify-between">
            <!-- Resource name and count -->
            <div class="flex items-center gap-2 min-w-0">
              <span class="font-medium text-sm">{{ resource }}</span>
              <span class="text-xs text-muted-foreground">
                {{ getResourceAssignedCount(resource) }}/{{ getResourceTotalCount(resource) }}
              </span>
            </div>

            <!-- Permission toggles -->
            <div class="flex items-center gap-2">
              <template v-for="action in allActions" :key="`${resource}-${action}`">
                <template v-if="findPermission(resource, action)">
                  <!-- Toggling spinner -->
                  <div
                    v-if="isToggling(findPermission(resource, action)!.Id)"
                    class="h-8 w-16 flex items-center justify-center"
                  >
                    <Spinner size="sm" />
                  </div>
                  <!-- Toggle button -->
                  <button
                    v-else
                    class="h-8 px-2.5 rounded-md text-xs font-medium transition-all flex items-center gap-1 border"
                    :class="isAssigned(findPermission(resource, action)!.Id)
                      ? `${getActionBgColor(action)} ${getActionColor(action)} border-current/20 shadow-sm`
                      : 'bg-transparent text-muted-foreground/40 border-transparent hover:border-muted-foreground/20 hover:text-muted-foreground'"
                    @click="togglePermission(resource, action)"
                  >
                    <Check
                      v-if="isAssigned(findPermission(resource, action)!.Id)"
                      class="h-3 w-3"
                    />
                    <Minus v-else class="h-3 w-3" />
                    {{ action }}
                  </button>
                </template>
                <!-- No permission for this action -->
                <div v-else class="h-8 w-16" />
              </template>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
