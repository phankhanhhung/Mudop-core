<script setup lang="ts">
import { ref, computed, watch, onUnmounted } from 'vue'
import {
  DialogRoot,
  DialogPortal,
  DialogOverlay,
  DialogContent,
  DialogTitle,
  DialogDescription,
  DialogClose
} from 'radix-vue'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Select } from '@/components/ui/select'
import { Alert, AlertTitle, AlertDescription } from '@/components/ui/alert'
import { Spinner } from '@/components/ui/spinner'
import { Upload, X, Check, AlertCircle, Download, GripVertical } from 'lucide-vue-next'
import Sortable from 'sortablejs'
import Wizard from '@/components/smart/Wizard.vue'
import type { WizardStep } from '@/composables/useWizard'
import type { FieldMetadata } from '@/types/metadata'
import type { ColumnMapping, ImportResult, ParseResult } from '@/utils/dataImport'
import { parseCsvFile, parseExcelFile, mapColumns, transformImportData } from '@/utils/dataImport'
import { exportToCsv, generateFilename } from '@/utils/dataExport'
import importService from '@/services/importService'

interface Props {
  open: boolean
  entityType: string
  module: string
  fields: FieldMetadata[]
}

const props = defineProps<Props>()

const emit = defineEmits<{
  imported: [count: number]
  close: []
  'update:open': [value: boolean]
}>()

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

const parseResult = ref<ParseResult | null>(null)
const columnMappings = ref<ColumnMapping[]>([])
const importResult = ref<ImportResult | null>(null)
const isImporting = ref(false)
const importProgress = ref(0)
const importResponse = ref<{ successCount: number; errorCount: number; errors: { rowIndex: number; message: string; data: Record<string, unknown> }[] } | null>(null)
const uploadError = ref<string | null>(null)
const isDragOver = ref(false)
const currentStepIndex = ref(0)
const wizardKey = ref(0)

// Drag-drop for mapping table
const mappingTableBody = ref<HTMLElement | null>(null)
let sortableInstance: Sortable | null = null

watch(mappingTableBody, (el) => {
  if (el) {
    sortableInstance = Sortable.create(el, {
      handle: '.drag-handle',
      animation: 150,
      onEnd: (event) => {
        const { oldIndex, newIndex } = event
        if (oldIndex === undefined || newIndex === undefined || oldIndex === newIndex) return
        const newMappings = [...columnMappings.value]
        const [moved] = newMappings.splice(oldIndex, 1)
        newMappings.splice(newIndex, 0, moved)
        columnMappings.value = newMappings
      },
    })
  } else {
    sortableInstance?.destroy()
    sortableInstance = null
  }
})

onUnmounted(() => {
  sortableInstance?.destroy()
})

// Importable fields: skip computed and readonly
const importableFields = computed(() =>
  props.fields.filter((f) => !f.isComputed && !f.isReadOnly)
)

const mappedCount = computed(() =>
  columnMappings.value.filter((m) => m.fieldName).length
)

const previewRows = computed(() => {
  if (!importResult.value) return []
  const valid = importResult.value.validRows.slice(0, 5).map((row, i) => ({
    index: i + 1,
    data: row,
    valid: true as const,
    errors: {} as Record<string, string>,
  }))
  const invalid = importResult.value.invalidRows.slice(0, 5).map((row) => ({
    index: row.rowIndex,
    data: row.data as Record<string, unknown>,
    valid: false as const,
    errors: row.errors,
  }))
  return [...valid, ...invalid].sort((a, b) => a.index - b.index).slice(0, 10)
})

const wizardDescription = computed(() => {
  switch (currentStepIndex.value) {
    case 0: return 'Upload a CSV or Excel file to import records.'
    case 1: return 'Map file columns to entity fields.'
    case 2: return 'Review data before importing.'
    default: return isImporting.value ? 'Importing records...' : 'Import complete.'
  }
})

// ---------------------------------------------------------------------------
// Wizard Steps
// ---------------------------------------------------------------------------

const wizardSteps = computed<WizardStep[]>(() => [
  {
    key: 'upload',
    title: 'Upload',
    subtitle: 'Select a file',
    validate: () => !!parseResult.value,
  },
  {
    key: 'mapping',
    title: 'Map Columns',
    subtitle: `${mappedCount.value} mapped`,
    validate: () => {
      if (mappedCount.value === 0) return false
      proceedToPreview()
      return true
    },
  },
  {
    key: 'preview',
    title: 'Preview',
    subtitle: importResult.value
      ? `${importResult.value.validRows.length} valid`
      : '',
  },
  {
    key: 'result',
    title: 'Import',
    subtitle: 'Execute import',
    optional: true,
  },
])

// ---------------------------------------------------------------------------
// File Handling
// ---------------------------------------------------------------------------

function handleDragOver(e: DragEvent) {
  e.preventDefault()
  isDragOver.value = true
}

function handleDragLeave() {
  isDragOver.value = false
}

function handleDrop(e: DragEvent) {
  e.preventDefault()
  isDragOver.value = false
  const file = e.dataTransfer?.files[0]
  if (file) processFile(file)
}

function handleFileSelect(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  if (file) processFile(file)
  // Reset input so the same file can be re-selected
  input.value = ''
}

async function processFile(file: File) {
  uploadError.value = null

  const ext = file.name.split('.').pop()?.toLowerCase()
  let result: ParseResult

  if (ext === 'csv' || ext === 'tsv' || ext === 'txt') {
    result = await parseCsvFile(file)
  } else if (ext === 'xlsx' || ext === 'xls') {
    result = await parseExcelFile(file)
  } else {
    uploadError.value = `Unsupported file format: .${ext}. Please use a CSV file.`
    return
  }

  if (result.errors.length > 0 && result.rows.length === 0) {
    uploadError.value = result.errors.join('; ')
    return
  }

  if (result.rows.length === 0) {
    uploadError.value = 'The file contains no data rows.'
    return
  }

  parseResult.value = result
  columnMappings.value = mapColumns(result.headers, importableFields.value)
}

// ---------------------------------------------------------------------------
// Column Mapping
// ---------------------------------------------------------------------------

function updateMapping(index: number, fieldName: string) {
  const mapping = columnMappings.value[index]
  if (fieldName === '') {
    mapping.fieldName = null
    mapping.field = null
    mapping.auto = false
  } else {
    const field = importableFields.value.find((f) => f.name === fieldName)
    mapping.fieldName = fieldName
    mapping.field = field ?? null
    mapping.auto = false
  }
}

function proceedToPreview() {
  if (!parseResult.value) return

  importResult.value = transformImportData(
    parseResult.value.rows,
    columnMappings.value,
    props.fields
  )
}

// ---------------------------------------------------------------------------
// Import Execution
// ---------------------------------------------------------------------------

const CHUNK_SIZE = 50

async function executeImport() {
  if (!importResult.value || importResult.value.validRows.length === 0) return

  isImporting.value = true
  importProgress.value = 0

  const rows = importResult.value.validRows
  let totalSuccess = 0
  let totalErrors = 0
  const allErrors: { rowIndex: number; message: string; data: Record<string, unknown> }[] = []

  // Send in chunks
  for (let i = 0; i < rows.length; i += CHUNK_SIZE) {
    const chunk = rows.slice(i, i + CHUNK_SIZE)

    try {
      const result = await importService.bulkImport(props.module, props.entityType, {
        records: chunk,
        stopOnError: false,
      })

      totalSuccess += result.successCount
      totalErrors += result.errorCount

      for (const err of result.errors) {
        allErrors.push({
          ...err,
          rowIndex: err.rowIndex + i, // Adjust to global index
        })
      }
    } catch (err) {
      // Entire chunk failed
      totalErrors += chunk.length
      allErrors.push({
        rowIndex: i + 1,
        message: err instanceof Error ? err.message : 'Chunk import failed',
        data: {},
      })
    }

    importProgress.value = Math.round(((i + chunk.length) / rows.length) * 100)
  }

  importProgress.value = 100
  isImporting.value = false
  importResponse.value = {
    successCount: totalSuccess,
    errorCount: totalErrors,
    errors: allErrors,
  }

  if (totalSuccess > 0) {
    emit('imported', totalSuccess)
  }
}

// ---------------------------------------------------------------------------
// Error Export
// ---------------------------------------------------------------------------

function downloadErrors() {
  if (!importResponse.value || importResponse.value.errors.length === 0) return

  const errorRows = importResponse.value.errors.map((e) => ({
    RowIndex: String(e.rowIndex),
    Error: e.message,
    ...Object.fromEntries(
      Object.entries(e.data).map(([k, v]) => [k, v === null || v === undefined ? '' : String(v)])
    ),
  }))

  const headers = Object.keys(errorRows[0])
  const fields = headers.map((h) => ({
    name: h,
    type: 'String' as const,
    isRequired: false,
    isReadOnly: false,
    isComputed: false,
    annotations: {},
  }))

  exportToCsv({
    filename: generateFilename(`${props.entityType}_import_errors`),
    fields,
    data: errorRows,
  })
}

// Also include client-side invalid rows in downloadable errors
function downloadValidationErrors() {
  if (!importResult.value || importResult.value.invalidRows.length === 0) return

  const errorRows = importResult.value.invalidRows.map((r) => ({
    RowIndex: String(r.rowIndex),
    Errors: Object.entries(r.errors).map(([k, v]) => `${k}: ${v}`).join('; '),
    ...r.data,
  }))

  const headers = Object.keys(errorRows[0])
  const fields = headers.map((h) => ({
    name: h,
    type: 'String' as const,
    isRequired: false,
    isReadOnly: false,
    isComputed: false,
    annotations: {},
  }))

  exportToCsv({
    filename: generateFilename(`${props.entityType}_validation_errors`),
    fields,
    data: errorRows,
  })
}

// ---------------------------------------------------------------------------
// Navigation / Reset
// ---------------------------------------------------------------------------

function reset() {
  parseResult.value = null
  columnMappings.value = []
  importResult.value = null
  importResponse.value = null
  isImporting.value = false
  importProgress.value = 0
  uploadError.value = null
  currentStepIndex.value = 0
  wizardKey.value++ // Force Wizard remount
}

function handleClose() {
  reset()
  emit('update:open', false)
  emit('close')
}
</script>

<template>
  <DialogRoot :open="open" @update:open="(v) => { if (!v) handleClose() }">
    <DialogPortal>
      <DialogOverlay
        class="fixed inset-0 z-50 bg-black/50 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0"
      />
      <DialogContent
        class="fixed left-1/2 top-1/2 z-50 w-full max-w-3xl max-h-[85vh] -translate-x-1/2 -translate-y-1/2 rounded-lg border bg-background shadow-lg duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[state=closed]:slide-out-to-left-1/2 data-[state=closed]:slide-out-to-top-[48%] data-[state=open]:slide-in-from-left-1/2 data-[state=open]:slide-in-from-top-[48%] flex flex-col"
      >
        <!-- Header -->
        <div class="flex items-center justify-between p-6 pb-4 border-b">
          <div>
            <DialogTitle class="text-lg font-semibold text-foreground">
              Import {{ entityType }}
            </DialogTitle>
            <DialogDescription class="text-sm text-muted-foreground mt-1">
              {{ wizardDescription }}
            </DialogDescription>
          </div>
          <DialogClose as-child>
            <Button variant="ghost" size="icon" @click="handleClose">
              <X class="h-4 w-4" />
            </Button>
          </DialogClose>
        </div>

        <!-- Content area (scrollable) -->
        <div class="flex-1 overflow-y-auto p-6">

          <!-- Import in progress or completed: show result panel, hide wizard -->
          <div v-if="isImporting || importResponse">
            <div v-if="isImporting" class="space-y-4">
              <div class="flex items-center gap-3">
                <Spinner />
                <span class="text-sm">Importing records...</span>
              </div>
              <!-- Progress bar -->
              <div class="w-full bg-muted rounded-full h-2">
                <div
                  class="bg-primary h-2 rounded-full transition-all duration-300"
                  :style="{ width: `${importProgress}%` }"
                />
              </div>
              <p class="text-xs text-muted-foreground text-center">{{ importProgress }}%</p>
            </div>

            <div v-else-if="importResponse" class="space-y-4">
              <!-- Success summary -->
              <Alert v-if="importResponse.successCount > 0">
                <Check class="h-4 w-4" />
                <AlertTitle>Import Complete</AlertTitle>
                <AlertDescription>
                  Successfully imported {{ importResponse.successCount }} of
                  {{ importResponse.successCount + importResponse.errorCount }} records.
                </AlertDescription>
              </Alert>

              <!-- Error summary -->
              <Alert v-if="importResponse.errorCount > 0" variant="destructive">
                <AlertCircle class="h-4 w-4" />
                <AlertTitle>{{ importResponse.errorCount }} Failed</AlertTitle>
                <AlertDescription>
                  <div class="space-y-1 mt-2 max-h-[150px] overflow-auto">
                    <div
                      v-for="(err, i) in importResponse.errors.slice(0, 10)"
                      :key="i"
                      class="text-xs"
                    >
                      Row {{ err.rowIndex }}: {{ err.message }}
                    </div>
                    <div v-if="importResponse.errors.length > 10" class="text-xs">
                      ...and {{ importResponse.errors.length - 10 }} more errors
                    </div>
                  </div>
                  <Button variant="outline" size="sm" class="mt-3" @click="downloadErrors">
                    <Download class="mr-1.5 h-3.5 w-3.5" />
                    Download Errors CSV
                  </Button>
                </AlertDescription>
              </Alert>

              <div class="flex justify-end gap-2 pt-2">
                <Button variant="outline" size="sm" @click="reset">
                  Import Another File
                </Button>
                <DialogClose as-child>
                  <Button variant="outline" size="sm" @click="handleClose">
                    Close
                  </Button>
                </DialogClose>
              </div>
            </div>
          </div>

          <!-- Wizard: shown while no import is running/completed -->
          <Wizard
            v-else
            :key="wizardKey"
            :steps="wizardSteps"
            show-progress-bar
            class="h-full"
            @complete="executeImport"
            @step-change="(i) => (currentStepIndex = i)"
          >
            <!-- Step 1: Upload -->
            <template #step-upload>
              <div>
                <div
                  class="border-2 border-dashed rounded-lg p-12 text-center transition-colors cursor-pointer"
                  :class="isDragOver ? 'border-primary bg-primary/5' : 'border-muted-foreground/25 hover:border-muted-foreground/50'"
                  @dragover="handleDragOver"
                  @dragleave="handleDragLeave"
                  @drop="handleDrop"
                  @click="($refs.fileInput as HTMLInputElement)?.click()"
                >
                  <Upload class="h-10 w-10 mx-auto text-muted-foreground mb-4" />
                  <p class="text-sm font-medium">
                    Drop a CSV file here or click to browse
                  </p>
                  <p class="text-xs text-muted-foreground mt-2">
                    Supports .csv files with a header row
                  </p>
                  <input
                    ref="fileInput"
                    type="file"
                    accept=".csv,.tsv,.txt"
                    class="hidden"
                    @change="handleFileSelect"
                  />
                </div>

                <Alert v-if="uploadError" variant="destructive" class="mt-4">
                  <AlertCircle class="h-4 w-4" />
                  <AlertTitle>Upload Error</AlertTitle>
                  <AlertDescription>{{ uploadError }}</AlertDescription>
                </Alert>
              </div>
            </template>

            <!-- Step 2: Column Mapping -->
            <template #step-mapping>
              <div>
                <div class="mb-4 flex items-center justify-between">
                  <p class="text-sm text-muted-foreground">
                    {{ parseResult?.rows.length }} rows found.
                    {{ mappedCount }} of {{ columnMappings.length }} columns mapped.
                  </p>
                </div>

                <div class="border rounded-md overflow-hidden">
                  <table class="w-full text-sm">
                    <thead>
                      <tr class="bg-muted/50 border-b">
                        <th class="w-8 px-2 py-2" />
                        <th class="text-left px-3 py-2 font-medium">CSV Column</th>
                        <th class="text-left px-3 py-2 font-medium">Entity Field</th>
                        <th class="text-left px-3 py-2 font-medium">Sample Value</th>
                      </tr>
                    </thead>
                    <tbody ref="mappingTableBody">
                      <tr
                        v-for="(mapping, index) in columnMappings"
                        :key="mapping.csvHeader"
                        class="border-b last:border-b-0 border-l-2 transition-colors"
                        :class="
                          mapping.auto
                            ? 'border-l-green-500'
                            : mapping.fieldName
                              ? 'border-l-blue-500'
                              : 'border-l-transparent'
                        "
                      >
                        <td class="px-2 py-2 cursor-grab drag-handle">
                          <GripVertical class="h-4 w-4 text-muted-foreground" />
                        </td>
                        <td class="px-3 py-2 font-mono text-xs">
                          {{ mapping.csvHeader }}
                          <Badge v-if="mapping.auto" variant="secondary" class="ml-1 text-[10px]">auto</Badge>
                        </td>
                        <td class="px-3 py-2">
                          <Select
                            :modelValue="mapping.fieldName ?? ''"
                            @update:modelValue="(v) => updateMapping(index, String(v))"
                            class="h-8 text-xs"
                          >
                            <option value="">-- Skip --</option>
                            <option
                              v-for="field in importableFields"
                              :key="field.name"
                              :value="field.name"
                            >
                              {{ field.displayName || field.name }} ({{ field.type }})
                            </option>
                          </Select>
                        </td>
                        <td class="px-3 py-2 text-xs text-muted-foreground truncate max-w-[200px]">
                          {{ parseResult?.rows[0]?.[mapping.csvHeader] ?? '' }}
                        </td>
                      </tr>
                    </tbody>
                  </table>
                </div>

                <Alert v-if="parseResult && parseResult.errors.length > 0" class="mt-4">
                  <AlertCircle class="h-4 w-4" />
                  <AlertTitle>Parse Warnings</AlertTitle>
                  <AlertDescription>
                    <ul class="list-disc list-inside text-xs mt-1">
                      <li v-for="(err, i) in parseResult.errors.slice(0, 5)" :key="i">{{ err }}</li>
                      <li v-if="parseResult.errors.length > 5">
                        ...and {{ parseResult.errors.length - 5 }} more
                      </li>
                    </ul>
                  </AlertDescription>
                </Alert>
              </div>
            </template>

            <!-- Step 3: Preview -->
            <template #step-preview>
              <div>
                <div class="mb-4 flex items-center gap-4">
                  <Badge variant="default">
                    {{ importResult?.validRows.length ?? 0 }} valid
                  </Badge>
                  <Badge v-if="importResult?.invalidRows.length" variant="destructive">
                    {{ importResult.invalidRows.length }} invalid
                  </Badge>
                  <span class="text-sm text-muted-foreground">
                    of {{ importResult?.totalRows ?? 0 }} total rows
                  </span>
                </div>

                <!-- Preview table -->
                <div class="border rounded-md overflow-auto max-h-[300px]">
                  <table class="w-full text-xs">
                    <thead class="sticky top-0">
                      <tr class="bg-muted/50 border-b">
                        <th class="text-left px-2 py-1.5 font-medium w-10">Row</th>
                        <th class="text-left px-2 py-1.5 font-medium w-10">Status</th>
                        <th
                          v-for="mapping in columnMappings.filter(m => m.fieldName)"
                          :key="mapping.csvHeader"
                          class="text-left px-2 py-1.5 font-medium"
                        >
                          {{ mapping.field?.displayName || mapping.fieldName }}
                        </th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr
                        v-for="row in previewRows"
                        :key="row.index"
                        class="border-b last:border-b-0"
                        :class="row.valid ? '' : 'bg-destructive/5'"
                      >
                        <td class="px-2 py-1.5 text-muted-foreground">{{ row.index }}</td>
                        <td class="px-2 py-1.5">
                          <Check v-if="row.valid" class="h-3.5 w-3.5 text-green-600" />
                          <AlertCircle v-else class="h-3.5 w-3.5 text-destructive" />
                        </td>
                        <td
                          v-for="mapping in columnMappings.filter(m => m.fieldName)"
                          :key="mapping.csvHeader"
                          class="px-2 py-1.5 truncate max-w-[150px]"
                          :title="row.errors[mapping.fieldName!] || ''"
                        >
                          <span
                            :class="row.errors[mapping.fieldName!] ? 'text-destructive' : ''"
                          >
                            {{ row.data[mapping.fieldName!] ?? '' }}
                          </span>
                        </td>
                      </tr>
                    </tbody>
                  </table>
                </div>

                <!-- Validation errors -->
                <div v-if="importResult && importResult.invalidRows.length > 0" class="mt-4">
                  <div class="flex items-center justify-between mb-2">
                    <p class="text-sm font-medium text-destructive">
                      {{ importResult.invalidRows.length }} rows have validation errors
                    </p>
                    <Button variant="outline" size="sm" @click="downloadValidationErrors">
                      <Download class="mr-1.5 h-3.5 w-3.5" />
                      Download Errors CSV
                    </Button>
                  </div>
                  <div class="space-y-2 max-h-[120px] overflow-auto">
                    <div
                      v-for="row in importResult.invalidRows.slice(0, 5)"
                      :key="row.rowIndex"
                      class="text-xs bg-destructive/5 rounded px-3 py-2"
                    >
                      <span class="font-medium">Row {{ row.rowIndex }}:</span>
                      {{ Object.entries(row.errors).map(([k, v]) => `${k}: ${v}`).join(', ') }}
                    </div>
                  </div>
                </div>
              </div>
            </template>

            <!-- Step 4: result slot (not rendered — import triggers on @complete) -->
            <template #step-result>
              <div />
            </template>
          </Wizard>
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
