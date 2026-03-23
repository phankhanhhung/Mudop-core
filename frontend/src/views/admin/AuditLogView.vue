<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useMetadataStore } from '@/stores/metadata'
import { auditService } from '@/services/auditService'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import AuditFilterBar from '@/components/admin/AuditFilterBar.vue'
import AuditTimeline from '@/components/admin/AuditTimeline.vue'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Spinner } from '@/components/ui/spinner'
import {
  ScrollText,
  RefreshCw,
  ChevronLeft,
  ChevronRight,
  AlertCircle,
  Plus,
  Pencil,
  Trash2,
  Clock
} from 'lucide-vue-next'
import type { AuditLogEntry, AuditLogFilter } from '@/types/audit'

const { t } = useI18n()
const metadataStore = useMetadataStore()

const entries = ref<AuditLogEntry[]>([])
const totalCount = ref(0)
const isLoading = ref(false)
const error = ref<string | null>(null)
const currentFilter = ref<AuditLogFilter>({})
const actionFilter = ref<string | null>(null)

const pageSize = 50
const currentPage = ref(1)

// Stats computed from current page entries
const createCount = computed(() =>
  entries.value.filter(e =>
    e.eventName.toLowerCase().includes('create')
  ).length
)

const updateCount = computed(() =>
  entries.value.filter(e =>
    e.eventName.toLowerCase().includes('update')
  ).length
)

const deleteCount = computed(() =>
  entries.value.filter(e =>
    e.eventName.toLowerCase().includes('delete')
  ).length
)

const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize)))

const filteredEntries = computed(() => {
  if (!actionFilter.value) return entries.value
  return entries.value.filter(e =>
    e.eventName.toLowerCase().includes(actionFilter.value!)
  )
})

function toggleActionFilter(action: string) {
  actionFilter.value = actionFilter.value === action ? null : action
}

async function loadEntries() {
  isLoading.value = true
  error.value = null
  try {
    const skip = (currentPage.value - 1) * pageSize
    const result = await auditService.listLogs(currentFilter.value, pageSize, skip)
    entries.value = result.value
    totalCount.value = result.count
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('admin.audit.failedToLoad')
  } finally {
    isLoading.value = false
  }
}

function onFilterChange(filter: AuditLogFilter) {
  currentFilter.value = filter
  currentPage.value = 1
  actionFilter.value = null
  loadEntries()
}

function prevPage() {
  if (currentPage.value > 1) {
    currentPage.value--
    loadEntries()
  }
}

function nextPage() {
  if (currentPage.value < totalPages.value) {
    currentPage.value++
    loadEntries()
  }
}

function goToPage(page: number) {
  currentPage.value = page
  loadEntries()
}

onMounted(async () => {
  if (!metadataStore.hasModules) {
    try { await metadataStore.fetchModules() } catch { /* ignore */ }
  }
  loadEntries()
})
</script>

<template>
  <DefaultLayout>
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-3xl font-bold tracking-tight">{{ $t('admin.audit.title') }}</h1>
          <p class="text-muted-foreground mt-1">{{ $t('admin.audit.subtitle') }}</p>
        </div>
        <div class="flex gap-2">
          <Button variant="outline" size="sm" :disabled="isLoading" @click="loadEntries">
            <Spinner v-if="isLoading" size="sm" class="mr-2" />
            <RefreshCw v-else class="mr-2 h-4 w-4" />
            {{ $t('common.refresh') }}
          </Button>
        </div>
      </div>

      <!-- Stats Cards -->
      <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <Card
          class="cursor-pointer transition-all hover:shadow-md"
          :class="actionFilter === null ? 'ring-2 ring-primary' : ''"
          @click="actionFilter = null"
        >
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.audit.stats.total') }}</p>
                <p class="text-2xl font-bold mt-1">{{ totalCount }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center">
                <ScrollText class="h-5 w-5 text-primary" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card
          class="cursor-pointer transition-all hover:shadow-md"
          :class="actionFilter === 'create' ? 'ring-2 ring-emerald-500' : ''"
          @click="toggleActionFilter('create')"
        >
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.audit.stats.creates') }}</p>
                <p class="text-2xl font-bold mt-1 text-emerald-600">{{ createCount }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-emerald-500/10 flex items-center justify-center">
                <Plus class="h-5 w-5 text-emerald-500" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card
          class="cursor-pointer transition-all hover:shadow-md"
          :class="actionFilter === 'update' ? 'ring-2 ring-amber-500' : ''"
          @click="toggleActionFilter('update')"
        >
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.audit.stats.updates') }}</p>
                <p class="text-2xl font-bold mt-1 text-amber-600">{{ updateCount }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-amber-500/10 flex items-center justify-center">
                <Pencil class="h-5 w-5 text-amber-500" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card
          class="cursor-pointer transition-all hover:shadow-md"
          :class="actionFilter === 'delete' ? 'ring-2 ring-rose-500' : ''"
          @click="toggleActionFilter('delete')"
        >
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.audit.stats.deletes') }}</p>
                <p class="text-2xl font-bold mt-1 text-rose-600">{{ deleteCount }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-rose-500/10 flex items-center justify-center">
                <Trash2 class="h-5 w-5 text-rose-500" />
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      <!-- Filters -->
      <AuditFilterBar @filter-change="onFilterChange" />

      <!-- Error -->
      <Alert v-if="error" variant="destructive">
        <AlertCircle class="h-4 w-4" />
        <AlertDescription>{{ error }}</AlertDescription>
      </Alert>

      <!-- Loading -->
      <div v-if="isLoading" class="flex flex-col items-center justify-center py-16">
        <Spinner size="lg" />
        <p class="text-muted-foreground mt-3 text-sm">{{ $t('admin.audit.loading') }}</p>
      </div>

      <!-- Empty state -->
      <Card v-else-if="entries.length === 0 && !isLoading" class="border-dashed">
        <CardContent class="flex flex-col items-center justify-center py-16">
          <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
            <ScrollText class="h-8 w-8 text-muted-foreground" />
          </div>
          <h3 class="text-lg font-semibold mb-1">{{ $t('admin.audit.noEvents') }}</h3>
          <p class="text-muted-foreground text-sm text-center max-w-sm">
            {{ $t('admin.audit.noEventsDescription') }}
          </p>
        </CardContent>
      </Card>

      <!-- No results after action filter -->
      <Card v-else-if="filteredEntries.length === 0 && actionFilter" class="border-dashed">
        <CardContent class="flex flex-col items-center justify-center py-12">
          <ScrollText class="h-10 w-10 text-muted-foreground mb-3" />
          <h3 class="text-lg font-semibold mb-1">{{ $t('admin.audit.noMatchingEvents') }}</h3>
          <p class="text-muted-foreground text-sm">
            {{ $t('admin.audit.noMatchingEventsDescription') }}
          </p>
          <Button variant="outline" class="mt-3" @click="actionFilter = null">
            {{ $t('admin.audit.clearActionFilter') }}
          </Button>
        </CardContent>
      </Card>

      <!-- Timeline Card -->
      <Card v-else>
        <CardHeader>
          <div class="flex items-center justify-between">
            <div class="flex items-center gap-3">
              <div class="h-9 w-9 rounded-lg bg-primary/10 flex items-center justify-center">
                <Clock class="h-5 w-5 text-primary" />
              </div>
              <div>
                <CardTitle>{{ $t('admin.audit.events') }}</CardTitle>
                <CardDescription v-if="totalCount > 0">
                  {{ $t('admin.audit.showingRange', {
                    start: (currentPage - 1) * pageSize + 1,
                    end: Math.min(currentPage * pageSize, totalCount),
                    total: totalCount
                  }) }}
                  <span v-if="actionFilter" class="ml-1 text-xs">
                    ({{ $t('admin.audit.filteredTo', { action: actionFilter }) }})
                  </span>
                </CardDescription>
              </div>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <AuditTimeline :entries="filteredEntries" :is-loading="isLoading" />

          <!-- Pagination -->
          <div v-if="totalPages > 1" class="flex items-center justify-between px-4 py-3 border-t bg-muted/30 mt-6 -mx-6 -mb-6 rounded-b-lg">
            <p class="text-sm text-muted-foreground">
              {{ $t('admin.audit.showingPage', {
                start: (currentPage - 1) * pageSize + 1,
                end: Math.min(currentPage * pageSize, totalCount),
                total: totalCount
              }) }}
            </p>
            <div class="flex items-center gap-1">
              <Button
                variant="outline"
                size="sm"
                class="h-8 w-8 p-0"
                :disabled="currentPage <= 1"
                @click="prevPage"
              >
                <ChevronLeft class="h-4 w-4" />
              </Button>
              <template v-for="page in totalPages" :key="page">
                <Button
                  v-if="page === 1 || page === totalPages || Math.abs(page - currentPage) <= 1"
                  :variant="page === currentPage ? 'default' : 'outline'"
                  size="sm"
                  class="h-8 w-8 p-0"
                  @click="goToPage(page)"
                >
                  {{ page }}
                </Button>
                <span
                  v-else-if="page === 2 && currentPage > 3 || page === totalPages - 1 && currentPage < totalPages - 2"
                  class="px-1 text-muted-foreground"
                >...</span>
              </template>
              <Button
                variant="outline"
                size="sm"
                class="h-8 w-8 p-0"
                :disabled="currentPage >= totalPages"
                @click="nextPage"
              >
                <ChevronRight class="h-4 w-4" />
              </Button>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
