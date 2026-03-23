<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { metadataService } from '@/services/metadataService'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Select } from '@/components/ui/select'
import { Label } from '@/components/ui/label'
import { Spinner } from '@/components/ui/spinner'
import { Alert, AlertDescription } from '@/components/ui/alert'
import {
  Zap,
  Play,
  Package,
  RefreshCw,
  SquareFunction,
  Layers,
  AlertCircle,
  Server,
  ArrowRight
} from 'lucide-vue-next'
import ActionDialog from '@/components/entity/ActionDialog.vue'
import type { ModuleMetadata, ServiceMetadata, ActionMetadata } from '@/types/metadata'

const { t } = useI18n()

// Data
const modules = ref<ModuleMetadata[]>([])
const isLoadingModules = ref(false)
const loadError = ref<string | null>(null)

// Selection
const selectedModuleName = ref<string | number>('')
const selectedServiceName = ref<string | number>('')

// Dialog
const dialogOpen = ref(false)
const dialogAction = ref<ActionMetadata | null>(null)
const dialogOperationType = ref<'action' | 'function'>('action')

const selectedModule = computed<ModuleMetadata | null>(() =>
  modules.value.find((m) => m.name === selectedModuleName.value) ?? null
)

const services = computed<ServiceMetadata[]>(() =>
  selectedModule.value?.services ?? []
)

const selectedService = computed<ServiceMetadata | null>(() =>
  services.value.find((s) => s.name === selectedServiceName.value) ?? null
)

const unboundActions = computed<ActionMetadata[]>(() =>
  selectedService.value?.actions.filter((a) => !a.isBound) ?? []
)

const unboundFunctions = computed<ActionMetadata[]>(() =>
  selectedService.value?.functions.filter((f) => !f.isBound) ?? []
)

// Stats
const stats = computed(() => {
  const actions = unboundActions.value.length
  const functions = unboundFunctions.value.length
  const total = actions + functions
  return { actions, functions, total }
})

// Reset service selection when module changes
watch(selectedModuleName, () => {
  selectedServiceName.value = ''
})

onMounted(async () => {
  await loadModules()
})

async function loadModules() {
  isLoadingModules.value = true
  loadError.value = null
  try {
    modules.value = await metadataService.getModules()
  } catch (e) {
    loadError.value = e instanceof Error ? e.message : t('admin.actions.failedToLoad')
  } finally {
    isLoadingModules.value = false
  }
}

function openActionDialog(action: ActionMetadata, type: 'action' | 'function' = 'action') {
  dialogAction.value = action
  dialogOperationType.value = type
  dialogOpen.value = true
}

function handleDialogClose() {
  dialogOpen.value = false
  dialogAction.value = null
}
</script>

<template>
  <DefaultLayout>
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-3xl font-bold tracking-tight">{{ $t('admin.actions.title') }}</h1>
          <p class="text-muted-foreground mt-1">
            {{ $t('admin.actions.subtitle') }}
          </p>
        </div>
        <Button variant="outline" size="sm" @click="loadModules" :disabled="isLoadingModules">
          <Spinner v-if="isLoadingModules" size="sm" class="mr-2" />
          <RefreshCw v-else class="mr-2 h-4 w-4" />
          {{ $t('common.refresh') }}
        </Button>
      </div>

      <!-- Error -->
      <Alert v-if="loadError" variant="destructive">
        <AlertCircle class="h-4 w-4" />
        <AlertDescription>{{ loadError }}</AlertDescription>
      </Alert>

      <!-- Module & Service Selectors -->
      <Card>
        <CardHeader>
          <div class="flex items-center gap-2">
            <div class="h-8 w-8 rounded-lg bg-primary/10 flex items-center justify-center">
              <Package class="h-4 w-4 text-primary" />
            </div>
            <div>
              <CardTitle>{{ $t('admin.actions.selectService') }}</CardTitle>
              <CardDescription>
                {{ $t('admin.actions.selectServiceSubtitle') }}
              </CardDescription>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <div v-if="isLoadingModules && modules.length === 0" class="flex flex-col items-center justify-center py-8">
            <Spinner size="lg" />
            <p class="text-muted-foreground mt-3 text-sm">{{ $t('admin.actions.loadingModules') || 'Loading modules...' }}</p>
          </div>

          <div v-else-if="loadError && modules.length === 0" class="text-center py-8">
            <p class="text-sm text-destructive">{{ loadError }}</p>
            <Button class="mt-4" variant="outline" @click="loadModules">{{ $t('common.retry') }}</Button>
          </div>

          <div v-else class="grid gap-4 md:grid-cols-2">
            <div class="space-y-2">
              <Label class="flex items-center gap-2">
                <Package class="h-3.5 w-3.5 text-muted-foreground" />
                {{ $t('common.module') }}
              </Label>
              <Select
                v-model="selectedModuleName"
                placeholder="Select a module..."
              >
                <option v-for="mod in modules" :key="mod.name" :value="mod.name">
                  {{ mod.name }} (v{{ mod.version }})
                </option>
              </Select>
            </div>

            <div class="space-y-2">
              <Label class="flex items-center gap-2">
                <Server class="h-3.5 w-3.5 text-muted-foreground" />
                {{ $t('common.service') }}
              </Label>
              <Select
                v-model="selectedServiceName"
                placeholder="Select a service..."
                :disabled="!selectedModule"
              >
                <option v-for="svc in services" :key="svc.name" :value="svc.name">
                  {{ svc.name }}
                </option>
              </Select>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Stats Cards (shown when a service is selected) -->
      <div v-if="selectedService" class="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.actions.actionsTitle') }}</p>
                <p class="text-2xl font-bold mt-1 text-primary">{{ stats.actions }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center">
                <Zap class="h-5 w-5 text-primary" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.actions.functionsTitle') }}</p>
                <p class="text-2xl font-bold mt-1 text-emerald-600">{{ stats.functions }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-emerald-500/10 flex items-center justify-center">
                <SquareFunction class="h-5 w-5 text-emerald-500" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.actions.totalOperations') || 'Total Operations' }}</p>
                <p class="text-2xl font-bold mt-1 text-violet-600">{{ stats.total }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-violet-500/10 flex items-center justify-center">
                <Layers class="h-5 w-5 text-violet-500" />
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      <!-- No service selected (but module is selected) -->
      <Card
        v-if="!selectedService && selectedModule"
        class="border-dashed"
      >
        <CardContent class="flex flex-col items-center justify-center py-16">
          <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
            <Server class="h-8 w-8 text-muted-foreground" />
          </div>
          <h3 class="text-lg font-semibold mb-1">{{ $t('admin.actions.selectServiceHint') }}</h3>
          <p class="text-muted-foreground text-sm text-center max-w-sm">
            {{ $t('admin.actions.selectServiceDescription') || 'Choose a service from the selector above to view its available actions and functions.' }}
          </p>
        </CardContent>
      </Card>

      <!-- No module selected -->
      <Card
        v-if="!selectedModule && !isLoadingModules && !loadError"
        class="border-dashed"
      >
        <CardContent class="flex flex-col items-center justify-center py-16">
          <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
            <Package class="h-8 w-8 text-muted-foreground" />
          </div>
          <h3 class="text-lg font-semibold mb-1">{{ $t('admin.actions.selectModuleHint') || 'Select a module' }}</h3>
          <p class="text-muted-foreground text-sm text-center max-w-sm">
            {{ $t('admin.actions.selectModuleDescription') || 'Start by selecting a module above, then choose a service to explore its operations.' }}
          </p>
        </CardContent>
      </Card>

      <!-- Actions List -->
      <Card v-if="selectedService && unboundActions.length > 0">
        <CardHeader>
          <div class="flex items-center gap-2">
            <div class="h-8 w-8 rounded-lg bg-primary/10 flex items-center justify-center">
              <Zap class="h-4 w-4 text-primary" />
            </div>
            <div>
              <CardTitle>{{ $t('admin.actions.actionsTitle') }}</CardTitle>
              <CardDescription>
                {{ $t('admin.actions.actionsSubtitle', { name: selectedService.name }) }}
              </CardDescription>
            </div>
          </div>
        </CardHeader>
        <CardContent class="p-0">
          <div class="divide-y">
            <div
              v-for="action in unboundActions"
              :key="action.name"
              class="flex items-center gap-4 px-5 py-4 hover:bg-muted/50 transition-colors group"
            >
              <!-- Action icon -->
              <div class="h-10 w-10 rounded-lg bg-amber-500/10 flex items-center justify-center shrink-0">
                <Zap class="h-5 w-5 text-amber-500" />
              </div>

              <!-- Action info -->
              <div class="flex-1 min-w-0">
                <div class="flex items-center gap-2">
                  <span class="font-medium">{{ action.name }}</span>
                  <Badge variant="secondary" class="text-[10px] px-1.5 py-0">
                    ACTION
                  </Badge>
                </div>

                <!-- Parameters -->
                <div class="flex items-center gap-2 mt-1.5">
                  <div v-if="action.parameters.length > 0" class="flex flex-wrap gap-1">
                    <Badge
                      v-for="param in action.parameters"
                      :key="param.name"
                      variant="outline"
                      class="text-xs font-normal"
                    >
                      {{ param.name }}: {{ param.type }}
                      <span v-if="param.isRequired" class="text-destructive ml-0.5">*</span>
                    </Badge>
                  </div>
                  <span v-else class="text-muted-foreground text-xs">{{ $t('admin.actions.noParameters') || 'No parameters' }}</span>
                </div>
              </div>

              <!-- Return type -->
              <div class="hidden sm:flex items-center gap-2 shrink-0">
                <ArrowRight class="h-3.5 w-3.5 text-muted-foreground" />
                <Badge v-if="action.returnType" variant="secondary" class="font-mono text-xs">
                  {{ action.returnType }}
                </Badge>
                <span v-else class="text-muted-foreground text-sm italic">void</span>
              </div>

              <!-- Execute button -->
              <Button
                size="sm"
                class="shrink-0 opacity-80 group-hover:opacity-100 transition-opacity"
                @click="openActionDialog(action)"
              >
                <Play class="mr-1.5 h-3.5 w-3.5" />
                {{ $t('common.execute') }}
              </Button>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Functions List -->
      <Card v-if="selectedService && unboundFunctions.length > 0">
        <CardHeader>
          <div class="flex items-center gap-2">
            <div class="h-8 w-8 rounded-lg bg-emerald-500/10 flex items-center justify-center">
              <SquareFunction class="h-4 w-4 text-emerald-500" />
            </div>
            <div>
              <CardTitle>{{ $t('admin.actions.functionsTitle') }}</CardTitle>
              <CardDescription>
                {{ $t('admin.actions.functionsSubtitle', { name: selectedService.name }) }}
              </CardDescription>
            </div>
          </div>
        </CardHeader>
        <CardContent class="p-0">
          <div class="divide-y">
            <div
              v-for="fn in unboundFunctions"
              :key="fn.name"
              class="flex items-center gap-4 px-5 py-4 hover:bg-muted/50 transition-colors group"
            >
              <!-- Function icon -->
              <div class="h-10 w-10 rounded-lg bg-emerald-500/10 flex items-center justify-center shrink-0">
                <SquareFunction class="h-5 w-5 text-emerald-500" />
              </div>

              <!-- Function info -->
              <div class="flex-1 min-w-0">
                <div class="flex items-center gap-2">
                  <span class="font-medium">{{ fn.name }}</span>
                  <Badge variant="secondary" class="text-[10px] px-1.5 py-0 bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400">
                    FUNCTION
                  </Badge>
                </div>

                <!-- Parameters -->
                <div class="flex items-center gap-2 mt-1.5">
                  <div v-if="fn.parameters.length > 0" class="flex flex-wrap gap-1">
                    <Badge
                      v-for="param in fn.parameters"
                      :key="param.name"
                      variant="outline"
                      class="text-xs font-normal"
                    >
                      {{ param.name }}: {{ param.type }}
                      <span v-if="param.isRequired" class="text-destructive ml-0.5">*</span>
                    </Badge>
                  </div>
                  <span v-else class="text-muted-foreground text-xs">{{ $t('admin.actions.noParameters') || 'No parameters' }}</span>
                </div>
              </div>

              <!-- Return type -->
              <div class="hidden sm:flex items-center gap-2 shrink-0">
                <ArrowRight class="h-3.5 w-3.5 text-muted-foreground" />
                <Badge variant="secondary" class="font-mono text-xs">
                  {{ fn.returnType }}
                </Badge>
              </div>

              <!-- Execute button -->
              <Button
                size="sm"
                variant="outline"
                class="shrink-0 opacity-80 group-hover:opacity-100 transition-opacity"
                @click="openActionDialog(fn, 'function')"
              >
                <Play class="mr-1.5 h-3.5 w-3.5" />
                {{ $t('common.execute') }}
              </Button>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Empty state: service selected but no actions/functions -->
      <Card
        v-if="selectedService && unboundActions.length === 0 && unboundFunctions.length === 0"
        class="border-dashed"
      >
        <CardContent class="flex flex-col items-center justify-center py-16">
          <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
            <Zap class="h-8 w-8 text-muted-foreground" />
          </div>
          <h3 class="text-lg font-semibold mb-1">{{ $t('admin.actions.noActionsTitle') || 'No operations found' }}</h3>
          <p class="text-muted-foreground text-sm text-center max-w-sm">
            {{ $t('admin.actions.noActions', { name: selectedService.name }) }}
          </p>
        </CardContent>
      </Card>
    </div>

    <!-- Action execution dialog -->
    <ActionDialog
      :open="dialogOpen"
      :action="dialogAction"
      :module="String(selectedModuleName)"
      :serviceName="String(selectedServiceName)"
      entitySet=""
      mode="unbound"
      :operationType="dialogOperationType"
      @close="handleDialogClose"
      @executed="() => {}"
    />
  </DefaultLayout>
</template>
