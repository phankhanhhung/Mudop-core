<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { Label } from '@/components/ui/label'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { ChevronsUpDown, Check, X } from 'lucide-vue-next'
import type { FieldMetadata, AssociationMetadata } from '@/types/metadata'
import { metadataService } from '@/services/metadataService'
import { odataService } from '@/services/odataService'

interface Props {
  field: FieldMetadata
  association: AssociationMetadata
  modelValue?: string | null
  readonly?: boolean
  currentModule: string
}

const props = withDefaults(defineProps<Props>(), {
  readonly: false
})

const emit = defineEmits<{
  'update:modelValue': [value: string | null]
}>()

// Case-insensitive field access (OData responses may use PascalCase)
function getField(item: Record<string, unknown>, fieldName: string): unknown {
  if (fieldName in item) return item[fieldName]
  // Try case-insensitive match
  const lower = fieldName.toLowerCase()
  for (const key of Object.keys(item)) {
    if (key.toLowerCase() === lower) return item[key]
  }
  return undefined
}

// State
const isOpen = ref(false)
const searchQuery = ref('')
const options = ref<{ id: string; label: string }[]>([])
const isLoading = ref(false)
const displayField = ref<string | null>(null)
const keyField = ref('id')
const targetModule = ref('')
const targetEntity = ref('')
const selectedLabel = ref<string | null>(null)
const dropdownRef = ref<HTMLElement | null>(null)
let debounceTimer: ReturnType<typeof setTimeout> | null = null

// Resolve target module/entity from association
function resolveTarget() {
  const target = props.association.targetEntity
  const lastDot = target.lastIndexOf('.')
  if (lastDot >= 0) {
    targetModule.value = target.substring(0, lastDot)
    targetEntity.value = target.substring(lastDot + 1)
  } else {
    targetModule.value = props.currentModule
    targetEntity.value = target
  }
}

// Find best display field from entity metadata
async function resolveDisplayField() {
  try {
    const meta = await metadataService.getEntity(targetModule.value, targetEntity.value)

    // Find key field
    if (meta.keys.length > 0) {
      keyField.value = meta.keys[0]
    }

    // Priority: name > title > displayName > code > first non-key String > key
    const preferredNames = ['name', 'title', 'displayName', 'display_name', 'code']
    for (const pref of preferredNames) {
      const found = meta.fields.find(
        (f) => f.name.toLowerCase() === pref.toLowerCase() && !f.isReadOnly
      )
      if (found) {
        displayField.value = found.name
        return
      }
    }

    // First non-key String field
    const stringField = meta.fields.find(
      (f) => f.type === 'String' && !meta.keys.includes(f.name)
    )
    if (stringField) {
      displayField.value = stringField.name
      return
    }

    // Fallback to key
    displayField.value = keyField.value
  } catch {
    displayField.value = keyField.value
  }
}

// Fetch options from target entity
async function fetchOptions(search?: string) {
  isLoading.value = true
  try {
    const selectFields = [keyField.value]
    if (displayField.value && displayField.value !== keyField.value) {
      selectFields.push(displayField.value)
    }

    const queryOptions: Record<string, unknown> = {
      $select: selectFields.join(','),
      $top: 50,
      $orderby: displayField.value || keyField.value
    }

    if (search) {
      queryOptions.$filter = `contains(${displayField.value || keyField.value},'${search.replace(/'/g, "''")}')`
    }

    const response = await odataService.query<Record<string, unknown>>(
      targetModule.value,
      targetEntity.value,
      queryOptions as any
    )

    options.value = response.value.map((item) => ({
      id: String(getField(item, keyField.value) ?? ''),
      label: displayField.value
        ? String(getField(item, displayField.value) ?? getField(item, keyField.value) ?? '')
        : String(getField(item, keyField.value) ?? '')
    }))
  } catch {
    options.value = []
  } finally {
    isLoading.value = false
  }
}

// Load the label for the current value (edit mode)
async function loadCurrentValueLabel() {
  if (!props.modelValue) {
    selectedLabel.value = null
    return
  }

  // Check if already in options
  const existing = options.value.find((o) => o.id === props.modelValue)
  if (existing) {
    selectedLabel.value = existing.label
    return
  }

  // Fetch individually
  try {
    const item = await odataService.getById<Record<string, unknown>>(
      targetModule.value,
      targetEntity.value,
      props.modelValue
    )
    selectedLabel.value = displayField.value
      ? String(getField(item, displayField.value) ?? getField(item, keyField.value) ?? props.modelValue)
      : String(getField(item, keyField.value) ?? props.modelValue)
  } catch {
    selectedLabel.value = props.modelValue
  }
}

// Search with debounce
function handleSearch(value: string | number) {
  searchQuery.value = String(value)
  if (debounceTimer) clearTimeout(debounceTimer)
  debounceTimer = setTimeout(() => {
    fetchOptions(searchQuery.value || undefined)
  }, 300)
}

function selectOption(option: { id: string; label: string }) {
  if (debounceTimer) clearTimeout(debounceTimer)
  emit('update:modelValue', option.id)
  selectedLabel.value = option.label
  isOpen.value = false
  searchQuery.value = ''
}

function clearSelection() {
  emit('update:modelValue', null)
  selectedLabel.value = null
}

function toggleDropdown() {
  if (props.readonly || props.field.isReadOnly) return
  isOpen.value = !isOpen.value
  if (isOpen.value) {
    searchQuery.value = ''
    fetchOptions()
  }
}

// Close on outside click
function handleClickOutside(event: MouseEvent) {
  if (dropdownRef.value && !dropdownRef.value.contains(event.target as Node)) {
    isOpen.value = false
  }
}

onMounted(async () => {
  document.addEventListener('mousedown', handleClickOutside)
  resolveTarget()
  await resolveDisplayField()
  if (props.modelValue) {
    await loadCurrentValueLabel()
  }
})

onUnmounted(() => {
  document.removeEventListener('mousedown', handleClickOutside)
  if (debounceTimer) clearTimeout(debounceTimer)
})

// Reload label when modelValue changes externally
watch(
  () => props.modelValue,
  async (newVal) => {
    if (newVal) {
      await loadCurrentValueLabel()
    } else {
      selectedLabel.value = null
    }
  }
)

const displayText = computed(() => {
  if (selectedLabel.value) return selectedLabel.value
  if (props.modelValue) return props.modelValue
  return null
})
</script>

<template>
  <div class="space-y-2">
    <Label :for="field.name">
      {{ field.displayName || field.name }}
      <span v-if="field.isRequired" class="text-destructive">*</span>
    </Label>

    <div ref="dropdownRef" class="relative">
      <!-- Trigger button -->
      <button
        type="button"
        :id="field.name"
        class="flex h-9 w-full items-center justify-between rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-sm transition-colors focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50"
        :disabled="readonly || field.isReadOnly"
        @click="toggleDropdown"
      >
        <span :class="displayText ? 'text-foreground' : 'text-muted-foreground'">
          {{ displayText || 'Select...' }}
        </span>
        <div class="flex items-center gap-1">
          <button
            v-if="displayText && !readonly && !field.isReadOnly"
            type="button"
            class="rounded-sm opacity-50 hover:opacity-100"
            @click.stop="clearSelection"
          >
            <X class="h-3 w-3" />
          </button>
          <ChevronsUpDown class="h-4 w-4 opacity-50" />
        </div>
      </button>

      <!-- Dropdown -->
      <div
        v-if="isOpen"
        class="absolute z-50 mt-1 w-full rounded-md border bg-popover shadow-md"
      >
        <!-- Search input -->
        <div class="p-2 border-b">
          <Input
            :modelValue="searchQuery"
            type="text"
            placeholder="Search..."
            class="h-8"
            @update:modelValue="handleSearch"
          />
        </div>

        <!-- Options list -->
        <div class="max-h-60 overflow-y-auto p-1">
          <div v-if="isLoading" class="flex items-center justify-center py-4">
            <Spinner size="sm" />
          </div>

          <div
            v-else-if="options.length === 0"
            class="py-4 text-center text-sm text-muted-foreground"
          >
            No results found
          </div>

          <button
            v-else
            v-for="option in options"
            :key="option.id"
            type="button"
            class="relative flex w-full cursor-pointer select-none items-center rounded-sm px-2 py-1.5 text-sm outline-none hover:bg-accent hover:text-accent-foreground"
            @click="selectOption(option)"
          >
            <Check
              v-if="option.id === modelValue"
              class="mr-2 h-4 w-4 text-primary"
            />
            <span v-else class="mr-2 w-4" />
            <span class="truncate">{{ option.label }}</span>
            <span
              v-if="option.label !== option.id"
              class="ml-auto text-xs text-muted-foreground truncate max-w-[120px]"
            >
              {{ option.id.substring(0, 8) }}...
            </span>
          </button>
        </div>
      </div>
    </div>

    <p v-if="field.description" class="text-xs text-muted-foreground">
      {{ field.description }}
    </p>
  </div>
</template>
