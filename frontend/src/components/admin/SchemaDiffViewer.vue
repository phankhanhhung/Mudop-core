<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted, nextTick, shallowRef } from 'vue'
import * as monaco from 'monaco-editor'
import { cn } from '@/lib/utils'
import { useUiStore } from '@/stores/ui'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Columns2, AlignJustify, Copy, Plus, Minus } from 'lucide-vue-next'

interface Props {
  originalDdl: string
  modifiedDdl: string
  height?: string
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  height: '500px',
})

const uiStore = useUiStore()

const containerRef = ref<HTMLDivElement | null>(null)
const diffEditorInstance = shallowRef<monaco.editor.IDiffEditor | null>(null)
const originalModel = shallowRef<monaco.editor.ITextModel | null>(null)
const modifiedModel = shallowRef<monaco.editor.ITextModel | null>(null)

const sideBySide = ref(true)

// -- Diff stats --

const diffStats = computed(() => {
  const originalLines = props.originalDdl.split('\n')
  const modifiedLines = props.modifiedDdl.split('\n')

  const originalSet = new Set(originalLines.map((l) => l.trimEnd()))
  const modifiedSet = new Set(modifiedLines.map((l) => l.trimEnd()))

  let additions = 0
  let deletions = 0

  for (const line of modifiedLines) {
    if (!originalSet.has(line.trimEnd())) {
      additions++
    }
  }

  for (const line of originalLines) {
    if (!modifiedSet.has(line.trimEnd())) {
      deletions++
    }
  }

  return { additions, deletions }
})

// -- Editor lifecycle --

onMounted(() => {
  if (!containerRef.value) return

  const editor = monaco.editor.createDiffEditor(containerRef.value, {
    readOnly: true,
    originalEditable: false,
    renderSideBySide: sideBySide.value,
    lineNumbers: 'on',
    scrollBeyondLastLine: false,
    automaticLayout: true,
    fontSize: 13,
    minimap: { enabled: false },
    renderWhitespace: 'selection',
    padding: { top: 8, bottom: 8 },
    smoothScrolling: true,
    theme: uiStore.isDark ? 'vs-dark' : 'vs',
  })

  diffEditorInstance.value = editor

  const origModel = monaco.editor.createModel(props.originalDdl, 'sql')
  const modModel = monaco.editor.createModel(props.modifiedDdl, 'sql')
  originalModel.value = origModel
  modifiedModel.value = modModel

  editor.setModel({
    original: origModel,
    modified: modModel,
  })
})

onUnmounted(() => {
  originalModel.value?.dispose()
  modifiedModel.value?.dispose()
  diffEditorInstance.value?.dispose()
  originalModel.value = null
  modifiedModel.value = null
  diffEditorInstance.value = null
})

// -- Watch prop changes --

watch(
  () => props.originalDdl,
  (newVal) => {
    originalModel.value?.setValue(newVal)
  }
)

watch(
  () => props.modifiedDdl,
  (newVal) => {
    modifiedModel.value?.setValue(newVal)
  }
)

// -- Dark mode sync --

watch(
  () => uiStore.isDark,
  (dark) => {
    monaco.editor.setTheme(dark ? 'vs-dark' : 'vs')
  }
)

// -- Side-by-side toggle --

function toggleDiffMode() {
  sideBySide.value = !sideBySide.value
  diffEditorInstance.value?.updateOptions({
    renderSideBySide: sideBySide.value,
  })
  nextTick(() => {
    diffEditorInstance.value?.getModifiedEditor().layout()
  })
}

// -- Copy new DDL --

async function copyModifiedDdl() {
  try {
    await navigator.clipboard.writeText(props.modifiedDdl)
    uiStore.success('Copied', 'New DDL copied to clipboard')
  } catch {
    uiStore.error('Copy failed', 'Could not copy to clipboard')
  }
}
</script>

<template>
  <div
    :class="
      cn(
        'rounded-lg border overflow-hidden bg-background flex flex-col',
        props.class
      )
    "
  >
    <!-- Toolbar -->
    <div class="flex items-center justify-between px-3 py-1.5 border-b bg-muted/30 shrink-0">
      <!-- Left: Labels -->
      <div class="flex items-center gap-3 text-xs text-muted-foreground">
        <span class="font-medium">Current Schema</span>
        <span class="text-muted-foreground/50">vs</span>
        <span class="font-medium">New Schema</span>
      </div>

      <!-- Right: Actions + Stats -->
      <div class="flex items-center gap-2">
        <!-- Diff stats -->
        <div class="flex items-center gap-1.5">
          <Badge v-if="diffStats.additions > 0" variant="outline" class="gap-1 text-green-600 dark:text-green-400 border-green-300 dark:border-green-700">
            <Plus class="h-3 w-3" />
            {{ diffStats.additions }}
          </Badge>
          <Badge v-if="diffStats.deletions > 0" variant="outline" class="gap-1 text-red-600 dark:text-red-400 border-red-300 dark:border-red-700">
            <Minus class="h-3 w-3" />
            {{ diffStats.deletions }}
          </Badge>
          <span v-if="diffStats.additions === 0 && diffStats.deletions === 0" class="text-xs text-muted-foreground">
            No changes
          </span>
        </div>

        <!-- Toggle diff mode -->
        <Button
          variant="ghost"
          size="icon"
          class="h-7 w-7"
          :class="sideBySide && 'bg-accent text-accent-foreground'"
          :title="sideBySide ? 'Switch to inline diff' : 'Switch to side-by-side diff'"
          @click="toggleDiffMode"
        >
          <Columns2 v-if="sideBySide" class="h-3.5 w-3.5" />
          <AlignJustify v-else class="h-3.5 w-3.5" />
        </Button>

        <!-- Copy new DDL -->
        <Button
          variant="ghost"
          size="icon"
          class="h-7 w-7"
          title="Copy New DDL"
          @click="copyModifiedDdl"
        >
          <Copy class="h-3.5 w-3.5" />
        </Button>
      </div>
    </div>

    <!-- Diff editor container -->
    <div
      ref="containerRef"
      :style="{ height, width: '100%' }"
    />
  </div>
</template>
