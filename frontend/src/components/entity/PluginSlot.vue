<script setup lang="ts">
import { computed } from 'vue'
import { pluginRegistry } from '@/lib/plugins'

type SlotType = 'detail-sections' | 'custom-views' | 'cell'

interface Props {
  slotType: SlotType
  entityType: string
  // For 'cell' slot type:
  fieldType?: string
  fieldName?: string
  value?: unknown
  row?: Record<string, unknown>
  // For 'detail-sections' / 'custom-views':
  context?: Record<string, unknown>
}

const props = withDefaults(defineProps<Props>(), {
  context: () => ({}),
})

const emit = defineEmits<{
  action: [event: { type: string; payload?: unknown }]
}>()

const detailSections = computed(() =>
  props.slotType === 'detail-sections'
    ? pluginRegistry.getDetailSections(props.entityType)
    : [],
)

const customViews = computed(() =>
  props.slotType === 'custom-views'
    ? pluginRegistry.getCustomViews(props.entityType)
    : [],
)

const cellRenderer = computed(() => {
  if (props.slotType !== 'cell' || !props.fieldType) return null
  return pluginRegistry.getColumnRenderer(props.fieldType, props.entityType, props.fieldName)?.component ?? null
})

function handleAction(event: { type: string; payload?: unknown }) {
  emit('action', event)
}
</script>

<template>
  <!-- Detail sections: render each registered section component -->
  <template v-if="slotType === 'detail-sections'">
    <div
      v-for="section in detailSections"
      :key="section.id"
      class="plugin-detail-section"
      :data-section-id="section.id"
      :data-plugin-id="section.pluginId"
    >
      <component
        :is="section.component"
        :entity-type="entityType"
        :label="section.label"
        v-bind="context"
        @action="handleAction"
      />
    </div>
  </template>

  <!-- Custom views: render each registered view component -->
  <template v-else-if="slotType === 'custom-views'">
    <div
      v-for="view in customViews"
      :key="view.id"
      class="plugin-custom-view"
      :data-view-id="view.id"
      :data-plugin-id="view.pluginId"
    >
      <component
        :is="view.component"
        :entity-type="entityType"
        :label="view.label"
        v-bind="context"
        @action="handleAction"
      />
    </div>
  </template>

  <!-- Cell renderer: render a single field value component -->
  <template v-else-if="slotType === 'cell' && cellRenderer">
    <component
      :is="cellRenderer"
      :value="value"
      :row="row"
      :field-type="fieldType"
      :field-name="fieldName"
      :entity-type="entityType"
      @action="handleAction"
    />
  </template>

  <!-- Fallback slot for 'cell' when no plugin renderer is registered -->
  <template v-else-if="slotType === 'cell'">
    <slot />
  </template>
</template>
