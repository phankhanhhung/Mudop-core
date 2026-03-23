<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { metadataService } from '@/services/metadataService'
import { odataService } from '@/services/odataService'
import type { ModuleMetadata, EntityMetadata } from '@/types/metadata'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Spinner } from '@/components/ui/spinner'
import { Select } from '@/components/ui/select'
import { Alert, AlertDescription } from '@/components/ui/alert'
import {
  Book,
  ChevronDown,
  ChevronRight,
  Play,
  Clock,
  Database,
  Zap,
  FunctionSquare,
  Copy,
  Check,
  RefreshCw,
  Server,
  AlertCircle,
  FileCode
} from 'lucide-vue-next'

const { t } = useI18n()

// --- State ---
const modules = ref<ModuleMetadata[]>([])
const selectedModuleName = ref('')
const isLoadingModules = ref(false)
const error = ref<string | null>(null)

// Entity detail cache
const entityDetails = ref<Map<string, EntityMetadata>>(new Map())
const expandedEntities = ref<Set<string>>(new Set())
const loadingEntities = ref<Set<string>>(new Set())

// Try It panel state
const tryItEntitySet = ref('')
const tryItOperation = ref('GET_LIST')
const tryItId = ref('')
const tryItFilter = ref('')
const tryItSelect = ref('')
const tryItExpand = ref('')
const tryItTop = ref('')
const tryItSkip = ref('')
const tryItBody = ref('')
const tryItResponse = ref<string | null>(null)
const tryItUrl = ref('')
const tryItIsLoading = ref(false)
const tryItDuration = ref<number | null>(null)
const tryItStatusCode = ref<number | null>(null)

// Quick Reference toggle
const quickRefExpanded = ref(false)

// Clipboard feedback
const copiedUrl = ref(false)

// --- Computed ---
const selectedModule = computed<ModuleMetadata | undefined>(() =>
  modules.value.find(m => m.name === selectedModuleName.value)
)

const allEntitySets = computed(() => {
  if (!selectedModule.value) return []
  const sets: { name: string; entityType: string; serviceName: string }[] = []
  for (const service of selectedModule.value.services) {
    for (const entity of service.entities) {
      sets.push({ name: entity.name, entityType: entity.entityType, serviceName: service.name })
    }
  }
  return sets
})

const allActions = computed(() => {
  if (!selectedModule.value) return []
  const actions: { name: string; serviceName: string; parameters: { name: string; type: string; isRequired: boolean }[]; returnType?: string; isBound: boolean }[] = []
  for (const service of selectedModule.value.services) {
    for (const action of service.actions) {
      actions.push({ ...action, serviceName: service.name, parameters: action.parameters.map(p => ({ ...p, type: String(p.type) })) })
    }
  }
  return actions
})

const allFunctions = computed(() => {
  if (!selectedModule.value) return []
  const funcs: { name: string; serviceName: string; parameters: { name: string; type: string; isRequired: boolean }[]; returnType: string; isBound: boolean }[] = []
  for (const service of selectedModule.value.services) {
    for (const fn of service.functions) {
      funcs.push({ ...fn, serviceName: service.name, parameters: fn.parameters.map(p => ({ ...p, type: String(p.type) })), returnType: String(fn.returnType) })
    }
  }
  return funcs
})

const constructedUrl = computed(() => {
  if (!tryItEntitySet.value || !selectedModuleName.value) return ''
  const base = `/api/odata/${selectedModuleName.value}/${tryItEntitySet.value}`

  if (tryItOperation.value === 'GET_BY_ID') {
    if (!tryItId.value) return `${base}/{id}`
    const params = buildQueryParams()
    return `${base}/${tryItId.value}${params}`
  }

  if (tryItOperation.value === 'POST') {
    return base
  }

  // GET_LIST
  const params = buildQueryParams()
  return `${base}${params}`
})

function statusBadgeClass(code: number): string {
  if (code < 300) return 'bg-emerald-600 hover:bg-emerald-700 text-white'
  if (code < 500) return 'bg-amber-500 hover:bg-amber-600 text-white'
  return 'bg-rose-500 hover:bg-rose-600 text-white'
}

// --- Methods ---
function buildQueryParams(): string {
  const parts: string[] = []
  if (tryItFilter.value) parts.push(`$filter=${encodeURIComponent(tryItFilter.value)}`)
  if (tryItSelect.value) parts.push(`$select=${encodeURIComponent(tryItSelect.value)}`)
  if (tryItExpand.value) parts.push(`$expand=${encodeURIComponent(tryItExpand.value)}`)
  if (tryItTop.value) parts.push(`$top=${tryItTop.value}`)
  if (tryItSkip.value) parts.push(`$skip=${tryItSkip.value}`)
  return parts.length > 0 ? `?${parts.join('&')}` : ''
}

async function fetchModules() {
  isLoadingModules.value = true
  error.value = null
  try {
    modules.value = await metadataService.getModules()
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('admin.apiDocs.failedToLoad')
  } finally {
    isLoadingModules.value = false
  }
}

async function toggleEntityExpand(entityType: string) {
  if (expandedEntities.value.has(entityType)) {
    expandedEntities.value.delete(entityType)
    return
  }

  if (!entityDetails.value.has(entityType) && selectedModuleName.value) {
    loadingEntities.value.add(entityType)
    try {
      const detail = await metadataService.getEntity(selectedModuleName.value, entityType)
      entityDetails.value.set(entityType, detail)
    } catch {
      // silently fail
    } finally {
      loadingEntities.value.delete(entityType)
    }
  }
  expandedEntities.value.add(entityType)
}

function getEntityDetail(entityType: string): EntityMetadata | undefined {
  return entityDetails.value.get(entityType)
}

async function executeTryIt() {
  if (!selectedModuleName.value || !tryItEntitySet.value) return

  tryItIsLoading.value = true
  tryItResponse.value = null
  tryItDuration.value = null
  tryItStatusCode.value = null
  tryItUrl.value = constructedUrl.value

  const start = performance.now()

  try {
    let result: unknown

    if (tryItOperation.value === 'GET_LIST') {
      result = await odataService.query(
        selectedModuleName.value,
        tryItEntitySet.value,
        {
          $filter: tryItFilter.value || undefined,
          $select: tryItSelect.value || undefined,
          $expand: tryItExpand.value || undefined,
          $top: tryItTop.value ? parseInt(tryItTop.value) : undefined,
          $skip: tryItSkip.value ? parseInt(tryItSkip.value) : undefined,
          $count: true
        },
        { skipCache: true }
      )
      tryItStatusCode.value = 200
    } else if (tryItOperation.value === 'GET_BY_ID') {
      if (!tryItId.value) {
        tryItResponse.value = JSON.stringify({ error: t('admin.apiDocs.tryIt.enterIdHint') }, null, 2)
        tryItIsLoading.value = false
        return
      }
      result = await odataService.getById(
        selectedModuleName.value,
        tryItEntitySet.value,
        tryItId.value,
        {
          $select: tryItSelect.value || undefined,
          $expand: tryItExpand.value || undefined
        }
      )
      tryItStatusCode.value = 200
    } else if (tryItOperation.value === 'POST') {
      let body: Record<string, unknown> = {}
      if (tryItBody.value) {
        try {
          body = JSON.parse(tryItBody.value)
        } catch {
          tryItResponse.value = JSON.stringify({ error: t('admin.apiDocs.tryIt.invalidJson') }, null, 2)
          tryItIsLoading.value = false
          return
        }
      }
      result = await odataService.create(
        selectedModuleName.value,
        tryItEntitySet.value,
        body
      )
      tryItStatusCode.value = 201
    }

    tryItResponse.value = JSON.stringify(result, null, 2)
  } catch (e: unknown) {
    const err = e as { message?: string; status?: number }
    tryItStatusCode.value = err.status ?? 500
    tryItResponse.value = JSON.stringify(
      { error: err.message ?? String(e) },
      null,
      2
    )
  } finally {
    tryItDuration.value = Math.round(performance.now() - start)
    tryItIsLoading.value = false
  }
}

async function copyUrl() {
  try {
    await navigator.clipboard.writeText(constructedUrl.value)
    copiedUrl.value = true
    setTimeout(() => { copiedUrl.value = false }, 2000)
  } catch {
    // clipboard not available
  }
}

// Reset Try It when module changes
watch(selectedModuleName, () => {
  tryItEntitySet.value = ''
  tryItResponse.value = null
  tryItDuration.value = null
  tryItStatusCode.value = null
  expandedEntities.value.clear()
  entityDetails.value.clear()
})

onMounted(() => {
  fetchModules()
})

const queryOptions = [
  { name: '$filter', description: 'admin.apiDocs.reference.filterDesc', example: "Name eq 'Acme'" },
  { name: '$select', description: 'admin.apiDocs.reference.selectDesc', example: 'Name,Email,Status' },
  { name: '$expand', description: 'admin.apiDocs.reference.expandDesc', example: 'Orders($top=5)' },
  { name: '$orderby', description: 'admin.apiDocs.reference.orderbyDesc', example: 'Name asc,CreatedAt desc' },
  { name: '$top', description: 'admin.apiDocs.reference.topDesc', example: '10' },
  { name: '$skip', description: 'admin.apiDocs.reference.skipDesc', example: '20' },
  { name: '$count', description: 'admin.apiDocs.reference.countDesc', example: 'true' },
  { name: '$search', description: 'admin.apiDocs.reference.searchDesc', example: 'keyword' }
]
</script>

<template>
  <DefaultLayout>
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-3xl font-bold tracking-tight">{{ t('admin.apiDocs.title') }}</h1>
          <p class="text-muted-foreground mt-1">{{ t('admin.apiDocs.subtitle') }}</p>
        </div>
        <Button variant="outline" size="sm" @click="fetchModules" :disabled="isLoadingModules">
          <Spinner v-if="isLoadingModules" size="sm" class="mr-2" />
          <RefreshCw v-else class="mr-2 h-4 w-4" />
          {{ t('common.refresh') }}
        </Button>
      </div>

      <!-- Error -->
      <Alert v-if="error" variant="destructive">
        <AlertCircle class="h-4 w-4" />
        <AlertDescription>{{ error }}</AlertDescription>
      </Alert>

      <!-- Module Selector -->
      <Card class="transition-all hover:shadow-md">
        <CardContent class="p-4">
          <div class="flex items-center gap-4">
            <div class="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center shrink-0">
              <Book class="h-5 w-5 text-primary" />
            </div>
            <div class="flex-1 min-w-0">
              <p class="text-sm font-medium text-muted-foreground mb-1.5">{{ t('admin.apiDocs.selectModule') }}</p>
              <div v-if="isLoadingModules" class="flex items-center gap-2">
                <Spinner class="h-4 w-4" />
                <span class="text-sm text-muted-foreground">{{ t('common.loading') }}</span>
              </div>
              <div v-else-if="modules.length === 0" class="text-sm text-muted-foreground">
                {{ t('admin.apiDocs.noModules') }}
              </div>
              <Select
                v-else
                v-model="selectedModuleName"
                :placeholder="t('admin.apiDocs.selectModulePlaceholder')"
              >
                <option value="" disabled>{{ t('admin.apiDocs.selectModulePlaceholder') }}</option>
                <option v-for="mod in modules" :key="mod.name" :value="mod.name">
                  {{ mod.name }} (v{{ mod.version }})
                </option>
              </Select>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Stats Cards (visible when module selected) -->
      <div v-if="selectedModule" class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ t('admin.apiDocs.entitySets') }}</p>
                <p class="text-2xl font-bold mt-1 text-primary">{{ allEntitySets.length }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center">
                <Database class="h-5 w-5 text-primary" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ t('admin.apiDocs.actions') }}</p>
                <p class="text-2xl font-bold mt-1 text-emerald-600">{{ allActions.length }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-emerald-500/10 flex items-center justify-center">
                <Zap class="h-5 w-5 text-emerald-500" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ t('admin.apiDocs.functions') }}</p>
                <p class="text-2xl font-bold mt-1 text-violet-600">{{ allFunctions.length }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-violet-500/10 flex items-center justify-center">
                <FunctionSquare class="h-5 w-5 text-violet-500" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ t('admin.apiDocs.service') }}</p>
                <p class="text-2xl font-bold mt-1 text-amber-600">{{ selectedModule.services.length }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-amber-500/10 flex items-center justify-center">
                <Server class="h-5 w-5 text-amber-500" />
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      <!-- Content when module is selected -->
      <template v-if="selectedModule">
        <!-- Entity Sets Section -->
        <Card>
          <CardHeader>
            <CardTitle class="flex items-center gap-2">
              <Database class="h-5 w-5 text-primary" />
              {{ t('admin.apiDocs.entitySets') }}
            </CardTitle>
            <CardDescription>
              {{ t('admin.apiDocs.entitySetsSubtitle', { count: allEntitySets.length }) }}
            </CardDescription>
          </CardHeader>
          <CardContent>
            <div v-if="allEntitySets.length === 0">
              <Card class="border-dashed">
                <CardContent class="flex flex-col items-center justify-center py-12">
                  <div class="h-12 w-12 rounded-full bg-muted flex items-center justify-center mb-3">
                    <Database class="h-6 w-6 text-muted-foreground" />
                  </div>
                  <h3 class="text-base font-semibold mb-1">{{ t('admin.apiDocs.noEntitySets') }}</h3>
                  <p class="text-muted-foreground text-sm text-center max-w-sm">No entity sets are exposed by this module's services.</p>
                </CardContent>
              </Card>
            </div>
            <div v-else class="overflow-x-auto">
              <table class="w-full text-sm">
                <thead>
                  <tr class="border-b">
                    <th class="py-3 pr-4 text-left font-medium text-muted-foreground">{{ t('admin.apiDocs.entitySetName') }}</th>
                    <th class="py-3 pr-4 text-left font-medium text-muted-foreground">{{ t('admin.apiDocs.entityType') }}</th>
                    <th class="py-3 pr-4 text-left font-medium text-muted-foreground">{{ t('admin.apiDocs.service') }}</th>
                    <th class="py-3 text-left font-medium text-muted-foreground">{{ t('admin.apiDocs.details') }}</th>
                  </tr>
                </thead>
                <tbody>
                  <template v-for="es in allEntitySets" :key="es.name">
                    <tr class="border-b hover:bg-muted/50 transition-colors">
                      <td class="py-3 pr-4 font-mono text-xs font-medium">{{ es.name }}</td>
                      <td class="py-3 pr-4">
                        <Badge variant="secondary" class="font-mono text-xs">{{ es.entityType }}</Badge>
                      </td>
                      <td class="py-3 pr-4 text-muted-foreground">{{ es.serviceName }}</td>
                      <td class="py-3">
                        <Button
                          variant="ghost"
                          size="sm"
                          class="h-7 text-xs"
                          @click="toggleEntityExpand(es.entityType)"
                        >
                          <Spinner v-if="loadingEntities.has(es.entityType)" class="mr-1.5 h-3 w-3" />
                          <component
                            v-else
                            :is="expandedEntities.has(es.entityType) ? ChevronDown : ChevronRight"
                            class="mr-1 h-4 w-4"
                          />
                          {{ t('admin.apiDocs.fields') }}
                        </Button>
                      </td>
                    </tr>
                    <!-- Expanded field list -->
                    <tr v-if="expandedEntities.has(es.entityType)">
                      <td colspan="4" class="p-0">
                        <div class="bg-muted/30 border-b px-6 py-4">
                          <div v-if="loadingEntities.has(es.entityType)" class="flex items-center gap-2 text-sm py-2">
                            <Spinner class="h-4 w-4" />
                            {{ t('common.loading') }}
                          </div>
                          <div v-else-if="getEntityDetail(es.entityType)" class="space-y-3">
                            <!-- Entity metadata summary -->
                            <div class="flex items-center gap-4 text-xs">
                              <span class="flex items-center gap-1.5 text-muted-foreground">
                                <Badge variant="outline" class="text-[10px] font-normal">KEY</Badge>
                                <code class="rounded bg-background px-1.5 py-0.5 border text-xs">{{ getEntityDetail(es.entityType)!.keys.join(', ') }}</code>
                              </span>
                              <span class="text-muted-foreground">
                                {{ t('admin.apiDocs.fieldCount', { count: getEntityDetail(es.entityType)!.fields.length }) }}
                              </span>
                            </div>
                            <!-- Fields table -->
                            <div class="rounded-md border bg-background overflow-hidden">
                              <table class="w-full text-xs">
                                <thead>
                                  <tr class="border-b bg-muted/50">
                                    <th class="py-2 px-3 text-left font-medium text-muted-foreground">{{ t('admin.apiDocs.fieldName') }}</th>
                                    <th class="py-2 px-3 text-left font-medium text-muted-foreground">{{ t('common.type') }}</th>
                                    <th class="py-2 px-3 text-left font-medium text-muted-foreground">{{ t('admin.apiDocs.required') }}</th>
                                    <th class="py-2 px-3 text-left font-medium text-muted-foreground">{{ t('admin.apiDocs.attributes') }}</th>
                                  </tr>
                                </thead>
                                <tbody>
                                  <tr
                                    v-for="field in getEntityDetail(es.entityType)!.fields"
                                    :key="field.name"
                                    class="border-b last:border-b-0 hover:bg-muted/30 transition-colors"
                                  >
                                    <td class="py-2 px-3 font-mono">
                                      <span class="font-medium">{{ field.name }}</span>
                                      <Badge
                                        v-if="getEntityDetail(es.entityType)!.keys.includes(field.name)"
                                        variant="outline"
                                        class="ml-1.5 text-[10px] border-amber-300 text-amber-700 dark:border-amber-600 dark:text-amber-400"
                                      >KEY</Badge>
                                    </td>
                                    <td class="py-2 px-3">
                                      <code class="text-xs text-muted-foreground">{{ field.type }}<template v-if="field.maxLength">({{ field.maxLength }})</template><template v-else-if="field.precision">({{ field.precision }}<template v-if="field.scale">,{{ field.scale }}</template>)</template></code>
                                    </td>
                                    <td class="py-2 px-3">
                                      <Badge v-if="field.isRequired" class="text-[10px] bg-blue-600 hover:bg-blue-700 text-white">{{ t('admin.apiDocs.yes') }}</Badge>
                                      <span v-else class="text-muted-foreground">-</span>
                                    </td>
                                    <td class="py-2 px-3">
                                      <div class="flex flex-wrap gap-1">
                                        <Badge v-if="field.isReadOnly" variant="outline" class="text-[10px]">{{ t('admin.apiDocs.readOnly') }}</Badge>
                                        <Badge v-if="field.isComputed" variant="outline" class="text-[10px]">{{ t('admin.apiDocs.computed') }}</Badge>
                                        <span v-if="!field.isReadOnly && !field.isComputed" class="text-muted-foreground">-</span>
                                      </div>
                                    </td>
                                  </tr>
                                </tbody>
                              </table>
                            </div>
                          </div>
                        </div>
                      </td>
                    </tr>
                  </template>
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>

        <!-- Actions Section -->
        <Card v-if="allActions.length > 0">
          <CardHeader>
            <CardTitle class="flex items-center gap-2">
              <Zap class="h-5 w-5 text-emerald-500" />
              {{ t('admin.apiDocs.actions') }}
            </CardTitle>
            <CardDescription>
              {{ t('admin.apiDocs.actionsSubtitle', { count: allActions.length }) }}
            </CardDescription>
          </CardHeader>
          <CardContent>
            <div class="overflow-x-auto">
              <table class="w-full text-sm">
                <thead>
                  <tr class="border-b">
                    <th class="py-3 pr-4 text-left font-medium text-muted-foreground">{{ t('common.name') }}</th>
                    <th class="py-3 pr-4 text-left font-medium text-muted-foreground">{{ t('admin.apiDocs.parameters') }}</th>
                    <th class="py-3 pr-4 text-left font-medium text-muted-foreground">{{ t('admin.apiDocs.returnType') }}</th>
                    <th class="py-3 text-left font-medium text-muted-foreground">{{ t('admin.apiDocs.binding') }}</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="action in allActions" :key="action.name + action.serviceName" class="border-b hover:bg-muted/50 transition-colors">
                    <td class="py-3 pr-4 font-mono text-xs font-medium">{{ action.name }}</td>
                    <td class="py-3 pr-4">
                      <span v-if="action.parameters.length === 0" class="text-muted-foreground">-</span>
                      <div v-else class="flex flex-wrap gap-1">
                        <Badge v-for="p in action.parameters" :key="p.name" variant="outline" class="text-xs font-mono">
                          {{ p.name }}: {{ p.type }}
                        </Badge>
                      </div>
                    </td>
                    <td class="py-3 pr-4">
                      <code v-if="action.returnType" class="rounded bg-muted px-1.5 py-0.5 text-xs font-mono">{{ action.returnType }}</code>
                      <span v-else class="text-muted-foreground text-xs">void</span>
                    </td>
                    <td class="py-3">
                      <Badge
                        :class="action.isBound
                          ? 'bg-emerald-600 hover:bg-emerald-700 text-white'
                          : 'bg-muted text-muted-foreground'"
                        class="text-xs"
                      >
                        {{ action.isBound ? t('admin.apiDocs.bound') : t('admin.apiDocs.unbound') }}
                      </Badge>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>

        <!-- Functions Section -->
        <Card v-if="allFunctions.length > 0">
          <CardHeader>
            <CardTitle class="flex items-center gap-2">
              <FunctionSquare class="h-5 w-5 text-violet-500" />
              {{ t('admin.apiDocs.functions') }}
            </CardTitle>
            <CardDescription>
              {{ t('admin.apiDocs.functionsSubtitle', { count: allFunctions.length }) }}
            </CardDescription>
          </CardHeader>
          <CardContent>
            <div class="overflow-x-auto">
              <table class="w-full text-sm">
                <thead>
                  <tr class="border-b">
                    <th class="py-3 pr-4 text-left font-medium text-muted-foreground">{{ t('common.name') }}</th>
                    <th class="py-3 pr-4 text-left font-medium text-muted-foreground">{{ t('admin.apiDocs.parameters') }}</th>
                    <th class="py-3 pr-4 text-left font-medium text-muted-foreground">{{ t('admin.apiDocs.returnType') }}</th>
                    <th class="py-3 text-left font-medium text-muted-foreground">{{ t('admin.apiDocs.binding') }}</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="fn in allFunctions" :key="fn.name + fn.serviceName" class="border-b hover:bg-muted/50 transition-colors">
                    <td class="py-3 pr-4 font-mono text-xs font-medium">{{ fn.name }}</td>
                    <td class="py-3 pr-4">
                      <span v-if="fn.parameters.length === 0" class="text-muted-foreground">-</span>
                      <div v-else class="flex flex-wrap gap-1">
                        <Badge v-for="p in fn.parameters" :key="p.name" variant="outline" class="text-xs font-mono">
                          {{ p.name }}: {{ p.type }}
                        </Badge>
                      </div>
                    </td>
                    <td class="py-3 pr-4">
                      <code class="rounded bg-muted px-1.5 py-0.5 text-xs font-mono">{{ fn.returnType }}</code>
                    </td>
                    <td class="py-3">
                      <Badge
                        :class="fn.isBound
                          ? 'bg-violet-600 hover:bg-violet-700 text-white'
                          : 'bg-muted text-muted-foreground'"
                        class="text-xs"
                      >
                        {{ fn.isBound ? t('admin.apiDocs.bound') : t('admin.apiDocs.unbound') }}
                      </Badge>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>

        <!-- Try It Panel -->
        <Card>
          <CardHeader>
            <CardTitle class="flex items-center gap-2">
              <Play class="h-5 w-5 text-primary" />
              {{ t('admin.apiDocs.tryIt.title') }}
            </CardTitle>
            <CardDescription>{{ t('admin.apiDocs.tryIt.subtitle') }}</CardDescription>
          </CardHeader>
          <CardContent class="space-y-5">
            <!-- Entity Set + Operation -->
            <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div class="space-y-2">
                <Label class="text-sm font-medium">{{ t('admin.apiDocs.tryIt.entitySet') }}</Label>
                <Select v-model="tryItEntitySet" :placeholder="t('admin.apiDocs.tryIt.selectEntitySet')">
                  <option value="" disabled>{{ t('admin.apiDocs.tryIt.selectEntitySet') }}</option>
                  <option v-for="es in allEntitySets" :key="es.name" :value="es.entityType">
                    {{ es.name }} ({{ es.entityType }})
                  </option>
                </Select>
              </div>
              <div class="space-y-2">
                <Label class="text-sm font-medium">{{ t('admin.apiDocs.tryIt.operation') }}</Label>
                <Select v-model="tryItOperation">
                  <option value="GET_LIST">GET {{ t('admin.apiDocs.tryIt.list') }}</option>
                  <option value="GET_BY_ID">GET {{ t('admin.apiDocs.tryIt.byId') }}</option>
                  <option value="POST">POST {{ t('admin.apiDocs.tryIt.create') }}</option>
                </Select>
              </div>
            </div>

            <!-- ID field for GET by ID -->
            <div v-if="tryItOperation === 'GET_BY_ID'" class="space-y-2">
              <Label class="text-sm font-medium">{{ t('admin.apiDocs.tryIt.entityId') }}</Label>
              <Input v-model="tryItId" :placeholder="t('admin.apiDocs.tryIt.entityIdPlaceholder')" />
            </div>

            <!-- Query options for GET operations -->
            <div v-if="tryItOperation !== 'POST'">
              <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider mb-3">Query Options</p>
              <div class="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
                <div class="space-y-1.5">
                  <Label class="text-xs font-medium">$filter</Label>
                  <Input v-model="tryItFilter" placeholder="Name eq 'Acme'" class="text-xs" />
                </div>
                <div class="space-y-1.5">
                  <Label class="text-xs font-medium">$select</Label>
                  <Input v-model="tryItSelect" placeholder="Name,Email" class="text-xs" />
                </div>
                <div class="space-y-1.5">
                  <Label class="text-xs font-medium">$expand</Label>
                  <Input v-model="tryItExpand" placeholder="Orders" class="text-xs" />
                </div>
                <div class="space-y-1.5">
                  <Label class="text-xs font-medium">$top</Label>
                  <Input v-model="tryItTop" placeholder="10" type="number" class="text-xs" />
                </div>
                <div class="space-y-1.5">
                  <Label class="text-xs font-medium">$skip</Label>
                  <Input v-model="tryItSkip" placeholder="0" type="number" class="text-xs" />
                </div>
              </div>
            </div>

            <!-- Body for POST -->
            <div v-if="tryItOperation === 'POST'" class="space-y-2">
              <Label class="text-sm font-medium">{{ t('admin.apiDocs.tryIt.requestBody') }}</Label>
              <textarea
                v-model="tryItBody"
                :placeholder="t('admin.apiDocs.tryIt.requestBodyPlaceholder')"
                class="min-h-[120px] w-full rounded-md border border-input bg-background px-3 py-2 font-mono text-xs ring-offset-background placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
              />
            </div>

            <!-- Constructed URL -->
            <div v-if="constructedUrl" class="space-y-2">
              <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider">{{ t('admin.apiDocs.tryIt.constructedUrl') }}</p>
              <div class="flex items-center gap-2 rounded-md border bg-muted/30 px-3 py-2.5">
                <Badge
                  :class="tryItOperation === 'POST' ? 'bg-emerald-600 text-white' : 'bg-blue-600 text-white'"
                  class="text-[10px] font-semibold shrink-0"
                >
                  {{ tryItOperation === 'POST' ? 'POST' : 'GET' }}
                </Badge>
                <code class="flex-1 font-mono text-xs break-all">{{ constructedUrl }}</code>
                <Button variant="ghost" size="sm" class="h-7 w-7 p-0 shrink-0" @click="copyUrl">
                  <component :is="copiedUrl ? Check : Copy" class="h-3.5 w-3.5" />
                </Button>
              </div>
            </div>

            <!-- Execute button + result metadata -->
            <div class="flex items-center gap-3 pt-1">
              <Button
                :disabled="!tryItEntitySet || tryItIsLoading"
                @click="executeTryIt"
                class="min-w-[120px]"
              >
                <Spinner v-if="tryItIsLoading" size="sm" class="mr-2" />
                <Play v-else class="mr-2 h-4 w-4" />
                {{ t('common.execute') }}
              </Button>
              <div v-if="tryItDuration !== null" class="flex items-center gap-1.5 text-xs text-muted-foreground">
                <Clock class="h-3.5 w-3.5" />
                {{ tryItDuration }}ms
              </div>
              <Badge
                v-if="tryItStatusCode !== null"
                :class="statusBadgeClass(tryItStatusCode)"
                class="text-xs font-semibold"
              >
                {{ tryItStatusCode }}
              </Badge>
            </div>

            <!-- Response -->
            <div v-if="tryItResponse !== null" class="space-y-2">
              <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider">{{ t('admin.apiDocs.tryIt.response') }}</p>
              <div class="rounded-md border overflow-hidden">
                <div class="bg-muted/50 px-3 py-1.5 border-b flex items-center justify-between">
                  <span class="text-[10px] font-medium text-muted-foreground uppercase tracking-wider">JSON</span>
                  <div class="flex items-center gap-2">
                    <Badge
                      v-if="tryItStatusCode !== null"
                      :class="statusBadgeClass(tryItStatusCode)"
                      class="text-[10px]"
                    >
                      {{ tryItStatusCode }}
                    </Badge>
                    <span v-if="tryItDuration !== null" class="text-[10px] text-muted-foreground">{{ tryItDuration }}ms</span>
                  </div>
                </div>
                <pre class="max-h-[400px] overflow-auto p-4 font-mono text-xs leading-relaxed bg-background">{{ tryItResponse }}</pre>
              </div>
            </div>
          </CardContent>
        </Card>

        <!-- OData Quick Reference -->
        <Card>
          <CardHeader
            class="cursor-pointer select-none hover:bg-muted/30 transition-colors rounded-t-lg"
            @click="quickRefExpanded = !quickRefExpanded"
          >
            <CardTitle class="flex items-center gap-2">
              <component :is="quickRefExpanded ? ChevronDown : ChevronRight" class="h-5 w-5 transition-transform" />
              <FileCode class="h-5 w-5 text-muted-foreground" />
              {{ t('admin.apiDocs.reference.title') }}
            </CardTitle>
            <CardDescription>{{ t('admin.apiDocs.reference.subtitle') }}</CardDescription>
          </CardHeader>
          <transition
            enter-active-class="transition-all duration-200 ease-out"
            leave-active-class="transition-all duration-150 ease-in"
            enter-from-class="opacity-0 -translate-y-1"
            enter-to-class="opacity-100 translate-y-0"
            leave-from-class="opacity-100 translate-y-0"
            leave-to-class="opacity-0 -translate-y-1"
          >
            <CardContent v-if="quickRefExpanded">
              <div class="overflow-x-auto">
                <table class="w-full text-sm">
                  <thead>
                    <tr class="border-b">
                      <th class="py-3 pr-4 text-left font-medium text-muted-foreground">{{ t('admin.apiDocs.reference.option') }}</th>
                      <th class="py-3 pr-4 text-left font-medium text-muted-foreground">{{ t('common.description') }}</th>
                      <th class="py-3 text-left font-medium text-muted-foreground">{{ t('admin.apiDocs.reference.example') }}</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="opt in queryOptions" :key="opt.name" class="border-b last:border-b-0 hover:bg-muted/50 transition-colors">
                      <td class="py-3 pr-4 font-mono text-xs font-semibold text-primary">{{ opt.name }}</td>
                      <td class="py-3 pr-4 text-muted-foreground text-sm">{{ t(opt.description) }}</td>
                      <td class="py-3">
                        <code class="rounded-md bg-muted px-2 py-1 text-xs font-mono">{{ opt.example }}</code>
                      </td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </CardContent>
          </transition>
        </Card>
      </template>

      <!-- Empty state: no module selected -->
      <Card v-if="!selectedModuleName && modules.length > 0 && !isLoadingModules" class="border-dashed">
        <CardContent class="flex flex-col items-center justify-center py-16">
          <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
            <Book class="h-8 w-8 text-muted-foreground" />
          </div>
          <h3 class="text-lg font-semibold mb-1">{{ t('admin.apiDocs.selectModule') }}</h3>
          <p class="text-muted-foreground text-sm text-center max-w-sm">{{ t('admin.apiDocs.selectModuleHint') }}</p>
        </CardContent>
      </Card>

      <!-- Loading state -->
      <div v-if="isLoadingModules && modules.length === 0" class="flex flex-col items-center justify-center py-16">
        <Spinner size="lg" />
        <p class="text-muted-foreground mt-3 text-sm">{{ t('common.loading') }}</p>
      </div>
    </div>
  </DefaultLayout>
</template>
