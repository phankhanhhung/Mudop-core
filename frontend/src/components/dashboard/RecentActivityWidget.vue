<script setup lang="ts">
import { computed } from 'vue'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card'
import { Spinner } from '@/components/ui/spinner'
import FeedList, { type FeedItem } from '@/components/smart/FeedList.vue'
import { Activity, Clock } from 'lucide-vue-next'

interface Props {
  activities: any[]
  isLoading: boolean
}

const props = defineProps<Props>()

// Convert activity data to FeedItem format
const feedItems = computed<FeedItem[]>(() =>
  props.activities.slice(0, 10).map((item, idx) => ({
    id: item.id || String(idx),
    author: item.userName || item.user || 'System',
    datetime: item.timestamp || item.createdAt || new Date().toISOString(),
    content: `${item.eventType || item.action || 'Action'}: ${item.entityName || item.entity || 'Unknown'}`,
  }))
)
</script>

<template>
  <Card>
    <CardHeader class="pb-3">
      <div class="flex items-center gap-3">
        <div class="h-9 w-9 rounded-lg bg-violet-500/10 flex items-center justify-center">
          <Activity class="h-5 w-5 text-violet-500" />
        </div>
        <div>
          <CardTitle class="text-base">{{ $t('dashboard.recentActivity') }}</CardTitle>
          <CardDescription>{{ $t('dashboard.recentActivitySubtitle') }}</CardDescription>
        </div>
      </div>
    </CardHeader>
    <CardContent>
      <div v-if="isLoading" class="flex flex-col items-center justify-center py-12">
        <Spinner size="lg" />
        <p class="text-muted-foreground mt-3 text-sm">{{ $t('common.loading') }}</p>
      </div>

      <div v-else-if="activities.length === 0" class="flex flex-col items-center justify-center py-12">
        <div class="h-14 w-14 rounded-full bg-muted flex items-center justify-center mb-3">
          <Clock class="h-7 w-7 text-muted-foreground" />
        </div>
        <h3 class="text-sm font-semibold mb-1">{{ $t('dashboard.noRecentActivity') }}</h3>
        <p class="text-xs text-muted-foreground text-center max-w-xs">
          {{ $t('dashboard.noRecentActivityHint') }}
        </p>
      </div>

      <!-- Activity feed -->
      <FeedList
        v-else
        :items="feedItems"
        :showInput="false"
        :readonly="true"
      />
    </CardContent>
  </Card>
</template>
