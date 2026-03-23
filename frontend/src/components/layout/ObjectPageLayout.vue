<script setup lang="ts">
import {
  ref,
  provide,
  onMounted,
  onBeforeUnmount,
  nextTick,
  type HTMLAttributes,
} from 'vue'
import { cn } from '@/lib/utils'

interface SectionEntry {
  id: string
  title: string
  el: HTMLElement | null
}

interface Props {
  /** Show the dynamic anchor/tab bar when header collapses */
  showAnchorBar?: boolean
  /** Enable header collapsing on scroll */
  headerCollapsible?: boolean
  /** Show section title separator lines */
  showSectionSeparators?: boolean
  /** Upper-case section titles (Fiori style) */
  upperCaseSectionTitles?: boolean
  /** CSS class for the page container */
  class?: HTMLAttributes['class']
}

const props = withDefaults(defineProps<Props>(), {
  showAnchorBar: true,
  headerCollapsible: true,
  showSectionSeparators: true,
  upperCaseSectionTitles: true,
  class: undefined,
})

// ── Sections registry (provide/inject) ──────────────────────────────

const sections = ref<SectionEntry[]>([])
const activeSectionId = ref<string>('')

provide('object-page-register-section', (section: SectionEntry) => {
  // Avoid duplicate registrations
  if (!sections.value.some((s) => s.id === section.id)) {
    sections.value.push(section)
    // Observe the new section element for active tracking
    nextTick(() => {
      if (section.el && sectionObserver) {
        sectionObserver.observe(section.el)
      }
    })
  }
})

provide('object-page-unregister-section', (id: string) => {
  const section = sections.value.find((s) => s.id === id)
  if (section?.el && sectionObserver) {
    sectionObserver.unobserve(section.el)
  }
  sections.value = sections.value.filter((s) => s.id !== id)
})

provide('object-page-uppercase-titles', props.upperCaseSectionTitles)

// ── Header collapse detection ───────────────────────────────────────

const headerSentinel = ref<HTMLElement | null>(null)
const isHeaderCollapsed = ref(false)
let headerObserver: IntersectionObserver | null = null

function initHeaderObserver(): void {
  if (!props.headerCollapsible || !headerSentinel.value) return

  headerObserver = new IntersectionObserver(
    ([entry]) => {
      if (entry) {
        isHeaderCollapsed.value = !entry.isIntersecting
      }
    },
    { threshold: 0 },
  )

  headerObserver.observe(headerSentinel.value)
}

// ── Active section tracking ─────────────────────────────────────────

let sectionObserver: IntersectionObserver | null = null

function initSectionObserver(): void {
  sectionObserver = new IntersectionObserver(
    (entries) => {
      for (const entry of entries) {
        if (entry.isIntersecting) {
          activeSectionId.value = entry.target.id
        }
      }
    },
    { rootMargin: '-100px 0px -66% 0px' },
  )

  // Observe already-registered sections
  for (const section of sections.value) {
    if (section.el) {
      sectionObserver.observe(section.el)
    }
  }
}

// ── Anchor bar navigation ───────────────────────────────────────────

function scrollToSection(sectionId: string): void {
  const section = sections.value.find((s) => s.id === sectionId)
  if (section?.el) {
    section.el.scrollIntoView({ behavior: 'smooth', block: 'start' })
    activeSectionId.value = sectionId
  }
}

function handleTabKeydown(event: KeyboardEvent, sectionId: string): void {
  const ids = sections.value.map((s) => s.id)
  const currentIndex = ids.indexOf(sectionId)

  let targetIndex = -1

  switch (event.key) {
    case 'ArrowRight':
    case 'ArrowDown':
      event.preventDefault()
      targetIndex = (currentIndex + 1) % ids.length
      break
    case 'ArrowLeft':
    case 'ArrowUp':
      event.preventDefault()
      targetIndex = (currentIndex - 1 + ids.length) % ids.length
      break
    case 'Home':
      event.preventDefault()
      targetIndex = 0
      break
    case 'End':
      event.preventDefault()
      targetIndex = ids.length - 1
      break
    default:
      return
  }

  if (targetIndex >= 0 && ids[targetIndex]) {
    activeSectionId.value = ids[targetIndex]
    scrollToSection(ids[targetIndex])
    // Focus the new tab
    const tabEl = document.querySelector<HTMLElement>(
      `[data-anchor-tab="${ids[targetIndex]}"]`,
    )
    tabEl?.focus()
  }
}

// ── Lifecycle ───────────────────────────────────────────────────────

onMounted(() => {
  initHeaderObserver()
  initSectionObserver()

  // Set initial active section
  if (sections.value.length > 0 && !activeSectionId.value) {
    activeSectionId.value = sections.value[0].id
  }
})

onBeforeUnmount(() => {
  headerObserver?.disconnect()
  sectionObserver?.disconnect()
})
</script>

<template>
  <div :class="cn('object-page-layout relative min-h-0 flex flex-col', props.class)">
    <!-- Header sentinel — when this leaves the viewport, we collapse the header -->
    <div ref="headerSentinel" class="h-0 w-full flex-shrink-0" aria-hidden="true" />

    <!-- Sticky header area -->
    <div
      :class="cn(
        'sticky top-0 z-30 bg-background transition-all duration-200',
        isHeaderCollapsed ? 'shadow-sm border-b border-border' : '',
      )"
    >
      <!-- Expanded header -->
      <div
        :class="cn(
          'overflow-hidden transition-all duration-200',
          props.headerCollapsible && isHeaderCollapsed
            ? 'max-h-0 opacity-0 pointer-events-none'
            : 'max-h-[500px] opacity-100',
        )"
      >
        <div class="px-6 py-4">
          <div class="flex items-start justify-between gap-4">
            <div class="min-w-0 flex-1">
              <slot name="header" />
            </div>
            <div v-if="$slots.headerActions" class="flex items-center gap-2 flex-shrink-0">
              <slot name="headerActions" />
            </div>
          </div>
        </div>
      </div>

      <!-- Collapsed header: thin bar with actions only -->
      <div
        v-if="props.headerCollapsible"
        :class="cn(
          'overflow-hidden transition-all duration-200',
          isHeaderCollapsed
            ? 'max-h-16 opacity-100'
            : 'max-h-0 opacity-0 pointer-events-none',
        )"
      >
        <div class="flex items-center justify-between px-6 py-2">
          <div class="min-w-0 flex-1">
            <slot name="header" />
          </div>
          <div v-if="$slots.headerActions" class="flex items-center gap-2 flex-shrink-0">
            <slot name="headerActions" />
          </div>
        </div>
      </div>

      <!-- Separator below header content -->
      <div
        v-if="!isHeaderCollapsed"
        class="border-b border-border"
      />

      <!-- Anchor / Tab bar -->
      <div
        v-if="props.showAnchorBar && sections.length > 0"
        :class="cn(
          'overflow-hidden transition-all duration-200',
          isHeaderCollapsed
            ? 'max-h-12 opacity-100'
            : 'max-h-0 opacity-0 pointer-events-none',
        )"
      >
        <nav
          class="bg-muted/50 border-b border-border overflow-x-auto"
          aria-label="Page sections"
        >
          <div
            class="flex items-stretch px-6 min-w-max"
            role="tablist"
            aria-label="Section navigation"
          >
            <button
              v-for="section in sections"
              :key="section.id"
              :data-anchor-tab="section.id"
              role="tab"
              :aria-selected="activeSectionId === section.id"
              :aria-controls="section.id"
              :tabindex="activeSectionId === section.id ? 0 : -1"
              :class="cn(
                'relative px-4 py-2.5 text-sm font-medium whitespace-nowrap transition-colors duration-150',
                'hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1',
                activeSectionId === section.id
                  ? 'text-primary'
                  : 'text-muted-foreground',
              )"
              @click="scrollToSection(section.id)"
              @keydown="handleTabKeydown($event, section.id)"
            >
              {{ section.title }}
              <!-- Active indicator bar -->
              <span
                :class="cn(
                  'absolute bottom-0 left-0 right-0 h-0.5 transition-colors duration-150',
                  activeSectionId === section.id
                    ? 'bg-primary'
                    : 'bg-transparent',
                )"
              />
            </button>
          </div>
        </nav>
      </div>
    </div>

    <!-- Sections content area -->
    <div class="flex-1 overflow-y-auto">
      <div class="px-6 py-6">
        <div
          :class="cn(
            'flex flex-col',
            props.showSectionSeparators ? 'gap-6' : 'gap-4',
          )"
          role="tabpanel"
        >
          <slot />
        </div>
      </div>
    </div>
  </div>
</template>
