<script setup lang="ts">
import { ref, computed } from 'vue'
import { cn } from '@/lib/utils'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import {
  XCircle,
  AlertTriangle,
  Info,
  CheckCircle,
  Copy,
  Database,
  Server,
  Hash,
  Clock,
  Layers,
  Terminal,
  FileCode
} from 'lucide-vue-next'

interface CompileDiagnostic {
  line?: number
  column?: number
  message: string
  severity: 'error' | 'warning' | 'info'
  code?: string
}

interface Props {
  diagnostics?: CompileDiagnostic[]
  output?: string
  schema?: string
  isCompiling?: boolean
  success?: boolean | null
  stats?: {
    entityCount: number
    serviceCount: number
    enumCount: number
    compilationTime: string
  }
  height?: string
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  diagnostics: () => [],
  output: '',
  schema: '',
  isCompiling: false,
  success: null,
  height: '250px'
})

const emit = defineEmits<{
  'diagnostic-click': [payload: { line: number; column: number }]
}>()

type TabId = 'problems' | 'output' | 'schema'
const activeTab = ref<TabId>('problems')

const sortedDiagnostics = computed(() => {
  const severityOrder: Record<string, number> = { error: 0, warning: 1, info: 2 }
  return [...props.diagnostics].sort(
    (a, b) => (severityOrder[a.severity] ?? 3) - (severityOrder[b.severity] ?? 3)
  )
})

const errorCount = computed(() => props.diagnostics.filter((d) => d.severity === 'error').length)
const warningCount = computed(
  () => props.diagnostics.filter((d) => d.severity === 'warning').length
)
const infoCount = computed(() => props.diagnostics.filter((d) => d.severity === 'info').length)

const problemsBadgeCount = computed(() => props.diagnostics.length)

const problemsBadgeVariant = computed(() => {
  if (errorCount.value > 0) return 'destructive'
  if (warningCount.value > 0) return 'default'
  return 'secondary'
})

const tabs = computed(() => [
  {
    id: 'problems' as TabId,
    label: 'Problems',
    icon: XCircle,
    count: problemsBadgeCount.value || undefined,
    variant: problemsBadgeVariant.value
  },
  {
    id: 'output' as TabId,
    label: 'Output',
    icon: Terminal,
    count: undefined,
    variant: 'secondary' as const
  },
  {
    id: 'schema' as TabId,
    label: 'Schema',
    icon: FileCode,
    count: undefined,
    variant: 'secondary' as const
  }
])

const schemaCopied = ref(false)

async function copySchema() {
  if (!props.schema) return
  try {
    await navigator.clipboard.writeText(props.schema)
    schemaCopied.value = true
    setTimeout(() => {
      schemaCopied.value = false
    }, 2000)
  } catch {
    // Fallback: ignore clipboard errors in non-secure contexts
  }
}

function handleDiagnosticClick(diagnostic: CompileDiagnostic) {
  if (diagnostic.line != null) {
    emit('diagnostic-click', {
      line: diagnostic.line,
      column: diagnostic.column ?? 1
    })
  }
}

function severityIcon(severity: string) {
  switch (severity) {
    case 'error':
      return XCircle
    case 'warning':
      return AlertTriangle
    case 'info':
      return Info
    default:
      return Info
  }
}

function severityColor(severity: string) {
  switch (severity) {
    case 'error':
      return 'text-red-500'
    case 'warning':
      return 'text-amber-500'
    case 'info':
      return 'text-blue-500'
    default:
      return 'text-muted-foreground'
  }
}
</script>

<template>
  <div :class="cn('rounded-lg border overflow-hidden bg-background', props.class)">
    <!-- Tab bar -->
    <div class="flex items-center border-b bg-muted/30 px-1">
      <button
        v-for="tab in tabs"
        :key="tab.id"
        @click="activeTab = tab.id"
        class="flex items-center gap-1.5 px-3 py-2 text-xs font-medium border-b-2 transition-colors -mb-px"
        :class="
          activeTab === tab.id
            ? 'border-primary text-foreground'
            : 'border-transparent text-muted-foreground hover:text-foreground'
        "
      >
        <component :is="tab.icon" class="h-3.5 w-3.5" />
        {{ tab.label }}
        <Badge
          v-if="tab.count"
          :variant="tab.variant as any"
          class="ml-1 h-4 min-w-4 px-1 text-[10px] leading-none"
        >
          {{ tab.count }}
        </Badge>
      </button>
    </div>

    <!-- Tab content -->
    <div :style="{ height: props.height }" class="overflow-auto">
      <!-- Problems tab -->
      <div v-if="activeTab === 'problems'" class="h-full">
        <!-- Compiling state -->
        <div
          v-if="isCompiling"
          class="flex items-center justify-center h-full gap-2 text-muted-foreground"
        >
          <Spinner size="sm" />
          <span class="text-sm">Compiling...</span>
        </div>

        <!-- Empty state: no problems -->
        <div
          v-else-if="sortedDiagnostics.length === 0"
          class="flex items-center justify-center h-full text-muted-foreground"
        >
          <div class="text-center">
            <CheckCircle class="h-8 w-8 mx-auto mb-2 text-green-500 opacity-60" />
            <p class="text-sm">No problems detected</p>
          </div>
        </div>

        <!-- Diagnostics list -->
        <div v-else class="divide-y">
          <button
            v-for="(diagnostic, index) in sortedDiagnostics"
            :key="index"
            class="flex items-start gap-2 w-full px-3 py-1.5 text-left hover:bg-muted/50 transition-colors"
            :class="{ 'cursor-pointer': diagnostic.line != null, 'cursor-default': diagnostic.line == null }"
            @click="handleDiagnosticClick(diagnostic)"
          >
            <!-- Severity icon -->
            <component
              :is="severityIcon(diagnostic.severity)"
              class="h-3.5 w-3.5 mt-0.5 shrink-0"
              :class="severityColor(diagnostic.severity)"
            />

            <!-- Message -->
            <span class="text-xs leading-relaxed flex-1 min-w-0 break-words">
              {{ diagnostic.message }}
            </span>

            <!-- Line/column -->
            <span
              v-if="diagnostic.line != null"
              class="text-[10px] text-muted-foreground shrink-0 mt-0.5 hover:text-foreground hover:underline"
            >
              Ln {{ diagnostic.line }}<template v-if="diagnostic.column != null">, Col {{ diagnostic.column }}</template>
            </span>

            <!-- Error code badge -->
            <Badge
              v-if="diagnostic.code"
              variant="outline"
              class="text-[10px] px-1.5 py-0 h-4 shrink-0 mt-0.5 text-muted-foreground font-mono"
            >
              {{ diagnostic.code }}
            </Badge>
          </button>
        </div>
      </div>

      <!-- Output tab -->
      <div v-else-if="activeTab === 'output'" class="h-full">
        <!-- Not yet compiled -->
        <div
          v-if="success === null && !isCompiling && !output"
          class="flex items-center justify-center h-full text-muted-foreground"
        >
          <div class="text-center">
            <Terminal class="h-8 w-8 mx-auto mb-2 opacity-30" />
            <p class="text-sm">Run compile or validate to see output</p>
          </div>
        </div>

        <!-- Compilation output -->
        <div v-else class="p-3 space-y-3">
          <!-- Status line -->
          <div v-if="success !== null" class="flex items-center gap-2">
            <CheckCircle v-if="success" class="h-4 w-4 text-green-500" />
            <XCircle v-else class="h-4 w-4 text-red-500" />
            <span class="text-sm font-medium" :class="success ? 'text-green-600 dark:text-green-400' : 'text-red-600 dark:text-red-400'">
              {{ success ? 'Compilation succeeded' : 'Compilation failed' }}
            </span>
          </div>

          <!-- Compiling indicator -->
          <div v-if="isCompiling" class="flex items-center gap-2 text-muted-foreground">
            <Spinner size="sm" />
            <span class="text-sm">Compiling...</span>
          </div>

          <!-- Stats -->
          <div v-if="stats" class="flex flex-wrap gap-4 text-xs text-muted-foreground">
            <div class="flex items-center gap-1">
              <Database class="h-3 w-3" />
              <span>{{ stats.entityCount }} entities</span>
            </div>
            <div class="flex items-center gap-1">
              <Server class="h-3 w-3" />
              <span>{{ stats.serviceCount }} services</span>
            </div>
            <div class="flex items-center gap-1">
              <Hash class="h-3 w-3" />
              <span>{{ stats.enumCount }} enums</span>
            </div>
            <div class="flex items-center gap-1">
              <Clock class="h-3 w-3" />
              <span>{{ stats.compilationTime }}</span>
            </div>
          </div>

          <!-- Diagnostics summary in output tab -->
          <div
            v-if="diagnostics.length > 0"
            class="flex items-center gap-3 text-xs"
          >
            <span v-if="errorCount > 0" class="flex items-center gap-1 text-red-500">
              <XCircle class="h-3 w-3" />
              {{ errorCount }} error{{ errorCount !== 1 ? 's' : '' }}
            </span>
            <span v-if="warningCount > 0" class="flex items-center gap-1 text-amber-500">
              <AlertTriangle class="h-3 w-3" />
              {{ warningCount }} warning{{ warningCount !== 1 ? 's' : '' }}
            </span>
            <span v-if="infoCount > 0" class="flex items-center gap-1 text-blue-500">
              <Info class="h-3 w-3" />
              {{ infoCount }} info
            </span>
          </div>

          <!-- Full output text -->
          <pre
            v-if="output"
            class="text-xs font-mono bg-muted/50 rounded-md p-3 whitespace-pre-wrap break-words leading-relaxed"
          >{{ output }}</pre>
        </div>
      </div>

      <!-- Schema tab -->
      <div v-else-if="activeTab === 'schema'" class="h-full">
        <!-- No schema yet -->
        <div
          v-if="!schema"
          class="flex items-center justify-center h-full text-muted-foreground"
        >
          <div class="text-center">
            <Layers class="h-8 w-8 mx-auto mb-2 opacity-30" />
            <p class="text-sm">Schema output will appear after compilation with 'Init Schema' enabled</p>
          </div>
        </div>

        <!-- Schema DDL output -->
        <div v-else class="relative h-full">
          <!-- Copy button -->
          <div class="absolute top-2 right-2 z-10">
            <Button
              variant="outline"
              size="sm"
              class="h-7 px-2 text-xs gap-1 bg-background/80 backdrop-blur-sm"
              @click="copySchema"
            >
              <Copy class="h-3 w-3" />
              {{ schemaCopied ? 'Copied!' : 'Copy' }}
            </Button>
          </div>

          <!-- SQL output -->
          <pre class="text-xs font-mono p-3 pr-24 whitespace-pre-wrap break-words leading-relaxed h-full">{{ schema }}</pre>
        </div>
      </div>
    </div>
  </div>
</template>
