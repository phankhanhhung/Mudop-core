<script setup lang="ts">
import { ref, computed, onMounted, watchEffect } from 'vue'
import { useRouter, RouterLink } from 'vue-router'
import { useMetadata } from '@/composables/useMetadata'
import { useEntityRouteParams } from '@/composables/useEntityRouteParams'
import { useAssociationHelpers } from '@/composables/useAssociationHelpers'
import { useConfirmDialog } from '@/composables/useConfirmDialog'
import { useUiStore } from '@/stores/ui'
import { odataService } from '@/services'
import { NavigationBinding } from '@/odata/NavigationBinding'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Spinner } from '@/components/ui/spinner'
import { ConfirmDialog } from '@/components/common'
import {
  ArrowLeft,
  Pencil,
  Trash2,
  History,
  Database,
  FileText,
  ChevronRight,
  Link2,
  Layers,
  Eye,
  Hash,
  Calendar,
  ExternalLink,
  AlertCircle
} from 'lucide-vue-next'
import SmartField from '@/components/smart/SmartField.vue'
import { useRecentItems } from '@/composables/useRecentItems'
import { formatDateTime } from '@/utils/formatting'
import type { FieldMetadata } from '@/types/metadata'
import CompositionSection from '@/components/entity/CompositionSection.vue'
import ManyToManySection from '@/components/entity/ManyToManySection.vue'
import IconTabBar from '@/components/smart/IconTabBar.vue'
import type { IconTab } from '@/composables/useIconTabBar'
import ReferenceManager from '@/components/entity/ReferenceManager.vue'
import MediaField from '@/components/entity/fields/MediaField.vue'
import ActionButtonGroup from '@/components/entity/ActionButtonGroup.vue'
import VersionHistory from '@/components/entity/VersionHistory.vue'
import ObjectPageLayout from '@/components/layout/ObjectPageLayout.vue'
import ObjectPageSection from '@/components/layout/ObjectPageSection.vue'
import ObjectPageHeaderKpi from '@/components/layout/ObjectPageHeaderKpi.vue'
import ObjectPageHeaderAttribute from '@/components/layout/ObjectPageHeaderAttribute.vue'
import { useI18n } from 'vue-i18n'
import PluginSlot from '@/components/entity/PluginSlot.vue'
import { usePlugins } from '@/composables/usePlugins'
import CommentSection from '@/components/entity/CommentSection.vue'
import RecordLockBadge from '@/components/entity/RecordLockBadge.vue'
import ChangeRequestPanel from '@/components/entity/ChangeRequestPanel.vue'
import { useRecordLock } from '@/composables/useRecordLock'
import { useAuthStore } from '@/stores/auth'

const { t } = useI18n()
const router = useRouter()
const uiStore = useUiStore()
const { addRecentItem } = useRecentItems()
const confirmDialog = useConfirmDialog()

const { module, entity, entityId: routeEntityId } = useEntityRouteParams()
const entityId = computed(() => routeEntityId.value ?? '')

const { detailSections: pluginSections } = usePlugins(entity, module)

const authStore = useAuthStore()
const currentUserId = computed(() => authStore.user?.id ?? authStore.user?.username ?? '')
const currentUserName = computed(() => authStore.user?.username ?? authStore.user?.email ?? 'You')

const { lockState, initialize: initializeLock } = useRecordLock(
  module.value, entity.value, entityId.value, currentUserId.value
)

// Show/hide collaboration sections
const showComments = ref(true)
const showChangeRequests = ref(false)

const {
  metadata,
  fields,
  isLoading: metadataLoading,
  error: metadataError,
  load: loadMetadata
} = useMetadata({
  module: module.value,
  entity: entity.value,
  autoLoad: false
})

const entityData = ref<Record<string, unknown> | null>(null)
const isLoadingData = ref(false)
const loadError = ref<string | null>(null)
const isDeleting = ref(false)
const concurrencyError = ref(false)

const displayName = computed(() => metadata.value?.displayName || entity.value)
const isLoading = computed(() => metadataLoading.value || isLoadingData.value)
const error = computed(() => metadataError.value || loadError.value)

const { expandableAssociations, getFormattedValue, getAssociationLink, isAssociationField } = useAssociationHelpers(fields, metadata, entityData)

onMounted(async () => {
  await loadMetadata()
  await loadEntity()

  // Track recently viewed entity
  if (entityData.value) {
    const title = entityData.value['Name'] || entityData.value['name'] || entityData.value['Title'] || entityData.value['title'] || entityId.value
    addRecentItem(module.value, entity.value, displayName.value, entityId.value, String(title))
  }

  await initializeLock()
})


async function loadEntity() {
  isLoadingData.value = true
  loadError.value = null

  try {
    const opts: Record<string, string> = {}
    const assocs = expandableAssociations.value
    if (assocs.length > 0) {
      opts.$expand = assocs.map((a) => a.name).join(',')
    }
    if (isSingleton.value) {
      entityData.value = await odataService.getSingleton<Record<string, unknown>>(
        module.value, entity.value, opts)
    } else {
      entityData.value = await odataService.getById<Record<string, unknown>>(
        module.value, entity.value, entityId.value, opts)
    }
  } catch (e) {
    loadError.value = e instanceof Error ? e.message : t('entity.failedToLoad')
  } finally {
    isLoadingData.value = false
  }
}

function goBack() {
  router.push(`/odata/${module.value}/${entity.value}`)
}

function handleEdit() {
  if (isSingleton.value) {
    router.push(`/odata/${module.value}/${entity.value}/_singleton/edit`)
  } else {
    router.push(`/odata/${module.value}/${entity.value}/${entityId.value}/edit`)
  }
}

async function handleDelete() {
  const confirmed = await confirmDialog.confirm({
    title: t('entity.deleteRecord'),
    description: t('entity.deleteConfirm'),
    confirmLabel: t('common.delete'),
    variant: 'destructive'
  })
  if (!confirmed) return

  isDeleting.value = true
  try {
    concurrencyError.value = false
    await odataService.delete(module.value, entity.value, entityId.value)
    uiStore.success(t('entity.deleted'), t('entity.deletedSuccess'))
    goBack()
  } catch (e) {
    const axiosErr = e as { response?: { status?: number } }
    if (axiosErr.response?.status === 412) {
      concurrencyError.value = true
      uiStore.error(
        t('entity.conflict.title'),
        t('entity.conflict.message')
      )
    } else if (axiosErr.response?.status === 409) {
      uiStore.error(
        'Cannot Delete',
        e instanceof Error ? e.message : 'This record is referenced by other entities and cannot be deleted.'
      )
    } else {
      uiStore.error(t('entity.deleteFailed'), e instanceof Error ? e.message : t('entity.deleteFailedMessage'))
    }
  } finally {
    isDeleting.value = false
  }
}

function getFieldValue(fieldName: string): unknown {
  return entityData.value?.[fieldName]
}

// System/audit fields to group separately
const systemFieldPatterns = [
  'CreatedAt', 'UpdatedAt', 'ModifiedAt', 'DeletedAt',
  'CreatedBy', 'UpdatedBy', 'ModifiedBy', 'DeletedBy',
  'TenantId', 'IsDeleted', 'SystemStart', 'SystemEnd', 'Version'
]

function isSystemField(field: FieldMetadata): boolean {
  return systemFieldPatterns.some(p => field.name === p)
}

function isKeyField(field: FieldMetadata): boolean {
  return metadata.value?.keys.includes(field.name) ?? false
}

// Grouped fields
const keyFieldsList = computed(() =>
  fields.value.filter(f => isKeyField(f))
)

const regularFields = computed(() =>
  fields.value.filter(f => !isKeyField(f) && !isSystemField(f) && !isAssociationField(f))
)

const associationFields = computed(() =>
  fields.value.filter(f => isAssociationField(f))
)

const systemFields = computed(() =>
  fields.value.filter(f => isSystemField(f))
)

// Composition associations (OneToMany contained children)
const compositionAssociations = computed(() =>
  metadata.value?.associations.filter(
    (a) => a.isComposition && (a.cardinality === 'Many' || a.cardinality === 'OneOrMore')
  ) ?? []
)

// Navigation bindings for composition sections (lazy loading)
const navigationBindings = ref(new Map<string, NavigationBinding>())

watchEffect((onCleanup) => {
  // Destroy old bindings when compositions change
  for (const binding of navigationBindings.value.values()) binding.destroy()

  const map = new Map<string, NavigationBinding>()
  for (const assoc of compositionAssociations.value) {
    map.set(
      assoc.name,
      new NavigationBinding({
        parentContext: { module: module.value, entitySet: entity.value, key: entityId.value },
        navigationProperty: assoc.name,
        association: assoc,
        lazy: true,
        pageSize: 10,
      })
    )
  }
  navigationBindings.value = map

  onCleanup(() => {
    for (const binding of navigationBindings.value.values()) binding.destroy()
  })
})

// Reference associations (ManyToOne / OneToOne that are NOT compositions)
const referenceAssociations = computed(() =>
  metadata.value?.associations.filter(
    (a) => !a.isComposition && (a.cardinality === 'ZeroOrOne' || a.cardinality === 'One')
  ) ?? []
)

// ManyToMany associations (no FK, not composition, cardinality Many)
const manyToManyAssociations = computed(() =>
  metadata.value?.associations.filter(
    (a) => a.cardinality === 'Many' && !a.foreignKey && !a.isComposition
  ) ?? []
)

// Temporal support
const isSingleton = computed(() => metadata.value?.isSingleton === true)
const isTemporal = computed(() => metadata.value?.isTemporal === true)
const showVersionHistory = ref(false)

// Composition tabs for IconTabBar (when multiple compositions exist)
const activeCompositionTab = ref('')
const compositionTabs = computed<IconTab[]>(() =>
  compositionAssociations.value.map((comp) => ({
    key: comp.name,
    label: comp.name,
    icon: 'Layers',
  }))
)
const hasMultipleCompositions = computed(() => compositionAssociations.value.length > 1)

// Stats for info cards
const stats = computed(() => ({
  fields: fields.value.length,
  compositions: compositionAssociations.value.length,
  associations: expandableAssociations.value.length
}))

// Short ID for display
const shortId = computed(() => {
  const id = entityId.value
  if (id && id.length > 12) {
    return id.substring(0, 8) + '...'
  }
  return id
})

// Audit info from entity data
const createdAt = computed(() => {
  const v = entityData.value?.['CreatedAt']
  return v ? formatDateTime(v as string) : null
})

const updatedAt = computed(() => {
  const v = entityData.value?.['UpdatedAt'] || entityData.value?.['ModifiedAt']
  return v ? formatDateTime(v as string) : null
})
</script>

<template>
  <DefaultLayout>
    <!-- Loading state -->
    <div v-if="isLoading" class="flex flex-col items-center justify-center py-20">
      <Spinner size="lg" />
      <p class="text-muted-foreground mt-3 text-sm">{{ $t('common.loading') }}</p>
    </div>

    <!-- Error state -->
    <Card v-else-if="error">
      <CardContent class="flex flex-col items-center justify-center py-16">
        <div class="h-16 w-16 rounded-full bg-destructive/10 flex items-center justify-center mb-4">
          <AlertCircle class="h-8 w-8 text-destructive" />
        </div>
        <h3 class="text-lg font-semibold mb-1">{{ $t('common.error') }}</h3>
        <p class="text-muted-foreground text-sm mb-4 text-center max-w-sm">{{ error }}</p>
        <Button @click="goBack">
          <ArrowLeft class="mr-2 h-4 w-4" />
          {{ $t('entity.backToList') }}
        </Button>
      </CardContent>
    </Card>

    <!-- Detail view: ObjectPageLayout -->
    <ObjectPageLayout v-else-if="entityData">
      <template #header>
        <!-- Title row (visible in both expanded and collapsed states) -->
        <div class="flex items-center gap-3">
          <div class="h-9 w-9 rounded-full bg-primary/10 flex items-center justify-center shrink-0">
            <Database class="h-5 w-5 text-primary" />
          </div>
          <div class="min-w-0 flex-1">
            <h1 class="text-xl font-semibold truncate">
              {{ $t('entity.details', { name: displayName }) }}
            </h1>
            <p class="text-sm text-muted-foreground truncate">{{ module }}.{{ entity }} &mdash; {{ shortId }}</p>
          </div>
          <Badge v-if="isTemporal" variant="outline" class="text-xs shrink-0">
            <History class="mr-1 h-3 w-3" />
            Temporal
          </Badge>
        </div>

        <!-- Below: visible when expanded, clipped when collapsed -->
        <!-- Breadcrumb -->
        <div class="flex items-center gap-2 text-sm text-muted-foreground mt-3">
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
          <span class="text-foreground font-medium">{{ shortId }}</span>
        </div>

        <!-- KPIs -->
        <div class="flex flex-wrap gap-4 mt-4">
          <ObjectPageHeaderKpi :label="$t('entity.fields')" :value="stats.fields" :icon="Hash" />
          <ObjectPageHeaderKpi :label="$t('entity.compositions')" :value="stats.compositions" :icon="Layers" />
          <ObjectPageHeaderKpi :label="$t('entity.associations')" :value="stats.associations" :icon="Link2" />
          <ObjectPageHeaderKpi v-if="createdAt" :label="$t('common.created')" :value="createdAt" :icon="Calendar" />
        </div>

        <!-- Attributes -->
        <div class="flex flex-wrap items-center gap-x-6 gap-y-1 mt-3 text-sm">
          <ObjectPageHeaderAttribute :label="$t('common.module')" :value="module" />
          <ObjectPageHeaderAttribute :label="$t('entity.recordId')" :value="entityId" />
          <ObjectPageHeaderAttribute v-if="updatedAt" :label="$t('common.update')" :value="updatedAt" />
        </div>
      </template>

      <template #headerActions>
        <ActionButtonGroup
          v-if="metadata?.boundActions?.length || metadata?.boundFunctions?.length"
          :actions="metadata?.boundActions ?? []"
          :functions="metadata?.boundFunctions ?? []"
          :module="module"
          :entitySet="entity"
          :entityId="entityId"
        />
        <Button
          v-if="isTemporal"
          variant="outline"
          size="sm"
          @click="showVersionHistory = true"
        >
          <History class="mr-2 h-4 w-4" />
          {{ $t('common.history') }}
        </Button>
        <RecordLockBadge :lock-state="lockState" :current-user-id="currentUserId" class="ml-2" />
        <Button variant="outline" size="sm" @click="handleEdit">
          <Pencil class="mr-2 h-4 w-4" />
          {{ $t('common.edit') }}
        </Button>
        <Button
          v-if="!isSingleton"
          variant="destructive"
          size="sm"
          @click="handleDelete"
          :disabled="isDeleting"
        >
          <Spinner v-if="isDeleting" size="sm" class="mr-2" />
          <Trash2 v-else class="mr-2 h-4 w-4" />
          {{ $t('common.delete') }}
        </Button>
      </template>

      <!-- Key Fields section -->
      <ObjectPageSection
        v-if="keyFieldsList.length > 0"
        id="key-fields"
        :title="$t('entity.keyFields')"
        :icon="Hash"
      >
        <dl class="grid gap-4 md:grid-cols-2">
          <div v-for="field in keyFieldsList" :key="field.name" class="space-y-1">
            <dt class="text-xs font-medium text-muted-foreground uppercase tracking-wider">
              {{ field.displayName || field.name }}
            </dt>
            <dd class="text-sm font-mono bg-muted/50 rounded px-2.5 py-1.5 break-all">
              <SmartField
                :field="field"
                :modelValue="getFieldValue(field.name)"
                mode="display"
                :module="module"
                :entitySet="entity"
                :showLabel="false"
              />
            </dd>
          </div>
        </dl>
      </ObjectPageSection>

      <!-- General Information section -->
      <ObjectPageSection
        v-if="regularFields.length > 0"
        id="general"
        :title="$t('entity.generalInfo')"
        :icon="FileText"
      >
        <dl class="grid gap-4 md:grid-cols-2">
          <div v-for="field in regularFields" :key="field.name" class="space-y-1">
            <dt class="text-xs font-medium text-muted-foreground uppercase tracking-wider">
              {{ field.displayName || field.name }}
            </dt>
            <dd>
              <SmartField
                :field="field"
                :modelValue="getFieldValue(field.name)"
                mode="display"
                :module="module"
                :entitySet="entity"
                :showLabel="false"
              />
            </dd>
          </div>
        </dl>
      </ObjectPageSection>

      <!-- Associations section -->
      <ObjectPageSection
        v-if="associationFields.length > 0"
        id="associations"
        :title="$t('entity.associations')"
        :icon="Link2"
      >
        <div class="divide-y">
          <div
            v-for="field in associationFields"
            :key="field.name"
            class="flex items-center justify-between py-3 first:pt-0 last:pb-0"
          >
            <div class="space-y-0.5">
              <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider">
                {{ field.displayName || field.name }}
              </p>
              <RouterLink
                v-if="getAssociationLink(field)"
                :to="getAssociationLink(field)!"
                class="text-sm text-primary hover:underline inline-flex items-center gap-1"
              >
                {{ getFormattedValue(field) }}
                <ExternalLink class="h-3 w-3" />
              </RouterLink>
              <span v-else class="text-sm text-muted-foreground">
                {{ getFormattedValue(field) || t('entity.noValue') }}
              </span>
            </div>
          </div>
        </div>
      </ObjectPageSection>

      <!-- Media Content section -->
      <ObjectPageSection
        v-if="metadata?.hasStream && entityData"
        id="media"
        :title="$t('entity.mediaContent')"
      >
        <MediaField
          :module="module"
          :entitySet="entity"
          :entityId="entityId"
          :mediaReadLink="entityData['@odata.mediaReadLink'] as string | undefined"
          :mediaContentType="entityData['@odata.mediaContentType'] as string | undefined"
          @uploaded="loadEntity()"
          @deleted="loadEntity()"
        />
      </ObjectPageSection>

      <!-- Composition sections: IconTabBar when multiple, stacked when single -->
      <template v-if="compositionAssociations.length > 0">
        <!-- Multiple compositions: IconTabBar -->
        <ObjectPageSection
          v-if="hasMultipleCompositions"
          id="compositions"
          :title="$t('entity.compositions')"
          :icon="Layers"
        >
          <IconTabBar
            v-model="activeCompositionTab"
            :tabs="compositionTabs"
            :dense="true"
          >
            <template v-for="comp in compositionAssociations" :key="comp.name" #[`tab-${comp.name}`]>
              <CompositionSection
                :module="module"
                :parentEntity="entity"
                :parentId="entityId"
                :association="comp"
              />
            </template>
          </IconTabBar>
        </ObjectPageSection>

        <!-- Single composition: just show it directly -->
        <CompositionSection
          v-else
          v-for="comp in compositionAssociations"
          :key="comp.name"
          :module="module"
          :parentEntity="entity"
          :parentId="entityId"
          :association="comp"
        />
      </template>

      <!-- Reference Manager sections -->
      <ReferenceManager
        v-for="assoc in referenceAssociations"
        :key="assoc.name"
        :module="module"
        :entitySet="entity"
        :entityId="entityId"
        :association="assoc"
        :entityData="entityData ?? {}"
      />

      <!-- ManyToMany sections -->
      <ManyToManySection
        v-for="assoc in manyToManyAssociations"
        :key="assoc.name"
        :module="module"
        :parentEntity="entity"
        :parentId="entityId"
        :association="assoc"
      />

      <!-- System Fields section -->
      <ObjectPageSection
        v-if="systemFields.length > 0"
        id="system"
        :title="$t('common.system')"
        :icon="Eye"
      >
        <div class="grid gap-4 md:grid-cols-2">
          <div v-for="field in systemFields" :key="field.name">
            <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider">
              {{ field.displayName || field.name }}
            </p>
            <div class="mt-0.5 text-muted-foreground">
              <SmartField
                :field="field"
                :modelValue="getFieldValue(field.name)"
                mode="display"
                :showLabel="false"
              />
            </div>
          </div>
        </div>
      </ObjectPageSection>
    </ObjectPageLayout>

    <!-- Plugin detail sections -->
    <PluginSlot
      v-if="pluginSections.length > 0"
      slot-type="detail-sections"
      :entity-type="entity"
      :context="{ entityData, metadata }"
    />

    <VersionHistory
      v-if="isTemporal"
      :open="showVersionHistory"
      :module="module"
      :entitySet="entity"
      :entityId="entityId"
      :fields="fields"
      @update:open="showVersionHistory = $event"
      @select-version="() => { showVersionHistory = false }"
    />

    <!-- Collaboration section -->
    <div v-if="entityData" class="mt-6 space-y-4">
      <!-- Tab switcher: Comments | Change Requests -->
      <div class="flex gap-1 border-b">
        <button
          class="px-4 py-2 text-sm font-medium transition-colors"
          :class="showComments && !showChangeRequests
            ? 'border-b-2 border-primary text-primary'
            : 'text-muted-foreground hover:text-foreground'"
          @click="showComments = true; showChangeRequests = false"
        >
          {{ $t('collaboration.comments') }}
        </button>
        <button
          class="px-4 py-2 text-sm font-medium transition-colors"
          :class="showChangeRequests
            ? 'border-b-2 border-primary text-primary'
            : 'text-muted-foreground hover:text-foreground'"
          @click="showComments = false; showChangeRequests = true"
        >
          {{ $t('collaboration.changeRequests') }}
        </button>
      </div>

      <CommentSection
        v-if="showComments && !showChangeRequests"
        :module="module"
        :entity-type="entity"
        :entity-id="entityId"
        :current-user-id="currentUserId"
        :current-user-name="currentUserName"
      />

      <ChangeRequestPanel
        v-if="showChangeRequests"
        :module="module"
        :entity-type="entity"
        :entity-id="entityId"
        :current-user-id="currentUserId"
        :current-user-name="currentUserName"
        :entity-data="entityData"
        :can-review="true"
      />
    </div>

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
