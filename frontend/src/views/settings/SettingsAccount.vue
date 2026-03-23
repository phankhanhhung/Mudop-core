<script setup lang="ts">
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { authService } from '@/services/authService'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Input } from '@/components/ui/input'
import { Badge } from '@/components/ui/badge'
import { Key, Monitor, Trash2, AlertTriangle, Eye, EyeOff, Check } from 'lucide-vue-next'

const { t } = useI18n()

// Password form
const passwordForm = ref({
  currentPassword: '',
  newPassword: '',
  confirmPassword: '',
})
const showCurrentPassword = ref(false)
const showNewPassword = ref(false)
const showConfirmPassword = ref(false)
const passwordError = ref<string | null>(null)
const passwordSuccess = ref(false)

const passwordsMatch = computed(() =>
  passwordForm.value.newPassword === passwordForm.value.confirmPassword
)

const canSubmitPassword = computed(() =>
  passwordForm.value.currentPassword.length > 0 &&
  passwordForm.value.newPassword.length >= 8 &&
  passwordsMatch.value
)

async function handlePasswordChange() {
  passwordError.value = null
  passwordSuccess.value = false

  if (!passwordsMatch.value) {
    passwordError.value = t('settings.account.passwordMismatch')
    return
  }

  try {
    await authService.changePassword({
      currentPassword: passwordForm.value.currentPassword,
      newPassword: passwordForm.value.newPassword,
      confirmPassword: passwordForm.value.confirmPassword
    })
    passwordSuccess.value = true
    passwordForm.value = { currentPassword: '', newPassword: '', confirmPassword: '' }
    setTimeout(() => { passwordSuccess.value = false }, 3000)
  } catch (err: any) {
    passwordError.value = err?.response?.data?.message || t('settings.account.passwordChangeFailed')
  }
}
</script>

<template>
  <div class="space-y-6">
    <!-- Password change -->
    <Card>
      <CardHeader>
        <div class="flex items-center gap-2">
          <Key class="h-5 w-5 text-primary" />
          <div>
            <CardTitle>{{ $t('settings.account.changePassword') }}</CardTitle>
            <CardDescription>{{ $t('settings.account.changePasswordDescription') }}</CardDescription>
          </div>
        </div>
      </CardHeader>
      <CardContent class="space-y-4">
        <!-- Success message -->
        <div
          v-if="passwordSuccess"
          class="flex items-center gap-2 rounded-lg bg-emerald-50 dark:bg-emerald-900/20 border border-emerald-200 dark:border-emerald-800 px-4 py-3"
        >
          <Check class="h-4 w-4 text-emerald-600 dark:text-emerald-400" />
          <span class="text-sm text-emerald-700 dark:text-emerald-300">{{ $t('settings.account.passwordChanged') }}</span>
        </div>

        <!-- Error message -->
        <div
          v-if="passwordError"
          class="flex items-center gap-2 rounded-lg bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 px-4 py-3"
        >
          <AlertTriangle class="h-4 w-4 text-red-600 dark:text-red-400" />
          <span class="text-sm text-red-700 dark:text-red-300">{{ passwordError }}</span>
        </div>

        <div class="space-y-4 max-w-md">
          <!-- Current password -->
          <div class="space-y-2">
            <Label for="current-password">{{ $t('settings.account.currentPassword') }}</Label>
            <div class="relative">
              <Input
                id="current-password"
                :type="showCurrentPassword ? 'text' : 'password'"
                v-model="passwordForm.currentPassword"
                :placeholder="$t('settings.account.currentPasswordPlaceholder')"
                class="pr-10"
              />
              <button
                type="button"
                class="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
                @click="showCurrentPassword = !showCurrentPassword"
              >
                <component :is="showCurrentPassword ? EyeOff : Eye" class="h-4 w-4" />
              </button>
            </div>
          </div>

          <!-- New password -->
          <div class="space-y-2">
            <Label for="new-password">{{ $t('settings.account.newPassword') }}</Label>
            <div class="relative">
              <Input
                id="new-password"
                :type="showNewPassword ? 'text' : 'password'"
                v-model="passwordForm.newPassword"
                :placeholder="$t('settings.account.newPasswordPlaceholder')"
                class="pr-10"
              />
              <button
                type="button"
                class="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
                @click="showNewPassword = !showNewPassword"
              >
                <component :is="showNewPassword ? EyeOff : Eye" class="h-4 w-4" />
              </button>
            </div>
            <p v-if="passwordForm.newPassword.length > 0 && passwordForm.newPassword.length < 8" class="text-xs text-amber-600">
              {{ $t('settings.account.passwordMinLength') }}
            </p>
          </div>

          <!-- Confirm password -->
          <div class="space-y-2">
            <Label for="confirm-password">{{ $t('settings.account.confirmPassword') }}</Label>
            <div class="relative">
              <Input
                id="confirm-password"
                :type="showConfirmPassword ? 'text' : 'password'"
                v-model="passwordForm.confirmPassword"
                :placeholder="$t('settings.account.confirmPasswordPlaceholder')"
                class="pr-10"
              />
              <button
                type="button"
                class="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
                @click="showConfirmPassword = !showConfirmPassword"
              >
                <component :is="showConfirmPassword ? EyeOff : Eye" class="h-4 w-4" />
              </button>
            </div>
            <p
              v-if="passwordForm.confirmPassword.length > 0 && !passwordsMatch"
              class="text-xs text-red-600"
            >
              {{ $t('settings.account.passwordMismatch') }}
            </p>
          </div>

          <Button
            :disabled="!canSubmitPassword"
            @click="handlePasswordChange"
          >
            <Key class="h-4 w-4 mr-2" />
            {{ $t('settings.account.updatePassword') }}
          </Button>
        </div>
      </CardContent>
    </Card>

    <!-- Active sessions -->
    <Card>
      <CardHeader>
        <div class="flex items-center gap-2">
          <Monitor class="h-5 w-5 text-primary" />
          <div>
            <CardTitle>{{ $t('settings.account.activeSessions') }}</CardTitle>
            <CardDescription>{{ $t('settings.account.activeSessionsDescription') }}</CardDescription>
          </div>
        </div>
      </CardHeader>
      <CardContent>
        <div class="flex items-center gap-3 rounded-lg border bg-muted/30 px-4 py-3">
          <div class="h-9 w-9 rounded-lg bg-emerald-500/10 flex items-center justify-center">
            <Monitor class="h-4 w-4 text-emerald-500" />
          </div>
          <div class="flex-1">
            <p class="text-sm font-medium">{{ $t('settings.account.currentSession') }}</p>
            <p class="text-xs text-muted-foreground">{{ $t('settings.account.currentSessionDetails') }}</p>
          </div>
          <Badge variant="secondary" class="bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400">
            {{ $t('common.active') }}
          </Badge>
        </div>
      </CardContent>
    </Card>

    <!-- Danger zone -->
    <Card class="border-red-200 dark:border-red-900/50">
      <CardHeader class="pb-3">
        <div class="flex items-center gap-2">
          <div class="h-8 w-8 rounded-lg bg-red-100 dark:bg-red-900/30 flex items-center justify-center">
            <Trash2 class="h-4 w-4 text-red-600 dark:text-red-400" />
          </div>
          <div>
            <CardTitle class="text-red-700 dark:text-red-400">{{ $t('settings.dangerZone.title') }}</CardTitle>
            <CardDescription>{{ $t('settings.dangerZone.subtitle') }}</CardDescription>
          </div>
        </div>
      </CardHeader>
      <CardContent>
        <div class="rounded-lg border border-red-200 dark:border-red-900/50 bg-red-50/50 dark:bg-red-900/10 p-4">
          <div class="flex items-center justify-between">
            <div>
              <p class="text-sm font-medium text-red-800 dark:text-red-300">{{ $t('settings.dangerZone.deleteAccount') }}</p>
              <p class="text-xs text-red-600/80 dark:text-red-400/80 mt-0.5">
                {{ $t('settings.dangerZone.deleteDisabled') }}
              </p>
            </div>
            <Button variant="destructive" disabled size="sm">
              <Trash2 class="h-4 w-4 mr-2" />
              {{ $t('settings.dangerZone.deleteAccount') }}
            </Button>
          </div>
        </div>
      </CardContent>
    </Card>
  </div>
</template>
