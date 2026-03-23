<script setup lang="ts">
import { ref, watch, nextTick, onBeforeUnmount } from 'vue'
import Sortable from 'sortablejs'
import type { DesignerSection, DesignerField } from '@/types/formLayout'
import { GripVertical, X, Plus, ChevronDown, ChevronRight } from 'lucide-vue-next'

const props = defineProps<{
  sections: DesignerSection[]
  columns: 1 | 2 | 3
}>()

const emit = defineEmits<{
  'reorder-sections': [from: number, to: number]
  'reorder-fields': [sectionId: string, from: number, to: number]
  'hide-field': [sectionId: string, fieldName: string]
  'rename-section': [sectionId: string, title: string]
  'remove-section': [sectionId: string]
  'set-field-width': [sectionId: string, fieldName: string, width: 'full' | 'half' | 'third']
  'add-section': []
  'set-columns': [columns: 1 | 2 | 3]
  'drop-from-palette': [sectionId: string, fieldName: string]
}>()

// ── Section list sortable ──────────────────────────────────────────────────────
const sectionListEl = ref<HTMLElement | null>(null)
let sectionSortable: Sortable | null = null

// ── Per-section field sortables ───────────────────────────────────────────────
const fieldListRefs = ref<Record<string, HTMLElement | null>>({})
const fieldSortables: Record<string, Sortable> = {}

// ── Inline rename state ───────────────────────────────────────────────────────
const editingSectionId = ref<string | null>(null)
const editingTitle = ref('')

function startRename(section: DesignerSection) {
  editingSectionId.value = section.id
  editingTitle.value = section.title
  nextTick(() => {
    const el = document.getElementById(`rename-${section.id}`)
    el?.focus()
    ;(el as HTMLInputElement)?.select()
  })
}

function commitRename(sectionId: string) {
  const title = editingTitle.value.trim()
  if (title) emit('rename-section', sectionId, title)
  editingSectionId.value = null
}

// ── Sortable init helpers ─────────────────────────────────────────────────────
function initSectionSortable() {
  if (!sectionListEl.value) return
  sectionSortable?.destroy()
  sectionSortable = Sortable.create(sectionListEl.value, {
    handle: '.section-drag-handle',
    animation: 150,
    ghostClass: 'opacity-40',
    onEnd(evt) {
      const from = evt.oldIndex
      const to = evt.newIndex
      if (from !== undefined && to !== undefined && from !== to) {
        emit('reorder-sections', from, to)
      }
    },
  })
}

function initFieldSortable(sectionId: string, el: HTMLElement) {
  fieldSortables[sectionId]?.destroy()
  fieldSortables[sectionId] = Sortable.create(el, {
    group: { name: 'fields', put: true },
    handle: '.field-drag-handle',
    animation: 150,
    ghostClass: 'opacity-40',
    onEnd(evt) {
      const from = evt.oldIndex
      const to = evt.newIndex
      const fromContainer = evt.from
      const toContainer = evt.to
      const fromSectionId = fromContainer.dataset.sectionId
      const toSectionId = toContainer.dataset.sectionId
      if (from === undefined || to === undefined) return

      if (fromSectionId === toSectionId) {
        if (from !== to) emit('reorder-fields', sectionId, from, to)
      }
      // Cross-section moves are handled by moveFieldToSection via onAdd
    },
    onAdd(evt) {
      // Item dropped from palette or another section
      const fieldName = evt.item.dataset.fieldName ?? ''
      if (fieldName) {
        // Remove the cloned DOM node — Vue handles rendering via reactive data
        evt.item.parentNode?.removeChild(evt.item)
        emit('drop-from-palette', sectionId, fieldName)
      }
    },
  })
}

function setFieldRef(sectionId: string, el: HTMLElement | null) {
  fieldListRefs.value[sectionId] = el
  if (el) initFieldSortable(sectionId, el)
}

// ── Init section sortable once mounted ────────────────────────────────────────
watch(
  sectionListEl,
  (el) => { if (el) initSectionSortable() },
  { immediate: true },
)

onBeforeUnmount(() => {
  sectionSortable?.destroy()
  Object.values(fieldSortables).forEach((s) => s.destroy())
})

const WIDTHS = ['full', 'half', 'third'] as const
const WIDTH_LABELS: Record<string, string> = { full: 'F', half: 'H', third: 'T' }
</script>

<template>
  <div class="flex flex-col gap-4">
    <!-- Toolbar: columns + add section -->
    <div class="flex items-center justify-between">
      <div class="flex items-center gap-2 text-sm">
        <span class="text-muted-foreground">{{ $t('admin.formDesigner.columns') }}:</span>
        <div class="flex rounded-md border overflow-hidden">
          <button
            v-for="c in ([1, 2, 3] as const)"
            :key="c"
            type="button"
            class="px-3 py-1 text-sm transition-colors"
            :class="columns === c ? 'bg-primary text-primary-foreground' : 'hover:bg-muted'"
            @click="emit('set-columns', c)"
          >
            {{ c }}
          </button>
        </div>
      </div>
      <button
        type="button"
        class="flex items-center gap-1 text-sm text-primary hover:underline"
        @click="emit('add-section')"
      >
        <Plus class="h-4 w-4" />
        {{ $t('admin.formDesigner.addSection') }}
      </button>
    </div>

    <!-- Section list -->
    <ul
      ref="sectionListEl"
      class="flex flex-col gap-3"
      aria-label="Form sections"
    >
      <li
        v-for="section in sections"
        :key="section.id"
        class="rounded-lg border bg-card shadow-sm"
      >
        <!-- Section header -->
        <div class="flex items-center gap-2 border-b px-3 py-2">
          <span class="section-drag-handle cursor-grab text-muted-foreground active:cursor-grabbing">
            <GripVertical class="h-4 w-4" />
          </span>

          <!-- Inline rename -->
          <template v-if="editingSectionId === section.id">
            <input
              :id="`rename-${section.id}`"
              v-model="editingTitle"
              class="flex-1 rounded border px-2 py-0.5 text-sm outline-none focus:ring-1 focus:ring-primary"
              @keydown.enter="commitRename(section.id)"
              @keydown.escape="editingSectionId = null"
              @blur="commitRename(section.id)"
            />
          </template>
          <template v-else>
            <button
              type="button"
              class="flex-1 text-left text-sm font-medium hover:text-primary"
              :title="$t('admin.formDesigner.renamePlaceholder')"
              @dblclick="startRename(section)"
              @click.stop
            >
              {{ section.title }}
            </button>
          </template>

          <span class="text-xs text-muted-foreground">{{ section.fields.length }}</span>

          <button
            type="button"
            class="text-muted-foreground hover:text-destructive"
            :title="$t('admin.formDesigner.removeSection')"
            @click="emit('remove-section', section.id)"
          >
            <X class="h-4 w-4" />
          </button>
        </div>

        <!-- Field list -->
        <ul
          :ref="(el) => setFieldRef(section.id, el as HTMLElement | null)"
          :data-section-id="section.id"
          class="min-h-[48px] flex flex-col gap-1 p-2"
          :aria-label="`Fields in ${section.title}`"
        >
          <li
            v-for="field in section.fields"
            :key="field.name"
            :data-field-name="field.name"
            class="flex items-center gap-2 rounded border bg-background px-2 py-1.5 text-sm"
          >
            <span class="field-drag-handle cursor-grab text-muted-foreground active:cursor-grabbing">
              <GripVertical class="h-3.5 w-3.5" />
            </span>
            <span class="flex-1 font-medium">{{ field.displayName }}</span>
            <span class="text-xs text-muted-foreground">{{ field.type }}</span>

            <!-- Width selector -->
            <div class="flex rounded border overflow-hidden text-xs">
              <button
                v-for="w in WIDTHS"
                :key="w"
                type="button"
                class="px-1.5 py-0.5 transition-colors"
                :class="field.width === w ? 'bg-primary text-primary-foreground' : 'hover:bg-muted'"
                :title="$t(`admin.formDesigner.width${w.charAt(0).toUpperCase() + w.slice(1)}`)"
                @click="emit('set-field-width', section.id, field.name, w)"
              >
                {{ WIDTH_LABELS[w] }}
              </button>
            </div>

            <button
              type="button"
              class="text-muted-foreground hover:text-destructive"
              :title="$t('admin.formDesigner.hideField')"
              @click="emit('hide-field', section.id, field.name)"
            >
              <X class="h-3.5 w-3.5" />
            </button>
          </li>
          <li
            v-if="section.fields.length === 0"
            class="rounded border border-dashed border-muted-foreground/30 py-3 text-center text-xs text-muted-foreground"
          >
            {{ $t('admin.formDesigner.paletteHint') }}
          </li>
        </ul>
      </li>
    </ul>
  </div>
</template>
