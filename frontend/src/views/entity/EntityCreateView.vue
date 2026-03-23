<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter, RouterLink, onBeforeRouteLeave } from 'vue-router'
import { useMetadata } from '@/composables/useMetadata'
import { useUiStore } from '@/stores/ui'
import { useCompositionLoader } from '@/composables/useCompositionLoader'
import { useEntityNavigation } from '@/composables/useEntityNavigation'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardContent } from '@/components/ui/card'
import DynamicPage from '@/components/layout/DynamicPage.vue'
import DynamicPageHeader from '@/components/layout/DynamicPageHeader.vue'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import {
  ArrowLeft,
  ChevronRight,
  Plus,
  Database,
  Hash,
  Layers,
  FileText,
  AlertCircle,
  LayoutTemplate
} from 'lucide-vue-next'
import SmartForm from '@/components/smart/SmartForm.vue'
import { useDraft } from '@/composables/useDraft'
import { useFormLayout } from '@/composables/useFormLayout'
import { odataService } from '@/services'
import { ODataApiError } from '@/services/api'
import { useI18n } from 'vue-i18n'
import { useConfirmDialog } from '@/composables/useConfirmDialog'
import { ConfirmDialog } from '@/components/common'
import { usePlugins } from '@/composables/usePlugins'

const { t } = useI18n()
const confirmDialog = useConfirmDialog()
const route = useRoute()
const router = useRouter()
const uiStore = useUiStore()

const module = computed(() => route.params.module as string)
const entity = computed(() => route.params.entity as string)

const { runBeforeHooks, runAfterHooks } = usePlugins(entity, module)

// Form layout selection
const { layouts, selectedLayoutId, selectedLayout, selectLayout, clearLayout } = useFormLayout(module, entity)

// Parent FK query params (when creating a child from composition section)
const { parentFk, parentId, parentEntity, parentModule, goBack } = useEntityNavigation(module, entity)

const isChildCreation = computed(() => !!parentFk.value && !!parentId.value)

const parentInitialData = computed(() => {
  if (parentFk.value && parentId.value) {
    return { [parentFk.value]: parentId.value }
  }
  return undefined
})

// Mutable initialData — starts with parent FK, may be enriched with draft data
const initialData = ref<Record<string, unknown> | undefined>(undefined)

const {
  metadata,
  editableFields,
  keyFields,
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
})

const isSubmitting = ref(false)
const submitError = ref<string | null>(null)
const serverFieldErrors = ref<Record<string, string[]>>({})

const displayName = computed(() => metadata.value?.displayName || entity.value)
const keyField = computed(() => keyFields.value[0] || 'id')

// Composition support
const { compositionAssociations, compositionMeta, loadCompositionMetadata } = useCompositionLoader(module, metadata)

onMounted(async () => {
  await loadMetadata()

  // Guard: abstract entities cannot be created directly
  if (metadata.value?.isAbstract) {
    uiStore.error('Cannot Create', 'Cannot create abstract entity directly')
    router.replace(`/odata/${module.value}/${entity.value}`)
    return
  }

  // Guard: singletons redirect to edit
  if (metadata.value?.isSingleton) {
    router.replace(`/odata/${module.value}/${entity.value}/_singleton/edit`)
    return
  }

  await loadCompositionMetadata()

  // Draft integration: resume existing draft or initialize new one
  const baseData = parentInitialData.value
  if (draft.existingDraft.value) {
    const draftData = draft.resumeDraft()
    if (draftData) {
      initialData.value = { ...baseData, ...draftData }
    } else {
      initialData.value = baseData
    }
  } else {
    initialData.value = baseData
    draft.initDraft(baseData)
  }
})

async function handleSubmit(
  data: Record<string, unknown>,
  compositionData: Record<string, Record<string, unknown>[]>
) {
  isSubmitting.value = true
  submitError.value = null
  serverFieldErrors.value = {}

  try {
    // Merge composition rows into payload for deep insert
    const payload = { ...data }

    // Merge parent FK from parentInitialData (for composition child creation)
    // The FK field is implicit and not in metadata, so EntityForm doesn't include it
    if (parentInitialData.value) {
      for (const [key, value] of Object.entries(parentInitialData.value)) {
        if (!(key in payload)) {
          payload[key] = value
        }
      }
    }

    for (const [navName, rows] of Object.entries(compositionData)) {
      if (rows.length > 0) {
        payload[navName] = rows
      }
    }

    // Activate (finalize) the draft before submitting
    draft.activateDraft()

    const proceed = await runBeforeHooks('create', payload)
    if (!proceed) return

    const created = await odataService.create<Record<string, unknown>>(module.value, entity.value, payload)
    await runAfterHooks('create', created as Record<string, unknown>)
    uiStore.success(t('entity.create.success'), t('entity.create.successMessage', { name: displayName.value }))

    if (parentEntity.value && parentId.value) {
      // Navigate back to parent detail view (bust route key cache to force remount)
      const mod = parentModule.value || module.value
      router.push(`/odata/${mod}/${parentEntity.value}/${parentId.value}?_t=${Date.now()}`)
    } else {
      // Navigate to the new entity detail
      const key = keyField.value
      const id = created[key] ?? Object.entries(created).find(([k]) => k.toLowerCase() === key.toLowerCase())?.[1]
      router.push(`/odata/${module.value}/${entity.value}/${id}`)
    }
  } catch (e) {
    if (e instanceof ODataApiError && e.isValidationError) {
      serverFieldErrors.value = { ...e.fieldErrors }
      if (e.code === 'CardinalityViolation') {
        submitError.value = 'Required associations are missing. Please fill in all required relationship fields.'
      } else if (e.code === 'ForeignKeyViolation') {
        submitError.value = 'One or more referenced records do not exist. Please verify association values.'
      } else {
        submitError.value = e.message
      }
    } else {
      serverFieldErrors.value = {}
      submitError.value = e instanceof Error ? e.message : t('entity.create.failedMessage')
    }
    uiStore.error(t('entity.create.failed'), submitError.value)
  } finally {
    isSubmitting.value = false
  }
}

function handleSmartSubmit(data: Record<string, unknown>, compData?: Record<string, Record<string, unknown>[]>) {
  handleSubmit(data, compData ?? {})
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
    <div v-if="metadataLoading" class="flex flex-col items-center justify-center py-20">
      <Spinner size="lg" />
      <p class="text-muted-foreground mt-3 text-sm">{{ $t('common.loading') }}</p>
    </div>

    <!-- Error state -->
    <Card v-else-if="metadataError">
      <CardContent class="flex flex-col items-center justify-center py-16">
        <div class="h-16 w-16 rounded-full bg-destructive/10 flex items-center justify-center mb-4">
          <AlertCircle class="h-8 w-8 text-destructive" />
        </div>
        <h3 class="text-lg font-semibold mb-1">{{ $t('common.error') }}</h3>
        <p class="text-muted-foreground text-sm mb-4 text-center max-w-sm">{{ metadataError }}</p>
        <Button @click="goBack">
          <ArrowLeft class="mr-2 h-4 w-4" />
          {{ $t('common.goBack') }}
        </Button>
      </CardContent>
    </Card>

    <!-- Create form: DynamicPage -->
    <DynamicPage
      v-else
      showBackButton
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
          <span class="text-foreground font-medium">{{ $t('entity.newRecord') }}</span>
        </div>
      </template>

      <template #title>
        <div class="flex items-center gap-3">
          <div class="h-8 w-8 rounded-lg bg-emerald-500/10 flex items-center justify-center shrink-0">
            <Plus class="h-4 w-4 text-emerald-600" />
          </div>
          <div class="min-w-0">
            <h1 class="text-xl font-semibold truncate">
              {{ $t('entity.create.title', { name: displayName }) }}
            </h1>
            <p class="text-sm text-muted-foreground truncate">
              <span v-if="isChildCreation">{{ $t('entity.createdChildOf', { parent: parentEntity }) }}</span>
              <span v-else>{{ $t('entity.create.subtitle', { name: displayName.toLowerCase() }) }}</span>
            </p>
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
        <Button variant="ghost" size="sm" @click="goBack">
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
          <div v-if="isChildCreation" class="flex items-center gap-3 px-4 py-3 rounded-lg bg-muted/30 border border-border/50">
            <Database class="h-5 w-5 text-muted-foreground" />
            <div>
              <p class="text-xs text-muted-foreground uppercase tracking-wider">Parent</p>
              <RouterLink
                :to="`/odata/${parentModule || module}/${parentEntity}/${parentId}`"
                class="text-sm text-primary hover:underline"
              >
                {{ parentEntity }} / {{ parentId }}
              </RouterLink>
            </div>
          </div>
        </DynamicPageHeader>
      </template>

      <!-- Form content (full width) -->
      <SmartForm
        v-if="metadata"
        :module="module"
        :entitySet="entity"
        :metadata="metadata"
        :data="initialData"
        mode="create"
        :isLoading="isSubmitting"
        :error="submitError"
        :serverErrors="serverFieldErrors"
        :associations="metadata?.associations ?? []"
        :compositions="compositionAssociations"
        :compositionMetadata="compositionMeta"
        :layoutOverride="selectedLayout"
        @submit="handleSmartSubmit"
        @cancel="handleCancel"
      />
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
