<script setup lang="ts">
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { aiService } from '@/services/aiService'
import type { AiAssistResponse } from '@/services/aiService'
import type { EditorMarker } from './BmmdlCodeEditor.vue'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Spinner } from '@/components/ui/spinner'
import {
  BrainCircuit,
  X,
  Sparkles,
  Eye,
  AlertCircle,
  Copy,
  ArrowDownToLine,
  RefreshCw,
} from 'lucide-vue-next'

interface Props {
  context: string
  markers?: EditorMarker[]
}

const props = withDefaults(defineProps<Props>(), {
  markers: () => [],
})

const emit = defineEmits<{
  insert: [code: string]
  close: []
}>()

const { t } = useI18n()

type Tab = 'generate' | 'review' | 'explain'
const activeTab = ref<Tab>('generate')

// ── Generate ──────────────────────────────────────────────────────────────────
const generatePrompt = ref('')
const generateResult = ref<AiAssistResponse | null>(null)
const isGenerating = ref(false)
const generateError = ref<string | null>(null)

async function runGenerate() {
  if (!generatePrompt.value.trim()) return
  isGenerating.value = true
  generateError.value = null
  generateResult.value = null
  try {
    generateResult.value = await aiService.assist({
      operation: 'generate',
      context: props.context,
      prompt: generatePrompt.value,
    })
  } catch (e) {
    generateError.value = e instanceof Error ? e.message : t('ai.error')
  } finally {
    isGenerating.value = false
  }
}

// ── Review ────────────────────────────────────────────────────────────────────
const reviewResult = ref<AiAssistResponse | null>(null)
const isReviewing = ref(false)
const reviewError = ref<string | null>(null)

async function runReview() {
  isReviewing.value = true
  reviewError.value = null
  reviewResult.value = null
  try {
    reviewResult.value = await aiService.assist({
      operation: 'review',
      context: props.context,
    })
  } catch (e) {
    reviewError.value = e instanceof Error ? e.message : t('ai.error')
  } finally {
    isReviewing.value = false
  }
}

// ── Explain ───────────────────────────────────────────────────────────────────
const errorMarkers = computed(() =>
  (props.markers ?? []).filter((m) => m.severity === 'error' || m.severity === 'warning'),
)
const selectedErrorIdx = ref(0)
const selectedMarker = computed(() => errorMarkers.value[selectedErrorIdx.value] ?? null)
const explainResult = ref<AiAssistResponse | null>(null)
const isExplaining = ref(false)
const explainError = ref<string | null>(null)

async function runExplain() {
  if (!selectedMarker.value) return
  isExplaining.value = true
  explainError.value = null
  explainResult.value = null
  try {
    explainResult.value = await aiService.assist({
      operation: 'explain-error',
      context: props.context,
      error: `Line ${selectedMarker.value.line}, Col ${selectedMarker.value.column}: ${selectedMarker.value.message}`,
    })
  } catch (e) {
    explainError.value = e instanceof Error ? e.message : t('ai.error')
  } finally {
    isExplaining.value = false
  }
}

// ── Helpers ───────────────────────────────────────────────────────────────────
async function copyToClipboard(text: string) {
  try {
    await navigator.clipboard.writeText(text)
  } catch {
    // silently ignore
  }
}

function insertCode(code: string) {
  emit('insert', code)
}
</script>

<template>
  <div
    class="rounded-lg border bg-card text-card-foreground shadow-sm flex flex-col"
    data-testid="ai-assist-panel"
  >
    <!-- Panel header -->
    <div class="flex items-center justify-between px-4 py-3 border-b bg-gradient-to-r from-violet-500/10 to-blue-500/10">
      <div class="flex items-center gap-2">
        <BrainCircuit class="h-4 w-4 text-violet-600 dark:text-violet-400" />
        <span class="text-sm font-semibold">{{ t('ai.title') }}</span>
        <Badge variant="secondary" class="text-[10px] px-1.5 py-0">Beta</Badge>
      </div>
      <Button variant="ghost" size="icon" class="h-7 w-7" :title="t('ai.close')" @click="emit('close')">
        <X class="h-3.5 w-3.5" />
      </Button>
    </div>

    <!-- Tab bar -->
    <div class="flex border-b">
      <button
        v-for="tab in (['generate', 'review', 'explain'] as Tab[])"
        :key="tab"
        class="flex-1 px-3 py-2 text-xs font-medium transition-colors focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
        :class="activeTab === tab
          ? 'border-b-2 border-violet-500 text-violet-600 dark:text-violet-400'
          : 'text-muted-foreground hover:text-foreground'"
        @click="activeTab = tab"
      >
        <span class="flex items-center justify-center gap-1.5">
          <Sparkles v-if="tab === 'generate'" class="h-3 w-3" />
          <Eye v-else-if="tab === 'review'" class="h-3 w-3" />
          <AlertCircle v-else class="h-3 w-3" />
          {{ t(`ai.${tab}`) }}
        </span>
      </button>
    </div>

    <!-- Tab content -->
    <div class="flex-1 overflow-auto p-4 space-y-3 min-h-0">

      <!-- ── Generate Tab ── -->
      <template v-if="activeTab === 'generate'">
        <div class="space-y-2">
          <label class="text-xs font-medium text-muted-foreground">
            {{ t('ai.generatePromptLabel') }}
          </label>
          <textarea
            v-model="generatePrompt"
            class="w-full min-h-[80px] rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring resize-none"
            :placeholder="t('ai.generatePromptPlaceholder')"
            :disabled="isGenerating"
            @keydown.ctrl.enter.prevent="runGenerate"
          />
        </div>
        <Button
          size="sm"
          class="w-full"
          :disabled="!generatePrompt.trim() || isGenerating"
          @click="runGenerate"
        >
          <Spinner v-if="isGenerating" size="sm" class="mr-2" />
          <Sparkles v-else class="mr-2 h-3.5 w-3.5" />
          {{ isGenerating ? t('ai.loading') : t('ai.generateButton') }}
        </Button>

        <!-- Error -->
        <div v-if="generateError" class="text-xs text-destructive bg-destructive/10 rounded-md px-3 py-2">
          {{ generateError }}
        </div>

        <!-- Result -->
        <div v-if="generateResult" class="space-y-2">
          <div class="flex items-center justify-between">
            <span class="text-xs font-medium text-muted-foreground">Generated Code</span>
            <div class="flex gap-1">
              <Button variant="ghost" size="icon" class="h-6 w-6" :title="t('ai.copy')"
                @click="copyToClipboard(generateResult.result)">
                <Copy class="h-3 w-3" />
              </Button>
            </div>
          </div>
          <pre class="text-xs bg-muted rounded-md p-3 overflow-auto max-h-48 font-mono whitespace-pre-wrap">{{ generateResult.result }}</pre>
          <Button size="sm" variant="outline" class="w-full" @click="insertCode(generateResult.result)">
            <ArrowDownToLine class="mr-2 h-3.5 w-3.5" />
            {{ t('ai.insertCode') }}
          </Button>
        </div>
      </template>

      <!-- ── Review Tab ── -->
      <template v-else-if="activeTab === 'review'">
        <p class="text-xs text-muted-foreground">
          {{ t('ai.reviewDescription') }}
        </p>
        <Button
          size="sm"
          class="w-full"
          :disabled="!context.trim() || isReviewing"
          @click="runReview"
        >
          <Spinner v-if="isReviewing" size="sm" class="mr-2" />
          <RefreshCw v-else class="mr-2 h-3.5 w-3.5" />
          {{ isReviewing ? t('ai.loading') : t('ai.reviewButton') }}
        </Button>

        <!-- Error -->
        <div v-if="reviewError" class="text-xs text-destructive bg-destructive/10 rounded-md px-3 py-2">
          {{ reviewError }}
        </div>

        <!-- Result -->
        <div v-if="reviewResult" class="space-y-1.5">
          <p v-if="!reviewResult.suggestions?.length && !reviewResult.result.trim()"
            class="text-xs text-muted-foreground italic">
            {{ t('ai.reviewNoSuggestions') }}
          </p>
          <template v-else>
            <div
              v-for="(suggestion, idx) in (reviewResult.suggestions ?? reviewResult.result.split('\n').filter(Boolean))"
              :key="idx"
              class="flex gap-2 text-xs p-2 bg-muted/60 rounded-md"
            >
              <span class="shrink-0 w-4 h-4 rounded-full bg-violet-500/20 text-violet-700 dark:text-violet-300 flex items-center justify-center text-[10px] font-bold">
                {{ idx + 1 }}
              </span>
              <span class="text-foreground/90">{{ suggestion }}</span>
            </div>
          </template>
        </div>
      </template>

      <!-- ── Explain Tab ── -->
      <template v-else-if="activeTab === 'explain'">
        <div v-if="errorMarkers.length === 0" class="text-xs text-muted-foreground italic text-center py-4">
          {{ t('ai.explainNoErrors') }}
        </div>
        <template v-else>
          <div class="space-y-2">
            <label class="text-xs font-medium text-muted-foreground">{{ t('ai.explainSelectError') }}</label>
            <select
              v-model="selectedErrorIdx"
              class="w-full rounded-md border border-input bg-background px-3 py-1.5 text-xs ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            >
              <option v-for="(m, idx) in errorMarkers" :key="idx" :value="idx">
                [{{ m.severity === 'error' ? 'Error' : 'Warning' }}] Ln {{ m.line }}: {{ m.message.slice(0, 60) }}{{ m.message.length > 60 ? '…' : '' }}
              </option>
            </select>
          </div>

          <Button
            size="sm"
            class="w-full"
            :disabled="!selectedMarker || isExplaining"
            @click="runExplain"
          >
            <Spinner v-if="isExplaining" size="sm" class="mr-2" />
            <AlertCircle v-else class="mr-2 h-3.5 w-3.5" />
            {{ isExplaining ? t('ai.loading') : t('ai.explainButton') }}
          </Button>

          <!-- Error -->
          <div v-if="explainError" class="text-xs text-destructive bg-destructive/10 rounded-md px-3 py-2">
            {{ explainError }}
          </div>

          <!-- Result -->
          <div v-if="explainResult" class="text-xs bg-muted/60 rounded-md p-3 leading-relaxed">
            {{ explainResult.result }}
          </div>
        </template>
      </template>

    </div>
  </div>
</template>
