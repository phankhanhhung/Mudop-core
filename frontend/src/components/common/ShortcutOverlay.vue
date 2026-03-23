<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { X } from 'lucide-vue-next'

defineProps<{ open: boolean }>()
const emit = defineEmits<{ (e: 'close'): void }>()

const { t } = useI18n()

interface ShortcutItem {
  keys: string[]
  labelKey: string
}

interface ShortcutGroup {
  titleKey: string
  items: ShortcutItem[]
}

const groups: ShortcutGroup[] = [
  {
    titleKey: 'shortcuts.categories.general',
    items: [
      { keys: ['Ctrl', 'K'], labelKey: 'shortcuts.openCommandPalette' },
      { keys: ['?'], labelKey: 'shortcuts.openShortcutOverlay' },
      { keys: ['Esc'], labelKey: 'shortcuts.closeOverlay' }
    ]
  },
  {
    titleKey: 'shortcuts.categories.navigation',
    items: [
      { keys: ['G', 'D'], labelKey: 'shortcuts.goToDashboard' },
      { keys: ['G', 'T'], labelKey: 'shortcuts.goToTenants' },
      { keys: ['G', 'M'], labelKey: 'shortcuts.goToModules' },
      { keys: ['G', 'S'], labelKey: 'shortcuts.goToSettings' }
    ]
  },
  {
    titleKey: 'shortcuts.categories.actions',
    items: [
      { keys: ['N'], labelKey: 'shortcuts.newRecord' },
      { keys: ['E'], labelKey: 'shortcuts.editRecord' },
      { keys: ['R'], labelKey: 'shortcuts.refreshData' }
    ]
  }
]
</script>

<template>
  <Teleport to="body">
    <Transition name="fade">
      <div
        v-if="open"
        class="fixed inset-0 z-[100] flex items-center justify-center"
        @click.self="emit('close')"
        @keydown.escape="emit('close')"
      >
        <!-- Backdrop -->
        <div class="fixed inset-0 bg-black/50" aria-hidden="true" @click="emit('close')" />

        <!-- Dialog -->
        <div
          class="relative z-10 w-full max-w-lg overflow-hidden rounded-xl border bg-background shadow-2xl"
          role="dialog"
          :aria-label="t('shortcuts.title')"
        >
          <!-- Header -->
          <div class="flex items-center justify-between border-b px-6 py-4">
            <h2 class="text-lg font-semibold">{{ t('shortcuts.title') }}</h2>
            <button
              class="rounded-sm p-1 hover:bg-muted"
              :aria-label="t('common.cancel')"
              @click="emit('close')"
            >
              <X class="h-4 w-4" aria-hidden="true" />
            </button>
          </div>

          <!-- Content -->
          <div class="max-h-[60vh] overflow-y-auto p-6">
            <div v-for="group in groups" :key="group.titleKey" class="mb-6 last:mb-0">
              <h3 class="mb-3 text-sm font-semibold text-muted-foreground">
                {{ t(group.titleKey) }}
              </h3>
              <div class="space-y-2">
                <div
                  v-for="item in group.items"
                  :key="item.labelKey"
                  class="flex items-center justify-between py-1"
                >
                  <span class="text-sm">{{ t(item.labelKey) }}</span>
                  <div class="flex items-center gap-1">
                    <kbd
                      v-for="key in item.keys"
                      :key="key"
                      class="inline-flex h-6 min-w-[24px] items-center justify-center rounded border bg-muted px-1.5 font-mono text-xs"
                    >
                      {{ key }}
                    </kbd>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.15s ease;
}
.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
