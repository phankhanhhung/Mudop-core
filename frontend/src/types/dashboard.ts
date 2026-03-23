import type { CacheStats, EntityCount } from '@/services/dashboardService'

export type WidgetType =
  | 'entity-count'
  | 'recent-activity'
  | 'quick-links'
  | 'system-health'
  | 'kpi'
  | 'entity-bar-chart'

export interface EntityCountSettings {
  module?: string
}

export interface RecentActivitySettings {
  limit?: number
}

export type QuickLinksSettings = Record<string, never>
export type SystemHealthSettings = Record<string, never>

export interface KpiSettings {
  title: string
  description?: string
  color: 'primary' | 'emerald' | 'violet' | 'amber' | 'rose' | 'cyan'
  valueSource: 'total-records' | 'module-count' | 'entity-type-count' | 'static'
  staticValue?: string
}

export interface EntityBarChartSettings {
  title?: string
  maxBars?: number
}

export type WidgetSettings =
  | EntityCountSettings
  | RecentActivitySettings
  | QuickLinksSettings
  | SystemHealthSettings
  | KpiSettings
  | EntityBarChartSettings

export interface WidgetConfig {
  id: string
  type: WidgetType
  span: 1 | 2 | 3
  settings: WidgetSettings
}

export interface DashboardLayout {
  version: 1
  widgets: WidgetConfig[]
  columns: 3 | 4
}

export interface DashboardData {
  cacheStats: CacheStats | null
  entityCounts: EntityCount[]
  recentActivity: unknown[]
  isLoading: boolean
}

export interface WidgetCatalogEntry {
  type: WidgetType
  titleKey: string
  descKey: string
  defaultSpan: 1 | 2 | 3
  defaultSettings: WidgetSettings
}

export const WIDGET_CATALOG: WidgetCatalogEntry[] = [
  { type: 'entity-count', titleKey: 'dashboard.builder.widgetEntityCount', descKey: 'dashboard.builder.widgetEntityCountDesc', defaultSpan: 2, defaultSettings: {} },
  { type: 'recent-activity', titleKey: 'dashboard.builder.widgetRecentActivity', descKey: 'dashboard.builder.widgetRecentActivityDesc', defaultSpan: 1, defaultSettings: {} },
  { type: 'quick-links', titleKey: 'dashboard.builder.widgetQuickLinks', descKey: 'dashboard.builder.widgetQuickLinksDesc', defaultSpan: 2, defaultSettings: {} },
  { type: 'system-health', titleKey: 'dashboard.builder.widgetSystemHealth', descKey: 'dashboard.builder.widgetSystemHealthDesc', defaultSpan: 1, defaultSettings: {} },
  { type: 'kpi', titleKey: 'dashboard.builder.widgetKpi', descKey: 'dashboard.builder.widgetKpiDesc', defaultSpan: 1, defaultSettings: { title: 'Total Records', color: 'primary', valueSource: 'total-records' } as KpiSettings },
  { type: 'entity-bar-chart', titleKey: 'dashboard.builder.widgetEntityBarChart', descKey: 'dashboard.builder.widgetEntityBarChartDesc', defaultSpan: 3, defaultSettings: {} as EntityBarChartSettings },
]

export const DEFAULT_DASHBOARD_LAYOUT: DashboardLayout = {
  version: 1,
  columns: 3,
  widgets: [
    { id: 'default-entity-count', type: 'entity-count', span: 2, settings: {} },
    { id: 'default-recent-activity', type: 'recent-activity', span: 1, settings: {} },
    { id: 'default-system-health', type: 'system-health', span: 1, settings: {} },
    { id: 'default-quick-links', type: 'quick-links', span: 2, settings: {} },
  ],
}
