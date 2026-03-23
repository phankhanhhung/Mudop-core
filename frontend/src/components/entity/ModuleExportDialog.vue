<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import {
  DialogRoot,
  DialogPortal,
  DialogOverlay,
  DialogContent,
  DialogTitle,
  DialogDescription,
  DialogClose,
} from 'radix-vue'
import { Button } from '@/components/ui/button'
import Wizard from '@/components/smart/Wizard.vue'
import type { WizardStep } from '@/composables/useWizard'
import { X, Package, Download, Loader2 } from 'lucide-vue-next'
import { metadataService } from '@/services/metadataService'
import { odataService } from '@/services/odataService'
import type { EntityMetadata } from '@/types/metadata'
import {
  buildCsvContent,
  buildSchemaCsv,
  packageAsZip,
  downloadBlob,
  generateFilename,
} from '@/utils/moduleExport'

interface Props {
  open: boolean
  module: string
}

const props = defineProps<Props>()
const emit = defineEmits<{ close: []; 'update:open': [value: boolean] }>()

// ── Entity list state ────────────────────────────────────────────────────────
const entities = ref<EntityMetadata[]>([])
const selectedEntities = ref<Set<string>>(new Set())
const loadingEntities = ref(false)

// ── Export options ───────────────────────────────────────────────────────────
const maxRows = ref(5000)
const includeSchema = ref(true)

// ── Wizard reset key ─────────────────────────────────────────────────────────
const wizardKey = ref(0)

// ── Export progress state ────────────────────────────────────────────────────
type ExportPhase = 'idle' | 'running' | 'done' | 'error'
const exportPhase = ref<ExportPhase>('idle')
const exportProgress = ref(0)
const exportCurrentEntity = ref('')
const exportRowCount = ref(0)
const exportError = ref<string | null>(null)
const exportedBlob = ref<Blob | null>(null)
const exportFilename = ref('')

// ── Load entities when dialog opens ─────────────────────────────────────────
watch(
  () => props.open,
  async (open) => {
    if (open) {
      loadingEntities.value = true
      try {
        const result = await metadataService.getEntities(props.module)
        // Filter out abstract entities
        entities.value = result.filter((e) => !e.isAbstract)
        // Select all by default
        selectedEntities.value = new Set(entities.value.map((e) => e.name))
      } catch {
        entities.value = []
      } finally {
        loadingEntities.value = false
      }
    }
  },
)

const selectedCount = computed(() => selectedEntities.value.size)

function toggleEntity(name: string): void {
  const s = new Set(selectedEntities.value)
  if (s.has(name)) {
    s.delete(name)
  } else {
    s.add(name)
  }
  selectedEntities.value = s
}

function toggleAll(): void {
  if (selectedCount.value === entities.value.length) {
    selectedEntities.value = new Set()
  } else {
    selectedEntities.value = new Set(entities.value.map((e) => e.name))
  }
}

// ── Wizard steps ─────────────────────────────────────────────────────────────
const wizardSteps = computed<WizardStep[]>(() => [
  {
    key: 'select',
    title: 'Select Entities',
    subtitle: `${selectedCount.value} selected`,
    validate: () => selectedCount.value > 0,
  },
  {
    key: 'options',
    title: 'Export Options',
  },
  {
    key: 'export',
    title: 'Export',
    subtitle: exportPhase.value === 'done' ? 'Ready to download' : undefined,
  },
])

// ── Export logic ─────────────────────────────────────────────────────────────
async function runExport(): Promise<void> {
  exportPhase.value = 'running'
  exportProgress.value = 0
  exportError.value = null
  exportRowCount.value = 0

  // Plain array — no .value needed
  const entitiesToExport = entities.value.filter((e) => selectedEntities.value.has(e.name))
  const results: EntityExportResult[] = []

  try {
    for (let i = 0; i < entitiesToExport.length; i++) {
      const entity = entitiesToExport[i]
      exportCurrentEntity.value = entity.name
      exportProgress.value = Math.round((i / entitiesToExport.length) * 80)

      const response = await odataService.query<Record<string, unknown>>(
        props.module,
        entity.name,
        { $top: maxRows.value },
        { skipCache: true },
      )

      const rows = response.value
      exportRowCount.value += rows.length

      const csvContent = buildCsvContent(entity.fields ?? [], rows)
      results.push({ entityType: entity.name, rowCount: rows.length, csvContent })
    }

    exportProgress.value = 90
    exportCurrentEntity.value = 'Packaging...'

    // Only include schema content when the option is enabled
    const schemaContent = includeSchema.value ? buildSchemaCsv(entitiesToExport) : ''

    const blob = await packageAsZip(props.module, schemaContent, results)
    exportedBlob.value = blob
    exportFilename.value = generateFilename(`${props.module}_export`, 'zip')
    exportProgress.value = 100
    exportPhase.value = 'done'
  } catch (err) {
    exportError.value = err instanceof Error ? err.message : 'Export failed'
    exportPhase.value = 'error'
  }
}

// Local type alias to avoid import cycle with moduleExport (used only in runExport)
interface EntityExportResult {
  entityType: string
  rowCount: number
  csvContent: string
}

function downloadZip(): void {
  if (exportedBlob.value) {
    downloadBlob(exportedBlob.value, exportFilename.value)
  }
}

function handleClose(): void {
  exportPhase.value = 'idle'
  exportProgress.value = 0
  exportedBlob.value = null
  exportError.value = null
  wizardKey.value++
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
        class="fixed left-1/2 top-1/2 z-50 w-full max-w-2xl max-h-[80vh] -translate-x-1/2 -translate-y-1/2 rounded-lg border bg-background shadow-lg duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 flex flex-col"
      >
        <!-- Header -->
        <div class="flex items-center justify-between p-6 pb-4 border-b">
          <div class="flex items-center gap-2">
            <Package class="h-5 w-5 text-muted-foreground" />
            <div>
              <DialogTitle class="text-lg font-semibold">Export Module</DialogTitle>
              <DialogDescription class="text-sm text-muted-foreground">
                Export entities from
                <span class="font-medium">{{ module }}</span>
                as a ZIP archive
              </DialogDescription>
            </div>
          </div>
          <DialogClose as-child>
            <Button variant="ghost" size="icon" @click="handleClose">
              <X class="h-4 w-4" />
            </Button>
          </DialogClose>
        </div>

        <!-- Content -->
        <div class="flex-1 overflow-y-auto p-6">
          <!-- Loading state -->
          <div
            v-if="loadingEntities"
            class="flex items-center justify-center py-8 text-muted-foreground"
          >
            <Loader2 class="h-5 w-5 animate-spin mr-2" />
            Loading entities...
          </div>

          <Wizard
            v-else
            :key="wizardKey"
            :steps="wizardSteps"
            show-progress-bar
            @complete="runExport"
          >
            <!-- Step 1: Entity selection -->
            <template #step-select>
              <div class="space-y-3">
                <div class="flex items-center justify-between mb-2">
                  <p class="text-sm text-muted-foreground">
                    Select entities to include in the export
                  </p>
                  <Button variant="ghost" size="sm" @click="toggleAll">
                    {{ selectedCount === entities.length ? 'Deselect All' : 'Select All' }}
                  </Button>
                </div>

                <div v-if="entities.length > 0" class="border rounded-lg overflow-hidden divide-y">
                  <label
                    v-for="entity in entities"
                    :key="entity.name"
                    class="flex items-center gap-3 px-4 py-3 hover:bg-muted/50 cursor-pointer"
                  >
                    <input
                      type="checkbox"
                      :checked="selectedEntities.has(entity.name)"
                      class="h-4 w-4 rounded"
                      @change="toggleEntity(entity.name)"
                    />
                    <div class="flex-1 min-w-0">
                      <div class="text-sm font-medium">
                        {{ entity.displayName ?? entity.name }}
                      </div>
                      <div class="text-xs text-muted-foreground">
                        {{ (entity.fields ?? []).length }} fields
                      </div>
                    </div>
                  </label>
                </div>

                <p
                  v-else
                  class="text-sm text-muted-foreground text-center py-4"
                >
                  No entities found in module {{ module }}
                </p>
              </div>
            </template>

            <!-- Step 2: Options -->
            <template #step-options>
              <div class="space-y-6">
                <div>
                  <label class="text-sm font-medium block mb-1.5">Max rows per entity</label>
                  <input
                    v-model.number="maxRows"
                    type="number"
                    min="100"
                    max="100000"
                    step="1000"
                    class="w-48 border rounded-lg px-3 py-1.5 text-sm bg-background"
                  />
                  <p class="text-xs text-muted-foreground mt-1">
                    Maximum number of rows to export per entity (up to 100,000)
                  </p>
                </div>

                <div class="flex items-center gap-2">
                  <input
                    id="include-schema"
                    v-model="includeSchema"
                    type="checkbox"
                    class="h-4 w-4 rounded"
                  />
                  <label for="include-schema" class="text-sm">
                    Include schema summary (_schema.csv)
                  </label>
                </div>

                <div class="rounded-lg bg-muted/50 p-4 text-sm">
                  <p class="font-medium mb-1">Export summary</p>
                  <p class="text-muted-foreground">
                    {{ selectedCount }} {{ selectedCount === 1 ? 'entity' : 'entities' }} will be
                    exported as a ZIP archive
                  </p>
                  <p class="text-muted-foreground">Format: CSV (UTF-8 with BOM)</p>
                </div>
              </div>
            </template>

            <!-- Step 3: Export progress + result -->
            <template #step-export>
              <div class="space-y-4">
                <!-- Idle: waiting for user to click Complete -->
                <div
                  v-if="exportPhase === 'idle'"
                  class="text-center py-8 text-muted-foreground"
                >
                  <Package class="h-10 w-10 mx-auto mb-3 opacity-40" />
                  <p class="text-sm">Click "Complete" below to start the export</p>
                </div>

                <!-- Running -->
                <div v-else-if="exportPhase === 'running'" class="space-y-4">
                  <div class="flex items-center gap-2 text-sm text-muted-foreground">
                    <Loader2 class="h-4 w-4 animate-spin" />
                    Exporting {{ exportCurrentEntity }}...
                  </div>
                  <div class="w-full bg-muted rounded-full h-2">
                    <div
                      class="bg-primary h-2 rounded-full transition-all duration-300"
                      :style="{ width: exportProgress + '%' }"
                    />
                  </div>
                  <p class="text-xs text-center text-muted-foreground">
                    {{ exportProgress }}% &mdash; {{ exportRowCount }} rows fetched
                  </p>
                </div>

                <!-- Done -->
                <div v-else-if="exportPhase === 'done'" class="space-y-4">
                  <div class="text-center py-4">
                    <div
                      class="h-12 w-12 rounded-full bg-green-100 dark:bg-green-900/30 flex items-center justify-center mx-auto mb-3"
                    >
                      <Download class="h-6 w-6 text-green-600" />
                    </div>
                    <p class="font-medium text-sm">Export complete!</p>
                    <p class="text-xs text-muted-foreground mt-1">
                      {{ exportRowCount }} total rows across {{ selectedCount }}
                      {{ selectedCount === 1 ? 'entity' : 'entities' }}
                    </p>
                  </div>
                  <div class="flex justify-center">
                    <Button @click="downloadZip">
                      <Download class="mr-2 h-4 w-4" />
                      Download {{ exportFilename }}
                    </Button>
                  </div>
                </div>

                <!-- Error -->
                <div
                  v-else-if="exportPhase === 'error'"
                  class="rounded-lg bg-destructive/10 border border-destructive/30 p-4 text-destructive text-sm"
                >
                  {{ exportError }}
                </div>
              </div>
            </template>
          </Wizard>
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
