<script setup lang="ts">
import {
  ref,
  computed,
  onMounted,
  onBeforeUnmount,
  type HTMLAttributes,
} from 'vue'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import {
  ArrowLeft,
  ChevronDown,
  ChevronUp,
  Pin,
  PinOff,
} from 'lucide-vue-next'

interface Props {
  /** Enable header collapsing on scroll */
  headerCollapsible?: boolean
  /** Show the pin/unpin toggle in the header */
  headerPinnable?: boolean
  /** Start with header collapsed */
  initialCollapsed?: boolean
  /** Auto-collapse header when content area receives focus/click */
  collapseOnContentFocus?: boolean
  /** Show the floating footer bar */
  showFooter?: boolean
  /** Footer content alignment */
  footerAlign?: 'left' | 'center' | 'right' | 'between'
  /** Show a back-navigation button in the title bar */
  showBackButton?: boolean
  /** CSS class for the page container */
  class?: HTMLAttributes['class']
}

const props = withDefaults(defineProps<Props>(), {
  headerCollapsible: true,
  headerPinnable: true,
  initialCollapsed: false,
  collapseOnContentFocus: false,
  showFooter: false,
  footerAlign: 'right',
  showBackButton: false,
  class: undefined,
})

const emit = defineEmits<{
  back: []
  'collapse-change': [collapsed: boolean]
  'pin-change': [pinned: boolean]
}>()

// ── Header collapse detection (content scroll) ─────────────────────

const contentRef = ref<HTMLElement | null>(null)
const isScrollCollapsed = ref(false)
const manualCollapse = ref<boolean | null>(props.initialCollapsed ? true : null)
const isPinned = ref(false)

/** Scroll threshold (px) before auto-collapsing the header */
const COLLAPSE_THRESHOLD = 32

const effectiveCollapsed = computed(() => {
  if (!props.headerCollapsible) return false
  if (isPinned.value) return false
  if (manualCollapse.value !== null) return manualCollapse.value
  return isScrollCollapsed.value
})

function onContentScroll(): void {
  if (!props.headerCollapsible || !contentRef.value) return
  const scrollTop = contentRef.value.scrollTop
  const shouldCollapse = scrollTop > COLLAPSE_THRESHOLD
  if (shouldCollapse !== isScrollCollapsed.value) {
    isScrollCollapsed.value = shouldCollapse
    // Reset manual override when scroll state changes
    manualCollapse.value = null
  }
}

function toggleCollapse(): void {
  manualCollapse.value = !effectiveCollapsed.value
  emit('collapse-change', manualCollapse.value)
}

function togglePin(): void {
  isPinned.value = !isPinned.value
  emit('pin-change', isPinned.value)
}

function onContentFocusIn(): void {
  if (!props.collapseOnContentFocus || !props.headerCollapsible || isPinned.value) return
  if (!effectiveCollapsed.value) {
    manualCollapse.value = true
    emit('collapse-change', true)
  }
}

// ── Lifecycle ───────────────────────────────────────────────────────

onMounted(() => {
  contentRef.value?.addEventListener('scroll', onContentScroll, { passive: true })
  if (props.collapseOnContentFocus) {
    contentRef.value?.addEventListener('focusin', onContentFocusIn, { passive: true })
    contentRef.value?.addEventListener('click', onContentFocusIn, { passive: true })
  }
})

onBeforeUnmount(() => {
  contentRef.value?.removeEventListener('scroll', onContentScroll)
  contentRef.value?.removeEventListener('focusin', onContentFocusIn)
  contentRef.value?.removeEventListener('click', onContentFocusIn)
})
</script>

<template>
  <div :class="cn('dynamic-page relative min-h-0 flex flex-col', props.class)">

    <!-- Sticky header -->
    <div
      :class="cn(
        'sticky top-0 z-30 bg-background transition-all duration-200',
        effectiveCollapsed ? 'shadow-sm border-b' : '',
      )"
    >
      <!-- Breadcrumb slot (always visible) -->
      <div v-if="$slots.breadcrumb" class="px-6 pt-3 pb-1">
        <slot name="breadcrumb" />
      </div>

      <!-- Title bar (always visible — shows even when collapsed) -->
      <div class="px-6 py-2 flex items-center gap-4">
        <div class="flex items-center gap-3 min-w-0 shrink-0">
          <Button
            v-if="showBackButton"
            variant="ghost"
            size="icon"
            class="shrink-0"
            @click="emit('back')"
          >
            <ArrowLeft class="h-4 w-4" />
          </Button>
          <div class="min-w-0">
            <slot name="title" />
          </div>
        </div>
        <div v-if="$slots.headerActions" class="flex items-center gap-2 flex-1 min-w-0 justify-end">
          <slot name="headerActions" />
        </div>
        <div class="flex items-center gap-1 shrink-0">
          <!-- Pin / Unpin button -->
          <Button
            v-if="headerCollapsible && headerPinnable"
            variant="ghost"
            size="icon"
            :title="isPinned ? 'Unpin header' : 'Pin header'"
            @click="togglePin"
          >
            <Pin v-if="isPinned" class="h-4 w-4" />
            <PinOff v-else class="h-4 w-4" />
          </Button>
          <!-- Expand / Collapse toggle -->
          <Button
            v-if="headerCollapsible"
            variant="ghost"
            size="icon"
            :title="effectiveCollapsed ? 'Expand header' : 'Collapse header'"
            @click="toggleCollapse"
          >
            <ChevronDown v-if="effectiveCollapsed" class="h-4 w-4" />
            <ChevronUp v-else class="h-4 w-4" />
          </Button>
        </div>
      </div>

      <!-- Collapsible header content -->
      <div
        class="overflow-hidden transition-all duration-200"
        :class="effectiveCollapsed ? 'max-h-0 opacity-0' : 'max-h-[500px] opacity-100'"
      >
        <div v-if="$slots.header" class="px-6 pb-4">
          <slot name="header" />
        </div>
      </div>

      <!-- Bottom border -->
      <div class="border-b border-border" />
    </div>

    <!-- Content area (scrollable) -->
    <div ref="contentRef" class="flex-1 overflow-y-auto">
      <div class="px-6 py-6">
        <slot />
      </div>
    </div>

    <!-- Floating footer -->
    <div
      v-if="showFooter && $slots.footer"
      class="sticky bottom-0 z-20 bg-background border-t shadow-[0_-2px_10px_rgba(0,0,0,0.05)]"
    >
      <div
        class="px-6 py-3 flex items-center"
        :class="{
          'justify-start': footerAlign === 'left',
          'justify-center': footerAlign === 'center',
          'justify-end': footerAlign === 'right',
          'justify-between': footerAlign === 'between',
        }"
      >
        <slot name="footer" />
      </div>
    </div>
  </div>
</template>
