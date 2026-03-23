<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { metadataService } from '@/services/metadataService'
import { useBatchQueue } from '@/composables/useBatchQueue'
import type { ModuleMetadata } from '@/types/metadata'
import type { BatchQueueItem } from '@/types/batch'
import BatchQueueList from '@/components/batch/BatchQueueList.vue'
import BatchItemForm from '@/components/batch/BatchItemForm.vue'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Select } from '@/components/ui/select'
import { Label } from '@/components/ui/label'
import { Spinner } from '@/components/ui/spinner'
import { Alert, AlertDescription } from '@/components/ui/alert'
import {
  Play,
  Trash2,
  Layers,
  CheckCircle,
  XCircle,
  AlertCircle,
  FileJson,
  Clock,
  Package,
  ListOrdered
} from 'lucide-vue-next'

const { t } = useI18n()

// -- Module loading --
const modules = ref<ModuleMetadata[]>([])
const selectedModule = ref('')
const isLoadingModules = ref(false)
const moduleError = ref<string | null>(null)

// -- Entity sets derived from selected module --
const entitySets = ref<string[]>([])

// -- Batch queue --
const { items, addItem, removeItem, reorderItems, clearQueue, execute, isExecuting, results, error: batchError } = useBatchQueue()

// Existing queue item IDs for dependency hints
const existingIds = computed(() => items.value.map((item) => item.id))

// -- Stats --
const pendingCount = computed(() => items.value.length - successCount.value - errorCount.value)
const successCount = computed(() => results.value.filter((r) => r.status >= 200 && r.status < 300).length)
const errorCount = computed(() => results.value.filter((r) => r.status >= 400).length)

// -- Load modules on mount --
async function loadModules() {
  isLoadingModules.value = true
  moduleError.value = null
  try {
    modules.value = await metadataService.getModules()
  } catch (e) {
    moduleError.value = e instanceof Error ? e.message : t('admin.batch.failedToLoadModules')
  } finally {
    isLoadingModules.value = false
  }
}
loadModules()

// -- When module changes, load its entity sets --
watch(selectedModule, async (moduleName) => {
  entitySets.value = []
  if (!moduleName) return

  try {
    const mod = modules.value.find((m) => m.name === moduleName)
    if (mod) {
      const sets: string[] = []
      for (const service of mod.services) {
        for (const entity of service.entities) {
          sets.push(entity.name)
        }
      }
      entitySets.value = sets
    }
  } catch {
    entitySets.value = []
  }
})

// -- Handlers --
function handleAdd(item: Omit<BatchQueueItem, 'status'>) {
  addItem(item)
}

function handleRemove(id: string) {
  removeItem(id)
}

function handleReorder(fromIndex: number, toIndex: number) {
  reorderItems(fromIndex, toIndex)
}

async function handleExecute() {
  if (!selectedModule.value) return
  await execute(selectedModule.value)
}

// -- Results helpers --
function statusColorClass(status: number): string {
  if (status >= 200 && status < 300) return 'text-emerald-700 dark:text-emerald-400'
  if (status >= 400 && status < 500) return 'text-amber-700 dark:text-amber-400'
  return 'text-rose-700 dark:text-rose-400'
}

function statusBadgeVariant(status: number): 'default' | 'secondary' | 'destructive' | 'outline' {
  if (status >= 200 && status < 300) return 'default'
  if (status >= 400 && status < 500) return 'secondary'
  return 'destructive'
}

function statusBgClass(status: number): string {
  if (status >= 200 && status < 300) return 'border-emerald-200 bg-emerald-50/50 dark:border-emerald-900/50 dark:bg-emerald-950/20'
  if (status >= 400 && status < 500) return 'border-amber-200 bg-amber-50/50 dark:border-amber-900/50 dark:bg-amber-950/20'
  return 'border-rose-200 bg-rose-50/50 dark:border-rose-900/50 dark:bg-rose-950/20'
}

function formatBody(body: unknown): string {
  if (body === null || body === undefined) return t('admin.batch.noResponseBody')
  try {
    return JSON.stringify(body, null, 2)
  } catch {
    return String(body)
  }
}

const hasResults = computed(() => results.value.length > 0)
</script>

<template>
  <DefaultLayout>
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-3xl font-bold tracking-tight">{{ $t('admin.batch.title') }}</h1>
          <p class="text-muted-foreground mt-1">
            {{ $t('admin.batch.subtitle') }}
          </p>
        </div>
        <div class="flex gap-2">
          <Button
            variant="outline"
            size="sm"
            :disabled="items.length === 0 || isExecuting"
            @click="clearQueue"
          >
            <Trash2 class="mr-2 h-4 w-4" />
            {{ $t('admin.batch.clearQueue') }}
          </Button>
          <Button
            :disabled="items.length === 0 || isExecuting || !selectedModule"
            :aria-busy="isExecuting"
            @click="handleExecute"
          >
            <Spinner v-if="isExecuting" size="sm" class="mr-2" />
            <Play v-else class="mr-2 h-4 w-4" />
            {{ $t('admin.batch.executeBatch') }}
          </Button>
        </div>
      </div>

      <!-- Module loading error -->
      <Alert v-if="moduleError" variant="destructive">
        <AlertCircle class="h-4 w-4" />
        <AlertDescription>{{ moduleError }}</AlertDescription>
      </Alert>

      <!-- Batch error -->
      <Alert v-if="batchError" variant="destructive">
        <XCircle class="h-4 w-4" />
        <AlertDescription>{{ batchError }}</AlertDescription>
      </Alert>

      <!-- Stats Cards -->
      <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.batch.stats.queueSize') }}</p>
                <p class="text-2xl font-bold mt-1">{{ items.length }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center">
                <Layers class="h-5 w-5 text-primary" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.batch.stats.pending') }}</p>
                <p class="text-2xl font-bold mt-1 text-amber-600">{{ pendingCount }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-amber-500/10 flex items-center justify-center">
                <Clock class="h-5 w-5 text-amber-500" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.batch.stats.successful') }}</p>
                <p class="text-2xl font-bold mt-1 text-emerald-600">{{ successCount }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-emerald-500/10 flex items-center justify-center">
                <CheckCircle class="h-5 w-5 text-emerald-500" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.batch.stats.failed') }}</p>
                <p class="text-2xl font-bold mt-1 text-rose-600">{{ errorCount }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-rose-500/10 flex items-center justify-center">
                <XCircle class="h-5 w-5 text-rose-500" />
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      <!-- Module Selector -->
      <Card class="transition-all hover:shadow-md">
        <CardContent class="p-5">
          <div class="flex items-center gap-4">
            <div class="h-10 w-10 rounded-lg bg-primary/10 flex items-center justify-center shrink-0">
              <Package class="h-5 w-5 text-primary" />
            </div>
            <div class="flex-1 space-y-1.5">
              <Label for="module-select" class="text-sm font-medium">{{ $t('admin.batch.targetModule') }}</Label>
              <Select
                id="module-select"
                v-model="selectedModule"
                placeholder="Select a module..."
                :disabled="isLoadingModules"
              >
                <option v-for="mod in modules" :key="mod.name" :value="mod.name">
                  {{ mod.name }} (v{{ mod.version }})
                </option>
              </Select>
            </div>
            <div v-if="isLoadingModules" class="self-end pb-2">
              <Spinner size="sm" />
            </div>
            <div v-else-if="selectedModule" class="self-end pb-2">
              <Badge variant="outline" class="gap-1">
                <Layers class="h-3 w-3" />
                {{ $t('admin.batch.entitySets', { count: entitySets.length }) }}
              </Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Main content: Queue + Form on left, Results on right -->
      <div class="grid gap-6 lg:grid-cols-2">
        <!-- Left: Queue + Add Form -->
        <div class="space-y-6">
          <!-- Queue list -->
          <BatchQueueList
            :items="items"
            @remove="handleRemove"
            @reorder="handleReorder"
          />

          <!-- Add form (only when module is selected) -->
          <BatchItemForm
            v-if="selectedModule && entitySets.length > 0"
            :entity-sets="entitySets"
            :existing-ids="existingIds"
            @add="handleAdd"
          />

          <!-- No entity sets for selected module -->
          <Card v-else-if="selectedModule && entitySets.length === 0" class="border-dashed">
            <CardContent class="flex flex-col items-center justify-center py-16">
              <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
                <Layers class="h-8 w-8 text-muted-foreground" />
              </div>
              <h3 class="text-lg font-semibold mb-1">{{ $t('admin.batch.noEntitySets') }}</h3>
              <p class="text-muted-foreground text-sm text-center max-w-sm">
                {{ $t('admin.batch.noEntitySetsHint') }}
              </p>
            </CardContent>
          </Card>

          <!-- No module selected -->
          <Card v-else class="border-dashed">
            <CardContent class="flex flex-col items-center justify-center py-16">
              <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
                <Package class="h-8 w-8 text-muted-foreground" />
              </div>
              <h3 class="text-lg font-semibold mb-1">{{ $t('admin.batch.selectModuleTitle') }}</h3>
              <p class="text-muted-foreground text-sm text-center max-w-sm">
                {{ $t('admin.batch.selectModuleHint') }}
              </p>
            </CardContent>
          </Card>
        </div>

        <!-- Right: Results panel -->
        <div>
          <Card class="self-start sticky top-20">
            <CardHeader class="pb-3">
              <div class="flex items-center justify-between">
                <div class="flex items-center gap-3">
                  <div class="h-9 w-9 rounded-lg bg-primary/10 flex items-center justify-center">
                    <ListOrdered class="h-4 w-4 text-primary" />
                  </div>
                  <div>
                    <CardTitle class="text-sm font-semibold">{{ $t('admin.batch.results') }}</CardTitle>
                    <p class="text-xs text-muted-foreground">
                      {{ hasResults
                        ? $t('admin.batch.responsesReceived', { count: results.length })
                        : $t('admin.batch.resultsHint')
                      }}
                    </p>
                  </div>
                </div>
                <div v-if="hasResults" class="flex gap-2">
                  <Badge variant="default" class="bg-emerald-600 hover:bg-emerald-700 gap-1">
                    <CheckCircle class="h-3 w-3" />
                    {{ successCount }}
                  </Badge>
                  <Badge v-if="errorCount > 0" variant="destructive" class="gap-1">
                    <XCircle class="h-3 w-3" />
                    {{ errorCount }}
                  </Badge>
                </div>
              </div>
            </CardHeader>
            <CardContent>
              <div aria-live="polite" aria-atomic="false">
              <!-- No results yet -->
              <Card
                v-if="!hasResults && !isExecuting"
                class="border-dashed"
              >
                <CardContent class="flex flex-col items-center justify-center py-16">
                  <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
                    <FileJson class="h-8 w-8 text-muted-foreground" />
                  </div>
                  <h3 class="text-lg font-semibold mb-1">{{ $t('admin.batch.noResults') }}</h3>
                  <p class="text-muted-foreground text-sm text-center max-w-sm">
                    {{ $t('admin.batch.noResultsHint') }}
                  </p>
                </CardContent>
              </Card>

              <!-- Executing spinner -->
              <div v-else-if="isExecuting" class="flex flex-col items-center justify-center py-16" role="status">
                <Spinner size="lg" />
                <p class="text-sm text-muted-foreground mt-3">{{ $t('admin.batch.executingBatch') }}</p>
              </div>

              <!-- Results list -->
              <div v-else-if="hasResults" class="space-y-3">
                <div
                  v-for="response in results"
                  :key="response.id"
                  class="rounded-lg border p-4 transition-all hover:shadow-sm"
                  :class="statusBgClass(response.status)"
                >
                  <!-- Response header -->
                  <div class="flex items-center gap-2 mb-3">
                    <CheckCircle
                      v-if="response.status >= 200 && response.status < 300"
                      class="h-4 w-4 text-emerald-600 dark:text-emerald-400 shrink-0"
                    />
                    <XCircle
                      v-else
                      class="h-4 w-4 text-rose-600 dark:text-rose-400 shrink-0"
                    />
                    <span class="text-sm font-medium" :class="statusColorClass(response.status)">
                      {{ $t('admin.batch.request', { id: response.id }) }}
                    </span>
                    <Badge :variant="statusBadgeVariant(response.status)" class="ml-auto">
                      {{ response.status }}
                    </Badge>
                  </div>

                  <!-- Response body -->
                  <pre
                    class="text-xs bg-background/80 rounded-md p-3 overflow-x-auto max-h-60 overflow-y-auto font-mono border"
                  >{{ formatBody(response.body) }}</pre>
                </div>
              </div>
              </div>
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  </DefaultLayout>
</template>
