<script setup lang="ts">
import { ref, watch, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import {
  LayoutDashboard,
  X,
  BarChart3,
  Activity,
  Zap,
  HeartPulse,
  TrendingUp,
  BarChart2
} from 'lucide-vue-next'
import { WIDGET_CATALOG, type WidgetType } from '@/types/dashboard'

const props = defineProps<{
  open: boolean
}>()

const emit = defineEmits<{
  close: []
  add: [type: WidgetType]
}>()

const { t } = useI18n()
const dialogRef = ref<HTMLElement | null>(null)

watch(
  () => props.open,
  (val) => {
    if (val) nextTick(() => dialogRef.value?.focus())
  }
)

const widgetIconMap: Record<WidgetType, unknown> = {
  'entity-count': BarChart3,
  'recent-activity': Activity,
  'quick-links': Zap,
  'system-health': HeartPulse,
  'kpi': TrendingUp,
  'entity-bar-chart': BarChart2,
}

function handleAdd(type: WidgetType) {
  emit('add', type)
  emit('close')
}
</script>

<template>
  <Teleport to="body">
    <Transition
      enter-active-class="transition ease-out duration-200"
      enter-from-class="opacity-0"
      enter-to-class="opacity-100"
      leave-active-class="transition ease-in duration-150"
      leave-from-class="opacity-100"
      leave-to-class="opacity-0"
    >
      <div
        v-if="open"
        class="fixed inset-0 z-[100] flex items-end sm:items-center justify-center bg-black/50"
        @keydown.escape="emit('close')"
      >
        <div
          ref="dialogRef"
          role="dialog"
          aria-modal="true"
          aria-labelledby="widget-catalog-title"
          tabindex="-1"
          class="w-full sm:max-w-lg mx-4 max-h-[80vh] flex flex-col outline-none"
          @keydown.escape.stop="emit('close')"
        >
          <Card class="shadow-xl flex flex-col min-h-0">
            <CardContent class="flex flex-col min-h-0 p-0">
              <!-- Header -->
              <div class="flex items-center justify-between px-6 py-4 border-b shrink-0">
                <div class="flex items-center gap-2">
                  <LayoutDashboard class="h-5 w-5 text-primary" />
                  <h2
                    id="widget-catalog-title"
                    class="text-lg font-semibold leading-none"
                  >
                    {{ $t('dashboard.builder.widgetCatalog') }}
                  </h2>
                </div>
                <Button
                  variant="ghost"
                  size="sm"
                  class="h-8 w-8 p-0"
                  :aria-label="$t('common.close')"
                  @click="emit('close')"
                >
                  <X class="h-4 w-4" />
                </Button>
              </div>

              <!-- Body -->
              <div class="flex-1 overflow-y-auto px-6 py-4">
                <p class="text-sm text-muted-foreground mb-4">
                  {{ $t('dashboard.builder.widgetCatalogSubtitle') }}
                </p>

                <div
                  v-for="entry in WIDGET_CATALOG"
                  :key="entry.type"
                  class="flex items-start gap-3 p-3 rounded-lg border hover:bg-accent/50 transition-colors mb-2"
                >
                  <!-- Icon -->
                  <div class="shrink-0 flex h-9 w-9 items-center justify-center rounded-md bg-primary/10 mt-0.5">
                    <component
                      :is="widgetIconMap[entry.type]"
                      class="h-5 w-5 text-primary"
                    />
                  </div>

                  <!-- Info -->
                  <div class="flex-1 min-w-0">
                    <div class="flex items-center gap-2 flex-wrap">
                      <span class="text-sm font-medium leading-none">
                        {{ $t(entry.titleKey) }}
                      </span>
                      <Badge variant="secondary" class="text-xs px-1.5 py-0">
                        {{ $t('dashboard.builder.span') }} {{ entry.defaultSpan }}
                      </Badge>
                    </div>
                    <p class="mt-1 text-xs text-muted-foreground">
                      {{ $t(entry.descKey) }}
                    </p>
                  </div>

                  <!-- Add button -->
                  <Button
                    variant="outline"
                    size="sm"
                    class="shrink-0"
                    @click="handleAdd(entry.type)"
                  >
                    {{ $t('common.add') }}
                  </Button>
                </div>
              </div>
            </CardContent>
          </Card>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>
