<script setup lang="ts">
import { onMounted, onBeforeUnmount, ref } from 'vue'
import Sortable from 'sortablejs'
import type { DesignerField } from '@/types/formLayout'
import { GripVertical } from 'lucide-vue-next'

const props = defineProps<{
  fields: DesignerField[]
}>()

const emit = defineEmits<{
  'show-in-section': [fieldName: string]
}>()

const listEl = ref<HTMLElement | null>(null)
let sortable: Sortable | null = null

onMounted(() => {
  if (!listEl.value) return
  sortable = Sortable.create(listEl.value, {
    group: { name: 'fields', pull: 'clone', put: false },
    sort: false,
    handle: '.palette-drag-handle',
    ghostClass: 'opacity-40',
    animation: 150,
    // Data attached to cloned item so FormCanvas can read field name
    setData(dataTransfer, el) {
      const name = el.dataset.fieldName ?? ''
      dataTransfer.setData('text/plain', name)
    },
  })
})

onBeforeUnmount(() => {
  sortable?.destroy()
  sortable = null
})
</script>

<template>
  <div class="flex flex-col gap-1">
    <div
      v-if="fields.length === 0"
      class="rounded-md border border-dashed border-muted-foreground/30 p-4 text-center text-sm text-muted-foreground"
    >
      {{ $t('admin.formDesigner.paletteEmpty') }}
    </div>
    <ul ref="listEl" class="flex flex-col gap-1" aria-label="Available fields">
      <li
        v-for="field in fields"
        :key="field.name"
        :data-field-name="field.name"
        class="flex items-center gap-2 rounded-md border bg-background px-3 py-2 text-sm shadow-sm"
      >
        <span class="palette-drag-handle cursor-grab text-muted-foreground active:cursor-grabbing">
          <GripVertical class="h-4 w-4" />
        </span>
        <span class="flex-1 font-medium">{{ field.displayName }}</span>
        <span class="text-xs text-muted-foreground">{{ field.type }}</span>
        <button
          type="button"
          class="ml-1 text-xs text-primary hover:underline"
          :title="$t('admin.formDesigner.showField')"
          @click="emit('show-in-section', field.name)"
        >
          +
        </button>
      </li>
    </ul>
  </div>
</template>
