<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useMetadataStore } from '@/stores/metadata'
import { useFormLayoutDesigner } from '@/composables/useFormLayoutDesigner'
import { useConfirmDialog } from '@/composables/useConfirmDialog'
import { useUiStore } from '@/stores/ui'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import FormCanvas from '@/components/admin/FormCanvas.vue'
import FieldPalette from '@/components/admin/FieldPalette.vue'
import SmartForm from '@/components/smart/SmartForm.vue'
import ConfirmDialog from '@/components/common/ConfirmDialog.vue'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import { Alert, AlertDescription } from '@/components/ui/alert'
import {
  LayoutTemplate, Eye, Pencil, Save, RotateCcw, AlertCircle,
  Plus, Trash2, Copy, Check
} from 'lucide-vue-next'
import type { EntityMetadata } from '@/types/metadata'

const { t } = useI18n()
const metadataStore = useMetadataStore()
const uiStore = useUiStore()
const confirmDialog = useConfirmDialog()

// ── Module / entity selection ─────────────────────────────────────────────────
const selectedModule = ref('')
const selectedEntity = ref('')

const entityOptions = computed<string[]>(() => {
  if (!selectedModule.value) return []
  const mod = metadataStore.getModule(selectedModule.value)
  if (!mod) return []
  return [...new Set(mod.services.flatMap((s) => s.entities.map((e) => e.entityType)))]
})

// Reset entity when module changes
watch(selectedModule, () => {
  selectedEntity.value = ''
})

// ── Load metadata when entity selected ───────────────────────────────────────
const metadata = ref<EntityMetadata | null>(null)
const metaLoading = ref(false)
const metaError = ref<string | null>(null)

watch(selectedEntity, async (entityName) => {
  metadata.value = null
  if (!entityName || !selectedModule.value) return
  metaLoading.value = true
  metaError.value = null
  try {
    metadata.value = await metadataStore.fetchEntity(selectedModule.value, entityName)
  } catch (e) {
    metaError.value = e instanceof Error ? e.message : 'Failed to load entity metadata'
  } finally {
    metaLoading.value = false
  }
})

// ── Load modules on mount ─────────────────────────────────────────────────────
metadataStore.fetchModules().catch(() => {})

// ── Form layout designer composable ──────────────────────────────────────────
const namespace = computed(() => {
  if (!selectedModule.value || !metadata.value) return ''
  return metadata.value.namespace ?? selectedModule.value
})
const entityNameRef = computed(() => selectedEntity.value)

const {
  savedLayouts,
  activeLayoutId,
  activeLayoutName,
  layout,
  isLoading,
  isSaving,
  isDirty,
  error: layoutError,
  paletteFields,
  designerSections,
  selectLayout,
  newLayout,
  saveLayout,
  saveAsNew,
  deleteLayout,
  renameLayout,
  resetToDefault,
  reorderSections,
  reorderFieldsInSection,
  moveFieldToSection,
  hideField,
  addSection,
  removeSection,
  renameSection,
  setFieldWidth,
  setColumns,
} = useFormLayoutDesigner(namespace, entityNameRef, metadata)

// ── Preview / Edit toggle ─────────────────────────────────────────────────────
const previewMode = ref(false)

// ── Layout name prompt ────────────────────────────────────────────────────────
const showNamePrompt = ref(false)
const promptMode = ref<'save' | 'saveAs' | 'rename'>('save')
const promptLayoutId = ref<string | null>(null)
const nameInput = ref('')

function openSavePrompt() {
  if (activeLayoutId.value) {
    // Existing layout — save directly
    handleSave()
  } else {
    // New layout — ask for name
    promptMode.value = 'save'
    nameInput.value = ''
    showNamePrompt.value = true
  }
}

function openSaveAsPrompt() {
  promptMode.value = 'saveAs'
  nameInput.value = activeLayoutName.value ? `${activeLayoutName.value} (Copy)` : ''
  showNamePrompt.value = true
}

function openRenamePrompt(id: string, currentName: string) {
  promptMode.value = 'rename'
  promptLayoutId.value = id
  nameInput.value = currentName
  showNamePrompt.value = true
}

async function handleNameConfirm() {
  const name = nameInput.value.trim()
  if (!name) return
  showNamePrompt.value = false
  try {
    if (promptMode.value === 'save') {
      await saveLayout(name)
      uiStore.success(t('admin.formDesigner.savedSuccess'))
    } else if (promptMode.value === 'saveAs') {
      await saveAsNew(name)
      uiStore.success(t('admin.formDesigner.savedSuccess'))
    } else if (promptMode.value === 'rename' && promptLayoutId.value) {
      await renameLayout(promptLayoutId.value, name)
    }
  } catch {
    uiStore.error(t('admin.formDesigner.saveError'))
  }
}

// ── Save ──────────────────────────────────────────────────────────────────────
async function handleSave() {
  try {
    await saveLayout()
    uiStore.success(t('admin.formDesigner.savedSuccess'))
  } catch {
    uiStore.error(t('admin.formDesigner.saveError'))
  }
}

// ── Reset ─────────────────────────────────────────────────────────────────────
async function handleReset() {
  const confirmed = await confirmDialog.confirm({
    title: t('admin.formDesigner.reset'),
    description: t('admin.formDesigner.resetConfirm'),
    variant: 'destructive',
    confirmLabel: t('admin.formDesigner.reset'),
  })
  if (confirmed) await resetToDefault()
}

// ── Delete ────────────────────────────────────────────────────────────────────
async function handleDelete(id: string) {
  const confirmed = await confirmDialog.confirm({
    title: t('admin.formDesigner.deleteLayout'),
    description: t('admin.formDesigner.deleteConfirm'),
    variant: 'destructive',
    confirmLabel: t('common.delete'),
  })
  if (confirmed) {
    await deleteLayout(id)
    uiStore.success(t('admin.formDesigner.deletedSuccess'))
  }
}

// ── Canvas event wiring ───────────────────────────────────────────────────────
function onDropFromPalette(sectionId: string, fieldName: string) {
  moveFieldToSection(fieldName, sectionId)
}

function onShowInSection(fieldName: string) {
  if (designerSections.value.length === 0) return
  moveFieldToSection(fieldName, designerSections.value[0].id)
}
</script>

<template>
  <DefaultLayout>
    <template #header>
      <div class="flex items-center gap-2">
        <LayoutTemplate class="h-5 w-5 text-muted-foreground" />
        <h1 class="text-lg font-semibold">{{ $t('admin.formDesigner.title') }}</h1>
      </div>
    </template>

    <div class="flex flex-col gap-6 p-6">
      <!-- Module / Entity selectors + toolbar -->
      <Card>
        <CardContent class="flex flex-wrap items-end gap-4 pt-4">
          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium" for="module-select">Module</label>
            <select
              id="module-select"
              v-model="selectedModule"
              class="h-9 rounded-md border bg-background px-3 text-sm"
            >
              <option value="" disabled>Select module…</option>
              <option v-for="m in metadataStore.moduleNames" :key="m" :value="m">{{ m }}</option>
            </select>
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium" for="entity-select">Entity</label>
            <select
              id="entity-select"
              v-model="selectedEntity"
              :disabled="!selectedModule || entityOptions.length === 0"
              class="h-9 rounded-md border bg-background px-3 text-sm disabled:opacity-50"
            >
              <option value="" disabled>Select entity…</option>
              <option v-for="e in entityOptions" :key="e" :value="e">{{ e }}</option>
            </select>
          </div>

          <div class="flex items-center gap-2 ml-auto">
            <Button
              variant="outline"
              size="sm"
              @click="previewMode = !previewMode"
              :disabled="!layout"
            >
              <Eye v-if="!previewMode" class="mr-1.5 h-4 w-4" />
              <Pencil v-else class="mr-1.5 h-4 w-4" />
              {{ previewMode ? $t('admin.formDesigner.editMode') : $t('admin.formDesigner.preview') }}
            </Button>
            <Button
              variant="outline"
              size="sm"
              :disabled="!isDirty || isSaving"
              @click="handleReset"
            >
              <RotateCcw class="mr-1.5 h-4 w-4" />
              {{ $t('admin.formDesigner.reset') }}
            </Button>
            <Button
              variant="outline"
              size="sm"
              :disabled="!activeLayoutId || !layout"
              @click="openSaveAsPrompt"
            >
              <Copy class="mr-1.5 h-4 w-4" />
              {{ $t('admin.formDesigner.saveAs') }}
            </Button>
            <Button
              size="sm"
              :disabled="!isDirty || isSaving"
              @click="openSavePrompt"
            >
              <Spinner v-if="isSaving" class="mr-1.5 h-4 w-4" />
              <Save v-else class="mr-1.5 h-4 w-4" />
              {{ isSaving ? $t('admin.formDesigner.saving') : $t('admin.formDesigner.save') }}
            </Button>
          </div>
        </CardContent>
      </Card>

      <!-- Error states -->
      <Alert v-if="metaError || layoutError" variant="destructive">
        <AlertCircle class="h-4 w-4" />
        <AlertDescription>{{ metaError ?? layoutError }}</AlertDescription>
      </Alert>

      <!-- Loading / empty state -->
      <div v-if="!selectedEntity" class="flex items-center justify-center py-20 text-muted-foreground text-sm">
        {{ $t('admin.formDesigner.noEntity') }}
      </div>
      <div v-else-if="metaLoading || isLoading" class="flex items-center justify-center py-20">
        <Spinner class="h-6 w-6" />
      </div>

      <!-- Main content: layout list + designer -->
      <div v-else-if="layout" class="flex gap-6">
        <!-- Left sidebar: layout list + palette -->
        <aside v-if="!previewMode" class="w-64 shrink-0 flex flex-col gap-4">
          <!-- Layout list -->
          <Card>
            <CardHeader class="pb-3 pt-4 px-4">
              <div class="flex items-center justify-between">
                <CardTitle class="text-sm">{{ $t('admin.formDesigner.layoutList') }}</CardTitle>
                <Button variant="ghost" size="icon" class="h-7 w-7" @click="newLayout">
                  <Plus class="h-4 w-4" />
                </Button>
              </div>
            </CardHeader>
            <CardContent class="px-2 pb-3">
              <div v-if="savedLayouts.length === 0" class="px-2 py-3 text-xs text-muted-foreground text-center">
                {{ $t('admin.formDesigner.noLayouts') }}
              </div>
              <div v-else class="flex flex-col gap-0.5">
                <div
                  v-for="sl in savedLayouts"
                  :key="sl.id"
                  class="group flex items-center gap-1 rounded-md px-2 py-1.5 text-sm cursor-pointer transition-colors"
                  :class="sl.id === activeLayoutId
                    ? 'bg-primary/10 text-primary font-medium'
                    : 'hover:bg-muted text-foreground'"
                  @click="selectLayout(sl.id)"
                >
                  <Check v-if="sl.id === activeLayoutId" class="h-3.5 w-3.5 shrink-0" />
                  <span class="truncate flex-1">{{ sl.name }}</span>
                  <div class="hidden group-hover:flex items-center gap-0.5 shrink-0">
                    <button
                      class="h-5 w-5 flex items-center justify-center rounded hover:bg-muted-foreground/20"
                      :title="$t('common.edit')"
                      @click.stop="openRenamePrompt(sl.id, sl.name)"
                    >
                      <Pencil class="h-3 w-3" />
                    </button>
                    <button
                      class="h-5 w-5 flex items-center justify-center rounded hover:bg-destructive/20 text-destructive"
                      :title="$t('common.delete')"
                      @click.stop="handleDelete(sl.id)"
                    >
                      <Trash2 class="h-3 w-3" />
                    </button>
                  </div>
                </div>
              </div>
              <!-- New unsaved indicator -->
              <div
                v-if="!activeLayoutId && layout"
                class="flex items-center gap-1 rounded-md px-2 py-1.5 text-sm bg-amber-500/10 text-amber-700 dark:text-amber-400 font-medium mt-1"
              >
                <Plus class="h-3.5 w-3.5 shrink-0" />
                <span class="truncate italic">{{ $t('admin.formDesigner.unsavedNew') }}</span>
              </div>
            </CardContent>
          </Card>

          <!-- Field palette -->
          <Card>
            <CardHeader class="pb-3 pt-4 px-4">
              <CardTitle class="text-sm">{{ $t('admin.formDesigner.palette') }}</CardTitle>
              <p class="text-xs text-muted-foreground">{{ $t('admin.formDesigner.paletteHint') }}</p>
            </CardHeader>
            <CardContent class="px-4 pb-4">
              <FieldPalette
                :fields="paletteFields"
                @show-in-section="onShowInSection"
              />
            </CardContent>
          </Card>
        </aside>

        <!-- Main canvas -->
        <div class="flex-1 min-w-0">
          <!-- Active layout name banner -->
          <div v-if="activeLayoutName" class="mb-3 text-sm text-muted-foreground flex items-center gap-2">
            <LayoutTemplate class="h-4 w-4" />
            <span class="font-medium text-foreground">{{ activeLayoutName }}</span>
            <span v-if="isDirty" class="text-amber-600 dark:text-amber-400 text-xs">(unsaved changes)</span>
          </div>

          <Card>
            <CardContent class="p-4">
              <!-- Edit mode: drag-and-drop canvas -->
              <FormCanvas
                v-if="!previewMode"
                :sections="designerSections"
                :columns="layout.columns"
                @reorder-sections="reorderSections"
                @reorder-fields="reorderFieldsInSection"
                @hide-field="hideField"
                @rename-section="renameSection"
                @remove-section="removeSection"
                @set-field-width="setFieldWidth"
                @add-section="addSection"
                @set-columns="setColumns"
                @drop-from-palette="onDropFromPalette"
              />

              <!-- Preview mode: actual SmartForm with layout override -->
              <SmartForm
                v-else-if="metadata"
                :metadata="metadata"
                :module="selectedModule"
                :entity-set="selectedEntity"
                mode="display"
                :layout-override="layout"
              />
            </CardContent>
          </Card>
        </div>
      </div>
    </div>

    <!-- Name prompt dialog -->
    <Teleport to="body">
      <Transition
        enter-active-class="transition ease-out duration-150"
        enter-from-class="opacity-0"
        enter-to-class="opacity-100"
        leave-active-class="transition ease-in duration-100"
        leave-from-class="opacity-100"
        leave-to-class="opacity-0"
      >
        <div
          v-if="showNamePrompt"
          class="fixed inset-0 z-[100] flex items-center justify-center bg-black/50"
          @keydown.escape="showNamePrompt = false"
        >
          <Card class="w-full max-w-sm mx-4 shadow-xl">
            <CardContent class="p-6">
              <h3 class="text-lg font-semibold mb-4">
                {{ promptMode === 'rename'
                  ? $t('admin.formDesigner.renameLayout')
                  : $t('admin.formDesigner.layoutName') }}
              </h3>
              <input
                ref="nameInputEl"
                v-model="nameInput"
                type="text"
                class="w-full h-9 rounded-md border bg-background px-3 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
                :placeholder="$t('admin.formDesigner.layoutNamePlaceholder')"
                @keydown.enter="handleNameConfirm"
              />
              <div class="flex justify-end gap-2 mt-4">
                <Button variant="outline" size="sm" @click="showNamePrompt = false">
                  {{ $t('common.cancel') }}
                </Button>
                <Button size="sm" :disabled="!nameInput.trim()" @click="handleNameConfirm">
                  {{ promptMode === 'rename' ? $t('admin.formDesigner.renameLayout') : $t('admin.formDesigner.save') }}
                </Button>
              </div>
            </CardContent>
          </Card>
        </div>
      </Transition>
    </Teleport>

    <ConfirmDialog
      :open="confirmDialog.isOpen.value"
      :title="confirmDialog.title.value"
      :description="confirmDialog.description.value"
      :confirm-label="confirmDialog.confirmLabel.value"
      :cancel-label="confirmDialog.cancelLabel.value"
      :variant="confirmDialog.variant.value"
      @confirm="confirmDialog.handleConfirm"
      @cancel="confirmDialog.handleCancel"
    />
  </DefaultLayout>
</template>
