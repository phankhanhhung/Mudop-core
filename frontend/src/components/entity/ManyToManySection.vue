<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { RouterLink } from 'vue-router'
import { refService, odataService } from '@/services'
import { useMetadataStore } from '@/stores/metadata'
import { useUiStore } from '@/stores/ui'
import { useConfirmDialog } from '@/composables/useConfirmDialog'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Spinner } from '@/components/ui/spinner'
import { ConfirmDialog } from '@/components/common'
import { Link2, Unlink } from 'lucide-vue-next'
import type { AssociationMetadata, EntityMetadata, FieldMetadata } from '@/types/metadata'
import { formatValue } from '@/utils/formValidator'
import EntityPicker from './EntityPicker.vue'

interface Props {
  module: string
  parentEntity: string
  parentId: string
  association: AssociationMetadata
  readonly?: boolean
}

const props = defineProps<Props>()

const metadataStore = useMetadataStore()
const uiStore = useUiStore()
const confirmDialog = useConfirmDialog()

const isPickerOpen = ref(false)
const isOperating = ref(false)
const targetMeta = ref<EntityMetadata | null>(null)
const linkedRows = ref<Record<string, unknown>[]>([])
const isLoading = ref(false)
const error = ref<string | null>(null)

// System/audit fields to hide
const hiddenFieldPatterns = [
  'CreatedAt', 'UpdatedAt', 'ModifiedAt', 'DeletedAt',
  'CreatedBy', 'UpdatedBy', 'ModifiedBy', 'DeletedBy',
  'TenantId', 'IsDeleted', 'SystemStart', 'SystemEnd', 'Version'
]

// Resolve target entity module and name from qualified name
function resolveTarget(targetEntity: string) {
  const lastDot = targetEntity.lastIndexOf('.')
  if (lastDot >= 0) {
    return {
      module: targetEntity.substring(0, lastDot),
      entity: targetEntity.substring(lastDot + 1)
    }
  }
  return { module: props.module, entity: targetEntity }
}

const target = computed(() => resolveTarget(props.association.targetEntity))

// Visible fields: exclude key, system/audit fields
const visibleFields = computed<FieldMetadata[]>(() => {
  if (!targetMeta.value) return []
  return targetMeta.value.fields.filter((f) => {
    if (hiddenFieldPatterns.some((p) => f.name === p)) return false
    if (f.name === 'ID' || f.name === 'Id') return false
    return true
  })
})

const displayName = computed(() => {
  return props.association.name.replace(/([A-Z])/g, ' $1').trim()
})

onMounted(async () => {
  await loadData()
})

async function loadData() {
  isLoading.value = true
  error.value = null

  try {
    const { module: targetModule, entity: targetEntity } = target.value
    targetMeta.value = await metadataStore.fetchEntity(targetModule, targetEntity)

    // Load linked records via navigation endpoint
    const response = await odataService.getChildren<Record<string, unknown>>(
      props.module,
      props.parentEntity,
      props.parentId,
      props.association.name
    )
    linkedRows.value = response.value ?? []
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to load linked data'
  } finally {
    isLoading.value = false
  }
}

function getRowId(row: Record<string, unknown>): string {
  return String(row['Id'] ?? row['id'] ?? row['ID'] ?? '')
}

function getDetailLink(row: Record<string, unknown>): string {
  const id = getRowId(row)
  if (!id) return '#'
  const { module: targetModule, entity: targetEntity } = target.value
  return `/odata/${targetModule}/${targetEntity}/${id}`
}

function getCellValue(row: Record<string, unknown>, field: FieldMetadata): string {
  const value = row[field.name]
  return formatValue(value, field.type, field.enumValues)
}

function openPicker() {
  isPickerOpen.value = true
}

function closePicker() {
  isPickerOpen.value = false
}

async function handleSelect(record: Record<string, unknown>) {
  closePicker()

  const selectedId = getRowId(record)
  if (!selectedId) {
    uiStore.error('Error', 'Selected entity has no ID')
    return
  }

  isOperating.value = true
  try {
    const { entity: targetEntity } = target.value
    await refService.createRef(
      props.module,
      props.parentEntity,
      props.parentId,
      props.association.name,
      selectedId,
      targetEntity
    )
    uiStore.success('Linked', `${displayName.value} linked successfully`)
    await loadData()
  } catch (e) {
    const msg = e instanceof Error ? e.message : 'Failed to create link'
    uiStore.error('Link failed', msg)
  } finally {
    isOperating.value = false
  }
}

async function handleUnlink(row: Record<string, unknown>) {
  const rowId = getRowId(row)
  if (!rowId) return

  const confirmed = await confirmDialog.confirm({
    title: `Unlink ${displayName.value}`,
    description: `Remove this ${displayName.value.toLowerCase()} link? The target record will not be deleted.`,
    confirmLabel: 'Unlink',
    variant: 'destructive'
  })
  if (!confirmed) return

  isOperating.value = true
  try {
    const { entity: targetEntity } = target.value
    await refService.deleteRefWithTarget(
      props.module,
      props.parentEntity,
      props.parentId,
      props.association.name,
      rowId,
      targetEntity
    )
    // Remove from local state immediately
    linkedRows.value = linkedRows.value.filter(r => getRowId(r) !== rowId)
    uiStore.success('Unlinked', `${displayName.value} unlinked successfully`)
  } catch (e) {
    const msg = e instanceof Error ? e.message : 'Failed to unlink'
    uiStore.error('Unlink failed', msg)
  } finally {
    isOperating.value = false
  }
}
</script>

<template>
  <Card>
    <CardHeader class="pb-3">
      <div class="flex items-center justify-between">
        <CardTitle class="text-base flex items-center gap-2">
          <Link2 class="h-4 w-4 text-muted-foreground" />
          {{ displayName }}
          <Badge v-if="!isLoading" variant="secondary" class="text-xs">
            {{ linkedRows.length }}
          </Badge>
        </CardTitle>
        <Button
          v-if="!readonly"
          size="sm"
          variant="outline"
          :disabled="isOperating"
          @click="openPicker"
        >
          <Link2 class="mr-1 h-4 w-4" />
          Link
        </Button>
      </div>
    </CardHeader>
    <CardContent>
      <!-- Loading -->
      <div v-if="isLoading" class="flex items-center justify-center py-6">
        <Spinner size="sm" />
      </div>

      <!-- Error -->
      <p v-else-if="error" class="text-sm text-destructive">{{ error }}</p>

      <!-- Empty state -->
      <div v-else-if="linkedRows.length === 0" class="text-center py-6">
        <p class="text-sm text-muted-foreground mb-3">
          No linked {{ displayName.toLowerCase() }} records
        </p>
        <Button v-if="!readonly" size="sm" variant="outline" @click="openPicker">
          <Link2 class="mr-1 h-4 w-4" />
          Link {{ displayName }}
        </Button>
      </div>

      <!-- Data table -->
      <div v-else class="overflow-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead v-for="field in visibleFields" :key="field.name" class="text-xs">
                {{ field.displayName || field.name }}
              </TableHead>
              <TableHead v-if="!readonly" class="text-xs w-20">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="(row, idx) in linkedRows" :key="idx">
              <TableCell v-for="(field, fIdx) in visibleFields" :key="field.name" class="text-sm">
                <RouterLink
                  v-if="fIdx === 0"
                  :to="getDetailLink(row)"
                  class="text-primary hover:underline"
                >
                  {{ getCellValue(row, field) }}
                </RouterLink>
                <span v-else>{{ getCellValue(row, field) }}</span>
              </TableCell>
              <TableCell v-if="!readonly" class="text-sm">
                <Button
                  size="icon"
                  variant="ghost"
                  class="h-7 w-7 text-destructive hover:text-destructive"
                  :disabled="isOperating"
                  @click="handleUnlink(row)"
                >
                  <Unlink class="h-3.5 w-3.5" />
                </Button>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
    </CardContent>

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
  </Card>

  <!-- Entity Picker Dialog -->
  <EntityPicker
    :open="isPickerOpen"
    :module="module"
    :targetEntity="association.targetEntity"
    :title="`Link ${displayName}`"
    @close="closePicker"
    @select="handleSelect"
  />
</template>
