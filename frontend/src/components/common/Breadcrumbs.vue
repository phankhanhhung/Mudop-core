<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { Home, ChevronRight } from 'lucide-vue-next'

interface BreadcrumbItem {
  label: string
  to?: string
  isHome?: boolean
}

const { t } = useI18n()
const route = useRoute()

const isAuthRoute = computed(() => route.path.startsWith('/auth'))

function truncateId(id: string): string {
  if (id.length > 8) {
    return id.substring(0, 8) + '...'
  }
  return id
}

function formatLabel(segment: string): string {
  // Convert kebab-case or camelCase to Title Case
  return segment
    .replace(/[-_]/g, ' ')
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/\b\w/g, (c) => c.toUpperCase())
}

const breadcrumbs = computed<BreadcrumbItem[]>(() => {
  const path = route.path
  const items: BreadcrumbItem[] = []

  // Always start with Home
  items.push({ label: t('breadcrumbs.home'), to: '/dashboard', isHome: true })

  if (path === '/dashboard' || path === '/') {
    // Home only — make it the active (last) item
    return [{ label: t('breadcrumbs.home'), isHome: true }]
  }

  // OData entity routes: /odata/:module/:entity[/:id[/edit]] or /odata/:module/:entity/new
  if (path.startsWith('/odata/')) {
    const module = route.params.module as string
    const entity = route.params.entity as string
    const id = route.params.id as string | undefined

    // Module
    items.push({ label: formatLabel(module) })

    // Entity list
    const entityListPath = `/odata/${module}/${entity}`
    const entityLabel = formatLabel(entity)

    if (!id && !path.endsWith('/new')) {
      // Entity list page (active)
      items.push({ label: entityLabel })
    } else {
      items.push({ label: entityLabel, to: entityListPath })

      if (path.endsWith('/new')) {
        items.push({ label: t('breadcrumbs.create') })
      } else if (id && path.endsWith('/edit')) {
        items.push({ label: truncateId(id), to: `/odata/${module}/${entity}/${id}` })
        items.push({ label: t('breadcrumbs.edit') })
      } else if (id) {
        items.push({ label: truncateId(id) })
      }
    }

    return items
  }

  // Tenants routes
  if (path === '/tenants') {
    items.push({ label: t('breadcrumbs.tenants') })
    return items
  }
  if (path === '/tenants/create') {
    items.push({ label: t('breadcrumbs.tenants'), to: '/tenants' })
    items.push({ label: t('breadcrumbs.create') })
    return items
  }

  // Admin routes
  if (path === '/admin/modules') {
    items.push({ label: t('breadcrumbs.admin') })
    items.push({ label: t('breadcrumbs.modules') })
    return items
  }

  // Settings
  if (path === '/settings') {
    items.push({ label: t('breadcrumbs.settings') })
    return items
  }

  // Fallback: build from path segments
  const segments = path.split('/').filter(Boolean)
  segments.forEach((segment, index) => {
    const segmentPath = '/' + segments.slice(0, index + 1).join('/')
    if (index < segments.length - 1) {
      items.push({ label: formatLabel(segment), to: segmentPath })
    } else {
      items.push({ label: formatLabel(segment) })
    }
  })

  return items
})
</script>

<template>
  <nav v-if="!isAuthRoute && breadcrumbs.length > 0" aria-label="Breadcrumb">
    <ol class="flex items-center gap-1.5 text-sm">
      <li
        v-for="(item, index) in breadcrumbs"
        :key="index"
        class="flex items-center gap-1.5"
      >
        <!-- Separator -->
        <ChevronRight
          v-if="index > 0"
          class="h-3.5 w-3.5 text-muted-foreground/60 flex-shrink-0"
        />

        <!-- Last item (active, not clickable) -->
        <span
          v-if="index === breadcrumbs.length - 1"
          class="text-foreground font-medium"
        >
          <Home v-if="item.isHome" class="h-4 w-4" />
          <template v-else>{{ item.label }}</template>
        </span>

        <!-- Clickable item -->
        <RouterLink
          v-else-if="item.to"
          :to="item.to"
          class="text-muted-foreground hover:text-foreground transition-colors"
        >
          <Home v-if="item.isHome" class="h-4 w-4" />
          <template v-else>{{ item.label }}</template>
        </RouterLink>

        <!-- Non-clickable intermediate item (no route) -->
        <span v-else class="text-muted-foreground">
          <Home v-if="item.isHome" class="h-4 w-4" />
          <template v-else>{{ item.label }}</template>
        </span>
      </li>
    </ol>
  </nav>
</template>
