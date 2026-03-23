<script setup lang="ts">
import { ref, computed } from 'vue'
import { useUiStore } from '@/stores/ui'
import AppHeader from '@/components/common/AppHeader.vue'
import AppSidebar from '@/components/common/AppSidebar.vue'
import Breadcrumbs from '@/components/common/Breadcrumbs.vue'
import ErrorBoundary from '@/components/common/ErrorBoundary.vue'
import OnboardingWizard from '@/components/common/OnboardingWizard.vue'
import CommandPalette from '@/components/common/CommandPalette.vue'
import GlobalSearchOverlay from '@/components/shell/GlobalSearchOverlay.vue'
import ShortcutOverlay from '@/components/common/ShortcutOverlay.vue'
import PwaUpdateBanner from '@/components/pwa/PwaUpdateBanner.vue'
import PwaInstallBanner from '@/components/pwa/PwaInstallBanner.vue'
import { useKeyboardShortcuts, useGlobalShortcut } from '@/composables/useKeyboardShortcuts'

const uiStore = useUiStore()

const globalSearchOpen = ref(false)
const entitySearchOpen = ref(false)
const shortcutOverlayOpen = ref(false)

const mainClass = computed(() => ({
  'ml-64': !uiStore.sidebarCollapsed,
  'ml-16': uiStore.sidebarCollapsed
}))

// Ctrl+K / Cmd+K — open command palette (works even in inputs)
useGlobalShortcut('k', () => {
  shortcutOverlayOpen.value = false
  entitySearchOpen.value = false
  globalSearchOpen.value = true
}, { ctrl: true })

// Escape — close any open overlay (works even in inputs)
useGlobalShortcut('Escape', () => {
  if (globalSearchOpen.value) {
    globalSearchOpen.value = false
  } else if (entitySearchOpen.value) {
    entitySearchOpen.value = false
  } else if (shortcutOverlayOpen.value) {
    shortcutOverlayOpen.value = false
  }
})

// ? — open shortcut overlay (only when not in input)
useKeyboardShortcuts([
  {
    key: 'F',
    ctrl: true,
    shift: true,
    handler: () => {
      shortcutOverlayOpen.value = false
      globalSearchOpen.value = false
      entitySearchOpen.value = true
    }
  },
  {
    key: '?',
    shift: true,
    handler: () => {
      globalSearchOpen.value = false
      shortcutOverlayOpen.value = true
    }
  }
])
</script>

<template>
  <div class="h-screen bg-background flex flex-col overflow-hidden">
    <!-- PWA update banner (full-width, top of layout) -->
    <PwaUpdateBanner />

    <!-- Skip to content link -->
    <a
      href="#main-content"
      class="sr-only focus:not-sr-only focus:absolute focus:z-[200] focus:top-2 focus:left-2 focus:rounded-md focus:bg-primary focus:px-4 focus:py-2 focus:text-primary-foreground focus:outline-none"
    >
      {{ $t('accessibility.skipToContent') }}
    </a>

    <!-- Sidebar -->
    <AppSidebar />

    <!-- Main area -->
    <div class="flex-1 flex flex-col min-h-0 transition-all duration-300" :class="mainClass">
      <!-- Header -->
      <AppHeader />

      <!-- Content -->
      <main id="main-content" class="flex-1 min-h-0 overflow-y-auto p-6" tabindex="-1">
        <Breadcrumbs class="mb-4" />
        <ErrorBoundary>
          <slot />
        </ErrorBoundary>
      </main>
    </div>

    <!-- Mobile overlay -->
    <div
      v-if="uiStore.sidebarMobileOpen"
      class="fixed inset-0 bg-black/50 z-40 lg:hidden"
      aria-hidden="true"
      @click="uiStore.closeMobileSidebar"
    />

    <!-- Onboarding wizard (first visit only) -->
    <OnboardingWizard />

    <!-- Command Palette (Ctrl+K / Cmd+K) -->
    <CommandPalette :open="globalSearchOpen" @close="globalSearchOpen = false" />

    <!-- Global Search (full entity data search) -->
    <GlobalSearchOverlay :open="entitySearchOpen" @close="entitySearchOpen = false" />

    <!-- Shortcut Overlay -->
    <ShortcutOverlay :open="shortcutOverlayOpen" @close="shortcutOverlayOpen = false" />

    <!-- PWA install banner (fixed bottom-right toast) -->
    <PwaInstallBanner />
  </div>
</template>
