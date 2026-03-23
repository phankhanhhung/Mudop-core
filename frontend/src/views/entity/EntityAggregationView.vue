<script setup lang="ts">
import { ref, computed, onMounted, useTemplateRef } from 'vue'
import { useRoute, RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useMetadata } from '@/composables/useMetadata'
import { useAggregation } from '@/composables/useAggregation'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import AggregationBuilder from '@/components/entity/AggregationBuilder.vue'
import AggregationChart from '@/components/entity/AggregationChart.vue'
import AnalyticalTable from '@/components/smart/AnalyticalTable.vue'
import type { GroupConfig, AggregateConfig } from '@/composables/useAnalyticalTable'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell
} from '@/components/ui/table'
import {
  ArrowLeft,
  BarChart3,
  RefreshCw,
  Hash,
  TrendingUp,
  Calculator,
  ArrowUpDown,
  ChevronRight,
  Download,
  Save,
  Bookmark,
  Trash2,
  Check,
  X
} from 'lucide-vue-next'
import type { AggregationConfig, ChartType } from '@/types/aggregation'

const route = useRoute()
const { t } = useI18n()

const module = computed(() => route.params.module as string)
const entity = computed(() => route.params.entity as string)

const {
  metadata,
  fields,
  isLoading: metadataLoading,
  error: metadataError,
  load: loadMetadata
} = useMetadata({
  module: module.value,
  entity: entity.value,
  autoLoad: false
})

const {
  config,
  results,
  isLoading: aggregationLoading,
  error: aggregationError,
  execute,
  summaryStats,
  presets,
  savePreset,
  loadPreset,
  deletePreset
} = useAggregation({
  module: module.value,
  entitySet: entity.value
})

const chartType = ref<ChartType>('bar')
const builderRef = useTemplateRef<InstanceType<typeof AggregationBuilder>>('builderRef')

// Saved presets
const showSavePreset = ref(false)
const presetName = ref('')

// Table sort
const tableSortField = ref<string | null>(null)
const tableSortDirection = ref<'asc' | 'desc'>('asc')

const displayName = computed(() => metadata.value?.displayName || entity.value)

// Column keys from raw data, categorized
const resultColumns = computed(() => {
  if (!results.value || results.value.rawData.length === 0) return []
  return Object.keys(results.value.rawData[0])
})

const groupByColumns = computed(() => {
  return config.value.groupByFields
})

const aggregateColumns = computed(() => {
  return config.value.aggregations.map(a => a.alias)
})

// Sorted raw data
const sortedData = computed(() => {
  if (!results.value) return []
  const data = [...results.value.rawData]
  if (!tableSortField.value) return data

  const field = tableSortField.value
  const dir = tableSortDirection.value === 'asc' ? 1 : -1

  return data.sort((a, b) => {
    const va = a[field]
    const vb = b[field]
    if (va == null && vb == null) return 0
    if (va == null) return dir
    if (vb == null) return -dir
    if (typeof va === 'number' && typeof vb === 'number') return (va - vb) * dir
    return String(va).localeCompare(String(vb)) * dir
  })
})

// Total row calculation
const totalRow = computed(() => {
  if (!results.value || results.value.rawData.length === 0) return null
  const totals: Record<string, unknown> = {}
  for (const col of resultColumns.value) {
    if (aggregateColumns.value.includes(col)) {
      // Sum of numeric values
      const sum = results.value.rawData.reduce((acc, row) => {
        const val = row[col]
        return acc + (typeof val === 'number' ? val : Number(val) || 0)
      }, 0)
      totals[col] = Math.round(sum * 100) / 100
    } else {
      totals[col] = null
    }
  }
  return totals
})

// Convert aggregation config to AnalyticalTable format
const analyticalGroupBy = computed<GroupConfig[]>(() =>
  config.value.groupByFields.map(f => ({ field: f }))
)

const analyticalAggregates = computed<AggregateConfig[]>(() =>
  config.value.aggregations.map(a => ({
    field: a.field || a.alias,
    fn: a.func as AggregateConfig['fn'],
    label: a.alias,
  }))
)

const useAnalyticalView = computed(() =>
  results.value && config.value.groupByFields.length > 0
)

onMounted(async () => {
  await loadMetadata()
})

function handleExecute(cfg: AggregationConfig) {
  config.value = cfg
  execute()
}

function handleRefresh() {
  if (config.value.groupByFields.length > 0 && config.value.aggregations.length > 0) {
    execute()
  }
}

function handleSort(field: string) {
  if (tableSortField.value === field) {
    tableSortDirection.value = tableSortDirection.value === 'asc' ? 'desc' : 'asc'
  } else {
    tableSortField.value = field
    tableSortDirection.value = 'asc'
  }
}

function isColumnGroupBy(col: string): boolean {
  return groupByColumns.value.includes(col)
}

function isColumnAggregate(col: string): boolean {
  return aggregateColumns.value.includes(col)
}

function formatNumber(val: unknown): string {
  if (val == null) return ''
  const num = typeof val === 'number' ? val : Number(val)
  if (isNaN(num)) return String(val)
  return num.toLocaleString(undefined, { maximumFractionDigits: 2 })
}

// Export CSV
function handleExportCsv() {
  if (!results.value) return
  const columns = resultColumns.value
  const rows = sortedData.value

  const lines: string[] = []
  lines.push(columns.join(','))
  for (const row of rows) {
    const cells = columns.map(col => {
      const val = row[col]
      if (val == null) return ''
      const str = String(val)
      if (str.includes(',') || str.includes('"') || str.includes('\n')) {
        return '"' + str.replace(/"/g, '""') + '"'
      }
      return str
    })
    lines.push(cells.join(','))
  }

  const bom = '\uFEFF'
  const blob = new Blob([bom + lines.join('\r\n')], { type: 'text/csv;charset=utf-8;' })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = `${entity.value}_aggregation_${new Date().toISOString().split('T')[0]}.csv`
  anchor.style.display = 'none'
  document.body.appendChild(anchor)
  anchor.click()
  document.body.removeChild(anchor)
  URL.revokeObjectURL(url)
}

// Preset management
function handleSavePreset() {
  if (!presetName.value.trim()) return
  savePreset(presetName.value.trim(), chartType.value)
  presetName.value = ''
  showSavePreset.value = false
}

function handleLoadPreset(id: string) {
  const preset = loadPreset(id)
  if (preset && builderRef.value) {
    builderRef.value.applyConfig(preset.config)
    chartType.value = preset.chartType
    config.value = JSON.parse(JSON.stringify(preset.config))
    execute()
  }
}

function handleDeletePreset(id: string) {
  deletePreset(id)
}
</script>

<template>
  <DefaultLayout>
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div class="flex items-center gap-4">
          <RouterLink :to="`/odata/${module}/${entity}`">
            <Button variant="outline" size="sm">
              <ArrowLeft class="mr-2 h-4 w-4" />
              {{ t('analytics.backToEntity') }}
            </Button>
          </RouterLink>
          <div>
            <div class="flex items-center gap-2 text-sm text-muted-foreground mb-0.5">
              <RouterLink :to="`/odata/${module}/${entity}`" class="hover:text-foreground transition-colors">
                {{ module }}
              </RouterLink>
              <ChevronRight class="h-3.5 w-3.5" />
              <RouterLink :to="`/odata/${module}/${entity}`" class="hover:text-foreground transition-colors">
                {{ displayName }}
              </RouterLink>
              <ChevronRight class="h-3.5 w-3.5" />
              <span class="text-foreground font-medium">{{ t('common.analytics') }}</span>
            </div>
            <h1 class="text-2xl font-bold tracking-tight">{{ t('analytics.title', { name: displayName }) }}</h1>
          </div>
        </div>
        <div class="flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            @click="handleRefresh"
            :disabled="aggregationLoading || config.groupByFields.length === 0"
          >
            <Spinner v-if="aggregationLoading" size="sm" class="mr-2" />
            <RefreshCw v-else class="mr-2 h-4 w-4" />
            {{ t('common.refresh') }}
          </Button>
        </div>
      </div>

      <!-- Loading metadata -->
      <div v-if="metadataLoading" class="flex flex-col items-center justify-center py-16">
        <Spinner size="lg" />
        <p class="text-muted-foreground mt-3 text-sm">{{ t('common.loading') }}</p>
      </div>

      <!-- Metadata error -->
      <Card v-else-if="metadataError">
        <CardContent class="flex flex-col items-center justify-center py-16">
          <div class="h-12 w-12 rounded-full bg-destructive/10 flex items-center justify-center mb-3">
            <X class="h-6 w-6 text-destructive" />
          </div>
          <p class="text-sm text-destructive font-medium">{{ metadataError }}</p>
        </CardContent>
      </Card>

      <template v-else>
        <!-- Stats Summary Cards (visible when results exist) -->
        <div v-if="results" class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          <Card class="transition-all hover:shadow-md">
            <CardContent class="p-4">
              <div class="flex items-center justify-between">
                <div>
                  <p class="text-sm font-medium text-muted-foreground">{{ t('analytics.stats.totalGroups') }}</p>
                  <p class="text-2xl font-bold mt-1">{{ summaryStats.totalGroups }}</p>
                </div>
                <div class="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center">
                  <Hash class="h-5 w-5 text-primary" />
                </div>
              </div>
            </CardContent>
          </Card>

          <Card class="transition-all hover:shadow-md">
            <CardContent class="p-4">
              <div class="flex items-center justify-between">
                <div>
                  <p class="text-sm font-medium text-muted-foreground">{{ t('analytics.stats.total') }}</p>
                  <p class="text-2xl font-bold mt-1 text-emerald-600">{{ formatNumber(summaryStats.primarySum) }}</p>
                </div>
                <div class="h-10 w-10 rounded-full bg-emerald-500/10 flex items-center justify-center">
                  <Calculator class="h-5 w-5 text-emerald-500" />
                </div>
              </div>
            </CardContent>
          </Card>

          <Card class="transition-all hover:shadow-md">
            <CardContent class="p-4">
              <div class="flex items-center justify-between">
                <div>
                  <p class="text-sm font-medium text-muted-foreground">{{ t('analytics.stats.average') }}</p>
                  <p class="text-2xl font-bold mt-1 text-violet-600">{{ formatNumber(summaryStats.primaryAvg) }}</p>
                </div>
                <div class="h-10 w-10 rounded-full bg-violet-500/10 flex items-center justify-center">
                  <TrendingUp class="h-5 w-5 text-violet-500" />
                </div>
              </div>
            </CardContent>
          </Card>

          <Card class="transition-all hover:shadow-md">
            <CardContent class="p-4">
              <div class="flex items-center justify-between">
                <div>
                  <p class="text-sm font-medium text-muted-foreground">{{ t('analytics.stats.range') }}</p>
                  <p class="text-2xl font-bold mt-1 text-amber-600">
                    {{ formatNumber(summaryStats.primaryMin) }} - {{ formatNumber(summaryStats.primaryMax) }}
                  </p>
                </div>
                <div class="h-10 w-10 rounded-full bg-amber-500/10 flex items-center justify-center">
                  <ArrowUpDown class="h-5 w-5 text-amber-500" />
                </div>
              </div>
            </CardContent>
          </Card>
        </div>

        <!-- Main content: Builder + Presets row -->
        <div class="grid grid-cols-1 lg:grid-cols-3 gap-6">
          <!-- Aggregation Builder (2/3 width) -->
          <Card class="lg:col-span-2">
            <CardHeader>
              <div class="flex items-center gap-2">
                <BarChart3 class="h-5 w-5" />
                <CardTitle>{{ t('analytics.configureAggregation') }}</CardTitle>
              </div>
              <CardDescription>{{ t('analytics.configureDescription') }}</CardDescription>
            </CardHeader>
            <CardContent>
              <AggregationBuilder
                ref="builderRef"
                :fields="fields"
                :isLoading="aggregationLoading"
                @execute="handleExecute"
              />
            </CardContent>
          </Card>

          <!-- Saved Presets (1/3 width) -->
          <Card>
            <CardHeader>
              <div class="flex items-center justify-between">
                <div class="flex items-center gap-2">
                  <Bookmark class="h-5 w-5" />
                  <CardTitle>{{ t('analytics.savedPresets') }}</CardTitle>
                </div>
                <Button
                  v-if="results"
                  variant="outline"
                  size="sm"
                  @click="showSavePreset = !showSavePreset"
                >
                  <Save class="mr-1.5 h-3.5 w-3.5" />
                  {{ t('analytics.save') }}
                </Button>
              </div>
              <CardDescription>{{ t('analytics.presetsDescription') }}</CardDescription>
            </CardHeader>
            <CardContent>
              <!-- Save preset form -->
              <div v-if="showSavePreset" class="mb-4 p-3 rounded-lg border bg-muted/30">
                <div class="flex gap-2">
                  <Input
                    v-model="presetName"
                    :placeholder="t('analytics.presetNamePlaceholder')"
                    class="flex-1 h-9"
                    @keydown.enter="handleSavePreset"
                  />
                  <Button size="sm" @click="handleSavePreset" :disabled="!presetName.trim()" class="h-9">
                    <Check class="h-3.5 w-3.5" />
                  </Button>
                  <Button variant="ghost" size="sm" @click="showSavePreset = false" class="h-9">
                    <X class="h-3.5 w-3.5" />
                  </Button>
                </div>
              </div>

              <!-- Preset list -->
              <div v-if="presets.length === 0" class="text-center py-8">
                <Bookmark class="h-8 w-8 text-muted-foreground/40 mx-auto mb-2" />
                <p class="text-sm text-muted-foreground">{{ t('analytics.noPresets') }}</p>
                <p class="text-xs text-muted-foreground mt-1">{{ t('analytics.noPresetsHint') }}</p>
              </div>

              <div v-else class="space-y-1.5">
                <div
                  v-for="preset in presets"
                  :key="preset.id"
                  class="flex items-center gap-2 p-2.5 rounded-lg border hover:bg-muted/50 transition-colors group cursor-pointer"
                  @click="handleLoadPreset(preset.id)"
                >
                  <div class="flex-1 min-w-0">
                    <p class="text-sm font-medium truncate">{{ preset.name }}</p>
                    <p class="text-xs text-muted-foreground">
                      {{ preset.config.groupByFields.length }} {{ t('analytics.groupByFieldsLabel') }}
                      &middot;
                      {{ preset.config.aggregations.length }} {{ t('analytics.aggregationsLabel') }}
                    </p>
                  </div>
                  <Badge variant="secondary" class="text-xs shrink-0">{{ preset.chartType }}</Badge>
                  <Button
                    variant="ghost"
                    size="sm"
                    class="h-7 w-7 p-0 opacity-0 group-hover:opacity-100 transition-opacity shrink-0"
                    @click.stop="handleDeletePreset(preset.id)"
                  >
                    <Trash2 class="h-3.5 w-3.5 text-muted-foreground hover:text-destructive" />
                  </Button>
                </div>
              </div>
            </CardContent>
          </Card>
        </div>

        <!-- Aggregation error -->
        <Card v-if="aggregationError">
          <CardContent class="py-6">
            <div class="flex items-center gap-3">
              <div class="h-8 w-8 rounded-full bg-destructive/10 flex items-center justify-center shrink-0">
                <X class="h-4 w-4 text-destructive" />
              </div>
              <p class="text-sm text-destructive">{{ aggregationError }}</p>
            </div>
          </CardContent>
        </Card>

        <!-- Aggregation loading -->
        <div v-if="aggregationLoading && !results" class="flex flex-col items-center justify-center py-16">
          <Spinner size="lg" />
          <p class="text-muted-foreground mt-3 text-sm">{{ t('analytics.executingQuery') }}</p>
        </div>

        <!-- Results -->
        <template v-if="results">
          <!-- Chart Visualization -->
          <Card>
            <CardHeader class="pb-3">
              <div class="flex items-center justify-between">
                <div class="flex items-center gap-2">
                  <BarChart3 class="h-5 w-5" />
                  <CardTitle>{{ t('analytics.visualization') }}</CardTitle>
                </div>
                <Badge variant="secondary">
                  {{ results.rawData.length }} {{ t('analytics.groups') }}
                </Badge>
              </div>
            </CardHeader>
            <CardContent>
              <AggregationChart
                :results="results"
                :chartType="chartType"
                @update:chartType="chartType = $event"
              />
            </CardContent>
          </Card>

          <!-- Results Table: AnalyticalTable when grouping is applied -->
          <AnalyticalTable
            v-if="useAnalyticalView && metadata"
            :data="results.rawData"
            :metadata="metadata"
            :groupBy="analyticalGroupBy"
            :aggregates="analyticalAggregates"
            :showSubtotals="true"
            :showGrandTotal="true"
            :title="t('analytics.results')"
            :sortField="tableSortField ?? undefined"
            :sortDirection="tableSortDirection"
            @sort="(field: string, dir: 'asc' | 'desc') => { tableSortField = field; tableSortDirection = dir }"
          />

          <!-- Results Table: Raw table fallback (no grouping) -->
          <Card v-else>
            <CardHeader class="pb-3">
              <div class="flex items-center justify-between">
                <div class="flex items-center gap-2">
                  <CardTitle>{{ t('analytics.results') }}</CardTitle>
                  <Badge variant="outline" class="text-xs">
                    {{ results.rawData.length }} {{ t('analytics.rows') }}
                  </Badge>
                </div>
                <Button variant="outline" size="sm" @click="handleExportCsv">
                  <Download class="mr-2 h-4 w-4" />
                  {{ t('analytics.exportCsv') }}
                </Button>
              </div>
            </CardHeader>
            <CardContent class="p-0">
              <div class="overflow-x-auto">
                <Table>
                  <TableHeader>
                    <TableRow class="hover:bg-transparent">
                      <TableHead
                        v-for="col in resultColumns"
                        :key="col"
                        :class="'cursor-pointer select-none whitespace-nowrap' + (isColumnGroupBy(col) ? ' bg-blue-50/50 dark:bg-blue-950/20' : '') + (isColumnAggregate(col) ? ' bg-emerald-50/50 dark:bg-emerald-950/20' : '')"
                        @click="handleSort(col)"
                      >
                        <div class="flex items-center gap-1.5">
                          <span>{{ col }}</span>
                          <Badge v-if="isColumnGroupBy(col)" variant="outline" class="text-[10px] px-1 py-0 font-normal">
                            {{ t('analytics.groupByBadge') }}
                          </Badge>
                          <Badge v-else-if="isColumnAggregate(col)" variant="outline" class="text-[10px] px-1 py-0 font-normal text-emerald-600">
                            {{ t('analytics.aggBadge') }}
                          </Badge>
                          <ArrowUpDown
                            class="h-3 w-3 text-muted-foreground"
                            :class="tableSortField === col ? 'text-foreground' : ''"
                          />
                        </div>
                      </TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    <TableRow v-for="(row, idx) in sortedData" :key="idx">
                      <TableCell
                        v-for="col in resultColumns"
                        :key="col"
                        :class="'whitespace-nowrap' + (isColumnGroupBy(col) ? ' bg-blue-50/30 dark:bg-blue-950/10 font-medium' : '') + (isColumnAggregate(col) ? ' bg-emerald-50/30 dark:bg-emerald-950/10 tabular-nums' : '')"
                      >
                        <template v-if="isColumnAggregate(col)">
                          {{ formatNumber(row[col]) }}
                        </template>
                        <template v-else>
                          {{ row[col] != null ? row[col] : '' }}
                        </template>
                      </TableCell>
                    </TableRow>
                    <!-- Total row -->
                    <TableRow v-if="totalRow" class="border-t-2 font-semibold bg-muted/50">
                      <TableCell
                        v-for="col in resultColumns"
                        :key="col"
                        class="whitespace-nowrap"
                      >
                        <template v-if="totalRow[col] != null">
                          {{ formatNumber(totalRow[col]) }}
                        </template>
                        <template v-else-if="resultColumns.indexOf(col) === 0">
                          {{ t('analytics.totalLabel') }}
                        </template>
                      </TableCell>
                    </TableRow>
                  </TableBody>
                </Table>
              </div>
            </CardContent>
          </Card>
        </template>

        <!-- Empty state when no results yet -->
        <Card v-if="!results && !aggregationLoading && !aggregationError">
          <CardContent class="flex flex-col items-center justify-center py-16">
            <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
              <BarChart3 class="h-8 w-8 text-muted-foreground" />
            </div>
            <h3 class="text-lg font-semibold mb-1">{{ t('analytics.emptyTitle') }}</h3>
            <p class="text-muted-foreground text-sm text-center max-w-sm">
              {{ t('analytics.emptyDescription') }}
            </p>
          </CardContent>
        </Card>
      </template>
    </div>
  </DefaultLayout>
</template>
