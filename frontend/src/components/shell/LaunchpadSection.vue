<script setup lang="ts">
import { type Component } from 'vue'
import { useI18n } from 'vue-i18n'
import { Badge } from '@/components/ui/badge'
import { ChevronDown } from 'lucide-vue-next'
import { cn } from '@/lib/utils'

defineProps<{
  title: string
  subtitle?: string
  icon: Component
  count?: number
}>()

const collapsed = defineModel<boolean>('collapsed', { default: false })

useI18n()

function toggle(): void {
  collapsed.value = !collapsed.value
}
</script>

<template>
  <section class="space-y-4">
    <!-- Header -->
    <button
      class="group flex w-full items-center gap-3 text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary rounded-lg px-1 py-1"
      :aria-expanded="!collapsed"
      @click="toggle"
    >
      <component :is="icon" class="h-5 w-5 shrink-0 text-muted-foreground" aria-hidden="true" />
      <div class="flex-1 min-w-0">
        <div class="flex items-center gap-2">
          <h2 class="text-lg font-semibold text-foreground">{{ title }}</h2>
          <Badge v-if="count !== undefined" variant="secondary" class="text-xs font-medium">
            {{ count }}
          </Badge>
        </div>
        <p v-if="subtitle" class="text-xs text-muted-foreground mt-0.5">{{ subtitle }}</p>
      </div>
      <ChevronDown
        :class="cn(
          'h-4 w-4 shrink-0 text-muted-foreground transition-transform duration-200',
          collapsed ? '-rotate-90' : 'rotate-0'
        )"
        aria-hidden="true"
      />
    </button>

    <!-- Collapsible content -->
    <div
      :class="cn(
        'grid gap-3 transition-all duration-300 ease-in-out overflow-hidden',
        'grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 2xl:grid-cols-6',
        collapsed ? 'max-h-0 opacity-0' : 'max-h-[2000px] opacity-100'
      )"
    >
      <slot />
    </div>
  </section>
</template>
