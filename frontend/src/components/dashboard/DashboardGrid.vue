<script setup lang="ts">
import { ref, watch, onMounted, onBeforeUnmount, computed } from 'vue'
import Sortable from 'sortablejs'
import type { WidgetConfig, DashboardData } from '@/types/dashboard'
import DashboardWidget from './DashboardWidget.vue'

interface Props {
  widgets: WidgetConfig[]
  columns: 3 | 4
  editMode: boolean
  dashboardData: DashboardData
}

const props = defineProps<Props>()
const emit = defineEmits<{
  reorder: [from: number, to: number]
  remove: [widgetId: string]
}>()

const gridEl = ref<HTMLElement | null>(null)
let sortable: Sortable | null = null

const columnsClass = computed(() =>
  props.columns === 4
    ? 'grid-cols-1 sm:grid-cols-2 lg:grid-cols-4'
    : 'grid-cols-1 sm:grid-cols-2 lg:grid-cols-3'
)

onMounted(() => {
  if (gridEl.value) {
    sortable = Sortable.create(gridEl.value, {
      handle: '.widget-drag-handle',
      animation: 150,
      ghostClass: 'opacity-40',
      disabled: !props.editMode,
      onEnd(evt) {
        if (
          evt.oldIndex !== undefined &&
          evt.newIndex !== undefined &&
          evt.oldIndex !== evt.newIndex
        ) {
          emit('reorder', evt.oldIndex, evt.newIndex)
        }
      },
    })
  }
})

watch(
  () => props.editMode,
  (val) => {
    sortable?.option('disabled', !val)
  }
)

onBeforeUnmount(() => {
  sortable?.destroy()
})
</script>

<template>
  <div>
    <!-- Empty state shown only in edit mode -->
    <div
      v-if="widgets.length === 0 && editMode"
      class="flex items-center justify-center rounded-lg border border-dashed border-border py-16 text-sm text-muted-foreground"
    >
      {{ $t('dashboard.builder.emptyHint', 'Add widgets to customize your dashboard') }}
    </div>

    <!-- Widget grid -->
    <div
      v-else
      ref="gridEl"
      class="grid gap-4"
      :class="columnsClass"
    >
      <div
        v-for="widget in widgets"
        :key="widget.id"
        :style="{ gridColumn: `span ${widget.span}` }"
      >
        <DashboardWidget
          :config="widget"
          :data="dashboardData"
          :edit-mode="editMode"
          @remove="emit('remove', $event)"
        />
      </div>
    </div>
  </div>
</template>
