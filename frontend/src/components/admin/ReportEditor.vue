<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { Checkbox } from '@/components/ui/checkbox'
import { Select } from '@/components/ui/select'
import { Spinner } from '@/components/ui/spinner'
import { GripVertical, X, Plus, Trash2 } from 'lucide-vue-next'
import type { FieldMetadata } from '@/types/metadata'
import type { ReportTemplate, CreateReportRequest, ReportField, SortConfig } from '@/services/reportService'

// ─── Props / Emits ────────────────────────────────────────────────────────────

const props = defineProps<{
  template: CreateReportRequest | ReportTemplate
  availableFields: FieldMetadata[]
  loading?: boolean
}>()

const emit = defineEmits<{
  'update:template': [patch: Partial<CreateReportRequest | ReportTemplate>]
  save: []
  cancel: []
}>()

// ─── Local reactive copy ───────────────────────────────────────────────────────

// We work on a shallow-reactive local copy and emit patches on every change.
const local = ref<CreateReportRequest | ReportTemplate>({ ...props.template, fields: [...props.template.fields], sortBy: [...props.template.sortBy], scheduleRecipients: [...props.template.scheduleRecipients] })

watch(
  () => props.template,
  (t) => {
    local.value = { ...t, fields: [...t.fields], sortBy: [...t.sortBy], scheduleRecipients: [...t.scheduleRecipients] }
  }
)

function patch(partial: Partial<CreateReportRequest | ReportTemplate>) {
  local.value = { ...local.value, ...partial } as CreateReportRequest | ReportTemplate
  emit('update:template', partial)
}

// ─── Layout type ──────────────────────────────────────────────────────────────

const layoutTypes = [
  { value: 'list', label: 'List' },
  { value: 'detail', label: 'Detail' },
  { value: 'summary', label: 'Summary' },
] as const

const showGroupingSection = computed(
  () => local.value.layoutType === 'list' || local.value.layoutType === 'summary'
)

// ─── Field selection ──────────────────────────────────────────────────────────

const selectedFieldNames = computed(() => new Set(local.value.fields.map((f) => f.name)))

function isFieldSelected(fieldName: string): boolean {
  return selectedFieldNames.value.has(fieldName)
}

function addField(meta: FieldMetadata) {
  if (isFieldSelected(meta.name)) return
  const newField: ReportField = {
    name: meta.name,
    label: meta.displayName ?? meta.name,
    width: 150,
  }
  patch({ fields: [...local.value.fields, newField] })
}

function removeField(index: number) {
  const updated = local.value.fields.filter((_, i) => i !== index)
  patch({ fields: updated })
}

function updateField(index: number, changes: Partial<ReportField>) {
  const updated = local.value.fields.map((f, i) => (i === index ? { ...f, ...changes } : f))
  patch({ fields: updated })
}

// ─── Drag-to-reorder (no external dep) ────────────────────────────────────────

const draggingIndex = ref<number | null>(null)
const dragOverIndex = ref<number | null>(null)

function onDragStart(index: number) {
  draggingIndex.value = index
}

function onDragEnter(index: number) {
  dragOverIndex.value = index
}

function onDragEnd() {
  const from = draggingIndex.value
  const to = dragOverIndex.value
  if (from !== null && to !== null && from !== to) {
    const updated = [...local.value.fields]
    const [item] = updated.splice(from, 1)
    updated.splice(to, 0, item)
    patch({ fields: updated })
  }
  draggingIndex.value = null
  dragOverIndex.value = null
}

// ─── Grouping & Sorting ────────────────────────────────────────────────────────

function addSortCriteria() {
  const firstField = local.value.fields[0]?.name ?? ''
  patch({ sortBy: [...local.value.sortBy, { field: firstField, direction: 'asc' }] })
}

function removeSortCriteria(index: number) {
  patch({ sortBy: local.value.sortBy.filter((_, i) => i !== index) })
}

function updateSort(index: number, changes: Partial<SortConfig>) {
  const updated = local.value.sortBy.map((s, i) => (i === index ? { ...s, ...changes } : s))
  patch({ sortBy: updated })
}

// ─── Recipients (tag input) ────────────────────────────────────────────────────

const recipientInput = ref('')

function addRecipient() {
  const raw = recipientInput.value.trim().replace(/,$/, '').trim()
  if (raw && !local.value.scheduleRecipients.includes(raw)) {
    patch({ scheduleRecipients: [...local.value.scheduleRecipients, raw] })
  }
  recipientInput.value = ''
}

function removeRecipient(index: number) {
  patch({ scheduleRecipients: local.value.scheduleRecipients.filter((_, i) => i !== index) })
}

// ─── Format / aggregate options ───────────────────────────────────────────────

const formatOptions = [
  { value: '', label: 'None' },
  { value: 'date', label: 'Date' },
  { value: 'datetime', label: 'DateTime' },
  { value: 'currency', label: 'Currency' },
  { value: 'percent', label: 'Percent' },
]

const aggregateOptions = [
  { value: '', label: 'None' },
  { value: 'sum', label: 'Sum' },
  { value: 'count', label: 'Count' },
  { value: 'avg', label: 'Avg' },
  { value: 'min', label: 'Min' },
  { value: 'max', label: 'Max' },
]
</script>

<template>
  <div class="space-y-6">
    <!-- ── Section 1: Basic Info ─────────────────────────────────────────── -->
    <Card>
      <CardHeader>
        <CardTitle class="text-base">Basic Info</CardTitle>
      </CardHeader>
      <CardContent class="space-y-4">
        <!-- Name -->
        <div class="space-y-2">
          <Label for="report-name" class="text-sm font-medium">
            Template Name <span class="text-destructive">*</span>
          </Label>
          <Input
            id="report-name"
            :model-value="local.name"
            placeholder="Enter template name"
            :disabled="loading"
            @update:model-value="(v) => patch({ name: String(v) })"
          />
        </div>

        <!-- Description -->
        <div class="space-y-2">
          <Label for="report-desc" class="text-sm font-medium">Description</Label>
          <Textarea
            id="report-desc"
            :model-value="local.description ?? ''"
            placeholder="Optional description"
            :rows="2"
            :disabled="loading"
            @update:model-value="(v) => patch({ description: v || undefined })"
          />
        </div>

        <!-- Layout type -->
        <div class="space-y-2">
          <Label class="text-sm font-medium">Layout Type</Label>
          <div class="flex gap-1 rounded-lg border border-input bg-muted p-1 w-fit">
            <button
              v-for="lt in layoutTypes"
              :key="lt.value"
              type="button"
              class="px-4 py-1.5 text-sm rounded-md transition-colors focus:outline-none focus:ring-2 focus:ring-ring"
              :class="
                local.layoutType === lt.value
                  ? 'bg-background text-foreground shadow-sm font-medium'
                  : 'text-muted-foreground hover:text-foreground'
              "
              :disabled="loading"
              @click="patch({ layoutType: lt.value })"
            >
              {{ lt.label }}
            </button>
          </div>
        </div>
      </CardContent>
    </Card>

    <!-- ── Section 2: Field Selection ───────────────────────────────────── -->
    <Card>
      <CardHeader>
        <CardTitle class="text-base">Field Selection</CardTitle>
      </CardHeader>
      <CardContent>
        <div class="grid grid-cols-2 gap-4">
          <!-- Available fields (left) -->
          <div>
            <p class="text-xs font-medium text-muted-foreground uppercase tracking-wide mb-2">
              Available Fields
            </p>
            <div class="border rounded-md divide-y divide-border max-h-72 overflow-y-auto">
              <div
                v-for="meta in availableFields"
                :key="meta.name"
                class="flex items-center gap-2 px-3 py-2 hover:bg-muted/50 cursor-pointer select-none"
                :class="isFieldSelected(meta.name) ? 'opacity-40 cursor-default' : ''"
                @click="addField(meta)"
              >
                <Checkbox
                  :model-value="isFieldSelected(meta.name)"
                  :disabled="isFieldSelected(meta.name) || loading"
                  @update:model-value="() => addField(meta)"
                />
                <span class="text-sm">{{ meta.displayName ?? meta.name }}</span>
                <span class="ml-auto text-xs text-muted-foreground">{{ meta.type }}</span>
              </div>
              <div v-if="availableFields.length === 0" class="px-3 py-4 text-sm text-muted-foreground text-center">
                No fields available
              </div>
            </div>
          </div>

          <!-- Selected fields (right) — drag to reorder -->
          <div>
            <p class="text-xs font-medium text-muted-foreground uppercase tracking-wide mb-2">
              Selected Fields (drag to reorder)
            </p>
            <div class="border rounded-md divide-y divide-border max-h-72 overflow-y-auto">
              <div
                v-for="(field, idx) in local.fields"
                :key="field.name"
                draggable="true"
                class="group flex items-start gap-2 px-2 py-2 hover:bg-muted/30 transition-colors"
                :class="dragOverIndex === idx ? 'bg-blue-50 dark:bg-blue-900/20' : ''"
                @dragstart="onDragStart(idx)"
                @dragenter.prevent="onDragEnter(idx)"
                @dragover.prevent
                @dragend="onDragEnd"
              >
                <!-- Drag handle -->
                <div class="cursor-grab active:cursor-grabbing mt-1 text-muted-foreground">
                  <GripVertical class="h-4 w-4" />
                </div>

                <!-- Field config -->
                <div class="flex-1 grid grid-cols-2 gap-x-2 gap-y-1 min-w-0">
                  <!-- Field name display -->
                  <span class="col-span-2 text-xs font-medium text-foreground truncate">{{ field.name }}</span>

                  <!-- Label override -->
                  <div class="col-span-2 space-y-0.5">
                    <Label :for="`field-label-${idx}`" class="text-xs text-muted-foreground">Label</Label>
                    <Input
                      :id="`field-label-${idx}`"
                      :model-value="field.label"
                      class="h-7 text-xs"
                      :disabled="loading"
                      @update:model-value="(v) => updateField(idx, { label: String(v) })"
                    />
                  </div>

                  <!-- Width -->
                  <div class="space-y-0.5">
                    <Label :for="`field-width-${idx}`" class="text-xs text-muted-foreground">Width</Label>
                    <Input
                      :id="`field-width-${idx}`"
                      type="number"
                      :model-value="field.width"
                      min="50"
                      max="500"
                      class="h-7 text-xs"
                      :disabled="loading"
                      @update:model-value="(v) => updateField(idx, { width: Number(v) })"
                    />
                  </div>

                  <!-- Format -->
                  <div class="space-y-0.5">
                    <Label :for="`field-format-${idx}`" class="text-xs text-muted-foreground">Format</Label>
                    <Select
                      :id="`field-format-${idx}`"
                      :model-value="field.format ?? ''"
                      class="h-7 text-xs"
                      :disabled="loading"
                      @update:model-value="(v) => updateField(idx, { format: String(v) || undefined })"
                    >
                      <option v-for="opt in formatOptions" :key="opt.value" :value="opt.value">
                        {{ opt.label }}
                      </option>
                    </Select>
                  </div>

                  <!-- Aggregate (list / summary only) -->
                  <div v-if="showGroupingSection" class="col-span-2 space-y-0.5">
                    <Label :for="`field-agg-${idx}`" class="text-xs text-muted-foreground">Aggregate</Label>
                    <Select
                      :id="`field-agg-${idx}`"
                      :model-value="field.aggregate ?? ''"
                      class="h-7 text-xs"
                      :disabled="loading"
                      @update:model-value="(v) => updateField(idx, { aggregate: String(v) || undefined })"
                    >
                      <option v-for="opt in aggregateOptions" :key="opt.value" :value="opt.value">
                        {{ opt.label }}
                      </option>
                    </Select>
                  </div>
                </div>

                <!-- Remove -->
                <button
                  type="button"
                  class="mt-1 text-muted-foreground hover:text-destructive focus:outline-none"
                  title="Remove field"
                  :disabled="loading"
                  @click="removeField(idx)"
                >
                  <X class="h-4 w-4" />
                </button>
              </div>

              <div v-if="local.fields.length === 0" class="px-3 py-4 text-sm text-muted-foreground text-center">
                No fields selected. Click fields on the left to add them.
              </div>
            </div>
          </div>
        </div>
      </CardContent>
    </Card>

    <!-- ── Section 3: Grouping & Sorting (list / summary only) ──────────── -->
    <Card v-if="showGroupingSection">
      <CardHeader>
        <CardTitle class="text-base">Grouping &amp; Sorting</CardTitle>
      </CardHeader>
      <CardContent class="space-y-4">
        <!-- Group By -->
        <div class="space-y-2">
          <Label for="group-by" class="text-sm font-medium">Group By</Label>
          <Select
            id="group-by"
            :model-value="local.groupBy ?? ''"
            :disabled="loading"
            @update:model-value="(v) => patch({ groupBy: String(v) || undefined })"
          >
            <option value="">None</option>
            <option v-for="f in local.fields" :key="f.name" :value="f.name">
              {{ f.label }}
            </option>
          </Select>
        </div>

        <!-- Sort By -->
        <div class="space-y-2">
          <div class="flex items-center justify-between">
            <Label class="text-sm font-medium">Sort By</Label>
            <Button
              type="button"
              variant="outline"
              size="sm"
              :disabled="loading || local.fields.length === 0"
              @click="addSortCriteria"
            >
              <Plus class="h-3.5 w-3.5 mr-1" />
              Add
            </Button>
          </div>

          <div class="space-y-2">
            <div
              v-for="(sort, idx) in local.sortBy"
              :key="idx"
              class="flex items-center gap-2"
            >
              <Select
                :model-value="sort.field"
                :disabled="loading"
                class="flex-1"
                @update:model-value="(v) => updateSort(idx, { field: String(v) })"
              >
                <option v-for="f in local.fields" :key="f.name" :value="f.name">
                  {{ f.label }}
                </option>
              </Select>

              <!-- Asc / Desc toggle -->
              <div class="flex rounded-md border border-input overflow-hidden">
                <button
                  type="button"
                  class="px-3 py-1.5 text-xs transition-colors"
                  :class="sort.direction === 'asc' ? 'bg-primary text-primary-foreground' : 'bg-background text-muted-foreground hover:bg-muted'"
                  :disabled="loading"
                  @click="updateSort(idx, { direction: 'asc' })"
                >
                  Asc
                </button>
                <button
                  type="button"
                  class="px-3 py-1.5 text-xs transition-colors"
                  :class="sort.direction === 'desc' ? 'bg-primary text-primary-foreground' : 'bg-background text-muted-foreground hover:bg-muted'"
                  :disabled="loading"
                  @click="updateSort(idx, { direction: 'desc' })"
                >
                  Desc
                </button>
              </div>

              <button
                type="button"
                class="text-muted-foreground hover:text-destructive focus:outline-none"
                :disabled="loading"
                @click="removeSortCriteria(idx)"
              >
                <Trash2 class="h-4 w-4" />
              </button>
            </div>

            <p v-if="local.sortBy.length === 0" class="text-sm text-muted-foreground">
              No sort criteria defined.
            </p>
          </div>
        </div>
      </CardContent>
    </Card>

    <!-- ── Section 4: Header & Footer ───────────────────────────────────── -->
    <Card>
      <CardHeader>
        <CardTitle class="text-base">Header &amp; Footer</CardTitle>
      </CardHeader>
      <CardContent class="space-y-4">
        <div class="space-y-2">
          <Label for="report-header" class="text-sm font-medium">Header</Label>
          <Textarea
            id="report-header"
            :model-value="local.header ?? ''"
            placeholder="Text displayed at the top of the report"
            :rows="2"
            :disabled="loading"
            @update:model-value="(v) => patch({ header: v || undefined })"
          />
        </div>
        <div class="space-y-2">
          <Label for="report-footer" class="text-sm font-medium">Footer</Label>
          <Textarea
            id="report-footer"
            :model-value="local.footer ?? ''"
            placeholder="Text displayed at the bottom of the report"
            :rows="2"
            :disabled="loading"
            @update:model-value="(v) => patch({ footer: v || undefined })"
          />
        </div>
      </CardContent>
    </Card>

    <!-- ── Section 5: Schedule & Share ──────────────────────────────────── -->
    <Card>
      <CardHeader>
        <CardTitle class="text-base">Schedule &amp; Share</CardTitle>
      </CardHeader>
      <CardContent class="space-y-4">
        <!-- Cron -->
        <div class="space-y-2">
          <Label for="schedule-cron" class="text-sm font-medium">Schedule (cron)</Label>
          <Input
            id="schedule-cron"
            :model-value="local.scheduleCron ?? ''"
            placeholder="e.g. 0 8 * * 1"
            :disabled="loading"
            @update:model-value="(v) => patch({ scheduleCron: String(v) || undefined })"
          />
          <p class="text-xs text-muted-foreground">
            e.g. <code class="font-mono bg-muted px-1 rounded">0 8 * * 1</code> = every Monday at 8am
          </p>
        </div>

        <!-- Recipients tag input -->
        <div class="space-y-2">
          <Label class="text-sm font-medium">Recipients</Label>
          <div class="rounded-md border border-input bg-background p-2">
            <div v-if="local.scheduleRecipients.length > 0" class="flex flex-wrap gap-1 mb-1">
              <span
                v-for="(email, i) in local.scheduleRecipients"
                :key="i"
                class="inline-flex items-center gap-1 px-2 py-0.5 bg-blue-100 dark:bg-blue-900/40 text-blue-800 dark:text-blue-200 text-xs rounded-full"
              >
                {{ email }}
                <button
                  type="button"
                  class="hover:text-red-500 focus:outline-none leading-none"
                  :aria-label="`Remove ${email}`"
                  @click="removeRecipient(i)"
                >
                  &times;
                </button>
              </span>
            </div>
            <input
              v-model="recipientInput"
              type="email"
              placeholder="email@example.com — press Enter or comma to add"
              :disabled="loading"
              class="w-full border-0 bg-transparent text-sm text-gray-900 dark:text-gray-100 placeholder:text-muted-foreground focus:outline-none focus:ring-0 p-0"
              @keydown.enter.prevent="addRecipient"
              @keydown.188.prevent="addRecipient"
            />
          </div>
        </div>

        <!-- Is Public toggle -->
        <div class="flex items-center justify-between rounded-lg border p-3 bg-muted/30">
          <div>
            <p class="text-sm font-medium">Public / Shareable</p>
            <p class="text-xs text-muted-foreground">
              Allow this report to be accessed via a public share link
            </p>
          </div>
          <button
            type="button"
            role="switch"
            :aria-checked="local.isPublic"
            :disabled="loading"
            class="relative inline-flex h-6 w-11 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
            :class="local.isPublic ? 'bg-primary' : 'bg-input'"
            @click="patch({ isPublic: !local.isPublic })"
          >
            <span
              class="pointer-events-none inline-block h-5 w-5 rounded-full bg-background shadow-lg ring-0 transition-transform duration-200"
              :class="local.isPublic ? 'translate-x-5' : 'translate-x-0'"
            />
          </button>
        </div>
      </CardContent>
    </Card>

    <!-- ── Save / Cancel ──────────────────────────────────────────────────── -->
    <div class="flex justify-end gap-3 pt-2 border-t">
      <Button variant="outline" type="button" :disabled="loading" @click="emit('cancel')">
        Cancel
      </Button>
      <Button type="button" :disabled="loading || !local.name.trim()" @click="emit('save')">
        <Spinner v-if="loading" size="sm" class="mr-2" />
        Save Template
      </Button>
    </div>
  </div>
</template>
