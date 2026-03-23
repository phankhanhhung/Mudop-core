<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import { X, Plus, Key, ChevronDown, AlertCircle } from 'lucide-vue-next'
import { userService } from '@/services/userService'

interface Props {
  userId: string
  tenantId: string
  permissions: string[]
  editable?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  editable: false
})

const emit = defineEmits<{
  updated: []
}>()

const { t } = useI18n()

const isDropdownOpen = ref(false)
const isProcessing = ref(false)
const isLoadingPermissions = ref(false)
const errorMessage = ref<string | null>(null)
const availablePermissions = ref<{ Id: string; Name: string; Description?: string; IsSystemRole?: boolean }[]>([])

function getPermissionColor(_permission: string): string {
  return 'bg-teal-100 text-teal-800 dark:bg-teal-900/40 dark:text-teal-300 border-teal-200 dark:border-teal-800'
}

function getPermissionIconColor(): string {
  return 'text-teal-600 dark:text-teal-400'
}

// Permissions that can still be assigned (not already assigned)
const unassignedPermissions = computed(() => {
  const assignedLower = new Set(props.permissions.map(p => p.toLowerCase()))
  return availablePermissions.value.filter(p => !assignedLower.has(p.Name.toLowerCase()))
})

async function fetchAvailablePermissions() {
  isLoadingPermissions.value = true
  try {
    availablePermissions.value = await userService.listPermissions(props.tenantId)
  } catch {
    // Non-critical, will show empty dropdown
  } finally {
    isLoadingPermissions.value = false
  }
}

async function toggleDropdown() {
  if (isDropdownOpen.value) {
    isDropdownOpen.value = false
    return
  }
  // Fetch permissions every time we open to get latest
  await fetchAvailablePermissions()
  isDropdownOpen.value = true
}

async function addPermission(permissionName: string) {
  if (isProcessing.value) return
  errorMessage.value = null

  isProcessing.value = true
  try {
    await userService.assignPermission(props.tenantId, props.userId, { permissionName })
    isDropdownOpen.value = false
    emit('updated')
  } catch (e) {
    errorMessage.value = e instanceof Error ? e.message : t('admin.users.permissionAssignFailed')
    setTimeout(() => { errorMessage.value = null }, 4000)
  } finally {
    isProcessing.value = false
  }
}

async function removePermission(permissionName: string) {
  if (isProcessing.value) return
  errorMessage.value = null

  isProcessing.value = true
  try {
    await userService.removePermission(props.tenantId, props.userId, permissionName)
    emit('updated')
  } catch (e) {
    errorMessage.value = e instanceof Error ? e.message : t('admin.users.permissionRemoveFailed')
    setTimeout(() => { errorMessage.value = null }, 4000)
  } finally {
    isProcessing.value = false
  }
}

// Close dropdown on click outside
function onClickOutside(event: MouseEvent) {
  const target = event.target as HTMLElement
  if (!target.closest('[data-permission-chips]')) {
    isDropdownOpen.value = false
  }
}

watch(isDropdownOpen, (open) => {
  if (open) {
    document.addEventListener('click', onClickOutside, { capture: true })
  } else {
    document.removeEventListener('click', onClickOutside, { capture: true })
  }
})

onMounted(() => {
  if (props.editable) {
    fetchAvailablePermissions()
  }
})
</script>

<template>
  <div data-permission-chips class="space-y-2">
    <!-- Permission badges -->
    <div class="flex flex-wrap items-center gap-2">
      <Badge
        v-for="permission in permissions"
        :key="permission"
        variant="outline"
        class="flex items-center gap-1.5 px-2.5 py-1 text-sm font-medium border"
        :class="getPermissionColor(permission)"
      >
        <Key class="h-3.5 w-3.5 shrink-0" :class="getPermissionIconColor()" />
        {{ permission }}
        <button
          v-if="editable"
          class="ml-1 rounded-full hover:bg-black/10 dark:hover:bg-white/10 p-0.5 transition-colors"
          :disabled="isProcessing"
          :title="t('admin.users.removePermission', { permission })"
          data-testid="remove-permission"
          @click.stop="removePermission(permission)"
        >
          <X class="h-3.5 w-3.5" />
        </button>
      </Badge>

      <span v-if="permissions.length === 0" class="text-sm text-muted-foreground italic">
        {{ t('admin.users.noPermissions') }}
      </span>

      <!-- Add permission dropdown trigger -->
      <div v-if="editable" class="relative">
        <Button
          variant="outline"
          size="sm"
          class="h-8 px-3 text-sm gap-1.5"
          data-testid="add-permission-button"
          :disabled="isProcessing"
          @click.stop="toggleDropdown"
        >
          <Plus class="h-4 w-4" />
          {{ t('admin.users.addPermission') }}
          <ChevronDown
            class="h-3.5 w-3.5 transition-transform"
            :class="isDropdownOpen ? 'rotate-180' : ''"
          />
        </Button>

        <!-- Dropdown menu -->
        <transition
          enter-active-class="transition ease-out duration-100"
          enter-from-class="opacity-0 scale-95"
          enter-to-class="opacity-100 scale-100"
          leave-active-class="transition ease-in duration-75"
          leave-from-class="opacity-100 scale-100"
          leave-to-class="opacity-0 scale-95"
        >
          <div
            v-if="isDropdownOpen"
            class="absolute left-0 top-full mt-1 z-50 min-w-[200px] max-h-[240px] overflow-y-auto rounded-lg border bg-background shadow-lg"
            data-testid="permission-dropdown"
          >
            <!-- Loading -->
            <div v-if="isLoadingPermissions" class="flex items-center justify-center py-4 px-3">
              <Spinner size="sm" class="mr-2" />
              <span class="text-sm text-muted-foreground">{{ t('admin.users.loadingPermissions') }}</span>
            </div>

            <!-- No permissions available to assign -->
            <div v-else-if="unassignedPermissions.length === 0" class="py-4 px-3 text-center">
              <Key class="h-5 w-5 text-muted-foreground mx-auto mb-1.5" />
              <p class="text-sm text-muted-foreground">{{ t('admin.users.allPermissionsAssigned') }}</p>
            </div>

            <!-- Permission list -->
            <template v-else>
              <button
                v-for="perm in unassignedPermissions"
                :key="perm.Id"
                class="w-full flex items-center gap-2.5 px-3 py-2.5 text-left hover:bg-muted/60 transition-colors text-sm"
                :class="isProcessing ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'"
                :disabled="isProcessing"
                data-testid="permission-option"
                @click.stop="addPermission(perm.Name)"
              >
                <div class="h-7 w-7 rounded-md flex items-center justify-center shrink-0" :class="getPermissionColor(perm.Name)">
                  <Key class="h-3.5 w-3.5" :class="getPermissionIconColor()" />
                </div>
                <div class="flex-1 min-w-0">
                  <span class="font-medium block truncate">{{ perm.Name }}</span>
                  <span v-if="perm.Description" class="text-xs text-muted-foreground block truncate">
                    {{ perm.Description }}
                  </span>
                </div>
                <Badge
                  v-if="perm.IsSystemRole"
                  variant="outline"
                  class="text-[10px] px-1.5 py-0 border-amber-300 text-amber-700 dark:border-amber-700 dark:text-amber-400 shrink-0"
                >
                  {{ t('common.system') }}
                </Badge>
              </button>
            </template>
          </div>
        </transition>
      </div>
    </div>

    <!-- Error message -->
    <div
      v-if="errorMessage"
      class="flex items-center gap-1.5 text-sm text-destructive"
      data-testid="permission-error"
    >
      <AlertCircle class="h-4 w-4 shrink-0" />
      {{ errorMessage }}
    </div>
  </div>
</template>
