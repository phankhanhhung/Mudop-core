<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useOnboarding } from '@/composables/useOnboarding'
import { useAuthStore } from '@/stores/auth'
import { useTenantStore } from '@/stores/tenant'
import { useUiStore } from '@/stores/ui'
import { Button } from '@/components/ui/button'
import ThemeToggle from './ThemeToggle.vue'
import HelpPanel from './HelpPanel.vue'
import WhatsNewModal from './WhatsNewModal.vue'
import NotificationCenter from '@/components/shell/NotificationCenter.vue'
import MessagePopover from '@/components/smart/MessagePopover.vue'
import NotificationBell from './NotificationBell.vue'
import {
  Menu,
  LogOut,
  User,
  Building2,
  ChevronDown,
  HelpCircle
} from 'lucide-vue-next'

const router = useRouter()
const authStore = useAuthStore()
const tenantStore = useTenantStore()
const uiStore = useUiStore()

const helpOpen = ref(false)
const whatsNewOpen = ref(false)
const notificationCenterOpen = ref(false)

const { hasNewVersion, isFirstRun } = useOnboarding()

// Auto-show What's New the first time the user sees this version.
// Wait until onboarding is complete so the two modals don't stack.
onMounted(() => {
  if (hasNewVersion.value && !isFirstRun.value) {
    whatsNewOpen.value = true
  }
})

function openWhatsNewFromHelp() {
  helpOpen.value = false
  whatsNewOpen.value = true
}

async function handleLogout() {
  await authStore.logout()
  tenantStore.reset()
  router.push('/auth/login')
}

function switchTenant() {
  router.push('/tenants')
}
</script>

<template>
  <header role="banner" class="sticky top-0 z-30 border-b bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/60">
    <div class="flex h-16 items-center gap-4 px-6">
      <!-- Mobile menu button -->
      <Button
        variant="ghost"
        size="icon"
        class="lg:hidden"
        :aria-label="$t('header.toggleMobileMenu')"
        @click="uiStore.toggleMobileSidebar"
      >
        <Menu class="h-5 w-5" />
      </Button>

      <!-- Sidebar toggle (desktop) -->
      <Button
        variant="ghost"
        size="icon"
        class="hidden lg:flex"
        :aria-label="$t('header.toggleSidebar')"
        @click="uiStore.toggleSidebar"
      >
        <Menu class="h-5 w-5" />
      </Button>

      <!-- Current tenant -->
      <div v-if="tenantStore.hasTenant" class="flex items-center gap-2">
        <Building2 class="h-4 w-4 text-muted-foreground" />
        <button
          class="flex items-center gap-1 text-sm font-medium hover:text-primary"
          @click="switchTenant"
        >
          {{ tenantStore.currentTenantName }}
          <ChevronDown class="h-4 w-4" />
        </button>
      </div>

      <!-- Spacer -->
      <div class="flex-1" />

      <!-- Right side -->
      <div class="flex items-center gap-2">
        <Button
          variant="ghost"
          size="icon"
          :title="$t('help.title')"
          :aria-label="$t('help.title')"
          @click="helpOpen = true"
        >
          <HelpCircle class="h-5 w-5" />
        </Button>
        <ThemeToggle />
        <MessagePopover />
        <NotificationBell @open-all="notificationCenterOpen = true" />

        <!-- User menu -->
        <div class="flex items-center gap-2 ml-2">
          <div class="hidden sm:block text-right">
            <div class="text-sm font-medium">{{ authStore.displayName }}</div>
            <div class="text-xs text-muted-foreground">{{ authStore.user?.email }}</div>
          </div>

          <Button
            variant="ghost"
            size="icon"
            :title="$t('header.profile')"
            :aria-label="$t('header.profile')"
            @click="router.push('/settings')"
          >
            <User class="h-5 w-5" />
          </Button>

          <Button
            variant="ghost"
            size="icon"
            :title="$t('header.logout')"
            :aria-label="$t('header.logout')"
            @click="handleLogout"
          >
            <LogOut class="h-5 w-5" />
          </Button>
        </div>
      </div>
    </div>

    <HelpPanel
      :open="helpOpen"
      @close="helpOpen = false"
      @open-whats-new="openWhatsNewFromHelp"
    />
    <WhatsNewModal
      :open="whatsNewOpen"
      @close="whatsNewOpen = false"
    />
    <NotificationCenter
      :open="notificationCenterOpen"
      @close="notificationCenterOpen = false"
    />
  </header>
</template>
