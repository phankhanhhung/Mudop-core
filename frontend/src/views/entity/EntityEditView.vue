<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRoute, RouterLink, onBeforeRouteLeave } from 'vue-router'
import { useMetadata } from '@/composables/useMetadata'
import { useUiStore } from '@/stores/ui'
import { useCompositionLoader } from '@/composables/useCompositionLoader'
import { useEntityNavigation } from '@/composables/useEntityNavigation'
import { odataService } from '@/services'
import { ETagManager } from '@/odata/ETagManager'
import type { ConflictInfo } from '@/odata/ETagManager'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardContent } from '@/components/ui/card'
import DynamicPage from '@/components/layout/DynamicPage.vue'
import DynamicPageHeader from '@/components/layout/DynamicPageHeader.vue'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Spinner } from '@/components/ui/spinner'
import {
  ArrowLeft,
  ChevronRight,
  Pencil,
  Hash,
  Layers,
  FileText,
  AlertTriangle,
  AlertCircle,
  RefreshCw,
  ShieldAlert,
  LayoutTemplate
} from 'lucide-vue-next'
import SmartForm from '@/components/smart/SmartForm.vue'
import ManyToManySection from '@/components/entity/ManyToManySection.vue'
import { useDraft } from '@/composables/useDraft'
import { useFormLayout } from '@/composables/useFormLayout'
import { ODataApiError } from '@/services/api'
import { useI18n } from 'vue-i18n'
import { useConfirmDialog } from '@/composables/useConfirmDialog'
import { ConfirmDialog } from '@/components/common'
import { usePlugins } from '@/composables/usePlugins'
import { useRecordLock } from '@/composables/useRecordLock'
import { useAuthStore } from '@/stores/auth'

const { t } = useI18n()
const confirmDialog = useConfirmDialog()
const route = useRoute()
const uiStore = useUiStore()

const module = computed(() => route.params.module as string)
const entity = computed(() => route.params.entity as string)
const entityId = computed(() => route.params.id as string)

const { runBeforeHooks, runAfterHooks } = usePlugins(entity, module)

// Form layout selection
const { layouts, selectedLayoutId, selectedLayout, selectLayout, clearLayout } = useFormLayout(module, entity)

const authStore = useAuthStore()
const currentUserId = computed(() => authStore.user?.id ?? authStore.user?.username ?? '')
const currentUserName = computed(() => authStore.user?.username ?? authStore.user?.email ?? 'Anonymous')

const { claimLock, releaseLock } = useRecordLock(
  module.value, entity.value, entityId.value, currentUserId.value
)

// Parent context query params (when editing a child from composition section)
const { parentFk, parentId, parentEntity, goBack, goToList } = useEntityNavigation(module, entity, entityId)

const isChildEdit = computed(() => !!parentFk.value && !!parentId.value)

const {
  metadata,
  editableFields,
  isLoading: metadataLoading,
  error: metadataError,
  load: loadMetadata
} = useMetadata({
  module: module.value,
  entity: entity.value,
  autoLoad: false
})

// Draft management
const draft = useDraft({
  module: module.value,
  entitySet: entity.value,
  entityKey: entityId.value,
})

// ETag manager with manual conflict resolution strategy
const etagManager = new ETagManager(module.value, {
  defaultStrategy: 'manual',
  maxRetries: 1,
  onConflict: async (info: ConflictInfo) => {
    // Store conflict info for the UI to show
    conflictDetails.value = info
    // Return 'cancel' to let the form handle it via the existing concurrency UI
    return 'cancel'
  },
})
const conflictDetails = ref<ConflictInfo | null>(null)
const concurrencyError = ref(false)

const entityData = ref<Record<string, unknown> | null>(null)
const isLoadingData = ref(false)
const loadError = ref<string | null>(null)
const isSubmitting = ref(false)
const submitError = ref<string | null>(null)
const serverFieldErrors = ref<Record<string, string[]>>({})

const hasConcurrencyError = computed(() => concurrencyError.value || etagManager.hasConflict.value)
const lastSubmittedPayload = ref<Record<string, unknown> | null>(null)

const isSingleton = computed(() => metadata.value?.isSingleton === true)
const displayName = computed(() => metadata.value?.displayName || entity.value)
const isLoading = computed(() => metadataLoading.value || isLoadingData.value)
const error = computed(() => metadataError.value || loadError.value)

// Composition support
const { compositionAssociations, compositionMeta, loadCompositionMetadata } = useCompositionLoader(module, metadata)

// ManyToMany associations (no FK, not composition, cardinality Many)
const manyToManyAssociations = computed(() =>
  metadata.value?.associations.filter(
    (a) => a.cardinality === 'Many' && !a.foreignKey && !a.isComposition
  ) ?? []
)

const compositionData = ref<Record<string, Record<string, unknown>[]>>({})

onMounted(async () => {
  await loadMetadata()
  await Promise.all([
    loadEntity(),
    loadCompositionMetadata()
  ])
  await loadCompositionData()
  await claimLock(currentUserName.value)
})

async function loadCompositionData() {
  const result: Record<string, Record<string, unknown>[]> = {}
  for (const comp of compositionAssociations.value) {
    try {
      const response = await odataService.getChildren<Record<string, unknown>>(
        module.value,
        entity.value,
        entityId.value,
        comp.name
      )
      result[comp.name] = response.value ?? []
    } catch (err) {
      console.warn(`Failed to load composition '${comp.name}':`, err)
      result[comp.name] = []
    }
  }
  compositionData.value = result
}

async function loadEntity() {
  isLoadingData.value = true
  loadError.value = null

  try {
    if (isSingleton.value) {
      entityData.value = await odataService.getSingleton<Record<string, unknown>>(
        module.value, entity.value)
    } else {
      entityData.value = await odataService.getById(module.value, entity.value, entityId.value)
    }

    // Draft integration: resume existing draft or initialize new one
    if (draft.existingDraft.value) {
      const draftData = draft.resumeDraft()
      if (draftData) {
        entityData.value = { ...entityData.value, ...draftData }
      }
    } else {
      draft.initDraft(entityData.value as Record<string, unknown>)
    }
  } catch (e) {
    loadError.value = e instanceof Error ? e.message : t('entity.failedToLoad')
  } finally {
    isLoadingData.value = false
  }
}

async function handleSubmit(
  data: Record<string, unknown>,
  compData: Record<string, Record<string, unknown>[]>
) {
  isSubmitting.value = true
  submitError.value = null
  serverFieldErrors.value = {}

  try {
    // Merge composition rows into payload for deep update
    const payload = { ...data }
    for (const [navName, rows] of Object.entries(compData)) {
      payload[navName] = rows
    }

    // Merge parent FK into payload (implicit composition FK not in metadata)
    if (parentFk.value && parentId.value) {
      if (!(parentFk.value in payload)) {
        payload[parentFk.value] = parentId.value
      }
    }

    // Store the payload in case we need to retry without ETag
    lastSubmittedPayload.value = payload

    // Activate (finalize) the draft before submitting
    draft.activateDraft()

    const proceed = await runBeforeHooks('update', payload, entityId.value)
    if (!proceed) return

    // Use ETagManager for update with conflict handling (singletons use updateSingleton)
    if (isSingleton.value) {
      await odataService.updateSingleton(module.value, entity.value, payload)
    } else {
      await etagManager.updateWithRetry(entity.value, entityId.value, payload)
    }
    await runAfterHooks('update', payload, entityId.value)
    await releaseLock()
    uiStore.success(t('entity.edit.success'), t('entity.edit.successMessage', { name: displayName.value }))
    goBack()
  } catch (e) {
    if (etagManager.hasConflict.value) {
      concurrencyError.value = true
      submitError.value = t('entity.edit.concurrencyError')
    } else if (e instanceof ODataApiError && e.isValidationError) {
      serverFieldErrors.value = { ...e.fieldErrors }
      if (e.code === 'CardinalityViolation') {
        submitError.value = 'Required associations are missing. Please fill in all required relationship fields.'
      } else if (e.code === 'ForeignKeyViolation') {
        submitError.value = 'One or more referenced records do not exist. Please verify association values.'
      } else {
        submitError.value = e.message
      }
      uiStore.error(t('entity.edit.failed'), submitError.value)
    } else {
      submitError.value = e instanceof Error ? e.message : t('entity.edit.failedMessage')
      uiStore.error(t('entity.edit.failed'), submitError.value)
    }
  } finally {
    isSubmitting.value = false
  }
}

async function handleConcurrencyReload() {
  concurrencyError.value = false
  etagManager.clearConflict()
  conflictDetails.value = null
  submitError.value = null
  lastSubmittedPayload.value = null
  await loadEntity()
  await loadCompositionData()
  uiStore.success(t('entity.edit.reloaded'), t('entity.edit.reloadedMessage'))
}

async function handleConcurrencyOverwrite() {
  if (!lastSubmittedPayload.value) return
  isSubmitting.value = true
  submitError.value = null

  try {
    // Use ETagManager force update
    await etagManager.forceUpdate(entity.value, entityId.value, lastSubmittedPayload.value)
    concurrencyError.value = false
    etagManager.clearConflict()
    conflictDetails.value = null
    lastSubmittedPayload.value = null
    uiStore.success(t('entity.edit.success'), t('entity.edit.overwriteSuccess', { name: displayName.value }))
    goBack()
  } catch (e) {
    submitError.value = e instanceof Error ? e.message : 'Failed to update record'
    uiStore.error('Update failed', submitError.value)
  } finally {
    isSubmitting.value = false
  }
}

function handleSmartSubmit(data: Record<string, unknown>, compData?: Record<string, Record<string, unknown>[]>) {
  handleSubmit(data, compData ?? {})
}

function handleDirtyChange(_isDirty: boolean) {
  // SmartForm tracks dirty state internally; draft auto-save handled by useDraft
}

function handleCancel() {
  goBack()
}

// Navigation guard: warn about unsaved draft changes
onBeforeRouteLeave(async () => {
  const msg = draft.guardMessage.value
  if (!msg) return true
  return await confirmDialog.confirm({
    title: 'Unsaved Changes',
    description: msg,
    confirmLabel: 'Leave',
    cancelLabel: 'Stay'
  })
})

onUnmounted(async () => {
  await releaseLock()
})

// Short ID for display
const shortId = computed(() => {
  const id = entityId.value
  if (id && id.length > 12) {
    return id.substring(0, 8) + '...'
  }
  return id
})

// Stats
const stats = computed(() => ({
  fields: editableFields.value.length,
  required: editableFields.value.filter(f => f.isRequired).length,
  compositions: compositionAssociations.value.length
}))
</script>

<template>
  <DefaultLayout>
    <!-- Loading state -->
    <div v-if="isLoading" class="flex flex-col items-center justify-center py-20">
      <Spinner size="lg" />
      <p class="text-muted-foreground mt-3 text-sm">{{ $t('common.loading') }}</p>
    </div>

    <!-- Error state (non-concurrency errors only) -->
    <Card v-else-if="error && !hasConcurrencyError">
      <CardContent class="flex flex-col items-center justify-center py-16">
        <div class="h-16 w-16 rounded-full bg-destructive/10 flex items-center justify-center mb-4">
          <AlertCircle class="h-8 w-8 text-destructive" />
        </div>
        <h3 class="text-lg font-semibold mb-1">{{ $t('common.error') }}</h3>
        <p class="text-muted-foreground text-sm mb-4 text-center max-w-sm">{{ error }}</p>
        <Button @click="goToList">
          <ArrowLeft class="mr-2 h-4 w-4" />
          {{ $t('common.goBack') }}
        </Button>
      </CardContent>
    </Card>

    <!-- Edit form: DynamicPage -->
    <DynamicPage
      v-if="!isLoading && entityData && (!error || hasConcurrencyError)"
      showBackButton
      :headerCollapsible="!hasConcurrencyError"
      class="min-h-[calc(100vh-10rem)]"
      @back="goBack"
    >
      <template #breadcrumb>
        <div class="flex items-center gap-2 text-sm text-muted-foreground">
          <RouterLink
            :to="`/odata/${module}/${entity}`"
            class="hover:text-foreground transition-colors"
          >
            {{ module }}
          </RouterLink>
          <ChevronRight class="h-3.5 w-3.5" />
          <RouterLink
            :to="`/odata/${module}/${entity}`"
            class="hover:text-foreground transition-colors"
          >
            {{ displayName }}
          </RouterLink>
          <ChevronRight class="h-3.5 w-3.5" />
          <RouterLink
            :to="`/odata/${module}/${entity}/${entityId}`"
            class="hover:text-foreground transition-colors"
          >
            {{ shortId }}
          </RouterLink>
          <ChevronRight class="h-3.5 w-3.5" />
          <span class="text-foreground font-medium">{{ $t('common.edit') }}</span>
        </div>
      </template>

      <template #title>
        <div class="flex items-center gap-3">
          <div class="h-8 w-8 rounded-lg bg-amber-500/10 flex items-center justify-center shrink-0">
            <Pencil class="h-4 w-4 text-amber-600" />
          </div>
          <div class="min-w-0">
            <h1 class="text-xl font-semibold truncate">
              {{ $t('entity.edit.title', { name: displayName }) }}
            </h1>
            <div class="flex items-center gap-3">
              <span class="text-sm text-muted-foreground font-mono truncate">{{ entityId }}</span>
              <Badge v-if="isChildEdit" variant="outline" class="text-xs shrink-0">
                {{ $t('entity.createdChildOf', { parent: parentEntity }) }}
              </Badge>
            </div>
          </div>
        </div>
      </template>

      <template #headerActions>
        <div v-if="layouts.length > 0" class="flex items-center gap-2 mr-2">
          <LayoutTemplate class="h-4 w-4 text-muted-foreground" />
          <select
            :value="selectedLayoutId ?? ''"
            class="h-8 rounded-md border border-input bg-background px-2 text-sm"
            @change="($event.target as HTMLSelectElement).value ? selectLayout(($event.target as HTMLSelectElement).value) : clearLayout()"
          >
            <option value="">{{ $t('entity.defaultFormLayout') }}</option>
            <option v-for="l in layouts" :key="l.id" :value="l.id">{{ l.name }}</option>
          </select>
        </div>
        <Button variant="ghost" size="sm" @click="goToList">
          {{ $t('common.cancel') }}
        </Button>
      </template>

      <template #header>
        <DynamicPageHeader>
          <div class="flex items-center gap-3 px-4 py-3 rounded-lg bg-muted/30 border border-border/50">
            <Hash class="h-5 w-5 text-muted-foreground" />
            <div>
              <p class="text-xs text-muted-foreground uppercase tracking-wider">{{ $t('entity.fields') }}</p>
              <p class="text-xl font-bold">{{ stats.fields }}</p>
            </div>
          </div>
          <div class="flex items-center gap-3 px-4 py-3 rounded-lg bg-muted/30 border border-border/50">
            <FileText class="h-5 w-5 text-amber-500" />
            <div>
              <p class="text-xs text-muted-foreground uppercase tracking-wider">{{ $t('entity.requiredFields') }}</p>
              <p class="text-xl font-bold text-amber-600">{{ stats.required }}</p>
            </div>
          </div>
          <div class="flex items-center gap-3 px-4 py-3 rounded-lg bg-muted/30 border border-border/50">
            <Layers class="h-5 w-5 text-violet-500" />
            <div>
              <p class="text-xs text-muted-foreground uppercase tracking-wider">{{ $t('entity.compositions') }}</p>
              <p class="text-xl font-bold text-violet-600">{{ stats.compositions }}</p>
            </div>
          </div>
        </DynamicPageHeader>

        <!-- Concurrency conflict alert (always visible, not collapsible) -->
        <div
          v-if="hasConcurrencyError"
          class="mt-4 rounded-lg border border-orange-300 bg-orange-50 p-4 dark:border-orange-700 dark:bg-orange-950"
        >
          <div class="flex items-start gap-4">
            <div class="h-10 w-10 rounded-full bg-orange-500/10 flex items-center justify-center shrink-0">
              <ShieldAlert class="h-5 w-5 text-orange-600 dark:text-orange-400" />
            </div>
            <div class="flex-1">
              <h4 class="font-semibold text-orange-800 dark:text-orange-200">
                {{ $t('entity.edit.conflictDetected') }}
              </h4>
              <p class="mt-1 text-sm text-orange-700 dark:text-orange-300">
                {{ $t('entity.edit.conflictMessage') }}
              </p>
              <div class="mt-3 flex gap-2">
                <Button size="sm" variant="outline" @click="handleConcurrencyReload">
                  <RefreshCw class="mr-2 h-4 w-4" />
                  {{ $t('entity.edit.reload') }}
                </Button>
                <Button size="sm" variant="destructive" @click="handleConcurrencyOverwrite">
                  <AlertTriangle class="mr-2 h-4 w-4" />
                  {{ $t('entity.edit.overwrite') }}
                </Button>
              </div>
            </div>
          </div>
        </div>
      </template>

      <!-- Form content (full width) -->
      <SmartForm
        v-if="metadata"
        :module="module"
        :entitySet="entity"
        :metadata="metadata"
        :data="entityData ?? undefined"
        mode="edit"
        :isLoading="isSubmitting"
        :error="submitError"
        :serverErrors="serverFieldErrors"
        :associations="metadata?.associations ?? []"
        :compositions="compositionAssociations"
        :compositionMetadata="compositionMeta"
        :compositionData="compositionData"
        :layoutOverride="selectedLayout"
        @submit="handleSmartSubmit"
        @cancel="handleCancel"
        @dirty-change="handleDirtyChange"
      />

      <!-- ManyToMany sections (immediate operations, not batched with form) -->
      <div v-if="manyToManyAssociations.length > 0" class="mt-6 space-y-4">
        <ManyToManySection
          v-for="assoc in manyToManyAssociations"
          :key="assoc.name"
          :module="module"
          :parentEntity="entity"
          :parentId="entityId"
          :association="assoc"
        />
      </div>
    </DynamicPage>
    <ConfirmDialog
      :open="confirmDialog.isOpen.value"
      :title="confirmDialog.title.value"
      :description="confirmDialog.description.value"
      :confirm-label="confirmDialog.confirmLabel.value"
      :cancel-label="confirmDialog.cancelLabel.value"
      :variant="confirmDialog.variant.value"
      @confirm="confirmDialog.handleConfirm"
      @cancel="confirmDialog.handleCancel"
      @update:open="confirmDialog.isOpen.value = $event"
    />
  </DefaultLayout>
</template>
