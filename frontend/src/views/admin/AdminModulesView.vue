<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { adminService, type CompileResponse, type ModuleStatus } from '@/services/adminService'
import { useMetadataStore } from '@/stores/metadata'
import { useConfirmDialog } from '@/composables/useConfirmDialog'
import { useAutoValidation } from '@/composables/useAutoValidation'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Spinner } from '@/components/ui/spinner'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ConfirmDialog } from '@/components/common'
import BmmdlCodeEditor from '@/components/admin/BmmdlCodeEditor.vue'
import type { EditorMarker } from '@/components/admin/BmmdlCodeEditor.vue'
import AiAssistPanel from '@/components/admin/AiAssistPanel.vue'
import CompileOutputPanel from '@/components/admin/CompileOutputPanel.vue'
import ModuleDependencyGraph from '@/components/admin/ModuleDependencyGraph.vue'
import SchemaDiffViewer from '@/components/admin/SchemaDiffViewer.vue'
import CompilationTimeline from '@/components/admin/CompilationTimeline.vue'
import ProcessFlow from '@/components/smart/ProcessFlow.vue'
import type { ProcessNode } from '@/composables/useProcessFlow'
import type { CompilationEntry } from '@/components/admin/CompilationTimeline.vue'
import ModuleTemplateGallery from '@/components/admin/ModuleTemplateGallery.vue'
import {
  Upload,
  Play,
  CheckCircle,
  XCircle,
  AlertCircle,
  Trash2,
  Heart,
  Package,
  Database,
  RefreshCw,
  Server,
  Layers,
  Eye,
  ChevronRight,
  X,
  Calendar,
  Hash,
  GitBranch,
  FileCode,
  History,
  LayoutTemplate,
  BrainCircuit,
} from 'lucide-vue-next'
import { formatDate } from '@/utils/formatting'

const { t } = useI18n()
const metadataStore = useMetadataStore()
const confirmDialog = useConfirmDialog()
const autoValidation = useAutoValidation({ debounceMs: 2000 })

const isLoading = ref(false)
const error = ref<string | null>(null)
const success = ref<string | null>(null)

// Published modules
const modules = ref<ModuleStatus[]>([])
const isLoadingModules = ref(false)

// Compile form
const moduleName = ref('')
const bmmdlSource = ref('')
const publishModule = ref(true)
const initSchema = ref(false)
const isCompiling = ref(false)
const compileResult = ref<CompileResponse | null>(null)

// Health check
const healthStatus = ref<{ status: string; endpoints: string[] } | null>(null)

// Detail panel
const selectedModule = ref<ModuleStatus | null>(null)

// Code editor
const codeEditorRef = ref<InstanceType<typeof BmmdlCodeEditor> | null>(null)

// DDL Preview
const isPreviewingDdl = ref(false)
const ddlPreview = ref('')
const showDependencyGraph = ref(false)

// Compilation history
const compilationHistory = ref<CompilationEntry[]>([])
const showTimeline = ref(false)

// Template gallery
const showTemplates = ref(false)

// AI Assist Panel
const showAiPanel = ref(false)

// Compilation pipeline visualization
const showPipeline = ref(false)

const COMPILATION_PASSES = [
  'LexicalPass', 'SyntacticPass', 'ModelBuildPass', 'SymbolResolutionPass',
  'DependencyGraphPass', 'ExpressionDependencyPass', 'BindingPass', 'TenantIsolationPass',
  'FileStorageValidationPass', 'TemporalValidationPass', 'SemanticValidationPass', 'OptimizationPass',
]

const pipelineNodes = computed<ProcessNode[]>(() => {
  if (!compileResult.value) return []
  const success = compileResult.value.success
  return COMPILATION_PASSES.map((pass, idx) => ({
    id: `pass-${idx}`,
    title: pass.replace(/Pass$/, ''),
    subtitle: `Pass ${idx + 1}`,
    status: success ? 'positive' as const : 'neutral' as const,
  }))
})

// Auto-validation: merge markers (compile results take priority over auto-validation)
const mergedMarkers = computed<EditorMarker[]>(() => {
  if (compileResult.value) return editorMarkers.value
  return autoValidation.markers.value
})

// Trigger auto-validation on source/moduleName changes
watch([bmmdlSource, moduleName], ([src, name]) => {
  if (src && name) {
    autoValidation.validate(src, name)
  }
})

// Stats
const stats = computed(() => {
  const totalModules = modules.value.length
  const totalEntities = modules.value.reduce((sum, m) => sum + m.entityCount, 0)
  const totalServices = modules.value.reduce((sum, m) => sum + m.serviceCount, 0)
  const schemasInitialized = modules.value.filter(m => m.schemaInitialized).length
  return { totalModules, totalEntities, totalServices, schemasInitialized }
})

function getModuleColor(mod: ModuleStatus): string {
  const colors = [
    'bg-blue-500', 'bg-emerald-500', 'bg-violet-500', 'bg-amber-500',
    'bg-rose-500', 'bg-cyan-500', 'bg-indigo-500', 'bg-teal-500',
    'bg-pink-500', 'bg-orange-500'
  ]
  let hash = 0
  for (const char of mod.name) {
    hash = char.charCodeAt(0) + ((hash << 5) - hash)
  }
  return colors[Math.abs(hash) % colors.length]
}

function getModuleInitials(mod: ModuleStatus): string {
  const words = mod.name.replace(/([A-Z])/g, ' $1').trim().split(/\s+/)
  if (words.length >= 2) {
    return (words[0][0] + words[1][0]).toUpperCase()
  }
  return mod.name.substring(0, 2).toUpperCase()
}

// ── Diagnostic parsing ──────────────────────────────────────────────────────
function parseLineCol(msg: string): { line: number; column: number } | null {
  const m = msg.match(/[Ll]ine\s+(\d+)(?:,?\s*[Cc]ol(?:umn)?\s+(\d+))?/)
  if (m) return { line: parseInt(m[1]), column: m[2] ? parseInt(m[2]) : 1 }
  return null
}

function parseCode(msg: string): string | undefined {
  const m = msg.match(/\[([A-Z_]+\d*)\]/)
  return m ? m[1] : undefined
}

const editorMarkers = computed<EditorMarker[]>(() => {
  if (!compileResult.value) return []
  const markers: EditorMarker[] = []
  for (const err of compileResult.value.errors || []) {
    const loc = parseLineCol(err)
    markers.push({ line: loc?.line ?? 1, column: loc?.column ?? 1, message: err, severity: 'error' })
  }
  for (const warn of compileResult.value.warnings || []) {
    const loc = parseLineCol(warn)
    markers.push({ line: loc?.line ?? 1, column: loc?.column ?? 1, message: warn, severity: 'warning' })
  }
  return markers
})

const compileDiagnostics = computed(() => {
  if (!compileResult.value) return []
  const diags: Array<{ line?: number; column?: number; message: string; severity: 'error' | 'warning' | 'info'; code?: string }> = []
  for (const err of compileResult.value.errors || []) {
    const loc = parseLineCol(err)
    diags.push({ line: loc?.line, column: loc?.column, message: err, severity: 'error', code: parseCode(err) })
  }
  for (const warn of compileResult.value.warnings || []) {
    const loc = parseLineCol(warn)
    diags.push({ line: loc?.line, column: loc?.column, message: warn, severity: 'warning', code: parseCode(warn) })
  }
  return diags
})

const compileStats = computed(() => {
  if (!compileResult.value?.success) return undefined
  return {
    entityCount: compileResult.value.entityCount,
    serviceCount: compileResult.value.serviceCount,
    enumCount: compileResult.value.enumCount,
    compilationTime: compileResult.value.compilationTime || ''
  }
})

const compileOutputText = computed(() => {
  if (!compileResult.value) return ''
  if (compileResult.value.success) {
    return `Module "${moduleName.value}" compiled successfully.\n` +
      `Entities: ${compileResult.value.entityCount}, Services: ${compileResult.value.serviceCount}, Enums: ${compileResult.value.enumCount}\n` +
      (compileResult.value.compilationTime ? `Time: ${compileResult.value.compilationTime}\n` : '') +
      (compileResult.value.schemaResult ? `Schema: ${compileResult.value.schemaResult}` : '')
  }
  return compileResult.value.errors?.join('\n') || 'Compilation failed'
})

const compileSuccess = computed<boolean | null>(() => {
  if (!compileResult.value) return null
  return compileResult.value.success
})

function handleDiagnosticClick(payload: { line: number; column: number }) {
  codeEditorRef.value?.revealLine(payload.line)
}

function handleTemplateSelect(payload: { source: string; moduleName: string }) {
  bmmdlSource.value = payload.source
  moduleName.value = payload.moduleName
  showTemplates.value = false
  compileResult.value = null
  autoValidation.reset()
}

function handleAiInsert(code: string) {
  codeEditorRef.value?.insertText(code)
}

function addToHistory(result: CompileResponse) {
  compilationHistory.value.unshift({
    id: crypto.randomUUID(),
    moduleName: moduleName.value,
    timestamp: new Date(),
    success: result.success,
    entityCount: result.entityCount,
    serviceCount: result.serviceCount,
    enumCount: result.enumCount,
    compilationTime: result.compilationTime || '',
    errors: result.errors || [],
    warnings: result.warnings || [],
    schemaResult: result.schemaResult,
    versionInfo: result.versionInfo,
  })
}

async function previewDdl() {
  if (!bmmdlSource.value.trim() || !moduleName.value.trim()) {
    error.value = t('admin.modules.enterSource')
    return
  }

  isPreviewingDdl.value = true
  error.value = null

  try {
    const result = await adminService.previewDdl({
      bmmdlSource: bmmdlSource.value,
      moduleName: moduleName.value,
    })
    if (result.success) {
      ddlPreview.value = result.ddl
    } else {
      error.value = result.errors?.join('; ') || 'DDL preview failed'
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'DDL preview failed'
  } finally {
    isPreviewingDdl.value = false
  }
}

function viewModule(mod: ModuleStatus) {
  selectedModule.value = selectedModule.value?.id === mod.id ? null : mod
}

async function checkHealth() {
  try {
    healthStatus.value = await adminService.healthCheck()
    success.value = `Admin API is ${healthStatus.value.status}`
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('admin.modules.healthCheck')
    healthStatus.value = null
  }
}

async function compileSource() {
  if (!bmmdlSource.value.trim()) {
    error.value = t('admin.modules.enterSource')
    return
  }
  if (!moduleName.value.trim()) {
    error.value = t('admin.modules.enterModuleName')
    return
  }

  isCompiling.value = true
  error.value = null
  success.value = null
  compileResult.value = null

  try {
    const result = await adminService.compile({
      bmmdlSource: bmmdlSource.value,
      moduleName: moduleName.value,
      publish: publishModule.value,
      initSchema: initSchema.value
    })
    compileResult.value = result
    addToHistory(result)
    autoValidation.reset()

    if (result.success) {
      success.value = `Module "${moduleName.value}" compiled: ${result.entityCount} entities, ${result.schemaResult || 'no schema init'}`
      fetchModules()
      // Refresh metadata store so sidebar picks up new entities
      metadataStore.clearCache()
      metadataStore.fetchModules()
    } else {
      error.value = result.errors?.join('; ') || t('admin.modules.compilationFailed')
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('admin.modules.compilationFailed')
  } finally {
    isCompiling.value = false
  }
}

async function validateSource() {
  if (!bmmdlSource.value.trim()) {
    error.value = t('admin.modules.enterSource')
    return
  }
  if (!moduleName.value.trim()) {
    error.value = t('admin.modules.enterModuleName')
    return
  }

  isCompiling.value = true
  error.value = null
  success.value = null
  compileResult.value = null

  try {
    const result = await adminService.validate(bmmdlSource.value, moduleName.value)
    compileResult.value = result
    addToHistory(result)
    autoValidation.reset()

    if (result.success) {
      success.value = t('admin.modules.validationPassed')
    } else {
      error.value = t('admin.modules.validationFailed')
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('admin.modules.validationFailed')
  } finally {
    isCompiling.value = false
  }
}

async function clearDatabase() {
  const confirmed = await confirmDialog.confirm({
    title: t('admin.modules.clearDatabase'),
    description: t('admin.modules.clearDatabaseWarning'),
    confirmLabel: t('admin.modules.clearDatabase'),
    variant: 'destructive'
  })
  if (!confirmed) return

  isLoading.value = true
  error.value = null
  success.value = null

  try {
    const result = await adminService.clearDatabase({
      clearRegistry: true,
      dropSchemas: true
    })
    if (result.success) {
      success.value = `Database cleared! Dropped ${result.droppedSchemas.length} schemas.`
      selectedModule.value = null
      fetchModules()
    } else {
      error.value = result.errors.join(', ')
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('admin.modules.clearDatabase')
  } finally {
    isLoading.value = false
  }
}

async function fetchModules() {
  isLoadingModules.value = true
  try {
    modules.value = await adminService.listModules()
  } catch (e) {
    // Silently fail - modules section will show empty
  } finally {
    isLoadingModules.value = false
  }
}

onMounted(() => {
  checkHealth()
  fetchModules()
})

</script>

<template>
  <DefaultLayout>
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-3xl font-bold tracking-tight">{{ $t('admin.modules.title') }}</h1>
          <p class="text-muted-foreground mt-1">
            {{ $t('admin.modules.subtitle') }}
          </p>
        </div>
        <div class="flex gap-2">
          <Button variant="outline" size="sm" @click="checkHealth" :disabled="isLoading">
            <Heart class="mr-2 h-4 w-4" />
            {{ $t('admin.modules.healthCheck') }}
          </Button>
          <Button variant="destructive" size="sm" @click="clearDatabase" :disabled="isLoading">
            <Trash2 class="mr-2 h-4 w-4" />
            {{ $t('admin.modules.clearDatabase') }}
          </Button>
        </div>
      </div>

      <!-- Health status -->
      <Alert v-if="healthStatus" variant="default" class="border-blue-500 bg-blue-50 dark:bg-blue-950">
        <Heart class="h-4 w-4 text-blue-600" />
        <AlertDescription class="text-blue-700 dark:text-blue-300">
          Registry API: {{ healthStatus.status }} |
          Endpoints: {{ healthStatus.endpoints.join(', ') }}
        </AlertDescription>
      </Alert>

      <!-- Success/Error messages -->
      <div aria-live="polite" aria-atomic="true">
        <Alert v-if="success" variant="default" class="border-green-500 bg-green-50 dark:bg-green-950">
          <CheckCircle class="h-4 w-4 text-green-600" />
          <AlertDescription class="text-green-700 dark:text-green-300">{{ success }}</AlertDescription>
        </Alert>

        <Alert v-if="error" variant="destructive">
          <XCircle class="h-4 w-4" />
          <AlertDescription>{{ error }}</AlertDescription>
        </Alert>
      </div>

      <!-- Stats Cards -->
      <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.modules.stats.totalModules') }}</p>
                <p class="text-2xl font-bold mt-1">{{ stats.totalModules }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center">
                <Package class="h-5 w-5 text-primary" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.modules.stats.totalEntities') }}</p>
                <p class="text-2xl font-bold mt-1 text-emerald-600">{{ stats.totalEntities }}</p>
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
                <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.modules.stats.totalServices') }}</p>
                <p class="text-2xl font-bold mt-1 text-violet-600">{{ stats.totalServices }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-violet-500/10 flex items-center justify-center">
                <Server class="h-5 w-5 text-violet-500" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.modules.stats.schemasInitialized') }}</p>
                <p class="text-2xl font-bold mt-1 text-cyan-600">{{ stats.schemasInitialized }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-cyan-500/10 flex items-center justify-center">
                <Layers class="h-5 w-5 text-cyan-500" />
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      <!-- Published Modules -->
      <Card>
        <CardHeader>
          <div class="flex items-center justify-between">
            <div class="flex items-center gap-2">
              <Package class="h-5 w-5" />
              <CardTitle>{{ $t('admin.modules.publishedModules') }}</CardTitle>
            </div>
            <Button variant="outline" size="sm" @click="fetchModules" :disabled="isLoadingModules">
              <Spinner v-if="isLoadingModules" size="sm" class="mr-2" />
              <RefreshCw v-else class="mr-2 h-4 w-4" />
              {{ $t('common.refresh') }}
            </Button>
          </div>
          <CardDescription>
            {{ $t('admin.modules.publishedSubtitle') }}
          </CardDescription>
        </CardHeader>
        <CardContent class="p-0">
          <!-- Loading -->
          <div v-if="isLoadingModules" class="flex flex-col items-center justify-center py-16" role="status" aria-label="Loading modules">
            <Spinner size="lg" />
            <p class="text-muted-foreground mt-3 text-sm">Loading modules...</p>
          </div>

          <!-- Empty state -->
          <div v-else-if="modules.length === 0" class="px-6 pb-6">
            <Card class="border-dashed">
              <CardContent class="flex flex-col items-center justify-center py-16">
                <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
                  <Package class="h-8 w-8 text-muted-foreground" />
                </div>
                <h3 class="text-lg font-semibold mb-1">{{ $t('admin.modules.noModules') }}</h3>
                <p class="text-muted-foreground text-sm mb-4 text-center max-w-sm">
                  Compile and publish your first BMMDL module using the form below to get started.
                </p>
              </CardContent>
            </Card>
          </div>

          <!-- Module list with detail panel -->
          <div v-else class="flex">
            <!-- Module list -->
            <div class="flex-1 min-w-0">
              <div class="divide-y">
                <div
                  v-for="mod in modules"
                  :key="mod.id"
                  class="flex items-center gap-4 px-5 py-3.5 hover:bg-muted/50 transition-colors group cursor-pointer"
                  :class="selectedModule?.id === mod.id ? 'bg-muted/50' : ''"
                  @click="viewModule(mod)"
                >
                  <!-- Module icon -->
                  <div
                    class="h-10 w-10 rounded-lg flex items-center justify-center text-white text-sm font-medium shrink-0"
                    :class="getModuleColor(mod)"
                  >
                    {{ getModuleInitials(mod) }}
                  </div>

                  <!-- Module info -->
                  <div class="flex-1 min-w-0">
                    <div class="flex items-center gap-2">
                      <span class="font-medium truncate">{{ mod.name }}</span>
                      <Badge variant="outline" class="shrink-0">v{{ mod.version }}</Badge>
                    </div>
                    <div class="flex items-center gap-3 mt-0.5">
                      <span class="text-sm text-muted-foreground">
                        {{ mod.entityCount }} {{ mod.entityCount === 1 ? 'entity' : 'entities' }}
                      </span>
                      <span class="text-muted-foreground">·</span>
                      <span class="text-sm text-muted-foreground">
                        {{ mod.serviceCount }} {{ mod.serviceCount === 1 ? 'service' : 'services' }}
                      </span>
                      <span v-if="mod.author" class="text-muted-foreground hidden sm:inline">·</span>
                      <span v-if="mod.author" class="text-sm text-muted-foreground truncate hidden sm:inline">
                        {{ mod.author }}
                      </span>
                    </div>
                  </div>

                  <!-- Schema status -->
                  <div class="hidden md:flex items-center shrink-0">
                    <Badge
                      v-if="mod.schemaInitialized"
                      variant="default"
                      class="bg-emerald-600 hover:bg-emerald-700"
                    >
                      <Database class="mr-1 h-3 w-3" />
                      {{ mod.schemaName }}
                    </Badge>
                    <Badge v-else variant="secondary">
                      {{ $t('admin.modules.notInitialized') }}
                    </Badge>
                  </div>

                  <!-- Actions (appear on hover) -->
                  <div class="flex items-center gap-1 shrink-0 opacity-0 group-hover:opacity-100 transition-opacity">
                    <Button
                      variant="ghost"
                      size="sm"
                      class="h-8 w-8 p-0"
                      :title="'View details'"
                      @click.stop="viewModule(mod)"
                    >
                      <Eye class="h-4 w-4" />
                    </Button>
                  </div>

                  <!-- Chevron -->
                  <ChevronRight
                    class="h-4 w-4 text-muted-foreground shrink-0 transition-transform"
                    :class="selectedModule?.id === mod.id ? 'text-primary rotate-90' : 'opacity-0 group-hover:opacity-50'"
                  />
                </div>
              </div>
            </div>

            <!-- Detail panel -->
            <transition
              enter-active-class="transition-all duration-200 ease-out"
              leave-active-class="transition-all duration-150 ease-in"
              enter-from-class="opacity-0 translate-x-4"
              enter-to-class="opacity-100 translate-x-0"
              leave-from-class="opacity-100 translate-x-0"
              leave-to-class="opacity-0 translate-x-4"
            >
              <div v-if="selectedModule" class="w-80 shrink-0 border-l self-start sticky top-20">
                <div class="p-5">
                  <!-- Close button -->
                  <div class="flex justify-end mb-2">
                    <Button
                      variant="ghost"
                      size="sm"
                      class="h-8 w-8 p-0"
                      @click="selectedModule = null"
                    >
                      <X class="h-4 w-4" />
                    </Button>
                  </div>

                  <!-- Module profile header -->
                  <div class="flex flex-col items-center text-center pb-4 border-b">
                    <div
                      class="h-16 w-16 rounded-lg flex items-center justify-center text-white text-xl font-semibold mb-3"
                      :class="getModuleColor(selectedModule)"
                    >
                      {{ getModuleInitials(selectedModule) }}
                    </div>
                    <h3 class="font-semibold text-lg">{{ selectedModule.name }}</h3>
                    <Badge variant="outline" class="mt-1.5">v{{ selectedModule.version }}</Badge>
                    <p v-if="selectedModule.author" class="text-sm text-muted-foreground mt-1">
                      {{ selectedModule.author }}
                    </p>
                  </div>

                  <!-- Stats -->
                  <div class="grid grid-cols-2 gap-3 py-4 border-b">
                    <div class="text-center">
                      <div class="h-8 w-8 rounded-full bg-emerald-500/10 flex items-center justify-center mx-auto mb-1">
                        <Database class="h-4 w-4 text-emerald-500" />
                      </div>
                      <p class="text-lg font-bold">{{ selectedModule.entityCount }}</p>
                      <p class="text-xs text-muted-foreground">Entities</p>
                    </div>
                    <div class="text-center">
                      <div class="h-8 w-8 rounded-full bg-violet-500/10 flex items-center justify-center mx-auto mb-1">
                        <Server class="h-4 w-4 text-violet-500" />
                      </div>
                      <p class="text-lg font-bold">{{ selectedModule.serviceCount }}</p>
                      <p class="text-xs text-muted-foreground">Services</p>
                    </div>
                    <div class="text-center">
                      <div class="h-8 w-8 rounded-full bg-cyan-500/10 flex items-center justify-center mx-auto mb-1">
                        <Hash class="h-4 w-4 text-cyan-500" />
                      </div>
                      <p class="text-lg font-bold">{{ selectedModule.tableCount }}</p>
                      <p class="text-xs text-muted-foreground">Tables</p>
                    </div>
                    <div class="text-center">
                      <div class="h-8 w-8 rounded-full flex items-center justify-center mx-auto mb-1"
                        :class="selectedModule.schemaInitialized ? 'bg-emerald-500/10' : 'bg-muted'"
                      >
                        <Layers class="h-4 w-4" :class="selectedModule.schemaInitialized ? 'text-emerald-500' : 'text-muted-foreground'" />
                      </div>
                      <p class="text-lg font-bold" :class="selectedModule.schemaInitialized ? 'text-emerald-600' : 'text-muted-foreground'">
                        {{ selectedModule.schemaInitialized ? 'Yes' : 'No' }}
                      </p>
                      <p class="text-xs text-muted-foreground">Schema</p>
                    </div>
                  </div>

                  <!-- Details -->
                  <div class="space-y-3 py-4">
                    <div v-if="selectedModule.schemaName">
                      <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider">Schema Name</p>
                      <p class="text-sm mt-0.5 font-mono">{{ selectedModule.schemaName }}</p>
                    </div>
                    <div>
                      <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider">Published</p>
                      <p class="text-sm mt-0.5 flex items-center gap-1.5">
                        <Calendar class="h-3.5 w-3.5 text-muted-foreground" />
                        {{ selectedModule.publishedAt ? formatDate(selectedModule.publishedAt) : '-' }}
                      </p>
                    </div>
                    <div>
                      <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider">Module ID</p>
                      <p class="text-sm mt-0.5 font-mono text-muted-foreground break-all">{{ selectedModule.id }}</p>
                    </div>
                  </div>
                </div>
              </div>
            </transition>
          </div>
        </CardContent>
      </Card>

      <!-- Module Dependency Graph -->
      <Card v-if="modules.length >= 2">
        <CardHeader class="cursor-pointer" @click="showDependencyGraph = !showDependencyGraph">
          <div class="flex items-center justify-between">
            <div class="flex items-center gap-2">
              <GitBranch class="h-5 w-5" />
              <CardTitle>Module Dependencies</CardTitle>
            </div>
            <Button variant="ghost" size="sm">
              <ChevronRight
                class="h-4 w-4 transition-transform"
                :class="showDependencyGraph && 'rotate-90'"
              />
            </Button>
          </div>
          <CardDescription>
            Visual graph of inter-module dependencies and resolution status
          </CardDescription>
        </CardHeader>
        <CardContent v-if="showDependencyGraph" class="pt-0">
          <ModuleDependencyGraph :modules="modules" />
        </CardContent>
      </Card>

      <!-- Compile Module Card -->
      <Card>
        <CardHeader>
          <div class="flex items-center gap-2">
            <Upload class="h-5 w-5" />
            <CardTitle>{{ $t('admin.modules.compileModule') }}</CardTitle>
          </div>
          <CardDescription>
            {{ $t('admin.modules.compileSubtitle') }}
          </CardDescription>
        </CardHeader>
        <CardContent class="space-y-5">
          <!-- Module name and options in a styled container -->
          <div class="grid gap-5 md:grid-cols-2">
            <div class="space-y-2">
              <Label for="moduleName">{{ $t('admin.modules.moduleName') }} *</Label>
              <Input
                id="moduleName"
                v-model="moduleName"
                placeholder="MyModule"
                :disabled="isCompiling"
              />
              <p class="text-xs text-muted-foreground">
                {{ $t('admin.modules.moduleNameHint') }}
              </p>
            </div>
            <div class="space-y-3">
              <Label>{{ $t('admin.modules.options') }}</Label>
              <div class="space-y-3">
                <label
                  class="flex items-center gap-3 p-2.5 rounded-lg border cursor-pointer transition-colors"
                  :class="publishModule ? 'border-primary/50 bg-primary/5' : 'border-border hover:border-muted-foreground/30'"
                >
                  <div class="relative">
                    <input
                      type="checkbox"
                      v-model="publishModule"
                      :disabled="isCompiling"
                      class="sr-only peer"
                    />
                    <div class="w-9 h-5 rounded-full transition-colors peer-checked:bg-primary bg-muted-foreground/20"></div>
                    <div class="absolute left-0.5 top-0.5 w-4 h-4 rounded-full bg-white shadow transition-transform peer-checked:translate-x-4"></div>
                  </div>
                  <div>
                    <span class="text-sm font-medium">{{ $t('admin.modules.publishModule') }}</span>
                    <p class="text-xs text-muted-foreground">Register module in the catalog</p>
                  </div>
                </label>
                <label
                  class="flex items-center gap-3 p-2.5 rounded-lg border cursor-pointer transition-colors"
                  :class="initSchema ? 'border-primary/50 bg-primary/5' : 'border-border hover:border-muted-foreground/30'"
                >
                  <div class="relative">
                    <input
                      type="checkbox"
                      v-model="initSchema"
                      :disabled="isCompiling"
                      class="sr-only peer"
                    />
                    <div class="w-9 h-5 rounded-full transition-colors peer-checked:bg-primary bg-muted-foreground/20"></div>
                    <div class="absolute left-0.5 top-0.5 w-4 h-4 rounded-full bg-white shadow transition-transform peer-checked:translate-x-4"></div>
                  </div>
                  <div>
                    <span class="text-sm font-medium">{{ $t('admin.modules.initSchema') }}</span>
                    <p class="text-xs text-muted-foreground">Create database tables and schema</p>
                  </div>
                </label>
              </div>
            </div>
          </div>

          <!-- Template Gallery -->
          <div>
            <Button variant="outline" size="sm" @click="showTemplates = !showTemplates">
              <LayoutTemplate class="mr-2 h-4 w-4" />
              {{ showTemplates ? 'Hide Templates' : 'Start from Template' }}
            </Button>
            <ModuleTemplateGallery
              v-if="showTemplates"
              class="mt-3"
              @select="handleTemplateSelect"
            />
          </div>

          <!-- BMMDL Code Editor -->
          <div class="space-y-2">
            <div class="flex items-center justify-between">
              <Label>{{ $t('admin.modules.bmmdlSource') }} *</Label>
              <div class="flex items-center gap-3">
                <!-- AI Assist toggle -->
                <Button
                  variant="outline"
                  size="sm"
                  class="h-7 gap-1.5 text-xs"
                  :class="showAiPanel && 'border-violet-500 text-violet-600 bg-violet-500/10'"
                  @click="showAiPanel = !showAiPanel"
                >
                  <BrainCircuit class="h-3.5 w-3.5" />
                  {{ $t('ai.title') }}
                </Button>
                <!-- Auto-validation status indicator -->
                <div class="flex items-center gap-2 text-xs">
                  <template v-if="autoValidation.status.value === 'validating'">
                    <Spinner size="sm" />
                    <span class="text-muted-foreground">Validating...</span>
                  </template>
                  <template v-else-if="autoValidation.status.value === 'valid'">
                    <CheckCircle class="h-3.5 w-3.5 text-green-500" />
                    <span class="text-green-600">Valid</span>
                  </template>
                  <template v-else-if="autoValidation.status.value === 'invalid'">
                    <XCircle class="h-3.5 w-3.5 text-red-500" />
                    <span class="text-red-600">
                      {{ autoValidation.errorCount.value }} {{ autoValidation.errorCount.value === 1 ? 'error' : 'errors' }}
                      <span v-if="autoValidation.warningCount.value > 0" class="text-amber-600">
                        · {{ autoValidation.warningCount.value }} {{ autoValidation.warningCount.value === 1 ? 'warning' : 'warnings' }}
                      </span>
                    </span>
                  </template>
                </div>
              </div>
            </div>
            <BmmdlCodeEditor
              ref="codeEditorRef"
              v-model="bmmdlSource"
              :markers="mergedMarkers"
              :readonly="isCompiling"
              height="500px"
              placeholder="module MyApp v1.0 { ... }"
            />
            <!-- AI Assist Panel -->
            <AiAssistPanel
              v-if="showAiPanel"
              :context="bmmdlSource"
              :markers="mergedMarkers"
              @insert="handleAiInsert"
              @close="showAiPanel = false"
            />
          </div>

          <!-- Action buttons -->
          <div class="flex gap-2">
            <Button @click="validateSource" variant="outline" :disabled="isCompiling || isPreviewingDdl">
              <Spinner v-if="isCompiling" size="sm" class="mr-2" />
              <Play v-else class="mr-2 h-4 w-4" />
              {{ $t('admin.modules.validateOnly') }}
            </Button>
            <Button @click="previewDdl" variant="outline" :disabled="isCompiling || isPreviewingDdl">
              <Spinner v-if="isPreviewingDdl" size="sm" class="mr-2" />
              <FileCode v-else class="mr-2 h-4 w-4" />
              Preview DDL
            </Button>
            <Button @click="compileSource" :disabled="isCompiling || isPreviewingDdl" :aria-busy="isCompiling">
              <Spinner v-if="isCompiling" size="sm" class="mr-2" />
              <Upload v-else class="mr-2 h-4 w-4" />
              {{ $t('admin.modules.compileAndRegister') }}
            </Button>
          </div>

          <!-- Compile Output Panel -->
          <CompileOutputPanel
            v-if="compileResult || isCompiling"
            :diagnostics="compileDiagnostics"
            :output="compileOutputText"
            :schema="compileResult?.schemaResult || ''"
            :is-compiling="isCompiling"
            :success="compileSuccess"
            :stats="compileStats"
            height="250px"
            @diagnostic-click="handleDiagnosticClick"
          />

          <!-- Compilation Pipeline Visualization -->
          <div v-if="compileResult?.success" class="space-y-2">
            <Button variant="outline" size="sm" @click="showPipeline = !showPipeline">
              <Play class="mr-2 h-4 w-4" />
              {{ showPipeline ? 'Hide' : 'Show' }} Compilation Pipeline
            </Button>
            <Card v-if="showPipeline" class="p-4">
              <ProcessFlow :nodes="pipelineNodes" :showLabels="true" />
            </Card>
          </div>

          <!-- DDL Preview -->
          <div v-if="ddlPreview" class="space-y-2">
            <div class="flex items-center justify-between">
              <Label class="flex items-center gap-2">
                <FileCode class="h-4 w-4" />
                Generated DDL Preview
              </Label>
              <Button variant="ghost" size="sm" @click="ddlPreview = ''">
                <X class="h-4 w-4" />
              </Button>
            </div>
            <SchemaDiffViewer
              original-ddl=""
              :modified-ddl="ddlPreview"
              height="400px"
            />
          </div>
        </CardContent>
      </Card>
      <!-- Compilation History -->
      <Card v-if="compilationHistory.length > 0">
        <CardHeader class="cursor-pointer" @click="showTimeline = !showTimeline">
          <div class="flex items-center justify-between">
            <div class="flex items-center gap-2">
              <History class="h-5 w-5" />
              <CardTitle>Compilation History</CardTitle>
              <Badge variant="secondary">{{ compilationHistory.length }}</Badge>
            </div>
            <Button variant="ghost" size="sm">
              <ChevronRight
                class="h-4 w-4 transition-transform"
                :class="showTimeline && 'rotate-90'"
              />
            </Button>
          </div>
          <CardDescription>
            Session compilation attempts and results
          </CardDescription>
        </CardHeader>
        <CardContent v-if="showTimeline" class="pt-0">
          <CompilationTimeline :entries="compilationHistory" />
        </CardContent>
      </Card>
    </div>

    <ConfirmDialog
      :open="confirmDialog.isOpen.value"
      :title="confirmDialog.title.value"
      :description="confirmDialog.description.value"
      :confirm-label="confirmDialog.confirmLabel.value"
      :cancel-label="confirmDialog.cancelLabel.value"
      :variant="confirmDialog.variant.value"
      @confirm="confirmDialog.handleConfirm"
      @cancel="confirmDialog.handleCancel"
      @update:open="confirmDialog.isOpen.value = $event"
    />
  </DefaultLayout>
</template>
