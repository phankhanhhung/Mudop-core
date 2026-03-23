<script setup lang="ts">
import { GripVertical, X } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import type { WidgetConfig, DashboardData, KpiSettings, EntityBarChartSettings } from '@/types/dashboard'
import EntityCountWidget from './EntityCountWidget.vue'
import RecentActivityWidget from './RecentActivityWidget.vue'
import QuickLinksWidget from './QuickLinksWidget.vue'
import SystemHealthWidget from './SystemHealthWidget.vue'
import KpiCardEnhanced from '@/components/analytics/KpiCardEnhanced.vue'
import BarChart, { type BarChartItem } from '@/components/analytics/BarChart.vue'

interface Props {
  config: WidgetConfig
  data: DashboardData
  editMode: boolean
}

const props = defineProps<Props>()
const emit = defineEmits<{
  remove: [widgetId: string]
}>()

const { t } = useI18n()

function resolveKpiValue(settings: KpiSettings): string | number {
  switch (settings.valueSource) {
    case 'total-records':
      return props.data.entityCounts.reduce((s, e) => s + e.count, 0)
    case 'module-count':
      return props.data.cacheStats?.moduleCount ?? 0
    case 'entity-type-count':
      return props.data.cacheStats?.entityCount ?? 0
    case 'static':
      return settings.staticValue ?? '—'
    default:
      return '—'
  }
}

function resolveBarChartData(settings: EntityBarChartSettings): BarChartItem[] {
  return props.data.entityCounts
    .slice(0, settings.maxBars ?? 10)
    .map((e) => ({ label: e.entity, value: e.count }))
}
</script>

<template>
  <div class="relative group">
    <!-- Edit mode overlay: drag handle + remove button -->
    <div
      v-if="editMode"
      class="absolute top-1 right-1 z-10 flex gap-1 bg-background/80 rounded-md p-0.5 backdrop-blur-sm shadow-sm"
    >
      <button
        class="widget-drag-handle flex items-center justify-center h-6 w-6 rounded text-muted-foreground hover:text-foreground hover:bg-accent cursor-grab active:cursor-grabbing transition-colors"
        :title="$t('dashboard.builder.dragToReorder', 'Drag to reorder')"
        type="button"
      >
        <GripVertical class="h-3.5 w-3.5" />
      </button>
      <button
        class="flex items-center justify-center h-6 w-6 rounded text-muted-foreground hover:text-destructive hover:bg-destructive/10 transition-colors"
        :title="$t('dashboard.builder.removeWidget', 'Remove widget')"
        type="button"
        @click="emit('remove', config.id)"
      >
        <X class="h-3.5 w-3.5" />
      </button>
    </div>

    <!-- Widget renderer -->
    <template v-if="config.type === 'entity-count'">
      <EntityCountWidget
        :counts="data.entityCounts"
        :is-loading="data.isLoading"
      />
    </template>

    <template v-else-if="config.type === 'recent-activity'">
      <RecentActivityWidget
        :activities="data.recentActivity as any[]"
        :is-loading="data.isLoading"
      />
    </template>

    <template v-else-if="config.type === 'quick-links'">
      <QuickLinksWidget />
    </template>

    <template v-else-if="config.type === 'system-health'">
      <SystemHealthWidget
        :stats="data.cacheStats"
        :is-loading="data.isLoading"
      />
    </template>

    <template v-else-if="config.type === 'kpi'">
      <KpiCardEnhanced
        :title="(config.settings as KpiSettings).title"
        :value="resolveKpiValue(config.settings as KpiSettings)"
        :description="(config.settings as KpiSettings).description"
        :color="(config.settings as KpiSettings).color"
        :loading="data.isLoading"
      />
    </template>

    <template v-else-if="config.type === 'entity-bar-chart'">
      <BarChart
        :data="resolveBarChartData(config.settings as EntityBarChartSettings)"
        :title="(config.settings as EntityBarChartSettings).title ?? t('dashboard.entityOverview')"
        :height="240"
        :max-bars="(config.settings as EntityBarChartSettings).maxBars ?? 10"
      />
    </template>
  </div>
</template>
