<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useUiStore } from '@/stores/ui'
import { Button } from '@/components/ui/button'
import { Sun, Moon, Monitor } from 'lucide-vue-next'

const { t } = useI18n()
const uiStore = useUiStore()

const icon = computed(() => {
  switch (uiStore.theme) {
    case 'light':
      return Sun
    case 'dark':
      return Moon
    default:
      return Monitor
  }
})

const tooltip = computed(() => {
  switch (uiStore.theme) {
    case 'light':
      return 'Light mode'
    case 'dark':
      return 'Dark mode'
    default:
      return 'System'
  }
})

const ariaLabel = computed(() => {
  switch (uiStore.theme) {
    case 'light':
      return t('accessibility.toggleThemeLight')
    case 'dark':
      return t('accessibility.toggleThemeDark')
    default:
      return t('accessibility.toggleThemeSystem')
  }
})
</script>

<template>
  <Button
    variant="ghost"
    size="icon"
    :title="tooltip"
    :aria-label="ariaLabel"
    @click="uiStore.toggleTheme"
  >
    <component :is="icon" class="h-5 w-5" aria-hidden="true" />
  </Button>
</template>
