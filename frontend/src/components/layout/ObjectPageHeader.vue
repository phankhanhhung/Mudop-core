<script setup lang="ts">
import { type Component } from 'vue'
import { User } from 'lucide-vue-next'
import { cn } from '@/lib/utils'

interface Props {
  /** Main title (entity name / record identifier) */
  title: string
  /** Subtitle (entity type, description, etc.) */
  subtitle?: string
  /** Additional description text below subtitle */
  description?: string
  /** Avatar image URL */
  avatarSrc?: string
  /** Avatar initials fallback (e.g. "AC" for "Acme Corp") */
  avatarInitials?: string
  /** Avatar icon component (fallback if no src/initials) */
  avatarIcon?: Component
  /** Avatar background color class (e.g. 'bg-primary', 'bg-emerald-500') */
  avatarColor?: string
  /** Whether the header is in collapsed state (controlled by ObjectPageLayout) */
  collapsed?: boolean
  /** Show breadcrumb slot area */
  showBreadcrumb?: boolean
}

withDefaults(defineProps<Props>(), {
  subtitle: undefined,
  description: undefined,
  avatarSrc: undefined,
  avatarInitials: undefined,
  avatarIcon: undefined,
  avatarColor: undefined,
  collapsed: false,
  showBreadcrumb: false,
})

const defaultIcon = User
</script>

<template>
  <header
    :class="cn(
      'object-page-header transition-all duration-200',
      collapsed ? 'py-2 px-6' : 'py-5 px-6'
    )"
    role="banner"
    :aria-label="`Object header: ${title}`"
  >
    <!-- Breadcrumb area -->
    <div
      v-if="showBreadcrumb && !collapsed"
      class="mb-3 transition-all duration-200"
    >
      <slot name="breadcrumb" />
    </div>

    <!-- Main header row -->
    <div class="flex items-start gap-4">
      <!-- Avatar -->
      <div
        :class="cn(
          'rounded-full flex items-center justify-center shrink-0 ring-2 ring-background shadow-sm transition-all duration-200',
          collapsed ? 'h-8 w-8' : 'h-14 w-14',
          avatarColor || 'bg-primary/10'
        )"
        role="img"
        :aria-label="avatarInitials ? `Avatar: ${avatarInitials}` : 'Avatar'"
      >
        <img
          v-if="avatarSrc"
          :src="avatarSrc"
          :alt="title"
          class="h-full w-full rounded-full object-cover"
        />
        <span
          v-else-if="avatarInitials"
          :class="cn(
            'font-bold transition-all duration-200',
            collapsed ? 'text-xs' : 'text-lg',
            avatarColor ? 'text-white' : 'text-primary'
          )"
        >
          {{ avatarInitials }}
        </span>
        <component
          v-else
          :is="avatarIcon || defaultIcon"
          :class="cn(
            'transition-all duration-200',
            collapsed ? 'h-4 w-4' : 'h-6 w-6',
            avatarColor ? 'text-white' : 'text-primary'
          )"
          aria-hidden="true"
        />
      </div>

      <!-- Title / Subtitle / Description -->
      <div class="flex-1 min-w-0">
        <div class="flex items-start justify-between gap-4">
          <div class="min-w-0">
            <h1
              :class="cn(
                'truncate transition-all duration-200',
                collapsed
                  ? 'text-lg font-semibold'
                  : 'text-2xl font-bold tracking-tight'
              )"
            >
              {{ title }}
            </h1>
            <p
              v-if="subtitle && !collapsed"
              class="text-sm text-muted-foreground mt-0.5 truncate transition-opacity duration-200"
            >
              {{ subtitle }}
            </p>
          </div>

          <!-- Status badges -->
          <div class="flex items-center gap-2 shrink-0">
            <slot name="status" />
          </div>
        </div>

        <!-- Description (expanded only) -->
        <p
          v-if="description && !collapsed"
          class="text-sm text-muted-foreground mt-1 max-w-2xl transition-opacity duration-200"
        >
          {{ description }}
        </p>
      </div>

      <!-- Actions (always visible) -->
      <div class="flex items-center gap-2 shrink-0">
        <slot name="actions" />
      </div>
    </div>

    <!-- Expandable content (KPIs + Attributes) -->
    <div
      :class="cn(
        'transition-all duration-200 overflow-hidden',
        collapsed
          ? 'max-h-0 opacity-0 mt-0'
          : 'max-h-96 opacity-100'
      )"
    >
      <!-- KPI facets -->
      <div class="flex flex-wrap gap-4 sm:gap-6 mt-4">
        <slot name="kpis" />
      </div>

      <!-- Attributes row -->
      <div class="flex flex-wrap items-center gap-x-6 gap-y-1 mt-3 text-sm">
        <slot name="attributes" />
      </div>
    </div>
  </header>
</template>
