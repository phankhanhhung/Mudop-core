<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { metadataService } from '@/services/metadataService'
import type { ModuleMetadata } from '@/types/metadata'
import { Card, CardContent } from '@/components/ui/card'
import { Select } from '@/components/ui/select'
import { Spinner } from '@/components/ui/spinner'
import { Badge } from '@/components/ui/badge'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import MetadataTree from '@/components/admin/MetadataTree.vue'
import MetadataDetailPanel from '@/components/admin/MetadataDetailPanel.vue'
import {
  Database,
  RefreshCw,
  Server,
  Zap,
  AlertCircle,
  FolderTree,
  FileText,
  X
} from 'lucide-vue-next'

interface SelectedNode {
  type: 'module' | 'service' | 'entity' | 'action' | 'function'
  data: any
  moduleName: string
  serviceName?: string
}

const { t } = useI18n()

const modules = ref<ModuleMetadata[]>([])
const selectedModuleName = ref<string>('')
const isLoadingModules = ref(false)
const error = ref<string | null>(null)
const selectedNode = ref<SelectedNode | null>(null)

const selectedModules = computed<ModuleMetadata[]>(() => {
  if (!selectedModuleName.value) return []
  const mod = modules.value.find(m => m.name === selectedModuleName.value)
  return mod ? [mod] : []
})

const stats = computed(() => {
  if (selectedModules.value.length === 0) {
    return { services: 0, entities: 0, actionsAndFunctions: 0 }
  }
  const mod = selectedModules.value[0]
  const services = mod.services.length
  const entities = mod.services.reduce((sum, s) => sum + s.entities.length, 0)
  const actionsAndFunctions = mod.services.reduce(
    (sum, s) => sum + s.actions.length + s.functions.length,
    0
  )
  return { services, entities, actionsAndFunctions }
})

async function fetchModules() {
  isLoadingModules.value = true
  error.value = null
  try {
    modules.value = await metadataService.getModules()
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('admin.metadataBrowser.failedToLoad')
  } finally {
    isLoadingModules.value = false
  }
}

watch(selectedModuleName, () => {
  selectedNode.value = null
})

function onNodeSelect(node: SelectedNode) {
  selectedNode.value = node
}

onMounted(() => {
  fetchModules()
})
</script>

<template>
  <DefaultLayout>
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-3xl font-bold tracking-tight">{{ $t('admin.metadataBrowser.title') }}</h1>
          <p class="text-muted-foreground mt-1">
            {{ $t('admin.metadataBrowser.subtitle') }}
          </p>
        </div>
        <Button variant="outline" size="sm" @click="fetchModules" :disabled="isLoadingModules">
          <Spinner v-if="isLoadingModules" size="sm" class="mr-2" />
          <RefreshCw v-else class="mr-2 h-4 w-4" />
          {{ $t('common.refresh') }}
        </Button>
      </div>

      <!-- Error -->
      <Alert v-if="error" variant="destructive">
        <AlertCircle class="h-4 w-4" />
        <AlertDescription>{{ error }}</AlertDescription>
      </Alert>

      <!-- Module selector card -->
      <Card class="transition-all hover:shadow-md">
        <CardContent class="p-4">
          <div class="flex items-center gap-4">
            <div class="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center shrink-0">
              <Database class="h-5 w-5 text-primary" />
            </div>
            <div class="flex-1 min-w-0">
              <label class="text-sm font-medium text-muted-foreground">Module</label>
              <Select
                v-model="selectedModuleName"
                :placeholder="$t('admin.metadataBrowser.modulePlaceholder')"
                class="mt-1"
                :disabled="isLoadingModules"
              >
                <option v-for="mod in modules" :key="mod.name" :value="mod.name">
                  {{ mod.name }} (v{{ mod.version }})
                </option>
              </Select>
            </div>
            <Badge v-if="selectedModuleName && selectedModules.length > 0" variant="secondary" class="text-xs shrink-0">
              v{{ selectedModules[0].version }}
            </Badge>
          </div>
        </CardContent>
      </Card>

      <!-- Loading state -->
      <div v-if="isLoadingModules" class="flex flex-col items-center justify-center py-16">
        <Spinner size="lg" />
        <p class="text-muted-foreground mt-3 text-sm">Loading modules...</p>
      </div>

      <!-- Empty state: no modules -->
      <Card v-else-if="modules.length === 0 && !error" class="border-dashed">
        <CardContent class="flex flex-col items-center justify-center py-16">
          <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
            <Database class="h-8 w-8 text-muted-foreground" />
          </div>
          <h3 class="text-lg font-semibold mb-1">{{ $t('admin.metadataBrowser.noModules') }}</h3>
          <p class="text-muted-foreground text-sm text-center max-w-sm">
            {{ $t('admin.metadataBrowser.noModulesDescription') }}
          </p>
        </CardContent>
      </Card>

      <!-- Empty state: no module selected -->
      <Card v-else-if="!selectedModuleName" class="border-dashed">
        <CardContent class="flex flex-col items-center justify-center py-16">
          <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
            <Database class="h-8 w-8 text-muted-foreground" />
          </div>
          <h3 class="text-lg font-semibold mb-1">{{ $t('admin.metadataBrowser.selectModuleHint') }}</h3>
          <p class="text-muted-foreground text-sm text-center max-w-sm">
            {{ $t('admin.metadataBrowser.selectModuleDescription') }}
          </p>
        </CardContent>
      </Card>

      <!-- Module selected: stats + two-panel layout -->
      <template v-else>
        <!-- Stats Cards -->
        <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <Card class="transition-all hover:shadow-md">
            <CardContent class="p-4">
              <div class="flex items-center justify-between">
                <div>
                  <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.metadataBrowser.stats.services') }}</p>
                  <p class="text-2xl font-bold mt-1">{{ stats.services }}</p>
                </div>
                <div class="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center">
                  <Server class="h-5 w-5 text-primary" />
                </div>
              </div>
            </CardContent>
          </Card>

          <Card class="transition-all hover:shadow-md">
            <CardContent class="p-4">
              <div class="flex items-center justify-between">
                <div>
                  <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.metadataBrowser.stats.entities') }}</p>
                  <p class="text-2xl font-bold mt-1 text-emerald-600">{{ stats.entities }}</p>
                </div>
                <div class="h-10 w-10 rounded-full bg-emerald-500/10 flex items-center justify-center">
                  <Database class="h-5 w-5 text-emerald-500" />
                </div>
              </div>
            </CardContent>
          </Card>

          <Card class="transition-all hover:shadow-md">
            <CardContent class="p-4">
              <div class="flex items-center justify-between">
                <div>
                  <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.metadataBrowser.stats.actionsAndFunctions') }}</p>
                  <p class="text-2xl font-bold mt-1 text-violet-600">{{ stats.actionsAndFunctions }}</p>
                </div>
                <div class="h-10 w-10 rounded-full bg-violet-500/10 flex items-center justify-center">
                  <Zap class="h-5 w-5 text-violet-500" />
                </div>
              </div>
            </CardContent>
          </Card>
        </div>

        <!-- Two-panel layout -->
        <div class="flex gap-6">
          <!-- Left panel: Tree -->
          <div class="w-80 shrink-0">
            <Card class="overflow-hidden">
              <!-- Panel header -->
              <div class="flex items-center gap-3 px-4 py-3 border-b">
                <div class="h-8 w-8 rounded-lg bg-primary/10 flex items-center justify-center">
                  <FolderTree class="h-4 w-4 text-primary" />
                </div>
                <div>
                  <h3 class="font-semibold text-sm">{{ $t('admin.metadataBrowser.schemaTree') }}</h3>
                  <p class="text-xs text-muted-foreground">{{ $t('admin.metadataBrowser.schemaTreeHint') }}</p>
                </div>
              </div>
              <CardContent class="p-2 overflow-y-auto" style="max-height: calc(100vh - 420px); min-height: 400px;">
                <MetadataTree
                  :modules="selectedModules"
                  @select="onNodeSelect"
                />
              </CardContent>
            </Card>
          </div>

          <!-- Right panel: Detail -->
          <Card class="flex-1 min-w-0 self-start sticky top-20">
            <CardContent class="p-0">
              <!-- Panel header -->
              <div class="flex items-center justify-between px-5 py-4 border-b">
                <div class="flex items-center gap-3">
                  <div class="h-9 w-9 rounded-lg bg-primary/10 flex items-center justify-center">
                    <FileText class="h-4 w-4 text-primary" />
                  </div>
                  <div>
                    <h3 class="font-semibold text-sm">
                      {{ selectedNode
                        ? `${selectedNode.type.charAt(0).toUpperCase() + selectedNode.type.slice(1)}: ${selectedNode.data?.name || selectedNode.data?.Name || ''}`
                        : $t('admin.metadataBrowser.detailPanel')
                      }}
                    </h3>
                    <p class="text-xs text-muted-foreground">
                      {{ selectedNode
                        ? selectedNode.moduleName + (selectedNode.serviceName ? ` / ${selectedNode.serviceName}` : '')
                        : $t('admin.metadataBrowser.detailPanelHint')
                      }}
                    </p>
                  </div>
                </div>
                <Button
                  v-if="selectedNode"
                  variant="ghost"
                  size="sm"
                  class="h-8 w-8 p-0"
                  @click="selectedNode = null"
                >
                  <X class="h-4 w-4" />
                </Button>
              </div>

              <!-- Empty state -->
              <div v-if="!selectedNode" class="flex flex-col items-center justify-center py-20 text-muted-foreground">
                <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
                  <FileText class="h-8 w-8 text-muted-foreground/50" />
                </div>
                <h3 class="font-medium mb-1">{{ $t('admin.metadataBrowser.selectNodeToView') }}</h3>
                <p class="text-sm text-center max-w-xs">{{ $t('admin.metadataBrowser.selectNodeDescription') }}</p>
              </div>

              <!-- Detail content -->
              <div v-else class="p-5">
                <MetadataDetailPanel :selected-node="selectedNode" />
              </div>
            </CardContent>
          </Card>
        </div>
      </template>
    </div>
  </DefaultLayout>
</template>
