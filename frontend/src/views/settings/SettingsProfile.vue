<script setup lang="ts">
import { useAuthStore } from '@/stores/auth'
import { useTenantStore } from '@/stores/tenant'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { Badge } from '@/components/ui/badge'
import { User, Globe, Shield, Building2 } from 'lucide-vue-next'

const props = defineProps<{
  getInitials: () => string
  getAvatarColor: () => string
  getRoleBadgeClass: (role: string) => string
}>()

const authStore = useAuthStore()
const tenantStore = useTenantStore()
</script>

<template>
  <Card>
    <CardHeader>
      <div class="flex items-center gap-2">
        <User class="h-5 w-5 text-primary" />
        <CardTitle>{{ $t('settings.profile.title') }}</CardTitle>
      </div>
      <CardDescription>{{ $t('settings.profile.subtitle') }}</CardDescription>
    </CardHeader>
    <CardContent class="space-y-6">
      <!-- Avatar section -->
      <div class="flex items-center gap-5">
        <div
          class="h-20 w-20 rounded-full flex items-center justify-center text-white text-2xl font-bold shadow-lg ring-4 ring-background"
          :class="props.getAvatarColor()"
        >
          {{ props.getInitials() }}
        </div>
        <div>
          <h3 class="text-lg font-semibold">{{ authStore.displayName }}</h3>
          <p class="text-sm text-muted-foreground">{{ authStore.user?.email }}</p>
        </div>
      </div>

      <div class="border-t pt-5" />

      <!-- Profile fields -->
      <div class="grid gap-5 md:grid-cols-2">
        <div class="space-y-2">
          <Label class="text-xs font-medium text-muted-foreground uppercase tracking-wider">
            {{ $t('settings.profile.username') }}
          </Label>
          <div class="flex items-center gap-2 rounded-lg border bg-muted/30 px-3 py-2.5">
            <User class="h-4 w-4 text-muted-foreground" />
            <span class="text-sm font-medium">{{ authStore.user?.username || '--' }}</span>
          </div>
        </div>
        <div class="space-y-2">
          <Label class="text-xs font-medium text-muted-foreground uppercase tracking-wider">
            {{ $t('settings.profile.email') }}
          </Label>
          <div class="flex items-center gap-2 rounded-lg border bg-muted/30 px-3 py-2.5">
            <Globe class="h-4 w-4 text-muted-foreground" />
            <span class="text-sm font-medium">{{ authStore.user?.email || '--' }}</span>
          </div>
        </div>
      </div>

      <!-- Roles -->
      <div class="space-y-3">
        <Label class="text-xs font-medium text-muted-foreground uppercase tracking-wider">
          {{ $t('settings.profile.rolesLabel') }}
        </Label>
        <div v-if="authStore.userRoles.length > 0" class="flex flex-wrap gap-2">
          <Badge
            v-for="role in authStore.userRoles"
            :key="role"
            variant="secondary"
            class="px-3 py-1 text-xs font-medium"
            :class="props.getRoleBadgeClass(role)"
          >
            <Shield class="h-3 w-3 mr-1.5" />
            {{ role }}
          </Badge>
        </div>
        <p v-else class="text-sm text-muted-foreground italic">
          {{ $t('settings.roles.noRoles') }}
        </p>
      </div>

      <!-- Tenant -->
      <div class="space-y-3">
        <Label class="text-xs font-medium text-muted-foreground uppercase tracking-wider">
          {{ $t('settings.profile.currentTenant') }}
        </Label>
        <div v-if="tenantStore.currentTenant" class="flex items-center gap-3 rounded-lg border bg-muted/30 px-4 py-3">
          <div class="h-9 w-9 rounded-lg bg-primary/10 flex items-center justify-center">
            <Building2 class="h-4 w-4 text-primary" />
          </div>
          <div>
            <p class="text-sm font-semibold">{{ tenantStore.currentTenantName }}</p>
            <p class="text-xs text-muted-foreground">{{ tenantStore.currentTenantId }}</p>
          </div>
        </div>
        <p v-else class="text-sm text-muted-foreground italic">
          {{ $t('settings.profile.noTenantSelected') }}
        </p>
      </div>
    </CardContent>
  </Card>
</template>
