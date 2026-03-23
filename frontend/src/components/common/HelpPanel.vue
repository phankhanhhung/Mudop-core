<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useOnboarding } from '@/composables/useOnboarding'
import { Button } from '@/components/ui/button'
import {
  X,
  BookOpen,
  Keyboard,
  Search,
  Settings,
  Database,
  ClipboardList,
  RotateCcw,
  Sparkles
} from 'lucide-vue-next'

const props = defineProps<{
  open: boolean
}>()

const emit = defineEmits<{
  close: []
  openWhatsNew: []
}>()

const router = useRouter()
const { resetOnboarding } = useOnboarding()

const expandedSection = ref<string | null>('getting-started')

function toggleSection(section: string) {
  expandedSection.value = expandedSection.value === section ? null : section
}

function navigateTo(path: string) {
  router.push(path)
  emit('close')
}

function handleRerunOnboarding() {
  resetOnboarding()
  emit('close')
}

function handleKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') {
    emit('close')
  }
}
</script>

<template>
  <Teleport to="body">
    <!-- Backdrop -->
    <Transition
      enter-active-class="transition ease-out duration-200"
      enter-from-class="opacity-0"
      enter-to-class="opacity-100"
      leave-active-class="transition ease-in duration-150"
      leave-from-class="opacity-100"
      leave-to-class="opacity-0"
    >
      <div
        v-if="props.open"
        class="fixed inset-0 z-50 bg-black/50"
        @click="emit('close')"
      />
    </Transition>

    <!-- Panel -->
    <Transition
      enter-active-class="transition ease-out duration-300"
      enter-from-class="translate-x-full"
      enter-to-class="translate-x-0"
      leave-active-class="transition ease-in duration-200"
      leave-from-class="translate-x-0"
      leave-to-class="translate-x-full"
    >
      <div
        v-if="props.open"
        class="fixed right-0 top-0 z-50 flex h-full w-full max-w-sm flex-col border-l bg-background shadow-xl"
        role="dialog"
        :aria-label="$t('help.title')"
        @keydown="handleKeydown"
      >
        <!-- Header -->
        <div class="flex items-center justify-between border-b px-6 py-4">
          <h2 class="text-lg font-semibold">{{ $t('help.title') }}</h2>
          <Button
            variant="ghost"
            size="icon"
            :aria-label="$t('common.cancel')"
            @click="emit('close')"
          >
            <X class="h-5 w-5" />
          </Button>
        </div>

        <!-- Content -->
        <div class="flex-1 overflow-y-auto px-6 py-4">
          <!-- Getting Started -->
          <div class="mb-4">
            <button
              class="flex w-full items-center gap-3 rounded-lg px-3 py-2 text-left hover:bg-accent"
              @click="toggleSection('getting-started')"
            >
              <BookOpen class="h-5 w-5 text-primary" />
              <span class="font-medium">{{ $t('help.gettingStarted.title') }}</span>
            </button>
            <div v-if="expandedSection === 'getting-started'" class="mt-2 space-y-2 pl-11">
              <p class="text-sm text-muted-foreground">{{ $t('help.gettingStarted.step1') }}</p>
              <p class="text-sm text-muted-foreground">{{ $t('help.gettingStarted.step2') }}</p>
              <p class="text-sm text-muted-foreground">{{ $t('help.gettingStarted.step3') }}</p>
            </div>
          </div>

          <!-- Keyboard Shortcuts -->
          <div class="mb-4">
            <button
              class="flex w-full items-center gap-3 rounded-lg px-3 py-2 text-left hover:bg-accent"
              @click="toggleSection('shortcuts')"
            >
              <Keyboard class="h-5 w-5 text-primary" />
              <span class="font-medium">{{ $t('help.shortcuts.title') }}</span>
            </button>
            <div v-if="expandedSection === 'shortcuts'" class="mt-2 space-y-1 pl-11">
              <div class="flex items-center justify-between text-sm">
                <span class="text-muted-foreground">{{ $t('help.shortcuts.search') }}</span>
                <kbd class="rounded border bg-muted px-2 py-0.5 text-xs font-mono">/</kbd>
              </div>
              <div class="flex items-center justify-between text-sm">
                <span class="text-muted-foreground">{{ $t('help.shortcuts.help') }}</span>
                <kbd class="rounded border bg-muted px-2 py-0.5 text-xs font-mono">?</kbd>
              </div>
              <div class="flex items-center justify-between text-sm">
                <span class="text-muted-foreground">{{ $t('help.shortcuts.escape') }}</span>
                <kbd class="rounded border bg-muted px-2 py-0.5 text-xs font-mono">Esc</kbd>
              </div>
            </div>
          </div>

          <!-- OData Query Tips -->
          <div class="mb-4">
            <button
              class="flex w-full items-center gap-3 rounded-lg px-3 py-2 text-left hover:bg-accent"
              @click="toggleSection('odata')"
            >
              <Search class="h-5 w-5 text-primary" />
              <span class="font-medium">{{ $t('help.odata.title') }}</span>
            </button>
            <div v-if="expandedSection === 'odata'" class="mt-2 space-y-2 pl-11">
              <p class="text-sm text-muted-foreground">{{ $t('help.odata.filter') }}</p>
              <p class="text-sm text-muted-foreground">{{ $t('help.odata.expand') }}</p>
              <p class="text-sm text-muted-foreground">{{ $t('help.odata.search') }}</p>
              <p class="text-sm text-muted-foreground">{{ $t('help.odata.orderby') }}</p>
            </div>
          </div>

          <!-- Divider -->
          <hr class="my-4" />

          <!-- Quick Links -->
          <h3 class="mb-3 px-3 text-sm font-medium text-muted-foreground">
            {{ $t('help.quickLinks') }}
          </h3>
          <div class="space-y-1">
            <button
              class="flex w-full items-center gap-3 rounded-lg px-3 py-2 text-left text-sm hover:bg-accent"
              @click="navigateTo('/admin/modules')"
            >
              <Settings class="h-4 w-4 text-muted-foreground" />
              {{ $t('help.links.modules') }}
            </button>
            <button
              class="flex w-full items-center gap-3 rounded-lg px-3 py-2 text-left text-sm hover:bg-accent"
              @click="navigateTo('/admin/metadata')"
            >
              <Database class="h-4 w-4 text-muted-foreground" />
              {{ $t('help.links.metadata') }}
            </button>
            <button
              class="flex w-full items-center gap-3 rounded-lg px-3 py-2 text-left text-sm hover:bg-accent"
              @click="navigateTo('/admin/audit')"
            >
              <ClipboardList class="h-4 w-4 text-muted-foreground" />
              {{ $t('help.links.audit') }}
            </button>
          </div>

          <!-- Divider -->
          <hr class="my-4" />

          <!-- Actions -->
          <div class="space-y-1">
            <button
              class="flex w-full items-center gap-3 rounded-lg px-3 py-2 text-left text-sm hover:bg-accent"
              @click="emit('openWhatsNew')"
            >
              <Sparkles class="h-4 w-4 text-muted-foreground" />
              {{ $t('help.whatsNew') }}
            </button>
            <button
              class="flex w-full items-center gap-3 rounded-lg px-3 py-2 text-left text-sm hover:bg-accent"
              @click="handleRerunOnboarding"
            >
              <RotateCcw class="h-4 w-4 text-muted-foreground" />
              {{ $t('help.rerunOnboarding') }}
            </button>
          </div>
        </div>

        <!-- Footer -->
        <div class="border-t px-6 py-3">
          <p class="text-xs text-muted-foreground text-center">
            {{ $t('help.version', { version: '1.0.0' }) }}
          </p>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>
