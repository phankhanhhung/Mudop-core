<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { Card, CardHeader, CardTitle, CardContent, CardFooter } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table'
import { Spinner } from '@/components/ui/spinner'
import { X } from 'lucide-vue-next'
import { metadataService } from '@/services/metadataService'
import { odataService } from '@/services/odataService'
import type { FieldMetadata } from '@/types/metadata'
import type { ODataQueryOptions } from '@/types/odata'

interface Props {
  open: boolean
  module: string
  targetEntity: string
  title?: string
  multiSelect?: boolean
  selectedKeys?: string[]
}

interface DisplayColumn {
  field: string
  label: string
}

const props = withDefaults(defineProps<Props>(), {
  multiSelect: false,
  selectedKeys: () => [],
})

const emit = defineEmits<{
  'update:open': [value: boolean]
  select: [value: { key: string; label: string }]
  'select:multi': [value: { key: string; label: string }[]]
}>()

// --------------------------------------------------------------------------
// State
// --------------------------------------------------------------------------
const searchQuery = ref('')
const results = ref<Record<string, unknown>[]>([])
const totalCount = ref(0)
const isLoading = ref(false)
const currentPage = ref(1)
const pageSize = 20
const displayColumns = ref<DisplayColumn[]>([])
const keyField = ref('Id')
const displayField = ref<string | null>(null)
const selectedRow = ref<Record<string, unknown> | null>(null)
const selectedRows = ref<Map<string, Record<string, unknown>>>(new Map())
const metadataLoaded = ref(false)

let debounceTimer: ReturnType<typeof setTimeout> | null = null

// --------------------------------------------------------------------------
// Derived
// --------------------------------------------------------------------------
const resolvedModule = computed(() => {
  const target = props.targetEntity
  const lastDot = target.lastIndexOf('.')
  if (lastDot >= 0) {
    return target.substring(0, lastDot)
  }
  return props.module
})

const resolvedEntity = computed(() => {
  const target = props.targetEntity
  const lastDot = target.lastIndexOf('.')
  if (lastDot >= 0) {
    return target.substring(lastDot + 1)
  }
  return target
})

const entityName = computed(() => resolvedEntity.value)

const hasMore = computed(() => {
  return currentPage.value * pageSize < totalCount.value
})

const hasSelection = computed(() => {
  if (props.multiSelect) {
    return selectedRows.value.size > 0
  }
  return selectedRow.value !== null
})

// --------------------------------------------------------------------------
// Case-insensitive field access (OData responses may use PascalCase)
// --------------------------------------------------------------------------
function getField(item: Record<string, unknown>, fieldName: string): unknown {
  if (fieldName in item) return item[fieldName]
  const lower = fieldName.toLowerCase()
  for (const key of Object.keys(item)) {
    if (key.toLowerCase() === lower) return item[key]
  }
  return undefined
}

function getKeyValue(row: Record<string, unknown>): string {
  return String(getField(row, keyField.value) ?? '')
}

// --------------------------------------------------------------------------
// Metadata loading
// --------------------------------------------------------------------------
async function loadMetadata() {
  try {
    const meta = await metadataService.getEntity(
      resolvedModule.value,
      resolvedEntity.value
    )

    // Determine key field
    if (meta.keys.length > 0) {
      keyField.value = meta.keys[0]
    }

    // Find best display field
    const preferredNames = ['name', 'title', 'displayName', 'display_name', 'code', 'label']
    for (const pref of preferredNames) {
      const found = meta.fields.find(
        (f: FieldMetadata) => f.name.toLowerCase() === pref.toLowerCase()
      )
      if (found) {
        displayField.value = found.name
        break
      }
    }

    // Fallback: first non-key string field
    if (!displayField.value) {
      const stringField = meta.fields.find(
        (f: FieldMetadata) => f.type === 'String' && !meta.keys.includes(f.name)
      )
      if (stringField) {
        displayField.value = stringField.name
      }
    }

    // Build display columns: key + display field + up to 2 extra relevant fields
    const columns: DisplayColumn[] = []

    if (displayField.value && displayField.value !== keyField.value) {
      columns.push({
        field: displayField.value,
        label: meta.fields.find(
          (f: FieldMetadata) => f.name === displayField.value
        )?.displayName ?? displayField.value,
      })
    }

    columns.push({
      field: keyField.value,
      label: meta.fields.find(
        (f: FieldMetadata) => f.name === keyField.value
      )?.displayName ?? keyField.value,
    })

    // Add a few extra columns (non-key, non-display, simple types)
    const simpleTypes = new Set(['String', 'Integer', 'Decimal', 'Boolean', 'Date', 'Enum'])
    const extraFields = meta.fields.filter(
      (f: FieldMetadata) =>
        f.name !== keyField.value &&
        f.name !== displayField.value &&
        simpleTypes.has(f.type) &&
        !f.isComputed &&
        !f.name.toLowerCase().includes('password') &&
        !f.name.toLowerCase().includes('hash')
    )

    for (const f of extraFields.slice(0, 2)) {
      columns.push({
        field: f.name,
        label: f.displayName ?? f.name,
      })
    }

    displayColumns.value = columns
    metadataLoaded.value = true
  } catch {
    // Fallback: just show key
    displayColumns.value = [{ field: keyField.value, label: keyField.value }]
    metadataLoaded.value = true
  }
}

// --------------------------------------------------------------------------
// Data fetching
// --------------------------------------------------------------------------
async function fetchResults() {
  isLoading.value = true
  try {
    const selectFields = displayColumns.value.map((c) => c.field)
    // Ensure key field is always included
    if (!selectFields.includes(keyField.value)) {
      selectFields.unshift(keyField.value)
    }

    const queryOptions: ODataQueryOptions = {
      $select: selectFields.join(','),
      $top: pageSize,
      $skip: (currentPage.value - 1) * pageSize,
      $count: true,
    }

    if (displayField.value) {
      queryOptions.$orderby = displayField.value
    }

    if (searchQuery.value.trim()) {
      queryOptions.$search = searchQuery.value.trim()
    }

    const response = await odataService.query<Record<string, unknown>>(
      resolvedModule.value,
      resolvedEntity.value,
      queryOptions,
      { skipCache: true }
    )

    results.value = response.value
    totalCount.value = response['@odata.count'] ?? response.value.length
  } catch {
    results.value = []
    totalCount.value = 0
  } finally {
    isLoading.value = false
  }
}

// --------------------------------------------------------------------------
// Selection
// --------------------------------------------------------------------------
function isSelected(row: Record<string, unknown>): boolean {
  const key = getKeyValue(row)
  if (props.multiSelect) {
    return selectedRows.value.has(key)
  }
  return selectedRow.value !== null && getKeyValue(selectedRow.value) === key
}

function selectRow(row: Record<string, unknown>) {
  if (props.multiSelect) {
    const key = getKeyValue(row)
    if (selectedRows.value.has(key)) {
      selectedRows.value.delete(key)
    } else {
      selectedRows.value.set(key, row)
    }
    // Force reactivity
    selectedRows.value = new Map(selectedRows.value)
  } else {
    selectedRow.value = row
  }
}

function formatCellValue(row: Record<string, unknown>, field: string): string {
  const value = getField(row, field)
  if (value === null || value === undefined) return ''
  if (typeof value === 'boolean') return value ? 'Yes' : 'No'
  // Truncate UUIDs for readability
  const str = String(value)
  if (/^[0-9a-f]{8}-[0-9a-f]{4}-/i.test(str) && str.length > 20) {
    return str.substring(0, 8) + '...'
  }
  return str
}

// --------------------------------------------------------------------------
// Pagination
// --------------------------------------------------------------------------
function nextPage() {
  if (hasMore.value) {
    currentPage.value++
    fetchResults()
  }
}

function prevPage() {
  if (currentPage.value > 1) {
    currentPage.value--
    fetchResults()
  }
}

// --------------------------------------------------------------------------
// Search debounce
// --------------------------------------------------------------------------
function debouncedSearch() {
  if (debounceTimer) clearTimeout(debounceTimer)
  debounceTimer = setTimeout(() => {
    currentPage.value = 1
    fetchResults()
  }, 300)
}

// --------------------------------------------------------------------------
// Confirm / Close
// --------------------------------------------------------------------------
function confirm() {
  if (props.multiSelect) {
    const selected: { key: string; label: string }[] = []
    for (const [key, row] of selectedRows.value) {
      selected.push({
        key,
        label: displayField.value
          ? String(getField(row, displayField.value) ?? key)
          : key,
      })
    }
    emit('select:multi', selected)
  } else if (selectedRow.value) {
    const key = getKeyValue(selectedRow.value)
    emit('select', {
      key,
      label: displayField.value
        ? String(getField(selectedRow.value, displayField.value) ?? key)
        : key,
    })
  }
  close()
}

function close() {
  emit('update:open', false)
}

// --------------------------------------------------------------------------
// Keyboard: close on Escape
// --------------------------------------------------------------------------
function handleKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape' && props.open) {
    close()
  }
}

onMounted(() => {
  document.addEventListener('keydown', handleKeydown)
})

onUnmounted(() => {
  document.removeEventListener('keydown', handleKeydown)
  if (debounceTimer) clearTimeout(debounceTimer)
})

// --------------------------------------------------------------------------
// Watch open state to load data
// --------------------------------------------------------------------------
watch(
  () => props.open,
  async (isOpen) => {
    if (isOpen) {
      // Reset state
      searchQuery.value = ''
      currentPage.value = 1
      selectedRow.value = null
      selectedRows.value = new Map()
      results.value = []

      // Pre-select keys if provided
      if (props.selectedKeys.length > 0) {
        for (const key of props.selectedKeys) {
          selectedRows.value.set(key, {})
        }
      }

      if (!metadataLoaded.value) {
        await loadMetadata()
      }
      await fetchResults()
    }
  }
)
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open"
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
      @click.self="close"
    >
      <Card class="w-full max-w-2xl max-h-[80vh] flex flex-col shadow-lg">
        <!-- Header -->
        <CardHeader class="flex flex-row items-center justify-between space-y-0 pb-4">
          <CardTitle class="text-lg">
            {{ title || `Select ${entityName}` }}
          </CardTitle>
          <Button variant="ghost" size="icon" @click="close">
            <X class="h-4 w-4" />
          </Button>
        </CardHeader>

        <CardContent class="flex-1 overflow-hidden flex flex-col gap-4 px-6 pb-0">
          <!-- Search -->
          <Input
            :modelValue="searchQuery"
            placeholder="Search..."
            @update:modelValue="(v: string | number) => { searchQuery = String(v); debouncedSearch() }"
          />

          <!-- Results Table -->
          <div class="flex-1 overflow-auto border rounded-md">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead class="w-10" />
                  <TableHead
                    v-for="col in displayColumns"
                    :key="col.field"
                  >
                    {{ col.label }}
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow
                  v-for="row in results"
                  :key="getKeyValue(row)"
                  :class="isSelected(row) ? 'cursor-pointer hover:bg-muted/50 bg-muted/30' : 'cursor-pointer hover:bg-muted/50'"
                  @click="selectRow(row)"
                >
                  <TableCell class="text-center">
                    <input
                      v-if="multiSelect"
                      type="checkbox"
                      :checked="isSelected(row)"
                      class="h-4 w-4 rounded border-gray-300 text-primary focus:ring-primary"
                      @click.stop
                      @change="selectRow(row)"
                    />
                    <input
                      v-else
                      type="radio"
                      :checked="isSelected(row)"
                      :name="'valuehelpradio'"
                      class="h-4 w-4 border-gray-300 text-primary focus:ring-primary"
                      @click.stop
                      @change="selectRow(row)"
                    />
                  </TableCell>
                  <TableCell
                    v-for="col in displayColumns"
                    :key="col.field"
                    class="truncate max-w-[200px]"
                  >
                    {{ formatCellValue(row, col.field) }}
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>

            <!-- Loading state -->
            <div v-if="isLoading" class="flex justify-center py-8">
              <Spinner />
            </div>

            <!-- Empty state -->
            <div
              v-else-if="results.length === 0"
              class="text-center py-8 text-muted-foreground text-sm"
            >
              No results found
            </div>
          </div>

          <!-- Pagination -->
          <div class="flex items-center justify-between py-2">
            <span class="text-sm text-muted-foreground">
              {{ totalCount }} result{{ totalCount === 1 ? '' : 's' }}
            </span>
            <div class="flex gap-2">
              <Button
                size="sm"
                variant="outline"
                :disabled="currentPage <= 1"
                @click="prevPage"
              >
                Previous
              </Button>
              <span class="flex items-center text-sm text-muted-foreground px-2">
                Page {{ currentPage }}
              </span>
              <Button
                size="sm"
                variant="outline"
                :disabled="!hasMore"
                @click="nextPage"
              >
                Next
              </Button>
            </div>
          </div>
        </CardContent>

        <!-- Footer -->
        <CardFooter class="flex justify-end gap-2 pt-4">
          <Button variant="outline" @click="close">
            Cancel
          </Button>
          <Button :disabled="!hasSelection" @click="confirm">
            Select
          </Button>
        </CardFooter>
      </Card>
    </div>
  </Teleport>
</template>
