<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import type { AssociationMetadata, EntityMetadata, FieldMetadata } from '@/types/metadata'
import { useMetadataStore } from '@/stores/metadata'
import { useUiStore } from '@/stores/ui'
import { useConfirmDialog } from '@/composables/useConfirmDialog'
import { odataService } from '@/services'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import { ConfirmDialog } from '@/components/common'
import { Plus, Pencil, Trash2 } from 'lucide-vue-next'
import { formatValue } from '@/utils/formValidator'
import { RouterLink } from 'vue-router'

interface Props {
  module: string
  parentEntity: string
  parentId: string
  association: AssociationMetadata
}

const props = defineProps<Props>()

const router = useRouter()
const metadataStore = useMetadataStore()
const uiStore = useUiStore()
const confirmDialog = useConfirmDialog()
const childMetadata = ref<EntityMetadata | null>(null)
const childRows = ref<Record<string, unknown>[]>([])
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

// Compute the parent FK field name to hide (PascalCase, e.g. "CustomerId")
const parentFkField = computed(() => `${props.parentEntity}Id`)

// Visible fields: exclude key, parent FK, system/audit fields
const visibleFields = computed<FieldMetadata[]>(() => {
  if (!childMetadata.value) return []
  return childMetadata.value.fields.filter((f) => {
    // Hide system/audit fields
    if (hiddenFieldPatterns.some((p) => f.name === p)) return false
    // Hide parent FK (case-insensitive)
    if (f.name.toLowerCase() === parentFkField.value.toLowerCase()) return false
    // Hide ID key field
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
    // Load child entity metadata
    const { module: targetModule, entity: targetEntity } = target.value
    childMetadata.value = await metadataStore.fetchEntity(targetModule, targetEntity)

    // Load child records via navigation endpoint
    const response = await odataService.getChildren<Record<string, unknown>>(
      props.module,
      props.parentEntity,
      props.parentId,
      props.association.name
    )
    childRows.value = response.value ?? []
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to load composition data'
  } finally {
    isLoading.value = false
  }
}

function getChildDetailLink(row: Record<string, unknown>): string {
  const id = row['Id'] ?? row['id'] ?? row['ID']
  if (!id) return '#'
  const { module: targetModule, entity: targetEntity } = target.value
  return `/odata/${targetModule}/${targetEntity}/${id}`
}

function getChildId(row: Record<string, unknown>): string {
  return String(row['Id'] ?? row['id'] ?? row['ID'] ?? '')
}

function getCellValue(row: Record<string, unknown>, field: FieldMetadata): string {
  const value = row[field.name]
  return formatValue(value, field.type, field.enumValues)
}

function handleAdd() {
  const { module: targetModule, entity: targetEntity } = target.value
  router.push({
    path: `/odata/${targetModule}/${targetEntity}/new`,
    query: {
      parentFk: parentFkField.value,
      parentId: props.parentId,
      parentEntity: props.parentEntity,
      parentModule: props.module
    }
  })
}

function handleEdit(row: Record<string, unknown>) {
  const id = getChildId(row)
  if (!id) return
  const { module: targetModule, entity: targetEntity } = target.value
  router.push({
    path: `/odata/${targetModule}/${targetEntity}/${id}/edit`,
    query: {
      parentFk: parentFkField.value,
      parentId: props.parentId,
      parentEntity: props.parentEntity,
      parentModule: props.module
    }
  })
}

async function handleDelete(row: Record<string, unknown>) {
  const id = getChildId(row)
  if (!id) return

  const confirmed = await confirmDialog.confirm({
    title: `Delete ${displayName.value}`,
    description: `Are you sure you want to delete this ${displayName.value.toLowerCase()} record? This action cannot be undone.`,
    confirmLabel: 'Delete',
    variant: 'destructive'
  })
  if (!confirmed) return

  try {
    const { module: targetModule, entity: targetEntity } = target.value
    await odataService.delete(targetModule, targetEntity, id)
    uiStore.success('Deleted', `${displayName.value} record deleted`)
    await loadData()
  } catch (e) {
    const msg = e instanceof Error ? e.message : 'Failed to delete record'
    uiStore.error('Delete failed', msg)
  }
}
</script>

<template>
  <Card>
    <CardHeader class="pb-3">
      <div class="flex items-center justify-between">
        <CardTitle class="text-base flex items-center gap-2">
          {{ displayName }}
          <Badge v-if="!isLoading" variant="secondary" class="text-xs">
            {{ childRows.length }}
          </Badge>
        </CardTitle>
        <Button size="sm" variant="outline" @click="handleAdd">
          <Plus class="mr-1 h-4 w-4" />
          Add
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
      <div v-else-if="childRows.length === 0" class="text-center py-6">
        <p class="text-sm text-muted-foreground mb-3">
          No {{ displayName.toLowerCase() }} records
        </p>
        <Button size="sm" variant="outline" @click="handleAdd">
          <Plus class="mr-1 h-4 w-4" />
          Add {{ displayName }}
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
              <TableHead class="text-xs w-20">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="(row, idx) in childRows" :key="idx">
              <TableCell v-for="(field, fIdx) in visibleFields" :key="field.name" class="text-sm">
                <RouterLink
                  v-if="fIdx === 0"
                  :to="getChildDetailLink(row)"
                  class="text-primary hover:underline"
                >
                  {{ getCellValue(row, field) }}
                </RouterLink>
                <span v-else>{{ getCellValue(row, field) }}</span>
              </TableCell>
              <TableCell class="text-sm">
                <div class="flex items-center gap-1">
                  <Button size="icon" variant="ghost" class="h-7 w-7" @click="handleEdit(row)">
                    <Pencil class="h-3.5 w-3.5" />
                  </Button>
                  <Button size="icon" variant="ghost" class="h-7 w-7 text-destructive hover:text-destructive" @click="handleDelete(row)">
                    <Trash2 class="h-3.5 w-3.5" />
                  </Button>
                </div>
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
</template>
