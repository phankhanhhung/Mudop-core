<script setup lang="ts">
import { computed } from 'vue'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import { AlertCircle, RefreshCw, Download, Printer } from 'lucide-vue-next'
import { formatValue, computeAggregates } from '@/utils/pdfGenerator'
import type { ReportTemplate, CreateReportRequest } from '@/services/reportService'

// ─── Props / Emits ────────────────────────────────────────────────────────────

const props = defineProps<{
  template: ReportTemplate | CreateReportRequest
  rows: Record<string, unknown>[]
  loading?: boolean
  error?: string | null
}>()

const emit = defineEmits<{
  refresh: []
  'download-pdf': []
  print: []
}>()

// ─── Computed helpers ──────────────────────────────────────────────────────────

const previewRows = computed(() => props.rows.slice(0, 50))

/** Aggregate footer row for list / summary whole-table aggregates. */
const aggregateRow = computed<Record<string, string> | null>(() => {
  const hasAgg = props.template.fields.some((f) => f.aggregate)
  if (!hasAgg) return null
  return computeAggregates(props.template.fields, props.rows)
})

/** Group rows by groupBy field for the summary layout. */
type GroupedRow = { key: string; rows: Record<string, unknown>[]; aggregates: Record<string, string> }
const groupedRows = computed<GroupedRow[]>(() => {
  const groupBy = props.template.groupBy
  if (!groupBy || props.template.layoutType !== 'summary') return []

  const map = new Map<string, Record<string, unknown>[]>()
  for (const row of previewRows.value) {
    const key = String(row[groupBy] ?? '')
    if (!map.has(key)) map.set(key, [])
    map.get(key)!.push(row)
  }

  return Array.from(map.entries()).map(([key, rows]) => ({
    key,
    rows,
    aggregates: computeAggregates(props.template.fields, rows),
  }))
})

const groupByLabel = computed(() => {
  const groupBy = props.template.groupBy
  if (!groupBy) return ''
  return props.template.fields.find((f) => f.name === groupBy)?.label ?? groupBy
})

const rowCount = computed(() => props.rows.length)
const isListLayout = computed(() => props.template.layoutType === 'list')
const isSummaryLayout = computed(() => props.template.layoutType === 'summary')
const isDetailLayout = computed(() => props.template.layoutType === 'detail')
const showTable = computed(() => isListLayout.value || isSummaryLayout.value)
</script>

<template>
  <div class="report-preview-printable space-y-3">
    <!-- ── Toolbar ──────────────────────────────────────────────────────── -->
    <div
      role="toolbar"
      class="no-print flex items-center gap-2 flex-wrap"
    >
      <Button
        type="button"
        variant="outline"
        size="sm"
        :disabled="loading"
        @click="emit('refresh')"
      >
        <RefreshCw class="h-4 w-4 mr-1.5" :class="loading ? 'animate-spin' : ''" />
        Refresh
      </Button>

      <Button
        type="button"
        variant="outline"
        size="sm"
        :disabled="loading || rows.length === 0"
        @click="emit('download-pdf')"
      >
        <Download class="h-4 w-4 mr-1.5" />
        Download PDF
      </Button>

      <Button
        type="button"
        variant="outline"
        size="sm"
        :disabled="loading || rows.length === 0"
        @click="emit('print')"
      >
        <Printer class="h-4 w-4 mr-1.5" />
        Print
      </Button>

      <span class="ml-auto text-sm text-muted-foreground">
        Showing {{ rowCount }} row{{ rowCount !== 1 ? 's' : '' }}
      </span>
    </div>

    <!-- ── Loading ─────────────────────────────────────────────────────── -->
    <div v-if="loading" class="flex items-center justify-center py-12">
      <Spinner size="lg" />
    </div>

    <!-- ── Error ───────────────────────────────────────────────────────── -->
    <div v-else-if="error" class="flex items-start gap-3 rounded-lg border border-destructive/30 bg-destructive/10 px-4 py-3 text-destructive">
      <AlertCircle class="h-5 w-5 mt-0.5 shrink-0" />
      <p class="text-sm">{{ error }}</p>
    </div>

    <!-- ── Empty ───────────────────────────────────────────────────────── -->
    <div v-else-if="rows.length === 0" class="flex flex-col items-center justify-center py-12 text-muted-foreground">
      <p class="text-sm">No data to preview.</p>
      <p class="text-xs mt-1">Click Refresh to load report data.</p>
    </div>

    <template v-else>
      <!-- Report header text -->
      <div v-if="template.header" class="text-sm text-muted-foreground italic">
        {{ template.header }}
      </div>

      <!-- ── List layout ─────────────────────────────────────────────── -->
      <div v-if="showTable && isListLayout" class="overflow-x-auto rounded-md border">
        <table class="w-full text-sm border-collapse">
          <thead>
            <tr class="bg-muted/60">
              <th
                v-for="field in template.fields"
                :key="field.name"
                class="px-3 py-2 text-left font-medium text-foreground whitespace-nowrap border-b"
                :style="field.width ? `min-width: ${field.width}px` : undefined"
              >
                {{ field.label }}
              </th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="(row, ri) in previewRows"
              :key="ri"
              class="border-b last:border-b-0 hover:bg-muted/30 transition-colors"
              :class="ri % 2 === 1 ? 'bg-muted/10' : ''"
            >
              <td
                v-for="field in template.fields"
                :key="field.name"
                class="px-3 py-2 text-muted-foreground"
              >
                {{ formatValue(row[field.name], field.format) }}
              </td>
            </tr>
          </tbody>
          <!-- Aggregate footer row -->
          <tfoot v-if="aggregateRow">
            <tr class="bg-muted font-medium">
              <td
                v-for="field in template.fields"
                :key="field.name"
                class="px-3 py-2 text-xs"
              >
                {{ aggregateRow[field.name] ?? '' }}
              </td>
            </tr>
          </tfoot>
        </table>
      </div>

      <!-- ── Summary layout (grouped) ───────────────────────────────── -->
      <div v-else-if="showTable && isSummaryLayout" class="space-y-4">
        <div v-if="groupedRows.length === 0" class="overflow-x-auto rounded-md border">
          <!-- No groupBy defined — render as plain list -->
          <table class="w-full text-sm border-collapse">
            <thead>
              <tr class="bg-muted/60">
                <th
                  v-for="field in template.fields"
                  :key="field.name"
                  class="px-3 py-2 text-left font-medium text-foreground whitespace-nowrap border-b"
                  :style="field.width ? `min-width: ${field.width}px` : undefined"
                >
                  {{ field.label }}
                </th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="(row, ri) in previewRows"
                :key="ri"
                class="border-b last:border-b-0 hover:bg-muted/30 transition-colors"
              >
                <td
                  v-for="field in template.fields"
                  :key="field.name"
                  class="px-3 py-2 text-muted-foreground"
                >
                  {{ formatValue(row[field.name], field.format) }}
                </td>
              </tr>
            </tbody>
            <tfoot v-if="aggregateRow">
              <tr class="bg-muted font-medium">
                <td
                  v-for="field in template.fields"
                  :key="field.name"
                  class="px-3 py-2 text-xs"
                >
                  {{ aggregateRow[field.name] ?? '' }}
                </td>
              </tr>
            </tfoot>
          </table>
        </div>

        <div v-for="group in groupedRows" :key="group.key" class="rounded-md border overflow-hidden">
          <!-- Group header -->
          <div class="px-4 py-2 bg-blue-50 dark:bg-blue-900/20 text-sm font-medium text-blue-800 dark:text-blue-200">
            {{ groupByLabel }}: {{ group.key }}
          </div>
          <table class="w-full text-sm border-collapse">
            <thead>
              <tr class="bg-muted/60 border-b">
                <th
                  v-for="field in template.fields"
                  :key="field.name"
                  class="px-3 py-2 text-left font-medium text-foreground whitespace-nowrap"
                  :style="field.width ? `min-width: ${field.width}px` : undefined"
                >
                  {{ field.label }}
                </th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="(row, ri) in group.rows"
                :key="ri"
                class="border-b last:border-b-0 hover:bg-muted/30 transition-colors"
                :class="ri % 2 === 1 ? 'bg-muted/10' : ''"
              >
                <td
                  v-for="field in template.fields"
                  :key="field.name"
                  class="px-3 py-2 text-muted-foreground"
                >
                  {{ formatValue(row[field.name], field.format) }}
                </td>
              </tr>
            </tbody>
            <!-- Per-group aggregate footer -->
            <tfoot v-if="Object.keys(group.aggregates).length > 0">
              <tr class="bg-muted/50 font-medium border-t">
                <td
                  v-for="field in template.fields"
                  :key="field.name"
                  class="px-3 py-1.5 text-xs"
                >
                  {{ group.aggregates[field.name] ?? '' }}
                </td>
              </tr>
            </tfoot>
          </table>
        </div>
      </div>

      <!-- ── Detail layout (card per row) ───────────────────────────── -->
      <div v-else-if="isDetailLayout" class="space-y-3">
        <div
          v-for="(row, ri) in previewRows"
          :key="ri"
          class="rounded-lg border bg-card p-4"
        >
          <p class="text-xs font-semibold text-muted-foreground mb-3">Record {{ ri + 1 }}</p>
          <div class="grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-2">
            <div
              v-for="field in template.fields"
              :key="field.name"
              class="flex gap-2"
            >
              <span class="text-xs font-medium text-muted-foreground w-28 shrink-0 pt-0.5">
                {{ field.label }}
              </span>
              <span class="text-sm text-foreground break-words">
                {{ formatValue(row[field.name], field.format) }}
              </span>
            </div>
          </div>
        </div>
      </div>

      <!-- Report footer text -->
      <div v-if="template.footer" class="text-xs text-muted-foreground italic pt-2 border-t">
        {{ template.footer }}
      </div>
    </template>
  </div>
</template>

<style>
@media print {
  /* Hide everything except the printable section */
  body > * {
    display: none !important;
  }

  .report-preview-printable {
    display: block !important;
    background: white !important;
    color: black !important;
    font-size: 11pt;
  }

  /* Hide toolbar and interactive controls */
  .report-preview-printable .no-print,
  .report-preview-printable [role="toolbar"],
  .report-preview-printable button {
    display: none !important;
  }

  /* Table layout for print */
  table {
    border-collapse: collapse !important;
    width: 100% !important;
  }

  thead {
    display: table-header-group;
  }

  tfoot {
    display: table-footer-group;
  }

  tr {
    page-break-inside: avoid;
  }

  th, td {
    border: 1px solid #ccc !important;
    padding: 4pt 6pt !important;
    background: white !important;
    color: black !important;
  }

  th {
    background: #f0f0f0 !important;
    font-weight: bold;
  }

  /* Keep group headers visible */
  .report-preview-printable .bg-blue-50 {
    background: #e8f0fe !important;
    color: #1a3a6e !important;
    font-weight: bold;
  }

  /* Page settings */
  @page {
    margin: 1.5cm;
  }
}
</style>
