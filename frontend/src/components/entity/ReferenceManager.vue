<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { RouterLink } from 'vue-router'
import { refService, odataService } from '@/services'
import { useMetadataStore } from '@/stores/metadata'
import { useUiStore } from '@/stores/ui'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Spinner } from '@/components/ui/spinner'
import { Link, RefreshCw, Trash2 } from 'lucide-vue-next'
import type { AssociationMetadata, EntityMetadata } from '@/types/metadata'
import EntityPicker from './EntityPicker.vue'

interface Props {
  module: string
  entitySet: string
  entityId: string
  association: AssociationMetadata
  entityData: Record<string, unknown>
}

const props = defineProps<Props>()

const metadataStore = useMetadataStore()
const uiStore = useUiStore()

const isPickerOpen = ref(false)
const isOperating = ref(false)
const targetMeta = ref<EntityMetadata | null>(null)
const currentRef = ref<Record<string, unknown> | null>(null)
const isLoading = ref(false)
const error = ref<string | null>(null)

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

const displayName = computed(() => {
  return props.association.name.replace(/([A-Z])/g, ' $1').trim()
})

const isOptional = computed(() => props.association.cardinality === 'ZeroOrOne')

// Get the FK value from the entity data
const fkValue = computed(() => {
  if (!props.association.foreignKey) return null
  const val = props.entityData[props.association.foreignKey]
  return val !== null && val !== undefined ? String(val) : null
})

// Get a display value for the referenced entity
const refDisplayValue = computed(() => {
  if (!currentRef.value) return null

  // Try to find the first string field value for display
  if (targetMeta.value) {
    const stringFields = targetMeta.value.fields.filter(
      (f) => f.type === 'String' && !targetMeta.value!.keys.includes(f.name)
    )
    for (const sf of stringFields) {
      const val = currentRef.value[sf.name]
      if (val !== null && val !== undefined && String(val).trim()) {
        return String(val)
      }
    }
  }

  // Fallback to ID
  const id = currentRef.value['Id'] ?? currentRef.value['ID'] ?? currentRef.value['id']
  return id ? String(id) : null
})

// Build link to the referenced entity's detail page
const refDetailLink = computed(() => {
  if (!fkValue.value) return null
  const { module: targetModule, entity: targetEntity } = target.value
  return `/odata/${targetModule}/${targetEntity}/${fkValue.value}`
})

// Get the referenced entity's ID from the current ref data
const refId = computed(() => {
  if (!currentRef.value) return null
  const id = currentRef.value['Id'] ?? currentRef.value['ID'] ?? currentRef.value['id']
  return id ? String(id) : null
})

onMounted(async () => {
  await loadRefData()
})

async function loadRefData() {
  isLoading.value = true
  error.value = null

  try {
    const { module: targetModule, entity: targetEntity } = target.value
    targetMeta.value = await metadataStore.fetchEntity(targetModule, targetEntity)

    // If there's a FK value, load the referenced entity
    if (fkValue.value) {
      try {
        const refData = await odataService.getById<Record<string, unknown>>(
          targetModule,
          targetEntity,
          fkValue.value
        )
        currentRef.value = refData
      } catch {
        // Referenced entity might have been deleted
        currentRef.value = null
      }
    } else {
      // Also check via expand on parent entity data
      const expanded = props.entityData[props.association.name]
      if (expanded && typeof expanded === 'object') {
        currentRef.value = expanded as Record<string, unknown>
      } else {
        currentRef.value = null
      }
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to load reference data'
  } finally {
    isLoading.value = false
  }
}

function openPicker() {
  isPickerOpen.value = true
}

function closePicker() {
  isPickerOpen.value = false
}

async function handleSelect(record: Record<string, unknown>) {
  closePicker()

  const selectedId = String(record['Id'] ?? record['ID'] ?? record['id'] ?? '')
  if (!selectedId) {
    uiStore.error('Error', 'Selected entity has no ID')
    return
  }

  isOperating.value = true
  try {
    const { entity: targetEntity } = target.value

    if (currentRef.value) {
      // Update existing reference
      await refService.updateRef(
        props.module,
        props.entitySet,
        props.entityId,
        props.association.name,
        selectedId,
        targetEntity
      )
      uiStore.success('Reference updated', `${displayName.value} reference updated`)
    } else {
      // Create new reference
      await refService.createRef(
        props.module,
        props.entitySet,
        props.entityId,
        props.association.name,
        selectedId,
        targetEntity
      )
      uiStore.success('Reference created', `${displayName.value} reference created`)
    }

    // Reload the reference data
    currentRef.value = record
  } catch (e) {
    const msg = e instanceof Error ? e.message : 'Failed to update reference'
    uiStore.error('Reference error', msg)
  } finally {
    isOperating.value = false
  }
}

async function handleRemove() {
  isOperating.value = true
  try {
    await refService.deleteRef(
      props.module,
      props.entitySet,
      props.entityId,
      props.association.name
    )
    currentRef.value = null
    uiStore.success('Reference removed', `${displayName.value} reference removed`)
  } catch (e) {
    const msg = e instanceof Error ? e.message : 'Failed to remove reference'
    uiStore.error('Reference error', msg)
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
          <Link class="h-4 w-4 text-muted-foreground" />
          {{ displayName }}
          <Badge variant="secondary" class="text-xs">
            {{ association.cardinality === 'One' ? 'Required' : 'Optional' }}
          </Badge>
        </CardTitle>
        <div class="flex items-center gap-2">
          <Button
            size="sm"
            variant="outline"
            :disabled="isOperating"
            @click="openPicker"
          >
            <Spinner v-if="isOperating" size="sm" class="mr-1" />
            <RefreshCw v-else class="mr-1 h-4 w-4" />
            {{ currentRef ? 'Change' : 'Set' }}
          </Button>
          <Button
            v-if="isOptional && currentRef"
            size="sm"
            variant="outline"
            class="text-destructive hover:text-destructive"
            :disabled="isOperating"
            @click="handleRemove"
          >
            <Trash2 class="mr-1 h-4 w-4" />
            Remove
          </Button>
        </div>
      </div>
    </CardHeader>
    <CardContent>
      <!-- Loading -->
      <div v-if="isLoading" class="flex items-center justify-center py-6">
        <Spinner size="sm" />
      </div>

      <!-- Error -->
      <p v-else-if="error" class="text-sm text-destructive">{{ error }}</p>

      <!-- Current reference -->
      <div v-else-if="currentRef && refDisplayValue" class="flex items-center gap-3">
        <div class="flex-1">
          <RouterLink
            v-if="refDetailLink"
            :to="refDetailLink"
            class="text-sm text-primary hover:underline font-medium"
          >
            {{ refDisplayValue }}
          </RouterLink>
          <span v-else class="text-sm font-medium">
            {{ refDisplayValue }}
          </span>
          <p v-if="refId && refDisplayValue !== refId" class="text-xs text-muted-foreground mt-0.5">
            ID: {{ refId }}
          </p>
        </div>
      </div>

      <!-- No reference set -->
      <div v-else class="text-center py-4">
        <p class="text-sm text-muted-foreground mb-3">
          No {{ displayName.toLowerCase() }} reference set
        </p>
        <Button size="sm" variant="outline" @click="openPicker">
          Set {{ displayName }}
        </Button>
      </div>
    </CardContent>
  </Card>

  <!-- Entity Picker Dialog -->
  <EntityPicker
    :open="isPickerOpen"
    :module="module"
    :targetEntity="association.targetEntity"
    :title="`Select ${displayName}`"
    @close="closePicker"
    @select="handleSelect"
  />
</template>
