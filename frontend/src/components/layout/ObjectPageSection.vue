<script setup lang="ts">
import { ref, inject, onMounted, onBeforeUnmount, type Component } from 'vue'
import { ChevronDown, ChevronRight } from 'lucide-vue-next'
import { cn } from '@/lib/utils'

interface Props {
  /** Unique section ID */
  id: string
  /** Section title displayed in the anchor bar */
  title: string
  /** Optional subtitle */
  subtitle?: string
  /** Whether section starts collapsed */
  defaultCollapsed?: boolean
  /** Show section title in the content area */
  showTitle?: boolean
  /** Optional icon component */
  icon?: Component
}

const props = withDefaults(defineProps<Props>(), {
  subtitle: undefined,
  defaultCollapsed: false,
  showTitle: true,
  icon: undefined,
})

const sectionEl = ref<HTMLElement | null>(null)
const isCollapsed = ref(props.defaultCollapsed)

// Register with parent ObjectPageLayout via provide/inject
type RegisterFn = (section: { id: string; title: string; el: HTMLElement | null }) => void
type UnregisterFn = (id: string) => void

const noop = (): void => { /* standalone usage — no parent layout */ }

const registerSection = inject<RegisterFn>('object-page-register-section', noop as RegisterFn)
const unregisterSection = inject<UnregisterFn>('object-page-unregister-section', noop as UnregisterFn)

const upperCase = inject<boolean>('object-page-uppercase-titles', true)

function toggleCollapsed(): void {
  isCollapsed.value = !isCollapsed.value
}

onMounted(() => {
  registerSection({ id: props.id, title: props.title, el: sectionEl.value })
})

onBeforeUnmount(() => {
  unregisterSection(props.id)
})
</script>

<template>
  <section
    :id="props.id"
    ref="sectionEl"
    class="object-page-section scroll-mt-32"
    role="region"
    :aria-label="props.title"
  >
    <div class="bg-card rounded-lg border">
      <!-- Section header -->
      <div
        v-if="props.showTitle"
        class="flex items-center justify-between px-6 py-4 cursor-pointer select-none"
        role="button"
        tabindex="0"
        :aria-expanded="!isCollapsed"
        :aria-controls="`${props.id}-content`"
        @click="toggleCollapsed"
        @keydown.enter="toggleCollapsed"
        @keydown.space.prevent="toggleCollapsed"
      >
        <div class="flex items-center gap-2 min-w-0">
          <component
            :is="props.icon"
            v-if="props.icon"
            class="h-4 w-4 flex-shrink-0 text-muted-foreground"
          />
          <div class="min-w-0">
            <h3
              :class="cn(
                'text-sm font-semibold tracking-wider text-muted-foreground',
                upperCase && 'uppercase',
              )"
            >
              {{ props.title }}
            </h3>
            <p
              v-if="props.subtitle"
              class="text-xs text-muted-foreground/70 mt-0.5 truncate"
            >
              {{ props.subtitle }}
            </p>
          </div>
        </div>

        <div class="flex items-center gap-2 flex-shrink-0">
          <!-- Section-level actions -->
          <div
            v-if="$slots.actions"
            class="flex items-center gap-1"
            @click.stop
            @keydown.stop
          >
            <slot name="actions" />
          </div>

          <!-- Collapse toggle -->
          <div class="text-muted-foreground transition-transform duration-200">
            <ChevronDown
              v-if="!isCollapsed"
              class="h-4 w-4"
            />
            <ChevronRight
              v-else
              class="h-4 w-4"
            />
          </div>
        </div>
      </div>

      <!-- Section content -->
      <div
        :id="`${props.id}-content`"
        :class="cn(
          'overflow-hidden transition-all duration-200',
          isCollapsed ? 'max-h-0 opacity-0' : 'max-h-[none] opacity-100',
        )"
      >
        <div
          :class="cn(
            'px-6 pb-6',
            props.showTitle ? 'pt-0' : 'pt-6',
          )"
        >
          <slot />
        </div>
      </div>
    </div>
  </section>
</template>
