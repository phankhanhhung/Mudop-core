<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import { X, Plus, Shield, ChevronDown, AlertCircle } from 'lucide-vue-next'
import { userService } from '@/services/userService'
import { roleService } from '@/services/roleService'
import type { Role } from '@/types/role'

interface Props {
  userId: string
  tenantId: string
  roles: string[]
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
const isLoadingRoles = ref(false)
const errorMessage = ref<string | null>(null)
const availableRoles = ref<Role[]>([])

const roleColors: Record<string, string> = {
  admin: 'bg-rose-100 text-rose-800 dark:bg-rose-900/40 dark:text-rose-300 border-rose-200 dark:border-rose-800',
  manager: 'bg-amber-100 text-amber-800 dark:bg-amber-900/40 dark:text-amber-300 border-amber-200 dark:border-amber-800',
  editor: 'bg-blue-100 text-blue-800 dark:bg-blue-900/40 dark:text-blue-300 border-blue-200 dark:border-blue-800',
  viewer: 'bg-slate-100 text-slate-800 dark:bg-slate-900/40 dark:text-slate-300 border-slate-200 dark:border-slate-800'
}

function getRoleColor(role: string): string {
  const key = role.toLowerCase()
  return roleColors[key] || 'bg-violet-100 text-violet-800 dark:bg-violet-900/40 dark:text-violet-300 border-violet-200 dark:border-violet-800'
}

function getRoleIcon(role: string): string {
  const key = role.toLowerCase()
  if (key === 'admin') return 'text-rose-600 dark:text-rose-400'
  if (key === 'manager') return 'text-amber-600 dark:text-amber-400'
  if (key === 'editor') return 'text-blue-600 dark:text-blue-400'
  if (key === 'viewer') return 'text-slate-600 dark:text-slate-400'
  return 'text-violet-600 dark:text-violet-400'
}

// Roles that can still be assigned (not already assigned)
const unassignedRoles = computed(() => {
  const assignedLower = new Set(props.roles.map(r => r.toLowerCase()))
  return availableRoles.value.filter(r => !assignedLower.has(r.Name.toLowerCase()))
})

async function fetchAvailableRoles() {
  isLoadingRoles.value = true
  try {
    availableRoles.value = await roleService.listRoles()
  } catch {
    // Non-critical, will show empty dropdown
  } finally {
    isLoadingRoles.value = false
  }
}

async function toggleDropdown() {
  if (isDropdownOpen.value) {
    isDropdownOpen.value = false
    return
  }
  // Fetch roles every time we open to get latest
  await fetchAvailableRoles()
  isDropdownOpen.value = true
}

async function addRole(roleName: string) {
  if (isProcessing.value) return
  errorMessage.value = null

  isProcessing.value = true
  try {
    await userService.assignRole(props.tenantId, props.userId, { roleName })
    isDropdownOpen.value = false
    emit('updated')
  } catch (e) {
    errorMessage.value = e instanceof Error ? e.message : t('admin.users.roleAssignFailed')
    // Auto-clear error after 4 seconds
    setTimeout(() => { errorMessage.value = null }, 4000)
  } finally {
    isProcessing.value = false
  }
}

async function removeRole(roleName: string) {
  if (isProcessing.value) return
  errorMessage.value = null

  isProcessing.value = true
  try {
    await userService.removeRole(props.tenantId, props.userId, roleName)
    emit('updated')
  } catch (e) {
    errorMessage.value = e instanceof Error ? e.message : t('admin.users.roleRemoveFailed')
    setTimeout(() => { errorMessage.value = null }, 4000)
  } finally {
    isProcessing.value = false
  }
}

// Close dropdown on click outside
function onClickOutside(event: MouseEvent) {
  const target = event.target as HTMLElement
  if (!target.closest('[data-role-chips]')) {
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
  // Pre-fetch roles if editable
  if (props.editable) {
    fetchAvailableRoles()
  }
})
</script>

<template>
  <div data-role-chips class="space-y-2">
    <!-- Role badges -->
    <div class="flex flex-wrap items-center gap-2">
      <Badge
        v-for="role in roles"
        :key="role"
        variant="outline"
        class="flex items-center gap-1.5 px-2.5 py-1 text-sm font-medium border"
        :class="getRoleColor(role)"
      >
        <Shield class="h-3.5 w-3.5 shrink-0" :class="getRoleIcon(role)" />
        {{ role }}
        <button
          v-if="editable"
          class="ml-1 rounded-full hover:bg-black/10 dark:hover:bg-white/10 p-0.5 transition-colors"
          :disabled="isProcessing"
          :title="t('admin.users.removeRole', { role })"
          data-testid="remove-role"
          @click.stop="removeRole(role)"
        >
          <X class="h-3.5 w-3.5" />
        </button>
      </Badge>

      <span v-if="roles.length === 0" class="text-sm text-muted-foreground italic">
        {{ t('admin.users.noRoles') }}
      </span>

      <!-- Add role dropdown trigger -->
      <div v-if="editable" class="relative">
        <Button
          variant="outline"
          size="sm"
          class="h-8 px-3 text-sm gap-1.5"
          data-testid="add-role-button"
          :disabled="isProcessing"
          @click.stop="toggleDropdown"
        >
          <Plus class="h-4 w-4" />
          {{ t('admin.users.addRole') }}
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
            data-testid="role-dropdown"
          >
            <!-- Loading -->
            <div v-if="isLoadingRoles" class="flex items-center justify-center py-4 px-3">
              <Spinner size="sm" class="mr-2" />
              <span class="text-sm text-muted-foreground">{{ t('admin.users.loadingRoles') }}</span>
            </div>

            <!-- No roles available to assign -->
            <div v-else-if="unassignedRoles.length === 0" class="py-4 px-3 text-center">
              <Shield class="h-5 w-5 text-muted-foreground mx-auto mb-1.5" />
              <p class="text-sm text-muted-foreground">{{ t('admin.users.allRolesAssigned') }}</p>
            </div>

            <!-- Role list -->
            <template v-else>
              <button
                v-for="role in unassignedRoles"
                :key="role.Id"
                class="w-full flex items-center gap-2.5 px-3 py-2.5 text-left hover:bg-muted/60 transition-colors text-sm"
                :class="isProcessing ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'"
                :disabled="isProcessing"
                data-testid="role-option"
                @click.stop="addRole(role.Name)"
              >
                <div class="h-7 w-7 rounded-md flex items-center justify-center shrink-0" :class="getRoleColor(role.Name)">
                  <Shield class="h-3.5 w-3.5" :class="getRoleIcon(role.Name)" />
                </div>
                <div class="flex-1 min-w-0">
                  <span class="font-medium block truncate">{{ role.Name }}</span>
                  <span v-if="role.Description" class="text-xs text-muted-foreground block truncate">
                    {{ role.Description }}
                  </span>
                </div>
                <Badge
                  v-if="role.IsSystem"
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
      data-testid="role-error"
    >
      <AlertCircle class="h-4 w-4 shrink-0" />
      {{ errorMessage }}
    </div>
  </div>
</template>
