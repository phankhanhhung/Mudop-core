<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import ProviderLinkSection from '@/components/auth/ProviderLinkSection.vue'
import SettingsProfile from './SettingsProfile.vue'
import SettingsAppearance from './SettingsAppearance.vue'
import SettingsPreferences from './SettingsPreferences.vue'
import SettingsSavedViews from './SettingsSavedViews.vue'
import SettingsAccount from './SettingsAccount.vue'
import { Card, CardContent } from '@/components/ui/card'
import {
  User,
  Palette,
  SlidersHorizontal,
  Key,
  Bookmark,
  Link2,
} from 'lucide-vue-next'

const { t } = useI18n()
const authStore = useAuthStore()

// Section navigation
type SettingsSection = 'profile' | 'appearance' | 'preferences' | 'saved-views' | 'account' | 'providers'
const activeSection = ref<SettingsSection>('profile')

const sections = computed(() => [
  { id: 'profile' as const, label: t('settings.sections.profile'), icon: User },
  { id: 'appearance' as const, label: t('settings.sections.appearance'), icon: Palette },
  { id: 'preferences' as const, label: t('settings.sections.preferences'), icon: SlidersHorizontal },
  { id: 'saved-views' as const, label: t('settings.sections.savedViews'), icon: Bookmark },
  { id: 'account' as const, label: t('settings.sections.account'), icon: Key },
  { id: 'providers' as const, label: t('settings.sections.providers'), icon: Link2 },
])

// Avatar helpers (used in both header and profile section)
function getInitials(): string {
  const name = authStore.displayName
  if (!name) return '?'
  const parts = name.split(/[\s@]+/)
  if (parts.length >= 2) {
    return (parts[0][0] + parts[1][0]).toUpperCase()
  }
  return name.substring(0, 2).toUpperCase()
}

function getAvatarColor(): string {
  const name = authStore.displayName || ''
  const colors = [
    'bg-blue-500', 'bg-emerald-500', 'bg-violet-500', 'bg-amber-500',
    'bg-rose-500', 'bg-cyan-500', 'bg-indigo-500', 'bg-teal-500',
    'bg-pink-500', 'bg-orange-500'
  ]
  let hash = 0
  for (const char of name) {
    hash = char.charCodeAt(0) + ((hash << 5) - hash)
  }
  return colors[Math.abs(hash) % colors.length]
}

// Role badge colors (passed to SettingsProfile)
function getRoleBadgeClass(role: string): string {
  const roleColors: Record<string, string> = {
    'Admin': 'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-400',
    'Manager': 'bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-400',
    'User': 'bg-emerald-100 text-emerald-800 dark:bg-emerald-900/30 dark:text-emerald-400',
  }
  return roleColors[role] || 'bg-violet-100 text-violet-800 dark:bg-violet-900/30 dark:text-violet-400'
}

// Saved views ref — load on section switch
const savedViewsRef = ref<InstanceType<typeof SettingsSavedViews> | null>(null)

watch(activeSection, (section) => {
  if (section === 'saved-views') {
    savedViewsRef.value?.loadSavedViews()
  }
})
</script>

<template>
  <DefaultLayout>
    <div class="max-w-5xl mx-auto space-y-6">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div class="flex items-center gap-4">
          <!-- User avatar -->
          <div
            class="h-14 w-14 rounded-full flex items-center justify-center text-white text-xl font-semibold shadow-md"
            :class="getAvatarColor()"
          >
            {{ getInitials() }}
          </div>
          <div>
            <h1 class="text-3xl font-bold tracking-tight">{{ $t('settings.title') }}</h1>
            <p class="text-muted-foreground mt-0.5">
              {{ authStore.displayName }} &mdash; {{ $t('settings.subtitle') }}
            </p>
          </div>
        </div>
      </div>

      <!-- Layout: sidebar nav + content -->
      <div class="flex gap-6">
        <!-- Section navigation sidebar -->
        <nav class="w-56 shrink-0">
          <div class="sticky top-20 space-y-1">
            <button
              v-for="section in sections"
              :key="section.id"
              class="w-full flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-colors text-left"
              :class="activeSection === section.id
                ? 'bg-primary text-primary-foreground shadow-sm'
                : 'text-muted-foreground hover:text-foreground hover:bg-muted'"
              @click="activeSection = section.id"
            >
              <component :is="section.icon" class="h-4 w-4" />
              {{ section.label }}
            </button>
          </div>
        </nav>

        <!-- Content area -->
        <div class="flex-1 min-w-0 space-y-6">

          <!-- ==================== PROFILE SECTION ==================== -->
          <SettingsProfile
            v-if="activeSection === 'profile'"
            :getInitials="getInitials"
            :getAvatarColor="getAvatarColor"
            :getRoleBadgeClass="getRoleBadgeClass"
          />

          <!-- ==================== APPEARANCE SECTION ==================== -->
          <SettingsAppearance v-if="activeSection === 'appearance'" />

          <!-- ==================== PREFERENCES SECTION ==================== -->
          <SettingsPreferences v-if="activeSection === 'preferences'" />

          <!-- ==================== SAVED VIEWS SECTION ==================== -->
          <SettingsSavedViews
            v-if="activeSection === 'saved-views'"
            ref="savedViewsRef"
          />

          <!-- ==================== ACCOUNT SECTION ==================== -->
          <SettingsAccount v-if="activeSection === 'account'" />

          <!-- ==================== PROVIDERS SECTION ==================== -->
          <template v-if="activeSection === 'providers'">
            <ProviderLinkSection />

            <!-- Fallback if no providers are configured -->
            <Card class="border-dashed">
              <CardContent class="flex flex-col items-center justify-center py-12">
                <div class="h-14 w-14 rounded-full bg-muted flex items-center justify-center mb-3">
                  <Link2 class="h-7 w-7 text-muted-foreground" />
                </div>
                <h3 class="font-semibold mb-1">{{ $t('settings.providers.noProviders') }}</h3>
                <p class="text-muted-foreground text-sm text-center max-w-sm">
                  {{ $t('settings.providers.noProvidersDescription') }}
                </p>
              </CardContent>
            </Card>
          </template>

        </div>
      </div>
    </div>
  </DefaultLayout>
</template>
