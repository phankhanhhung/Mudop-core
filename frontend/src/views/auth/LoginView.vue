<script setup lang="ts">
import { ref } from 'vue'
import { RouterLink } from 'vue-router'
import { useAuth } from '@/composables/useAuth'
import AuthLayout from '@/layouts/AuthLayout.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Spinner } from '@/components/ui/spinner'
import { AlertCircle } from 'lucide-vue-next'
import SocialLoginButtons from '@/components/auth/SocialLoginButtons.vue'

const { login, isLoading, error, clearError } = useAuth()

const usernameOrEmail = ref('')
const password = ref('')

async function handleSubmit() {
  clearError()
  await login({
    usernameOrEmail: usernameOrEmail.value,
    password: password.value
  })
}
</script>

<template>
  <AuthLayout>
    <Card>
      <CardHeader class="space-y-1">
        <CardTitle class="text-2xl text-center">{{ $t('auth.login.title') }}</CardTitle>
        <CardDescription class="text-center">
          {{ $t('auth.login.subtitle') }}
        </CardDescription>
      </CardHeader>

      <form @submit.prevent="handleSubmit">
        <CardContent class="space-y-4">
          <Alert v-if="error" variant="destructive">
            <AlertCircle class="h-4 w-4" />
            <AlertDescription>{{ error }}</AlertDescription>
          </Alert>

          <div class="space-y-2">
            <Label for="usernameOrEmail">{{ $t('auth.login.emailOrUsername') }}</Label>
            <Input
              id="usernameOrEmail"
              v-model="usernameOrEmail"
              type="text"
              placeholder="name@example.com"
              required
              :disabled="isLoading"
            />
          </div>

          <div class="space-y-2">
            <Label for="password">{{ $t('auth.login.password') }}</Label>
            <Input
              id="password"
              v-model="password"
              type="password"
              :placeholder="$t('auth.login.passwordPlaceholder')"
              required
              :disabled="isLoading"
            />
          </div>
        </CardContent>

        <CardFooter class="flex flex-col space-y-4">
          <Button type="submit" class="w-full" :disabled="isLoading">
            <Spinner v-if="isLoading" size="sm" class="mr-2" />
            {{ $t('auth.login.submit') }}
          </Button>

          <p class="text-sm text-center text-muted-foreground">
            {{ $t('auth.login.noAccount') }}
            <RouterLink to="/auth/register" class="text-primary hover:underline">
              {{ $t('auth.login.signUp') }}
            </RouterLink>
          </p>
        </CardFooter>
      </form>
    </Card>

    <SocialLoginButtons class="mt-4" />
  </AuthLayout>
</template>
