<script setup lang="ts">
import { useOAuth, type OAuthProvider } from '@/composables/useOAuth'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Spinner } from '@/components/ui/spinner'
import { Link2, AlertCircle } from 'lucide-vue-next'

const { isLoading, error, activeProvider, linkProvider, hasGoogle, hasMicrosoft, hasApple } = useOAuth()

interface ProviderInfo {
  id: OAuthProvider
  name: string
  enabled: boolean
}

const providers: ProviderInfo[] = [
  { id: 'google', name: 'Google', enabled: hasGoogle },
  { id: 'microsoft', name: 'Microsoft', enabled: hasMicrosoft },
  { id: 'apple', name: 'Apple', enabled: hasApple },
]

const visibleProviders = providers.filter(p => p.enabled)

async function handleConnect(provider: OAuthProvider) {
  try {
    await linkProvider(provider)
  } catch {
    // Error is already set in the composable
  }
}
</script>

<template>
  <Card v-if="visibleProviders.length > 0">
    <CardHeader>
      <div class="flex items-center gap-2">
        <Link2 class="h-5 w-5" />
        <CardTitle>{{ $t('settings.connectedAccounts.title') }}</CardTitle>
      </div>
      <CardDescription>{{ $t('settings.connectedAccounts.subtitle') }}</CardDescription>
    </CardHeader>
    <CardContent class="space-y-4">
      <Alert v-if="error" variant="destructive">
        <AlertCircle class="h-4 w-4" />
        <AlertDescription>{{ error }}</AlertDescription>
      </Alert>

      <div class="space-y-3">
        <div
          v-for="provider in visibleProviders"
          :key="provider.id"
          class="flex items-center justify-between rounded-lg border p-3"
        >
          <div class="flex items-center gap-3">
            <!-- Google icon -->
            <svg v-if="provider.id === 'google'" class="h-5 w-5" viewBox="0 0 24 24">
              <path d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92a5.06 5.06 0 0 1-2.2 3.32v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.1z" fill="#4285F4" />
              <path d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z" fill="#34A853" />
              <path d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z" fill="#FBBC05" />
              <path d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z" fill="#EA4335" />
            </svg>
            <!-- Microsoft icon -->
            <svg v-if="provider.id === 'microsoft'" class="h-5 w-5" viewBox="0 0 24 24">
              <rect x="1" y="1" width="10" height="10" fill="#F25022" />
              <rect x="13" y="1" width="10" height="10" fill="#7FBA00" />
              <rect x="1" y="13" width="10" height="10" fill="#00A4EF" />
              <rect x="13" y="13" width="10" height="10" fill="#FFB900" />
            </svg>
            <!-- Apple icon -->
            <svg v-if="provider.id === 'apple'" class="h-5 w-5" viewBox="0 0 24 24" fill="currentColor">
              <path d="M17.05 20.28c-.98.95-2.05.88-3.08.4-1.09-.5-2.08-.48-3.24 0-1.44.62-2.2.44-3.06-.4C2.79 15.25 3.51 7.59 9.05 7.31c1.35.07 2.29.74 3.08.8 1.18-.24 2.31-.93 3.57-.84 1.51.12 2.65.72 3.4 1.8-3.12 1.87-2.38 5.98.48 7.13-.57 1.5-1.31 2.99-2.54 4.09zM12.03 7.25c-.15-2.23 1.66-4.07 3.74-4.25.32 2.32-2.12 4.53-3.74 4.25z" />
            </svg>
            <div>
              <p class="text-sm font-medium">{{ provider.name }}</p>
              <p class="text-xs text-muted-foreground">{{ $t('common.notConnected') }}</p>
            </div>
          </div>
          <Button
            variant="outline"
            size="sm"
            :disabled="isLoading"
            @click="handleConnect(provider.id)"
          >
            <Spinner v-if="activeProvider === provider.id" size="sm" class="mr-2" />
            {{ $t('common.connect') }}
          </Button>
        </div>
      </div>
    </CardContent>
  </Card>
</template>
