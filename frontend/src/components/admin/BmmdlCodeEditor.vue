<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted, nextTick, computed, shallowRef } from 'vue'
import * as monaco from 'monaco-editor'
import { initBmmdlMonaco } from '@/lib/monaco'
import { cn } from '@/lib/utils'
import { useUiStore } from '@/stores/ui'
import { Button } from '@/components/ui/button'
import {
  WrapText,
  Map,
  AlignLeft,
  Copy,
  Maximize2,
  Minimize2,
  X,
} from 'lucide-vue-next'

export interface EditorMarker {
  line: number
  column: number
  endLine?: number
  endColumn?: number
  message: string
  severity: 'error' | 'warning' | 'info'
}

interface Props {
  modelValue: string
  markers?: EditorMarker[]
  readonly?: boolean
  height?: string
  placeholder?: string
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  markers: () => [],
  readonly: false,
  height: '400px',
  placeholder: '',
})

const emit = defineEmits<{
  'update:modelValue': [value: string]
  'cursor-change': [payload: { line: number; column: number }]
}>()

const uiStore = useUiStore()

const editorContainer = ref<HTMLDivElement | null>(null)
const editorInstance = shallowRef<monaco.editor.IStandaloneCodeEditor | null>(null)

const cursor = ref({ line: 1, col: 1 })
const wordWrapEnabled = ref(false)
const minimapEnabled = ref(true)
const isFullscreen = ref(false)

const isEmpty = computed(() => !props.modelValue || props.modelValue.length === 0)

// ── Editor Lifecycle ──

let ignoreModelChange = false

onMounted(() => {
  initBmmdlMonaco()

  if (!editorContainer.value) return

  const editor = monaco.editor.create(editorContainer.value, {
    value: props.modelValue,
    language: 'bmmdl',
    theme: uiStore.isDark ? 'bmmdl-dark' : 'bmmdl-light',
    fontSize: 13,
    lineNumbers: 'on',
    minimap: { enabled: minimapEnabled.value },
    wordWrap: wordWrapEnabled.value ? 'on' : 'off',
    scrollBeyondLastLine: false,
    automaticLayout: true,
    tabSize: 2,
    renderWhitespace: 'selection',
    bracketPairColorization: { enabled: true },
    guides: { bracketPairs: true, indentation: true },
    suggestOnTriggerCharacters: true,
    quickSuggestions: true,
    folding: true,
    glyphMargin: true,
    lineDecorationsWidth: 10,
    padding: { top: 8, bottom: 8 },
    smoothScrolling: true,
    cursorBlinking: 'smooth',
    cursorSmoothCaretAnimation: 'on',
    readOnly: props.readonly,
  })

  editorInstance.value = editor

  // Listen for content changes
  editor.onDidChangeModelContent(() => {
    if (ignoreModelChange) return
    const value = editor.getValue()
    emit('update:modelValue', value)
  })

  // Listen for cursor position changes
  editor.onDidChangeCursorPosition((e) => {
    cursor.value = { line: e.position.lineNumber, col: e.position.column }
    emit('cursor-change', { line: e.position.lineNumber, column: e.position.column })
  })

  // Escape key exits fullscreen
  editor.addCommand(monaco.KeyCode.Escape, () => {
    if (isFullscreen.value) {
      isFullscreen.value = false
    }
  })

  // Apply initial markers
  applyMarkers()
})

onUnmounted(() => {
  if (editorInstance.value) {
    editorInstance.value.dispose()
    editorInstance.value = null
  }
})

// ── Sync: modelValue → editor ──

watch(
  () => props.modelValue,
  (newValue) => {
    const editor = editorInstance.value
    if (!editor) return
    const currentValue = editor.getValue()
    if (newValue !== currentValue) {
      ignoreModelChange = true
      editor.setValue(newValue)
      ignoreModelChange = false
    }
  }
)

// ── Sync: readonly → editor ──

watch(
  () => props.readonly,
  (newVal) => {
    editorInstance.value?.updateOptions({ readOnly: newVal })
  }
)

// ── Dark mode sync ──

watch(
  () => uiStore.isDark,
  (dark) => {
    monaco.editor.setTheme(dark ? 'bmmdl-dark' : 'bmmdl-light')
  }
)

// ── Markers ──

function applyMarkers() {
  const editor = editorInstance.value
  if (!editor) return
  const model = editor.getModel()
  if (!model) return

  const mapped = (props.markers || []).map((m) => ({
    startLineNumber: m.line,
    startColumn: m.column || 1,
    endLineNumber: m.endLine || m.line,
    endColumn: m.endColumn || 1000,
    message: m.message,
    severity:
      m.severity === 'error'
        ? monaco.MarkerSeverity.Error
        : m.severity === 'warning'
          ? monaco.MarkerSeverity.Warning
          : monaco.MarkerSeverity.Info,
  }))

  monaco.editor.setModelMarkers(model, 'bmmdl-compiler', mapped)
}

watch(() => props.markers, applyMarkers, { deep: true })

// ── Toolbar Actions ──

function toggleWordWrap() {
  wordWrapEnabled.value = !wordWrapEnabled.value
  editorInstance.value?.updateOptions({
    wordWrap: wordWrapEnabled.value ? 'on' : 'off',
  })
}

function toggleMinimap() {
  minimapEnabled.value = !minimapEnabled.value
  editorInstance.value?.updateOptions({
    minimap: { enabled: minimapEnabled.value },
  })
}

function formatCode() {
  editorInstance.value?.getAction('editor.action.formatDocument')?.run()
}

async function copyAll() {
  const value = editorInstance.value?.getValue() ?? ''
  try {
    await navigator.clipboard.writeText(value)
    uiStore.success('Copied', 'Code copied to clipboard')
  } catch {
    uiStore.error('Copy failed', 'Could not copy to clipboard')
  }
}

function toggleFullscreen() {
  isFullscreen.value = !isFullscreen.value
  nextTick(() => {
    editorInstance.value?.layout()
    editorInstance.value?.focus()
  })
}

function exitFullscreen() {
  isFullscreen.value = false
  nextTick(() => {
    editorInstance.value?.layout()
  })
}

// ── Exposed Methods ──

function revealLine(line: number) {
  const editor = editorInstance.value
  if (!editor) return
  editor.revealLineInCenter(line)
  editor.setPosition({ lineNumber: line, column: 1 })
  editor.focus()
}

function focus() {
  editorInstance.value?.focus()
}

function insertText(text: string) {
  const editor = editorInstance.value
  if (!editor) return
  const position = editor.getPosition()
  if (!position) return
  const insertContent = '\n' + text
  editor.executeEdits('ai-insert', [
    {
      range: new monaco.Range(
        position.lineNumber,
        position.column,
        position.lineNumber,
        position.column,
      ),
      text: insertContent,
    },
  ])
  editor.focus()
}

defineExpose({
  revealLine,
  focus,
  formatCode,
  insertText,
})
</script>

<template>
  <div
    :class="
      cn(
        'rounded-lg border overflow-hidden bg-background flex flex-col',
        isFullscreen && 'fixed inset-0 z-50 rounded-none',
        props.class
      )
    "
  >
    <!-- Toolbar -->
    <div class="flex items-center justify-between px-3 py-1.5 border-b bg-muted/30 shrink-0">
      <span class="text-xs text-muted-foreground font-mono select-none">
        Ln {{ cursor.line }}, Col {{ cursor.col }}
      </span>
      <div class="flex items-center gap-0.5">
        <Button
          variant="ghost"
          size="icon"
          class="h-7 w-7"
          :class="wordWrapEnabled && 'bg-accent text-accent-foreground'"
          title="Toggle Word Wrap"
          @click="toggleWordWrap"
        >
          <WrapText class="h-3.5 w-3.5" />
        </Button>
        <Button
          variant="ghost"
          size="icon"
          class="h-7 w-7"
          :class="minimapEnabled && 'bg-accent text-accent-foreground'"
          title="Toggle Minimap"
          @click="toggleMinimap"
        >
          <Map class="h-3.5 w-3.5" />
        </Button>
        <Button
          variant="ghost"
          size="icon"
          class="h-7 w-7"
          title="Format Code"
          @click="formatCode"
        >
          <AlignLeft class="h-3.5 w-3.5" />
        </Button>
        <Button
          variant="ghost"
          size="icon"
          class="h-7 w-7"
          title="Copy All"
          @click="copyAll"
        >
          <Copy class="h-3.5 w-3.5" />
        </Button>
        <Button
          variant="ghost"
          size="icon"
          class="h-7 w-7"
          :title="isFullscreen ? 'Exit Fullscreen' : 'Fullscreen'"
          @click="toggleFullscreen"
        >
          <Minimize2 v-if="isFullscreen" class="h-3.5 w-3.5" />
          <Maximize2 v-else class="h-3.5 w-3.5" />
        </Button>
        <!-- Fullscreen close button -->
        <Button
          v-if="isFullscreen"
          variant="ghost"
          size="icon"
          class="h-7 w-7 ml-1"
          title="Close Fullscreen (Escape)"
          @click="exitFullscreen"
        >
          <X class="h-3.5 w-3.5" />
        </Button>
      </div>
    </div>

    <!-- Editor container -->
    <div class="relative flex-1 min-h-0">
      <div
        ref="editorContainer"
        :style="{
          height: isFullscreen ? 'calc(100vh - 40px)' : height,
          width: '100%',
        }"
      />

      <!-- Placeholder overlay -->
      <div
        v-if="isEmpty && placeholder"
        class="absolute top-2 left-14 text-sm text-muted-foreground/50 italic pointer-events-none select-none"
      >
        {{ placeholder }}
      </div>
    </div>
  </div>
</template>
