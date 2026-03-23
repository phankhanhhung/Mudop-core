<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  DialogRoot,
  DialogPortal,
  DialogOverlay,
  DialogContent,
  DialogTitle,
  DialogDescription,
  DialogClose
} from 'radix-vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Checkbox } from '@/components/ui/checkbox'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Spinner } from '@/components/ui/spinner'
import { AlertCircle, User, Mail, Lock, X } from 'lucide-vue-next'
import { userService } from '@/services/userService'
import type { TenantUser } from '@/types/user'

interface Props {
  open: boolean
  user?: TenantUser
  tenantId: string
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  saved: []
}>()

const { t } = useI18n()

const isEditMode = computed(() => !!props.user)
const title = computed(() => isEditMode.value ? t('admin.users.dialog.editTitle') : t('admin.users.dialog.createTitle'))

const username = ref('')
const email = ref('')
const password = ref('')
const firstName = ref('')
const lastName = ref('')
const isActive = ref(true)

const isSaving = ref(false)
const error = ref<string | null>(null)
const usernameInput = ref<HTMLInputElement | null>(null)

watch(() => props.open, (open) => {
  if (open) {
    error.value = null
    if (props.user) {
      username.value = props.user.username
      email.value = props.user.email
      firstName.value = props.user.firstName || ''
      lastName.value = props.user.lastName || ''
      isActive.value = props.user.isActive
    } else {
      username.value = ''
      email.value = ''
      password.value = ''
      firstName.value = ''
      lastName.value = ''
      isActive.value = true
    }
    nextTick(() => usernameInput.value?.focus())
  }
})

function getInitials(): string {
  if (firstName.value && lastName.value) {
    return (firstName.value[0] + lastName.value[0]).toUpperCase()
  }
  if (username.value) {
    return username.value.substring(0, 2).toUpperCase()
  }
  return '?'
}

async function save() {
  error.value = null

  if (!username.value.trim()) {
    error.value = t('admin.users.dialog.usernameRequired')
    return
  }
  if (!email.value.trim()) {
    error.value = t('admin.users.dialog.emailRequired')
    return
  }
  if (!isEditMode.value && !password.value.trim()) {
    error.value = t('admin.users.dialog.passwordRequired')
    return
  }

  isSaving.value = true
  try {
    if (isEditMode.value && props.user) {
      await userService.updateUser(props.tenantId, props.user.id, {
        username: username.value.trim(),
        email: email.value.trim(),
        firstName: firstName.value.trim() || undefined,
        lastName: lastName.value.trim() || undefined,
        isActive: isActive.value
      })
    } else {
      await userService.createUser(props.tenantId, {
        username: username.value.trim(),
        email: email.value.trim(),
        password: password.value,
        firstName: firstName.value.trim() || undefined,
        lastName: lastName.value.trim() || undefined
      })
    }
    emit('saved')
    emit('update:open', false)
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('admin.users.dialog.failedToSave')
  } finally {
    isSaving.value = false
  }
}

function onOpenChange(value: boolean) {
  emit('update:open', value)
}
</script>

<template>
  <DialogRoot :open="open" @update:open="onOpenChange">
    <DialogPortal>
      <DialogOverlay
        class="fixed inset-0 z-50 bg-black/50 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0"
      />
      <DialogContent
        class="fixed left-1/2 top-1/2 z-50 w-full max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-lg border bg-background shadow-lg duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[state=closed]:slide-out-to-left-1/2 data-[state=closed]:slide-out-to-top-[48%] data-[state=open]:slide-in-from-left-1/2 data-[state=open]:slide-in-from-top-[48%]"
      >
        <!-- Header with avatar preview -->
        <div class="flex items-start justify-between p-6 pb-0">
          <div class="flex items-center gap-3">
            <div class="h-11 w-11 rounded-full bg-primary/10 flex items-center justify-center text-primary text-sm font-semibold">
              {{ getInitials() }}
            </div>
            <div>
              <DialogTitle class="text-lg font-semibold text-foreground">
                {{ title }}
              </DialogTitle>
              <DialogDescription class="text-sm text-muted-foreground">
                {{ isEditMode ? $t('admin.users.dialog.editDescription') : $t('admin.users.dialog.createDescription') }}
              </DialogDescription>
            </div>
          </div>
          <DialogClose
            class="rounded-sm opacity-70 ring-offset-background transition-opacity hover:opacity-100 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
          >
            <X class="h-4 w-4" />
          </DialogClose>
        </div>

        <Alert v-if="error" variant="destructive" class="mx-6 mt-4">
          <AlertCircle class="h-4 w-4" />
          <AlertDescription>{{ error }}</AlertDescription>
        </Alert>

        <form class="p-6 space-y-5" @submit.prevent="save">
          <!-- Username -->
          <div class="space-y-2">
            <Label for="dialog-username" class="text-sm font-medium">
              {{ $t('admin.users.dialog.username') }}
              <span class="text-destructive">*</span>
            </Label>
            <div class="relative">
              <User class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                id="dialog-username"
                ref="usernameInput"
                v-model="username"
                :placeholder="$t('admin.users.dialog.usernamePlaceholder')"
                class="pl-9"
                :disabled="isSaving"
              />
            </div>
          </div>

          <!-- Email -->
          <div class="space-y-2">
            <Label for="dialog-email" class="text-sm font-medium">
              {{ $t('admin.users.dialog.email') }}
              <span class="text-destructive">*</span>
            </Label>
            <div class="relative">
              <Mail class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                id="dialog-email"
                v-model="email"
                type="email"
                :placeholder="$t('admin.users.dialog.emailPlaceholder')"
                class="pl-9"
                :disabled="isSaving"
              />
            </div>
          </div>

          <!-- Password (create mode only) -->
          <div v-if="!isEditMode" class="space-y-2">
            <Label for="dialog-password" class="text-sm font-medium">
              {{ $t('admin.users.dialog.password') }}
              <span class="text-destructive">*</span>
            </Label>
            <div class="relative">
              <Lock class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                id="dialog-password"
                v-model="password"
                type="password"
                :placeholder="$t('admin.users.dialog.passwordPlaceholder')"
                class="pl-9"
                :disabled="isSaving"
              />
            </div>
          </div>

          <!-- Name fields (side by side) -->
          <div class="grid grid-cols-2 gap-4">
            <div class="space-y-2">
              <Label for="dialog-firstname" class="text-sm font-medium">{{ $t('admin.users.dialog.firstName') }}</Label>
              <Input
                id="dialog-firstname"
                v-model="firstName"
                :placeholder="$t('admin.users.dialog.firstNamePlaceholder')"
                :disabled="isSaving"
              />
            </div>
            <div class="space-y-2">
              <Label for="dialog-lastname" class="text-sm font-medium">{{ $t('admin.users.dialog.lastName') }}</Label>
              <Input
                id="dialog-lastname"
                v-model="lastName"
                :placeholder="$t('admin.users.dialog.lastNamePlaceholder')"
                :disabled="isSaving"
              />
            </div>
          </div>

          <!-- Active checkbox (edit mode only) -->
          <div v-if="isEditMode" class="flex items-center gap-3 rounded-lg border p-3 bg-muted/30">
            <Checkbox
              id="dialog-active"
              :model-value="isActive"
              @update:model-value="isActive = $event"
              :disabled="isSaving"
            />
            <div>
              <Label for="dialog-active" class="cursor-pointer font-medium text-sm">{{ $t('admin.users.dialog.active') }}</Label>
              <p class="text-xs text-muted-foreground">{{ $t('admin.users.dialog.activeDescription') }}</p>
            </div>
          </div>

          <!-- Footer -->
          <div class="flex justify-end gap-3 pt-2 border-t">
            <DialogClose as-child>
              <Button variant="outline" type="button" :disabled="isSaving">
                {{ $t('common.cancel') }}
              </Button>
            </DialogClose>
            <Button type="submit" :disabled="isSaving">
              <Spinner v-if="isSaving" size="sm" class="mr-2" />
              {{ isEditMode ? $t('common.update') : $t('common.create') }}
            </Button>
          </div>
        </form>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
