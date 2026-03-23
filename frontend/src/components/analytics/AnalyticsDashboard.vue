<script setup lang="ts">
import { computed, ref, toRef } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  Hash,
  BarChart3,
  PieChart,
  TrendingUp,
  ChevronDown,
  ChevronRight,
  RefreshCw,
  AlertCircle,
  Activity,
} from 'lucide-vue-next'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Spinner } from '@/components/ui/spinner'
import { useAnalytics } from '@/composables/useAnalytics'
import type { EntityMetadata } from '@/types/metadata'
import KpiCardEnhanced from './KpiCardEnhanced.vue'
import BarChart from './BarChart.vue'
import DonutChart from './DonutChart.vue'
import SparklineChart from './SparklineChart.vue'

interface Props {
  module: string
  entity: string
  entityMetadata: EntityMetadata
}

const props = defineProps<Props>()

const { t } = useI18n()

const moduleRef = toRef(props, 'module')
const entityRef = toRef(props, 'entity')

const {
  entityCount,
  enumDistributions,
  numericStats,
  timeSeriesData,
  isLoading,
  error,
  refresh,
} = useAnalytics(moduleRef, entityRef)

// Collapsible sections
const sectionsExpanded = ref({
  overview: true,
  timeSeries: true,
  distributions: true,
  numericStats: true,
})

function toggleSection(section: keyof typeof sectionsExpanded.value) {
  sectionsExpanded.value[section] = !sectionsExpanded.value[section]
}

// Donut chart color palette
const DONUT_COLORS = [
  'rgb(59, 130, 246)',   // blue
  'rgb(16, 185, 129)',   // emerald
  'rgb(245, 158, 11)',   // amber
  'rgb(239, 68, 68)',    // red
  'rgb(139, 92, 246)',   // violet
  'rgb(6, 182, 212)',    // cyan
  'rgb(236, 72, 153)',   // pink
  'rgb(249, 115, 22)',   // orange
  'rgb(99, 102, 241)',   // indigo
  'rgb(168, 85, 247)',   // purple
]

// Computed data for charts
const hasTimeSeries = computed(() => timeSeriesData.value.length >= 2)
const hasEnumDistributions = computed(() => enumDistributions.value.size > 0)
const hasNumericStats = computed(() => numericStats.value.size > 0)
const hasAnyData = computed(
  () => entityCount.value > 0 || hasTimeSeries.value || hasEnumDistributions.value || hasNumericStats.value
)

const timeSeriesSparkline = computed(() => timeSeriesData.value.map((pt) => pt.count))

const enumChartData = computed(() => {
  const result: { fieldName: string; data: { label: string; value: number; color: string }[] }[] = []
  enumDistributions.value.forEach((items, fieldName) => {
    const data = items
      .filter((item) => item.value > 0)
      .sort((a, b) => b.value - a.value)
      .slice(0, 10) // Top 10 values
      .map((item, i) => ({
        label: item.label,
        value: item.value,
        color: DONUT_COLORS[i % DONUT_COLORS.length],
      }))
    if (data.length > 0) {
      result.push({ fieldName, data })
    }
  })
  return result
})

const numericKpiData = computed(() => {
  const result: {
    fieldName: string
    avg: number
    sum: number
    min: number
    max: number
  }[] = []
  numericStats.value.forEach((stats, fieldName) => {
    result.push({ fieldName, ...stats })
  })
  return result
})

const timeSeriesBarData = computed(() => {
  // Show last 30 data points as a bar chart
  const data = timeSeriesData.value.slice(-30)
  return data.map((pt) => ({
    label: pt.date.slice(5), // MM-DD
    value: pt.count,
  }))
})

function formatFieldName(name: string): string {
  // Convert PascalCase/camelCase to readable
  return name.replace(/([A-Z])/g, ' $1').replace(/^./, (s) => s.toUpperCase()).trim()
}

function formatStat(val: number): string {
  if (val >= 1_000_000) return `${(val / 1_000_000).toFixed(1)}M`
  if (val >= 1_000) return `${(val / 1_000).toFixed(1)}K`
  return val % 1 === 0 ? val.toLocaleString() : val.toFixed(2)
}
</script>

<template>
  <div class="space-y-6">
    <!-- Header -->
    <div class="flex items-center justify-between">
      <div>
        <h2 class="text-lg font-semibold text-foreground">
          {{ t('analytics.title', { name: entityMetadata.displayName ?? entity }) }}
        </h2>
        <p class="text-sm text-muted-foreground mt-0.5">
          {{ entityMetadata.description ?? t('common.analytics', 'Analytics') }}
        </p>
      </div>
      <Button
        variant="outline"
        size="sm"
        :disabled="isLoading"
        @click="refresh"
      >
        <RefreshCw class="h-4 w-4 mr-1.5" :class="{ 'animate-spin': isLoading }" />
        {{ t('common.refresh') }}
      </Button>
    </div>

    <!-- Loading state -->
    <div v-if="isLoading && !hasAnyData" class="flex flex-col items-center justify-center py-16">
      <Spinner size="lg" />
      <p class="text-sm text-muted-foreground mt-3">{{ t('common.loading') }}</p>
    </div>

    <!-- Error state -->
    <Card v-else-if="error && !hasAnyData" class="border-destructive/50">
      <CardContent class="flex items-center gap-3 py-6">
        <AlertCircle class="h-5 w-5 text-destructive shrink-0" />
        <div>
          <p class="text-sm font-medium text-destructive">{{ t('common.error') }}</p>
          <p class="text-sm text-muted-foreground">{{ error }}</p>
        </div>
        <Button variant="outline" size="sm" class="ml-auto" @click="refresh">
          {{ t('common.retry') }}
        </Button>
      </CardContent>
    </Card>

    <!-- Empty state -->
    <Card v-else-if="!hasAnyData && !isLoading">
      <CardContent class="flex flex-col items-center justify-center py-16">
        <BarChart3 class="h-12 w-12 text-muted-foreground/40 mb-4" />
        <p class="text-sm font-medium text-foreground">{{ t('common.noData') }}</p>
        <p class="text-sm text-muted-foreground mt-1">
          {{ t('analytics.emptyDescription', 'No data available for analysis.') }}
        </p>
      </CardContent>
    </Card>

    <!-- Analytics content -->
    <template v-else>
      <!-- Overview KPI Cards -->
      <section>
        <button
          class="flex items-center gap-2 mb-3 text-sm font-semibold text-foreground hover:text-primary transition-colors"
          @click="toggleSection('overview')"
        >
          <component :is="sectionsExpanded.overview ? ChevronDown : ChevronRight" class="h-4 w-4" />
          {{ t('analytics.stats.totalGroups', 'Overview') }}
        </button>
        <div v-if="sectionsExpanded.overview" class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          <KpiCardEnhanced
            :title="t('dashboard.entities', 'Total Records')"
            :value="entityCount"
            :icon="Hash"
            color="primary"
            :sparkline-data="timeSeriesSparkline.length >= 2 ? timeSeriesSparkline : undefined"
          />
          <KpiCardEnhanced
            v-for="kpi in numericKpiData.slice(0, 3)"
            :key="kpi.fieldName"
            :title="formatFieldName(kpi.fieldName)"
            :value="formatStat(kpi.avg)"
            :description="`${t('analytics.stats.range', 'Range')}: ${formatStat(kpi.min)} - ${formatStat(kpi.max)}`"
            :icon="Activity"
            color="emerald"
          />
        </div>
      </section>

      <!-- Time Series Section -->
      <section v-if="hasTimeSeries">
        <button
          class="flex items-center gap-2 mb-3 text-sm font-semibold text-foreground hover:text-primary transition-colors"
          @click="toggleSection('timeSeries')"
        >
          <component :is="sectionsExpanded.timeSeries ? ChevronDown : ChevronRight" class="h-4 w-4" />
          <TrendingUp class="h-4 w-4" />
          {{ t('dashboard.recentChanges', 'Records Over Time') }}
          <Badge variant="secondary" class="ml-1">{{ timeSeriesData.length }}</Badge>
        </button>
        <Card v-if="sectionsExpanded.timeSeries">
          <CardHeader class="pb-2">
            <CardTitle class="text-sm">{{ t('dashboard.recentChanges', 'Records Over Time') }}</CardTitle>
            <CardDescription>
              {{ t('dashboard.recentActivitySubtitle', 'Record creation timeline') }}
            </CardDescription>
          </CardHeader>
          <CardContent>
            <BarChart
              :data="timeSeriesBarData"
              :height="260"
              orientation="vertical"
              :show-values="false"
              :show-grid="true"
              :animate="true"
            />
          </CardContent>
        </Card>
      </section>

      <!-- Enum Distributions Section -->
      <section v-if="hasEnumDistributions">
        <button
          class="flex items-center gap-2 mb-3 text-sm font-semibold text-foreground hover:text-primary transition-colors"
          @click="toggleSection('distributions')"
        >
          <component :is="sectionsExpanded.distributions ? ChevronDown : ChevronRight" class="h-4 w-4" />
          <PieChart class="h-4 w-4" />
          {{ t('entity.fields', 'Field Distributions') }}
          <Badge variant="secondary" class="ml-1">{{ enumChartData.length }}</Badge>
        </button>
        <div v-if="sectionsExpanded.distributions" class="grid grid-cols-1 md:grid-cols-2 gap-4">
          <Card v-for="chart in enumChartData" :key="chart.fieldName">
            <CardHeader class="pb-2">
              <CardTitle class="text-sm">{{ formatFieldName(chart.fieldName) }}</CardTitle>
            </CardHeader>
            <CardContent class="flex justify-center">
              <DonutChart
                :data="chart.data"
                :size="180"
                :donut="true"
                :show-legend="true"
                :show-total="true"
                :animate="true"
              />
            </CardContent>
          </Card>
        </div>
      </section>

      <!-- Numeric Stats Section -->
      <section v-if="hasNumericStats">
        <button
          class="flex items-center gap-2 mb-3 text-sm font-semibold text-foreground hover:text-primary transition-colors"
          @click="toggleSection('numericStats')"
        >
          <component :is="sectionsExpanded.numericStats ? ChevronDown : ChevronRight" class="h-4 w-4" />
          <BarChart3 class="h-4 w-4" />
          {{ t('analytics.stats.total', 'Numeric Field Statistics') }}
          <Badge variant="secondary" class="ml-1">{{ numericKpiData.length }}</Badge>
        </button>
        <div v-if="sectionsExpanded.numericStats">
          <Card>
            <CardContent class="pt-4">
              <div class="overflow-x-auto">
                <table class="w-full text-sm">
                  <thead>
                    <tr class="border-b">
                      <th class="text-left py-2 px-3 font-medium text-muted-foreground">
                        {{ t('analytics.field', 'Field') }}
                      </th>
                      <th class="text-right py-2 px-3 font-medium text-muted-foreground">
                        {{ t('analytics.stats.total', 'Sum') }}
                      </th>
                      <th class="text-right py-2 px-3 font-medium text-muted-foreground">
                        {{ t('analytics.stats.average', 'Average') }}
                      </th>
                      <th class="text-right py-2 px-3 font-medium text-muted-foreground">
                        Min
                      </th>
                      <th class="text-right py-2 px-3 font-medium text-muted-foreground">
                        Max
                      </th>
                      <th class="py-2 px-3 font-medium text-muted-foreground">
                        {{ t('analytics.stats.range', 'Range') }}
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr
                      v-for="stat in numericKpiData"
                      :key="stat.fieldName"
                      class="border-b last:border-0"
                    >
                      <td class="py-2 px-3 font-medium">{{ formatFieldName(stat.fieldName) }}</td>
                      <td class="py-2 px-3 text-right tabular-nums">{{ formatStat(stat.sum) }}</td>
                      <td class="py-2 px-3 text-right tabular-nums">{{ formatStat(stat.avg) }}</td>
                      <td class="py-2 px-3 text-right tabular-nums">{{ formatStat(stat.min) }}</td>
                      <td class="py-2 px-3 text-right tabular-nums">{{ formatStat(stat.max) }}</td>
                      <td class="py-2 px-3">
                        <SparklineChart
                          :data="[stat.min, stat.avg, stat.max]"
                          :width="80"
                          :height="24"
                          color="primary"
                          :show-area="false"
                          :show-dots="true"
                          :animate="false"
                        />
                      </td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </CardContent>
          </Card>
        </div>
      </section>
    </template>
  </div>
</template>
