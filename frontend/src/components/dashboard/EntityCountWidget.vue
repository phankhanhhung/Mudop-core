<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import type { EntityCount } from '@/services/dashboardService'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Spinner } from '@/components/ui/spinner'
import { Database, ArrowRight, Table2, BarChart3 } from 'lucide-vue-next'
import { formatInteger } from '@/utils/formatting'

interface Props {
  counts: EntityCount[]
  isLoading: boolean
}

const props = defineProps<Props>()
const router = useRouter()

const sortedCounts = computed(() =>
  [...props.counts].sort((a, b) => b.count - a.count).slice(0, 10)
)

const totalCount = computed(() =>
  props.counts.reduce((sum, e) => sum + e.count, 0)
)

const maxCount = computed(() => {
  if (sortedCounts.value.length === 0) return 1
  return Math.max(...sortedCounts.value.map(e => e.count), 1)
})

function navigateTo(item: EntityCount) {
  router.push(`/odata/${item.module}/${item.entityType}`)
}

function getBarColor(index: number): string {
  const colors = [
    'bg-primary',
    'bg-emerald-500',
    'bg-violet-500',
    'bg-amber-500',
    'bg-cyan-500',
    'bg-rose-500',
    'bg-indigo-500',
    'bg-teal-500',
    'bg-pink-500',
    'bg-orange-500'
  ]
  return colors[index % colors.length]
}
</script>

<template>
  <Card class="col-span-1 lg:col-span-2">
    <CardHeader class="pb-3">
      <div class="flex items-center justify-between">
        <div class="flex items-center gap-3">
          <div class="h-9 w-9 rounded-lg bg-primary/10 flex items-center justify-center">
            <BarChart3 class="h-5 w-5 text-primary" />
          </div>
          <div>
            <CardTitle class="text-base">{{ $t('dashboard.entityOverview') }}</CardTitle>
            <CardDescription>{{ $t('dashboard.entityOverviewSubtitle') }}</CardDescription>
          </div>
        </div>
        <Badge v-if="!isLoading && counts.length > 0" variant="secondary" class="font-mono">
          {{ formatInteger(totalCount) }} {{ $t('dashboard.records') }}
        </Badge>
      </div>
    </CardHeader>
    <CardContent>
      <div v-if="isLoading" class="flex flex-col items-center justify-center py-12">
        <Spinner size="lg" />
        <p class="text-muted-foreground mt-3 text-sm">{{ $t('common.loading') }}</p>
      </div>

      <div v-else-if="sortedCounts.length === 0" class="flex flex-col items-center justify-center py-12">
        <div class="h-14 w-14 rounded-full bg-muted flex items-center justify-center mb-3">
          <Database class="h-7 w-7 text-muted-foreground" />
        </div>
        <h3 class="text-sm font-semibold mb-1">{{ $t('dashboard.noEntityData') }}</h3>
        <p class="text-xs text-muted-foreground text-center max-w-xs">
          {{ $t('dashboard.noEntityDataHint') }}
        </p>
      </div>

      <div v-else>
        <p v-if="counts.length > 10" class="text-xs text-muted-foreground mb-3">
          {{ $t('dashboard.topEntities', { count: Math.min(sortedCounts.length, 10) }) }}
        </p>
        <div class="space-y-2">
          <button
            v-for="(item, index) in sortedCounts"
            :key="`${item.module}-${item.entityType}`"
            class="group flex items-center gap-3 w-full px-3 py-2.5 rounded-lg text-sm hover:bg-accent transition-colors text-left"
            @click="navigateTo(item)"
          >
            <!-- Entity icon -->
            <div class="h-8 w-8 rounded-md bg-muted flex items-center justify-center shrink-0">
              <Table2 class="h-4 w-4 text-muted-foreground" />
            </div>

            <!-- Name and module -->
            <div class="flex-1 min-w-0">
              <div class="flex items-center gap-2">
                <span class="font-medium truncate">{{ item.entity }}</span>
                <Badge variant="outline" class="text-[10px] px-1.5 py-0 shrink-0">
                  {{ item.module }}
                </Badge>
              </div>
              <!-- Progress bar -->
              <div class="mt-1.5 h-1.5 w-full bg-muted rounded-full overflow-hidden">
                <div
                  class="h-full rounded-full transition-all duration-500"
                  :class="getBarColor(index)"
                  :style="{ width: `${Math.max((item.count / maxCount) * 100, 2)}%` }"
                />
              </div>
            </div>

            <!-- Count -->
            <div class="flex items-center gap-2 shrink-0">
              <span class="font-mono text-sm font-medium tabular-nums">{{ formatInteger(item.count) }}</span>
              <ArrowRight class="h-3.5 w-3.5 text-muted-foreground opacity-0 group-hover:opacity-100 transition-opacity" />
            </div>
          </button>
        </div>

        <button
          v-if="counts.length > 10"
          class="mt-4 flex items-center justify-center gap-2 text-sm text-primary hover:text-primary/80 w-full py-2 rounded-lg hover:bg-accent transition-colors"
          @click="router.push('/admin/modules')"
        >
          {{ $t('dashboard.viewAllEntities', { count: counts.length }) }}
          <ArrowRight class="h-3.5 w-3.5" />
        </button>
      </div>
    </CardContent>
  </Card>
</template>
