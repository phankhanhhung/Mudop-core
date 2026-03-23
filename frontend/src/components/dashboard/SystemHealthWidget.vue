<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { CacheStats } from '@/services/dashboardService'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Spinner } from '@/components/ui/spinner'
import {
  HeartPulse,
  CheckCircle,
  AlertTriangle,
  Database,
  Shield,
  Gavel,
  Package,
  ServerCrash
} from 'lucide-vue-next'
import { formatTime } from '@/utils/formatting'
import { type Component } from 'vue'

interface Props {
  stats: CacheStats | null
  isLoading: boolean
}

const props = defineProps<Props>()
const { t } = useI18n()

const isHealthy = computed(() => {
  if (!props.stats) return false
  return props.stats.entityCount > 0 && props.stats.moduleCount > 0
})

interface StatItem {
  label: string
  value: number
  icon: Component
  color: string
}

const statItems = computed<StatItem[]>(() => {
  if (!props.stats) return []
  return [
    {
      label: t('dashboard.entityDefinitions'),
      value: props.stats.entityCount,
      icon: Database,
      color: 'text-emerald-500'
    },
    {
      label: t('dashboard.businessRules'),
      value: props.stats.ruleCount,
      icon: Gavel,
      color: 'text-amber-500'
    },
    {
      label: t('dashboard.accessControls'),
      value: props.stats.accessControlCount,
      icon: Shield,
      color: 'text-violet-500'
    },
    {
      label: t('dashboard.modulesLoaded'),
      value: props.stats.moduleCount,
      icon: Package,
      color: 'text-cyan-500'
    }
  ]
})

function formatReloadTime(dateString?: string): string {
  if (!dateString) return t('common.unknown')
  return formatTime(dateString)
}
</script>

<template>
  <Card>
    <CardHeader class="pb-3">
      <div class="flex items-center justify-between">
        <div class="flex items-center gap-3">
          <div class="h-9 w-9 rounded-lg bg-rose-500/10 flex items-center justify-center">
            <HeartPulse class="h-5 w-5 text-rose-500" />
          </div>
          <div>
            <CardTitle class="text-base">{{ $t('dashboard.systemHealth') }}</CardTitle>
            <CardDescription>{{ $t('dashboard.systemHealthSubtitle') }}</CardDescription>
          </div>
        </div>
        <Badge
          v-if="!isLoading && stats"
          :variant="isHealthy ? 'default' : 'destructive'"
          :class="isHealthy ? 'bg-emerald-600 hover:bg-emerald-700' : ''"
        >
          <component :is="isHealthy ? CheckCircle : AlertTriangle" class="h-3 w-3 mr-1" />
          {{ isHealthy ? $t('dashboard.healthy') : $t('dashboard.degraded') }}
        </Badge>
      </div>
    </CardHeader>
    <CardContent>
      <div v-if="isLoading" class="flex flex-col items-center justify-center py-8">
        <Spinner size="lg" />
        <p class="text-muted-foreground mt-3 text-sm">{{ $t('common.loading') }}</p>
      </div>

      <div v-else-if="!stats" class="flex flex-col items-center justify-center py-8">
        <div class="h-14 w-14 rounded-full bg-muted flex items-center justify-center mb-3">
          <ServerCrash class="h-7 w-7 text-muted-foreground" />
        </div>
        <h3 class="text-sm font-semibold mb-1">{{ $t('dashboard.unableToRetrieve') }}</h3>
        <p class="text-xs text-muted-foreground text-center max-w-xs">
          {{ $t('dashboard.unableToRetrieveHint') }}
        </p>
      </div>

      <div v-else>
        <!-- Status indicator -->
        <div v-if="isHealthy" class="flex items-center gap-2 px-3 py-2 rounded-lg bg-emerald-50 dark:bg-emerald-950/30 border border-emerald-200 dark:border-emerald-900 mb-4">
          <CheckCircle class="h-4 w-4 text-emerald-600 dark:text-emerald-400 shrink-0" />
          <span class="text-xs font-medium text-emerald-700 dark:text-emerald-300">
            {{ $t('dashboard.allSystemsOperational') }}
          </span>
        </div>

        <!-- Stats grid -->
        <div class="grid grid-cols-2 gap-3">
          <div
            v-for="item in statItems"
            :key="item.label"
            class="flex items-center gap-3 p-2.5 rounded-lg bg-muted/50"
          >
            <component :is="item.icon" class="h-4 w-4 shrink-0" :class="item.color" />
            <div class="min-w-0">
              <p class="text-lg font-bold leading-none">{{ item.value }}</p>
              <p class="text-[10px] text-muted-foreground mt-0.5 truncate">{{ item.label }}</p>
            </div>
          </div>
        </div>

        <!-- Last reload -->
        <div v-if="stats.lastReloadAt" class="pt-3 mt-3 border-t text-xs text-muted-foreground flex items-center gap-1.5">
          <span>{{ $t('dashboard.lastReload', { time: formatReloadTime(stats.lastReloadAt) }) }}</span>
        </div>
      </div>
    </CardContent>
  </Card>
</template>
