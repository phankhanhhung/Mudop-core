<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { odataService } from '@/services'
import { useMetadataStore } from '@/stores/metadata'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { X, ChevronLeft, ChevronRight, Search } from 'lucide-vue-next'
import type { EntityMetadata, FieldMetadata } from '@/types/metadata'

interface Props {
  open: boolean
  module: string
  targetEntity: string
  title?: string
}

const props = withDefaults(defineProps<Props>(), {
  title: 'Select Entity'
})

const emit = defineEmits<{
  close: []
  select: [record: Record<string, unknown>]
}>()

const metadataStore = useMetadataStore()
const entityMeta = ref<EntityMetadata | null>(null)
const searchQuery = ref('')
const rows = ref<Record<string, unknown>[]>([])
const totalCount = ref(0)
const currentPage = ref(0)
const isLoading = ref(false)
const error = ref<string | null>(null)

const PAGE_SIZE = 10

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

const target = computed(() => resolveTarget(props.targetEntity))

// Determine display columns: key field + first 2-3 string fields
const displayColumns = computed<FieldMetadata[]>(() => {
  if (!entityMeta.value) return []
  const allFields = entityMeta.value.fields
  const keys = entityMeta.value.keys

  // Start with the first key field (usually Id)
  const keyField = allFields.find((f) => keys.includes(f.name))
  const columns: FieldMetadata[] = keyField ? [keyField] : []

  // Add up to 3 string fields (not keys, not system fields)
  const systemFields = [
    'CreatedAt', 'UpdatedAt', 'ModifiedAt', 'DeletedAt',
    'CreatedBy', 'UpdatedBy', 'ModifiedBy', 'DeletedBy',
    'TenantId', 'IsDeleted', 'SystemStart', 'SystemEnd', 'Version'
  ]

  const stringFields = allFields.filter(
    (f) =>
      f.type === 'String' &&
      !keys.includes(f.name) &&
      !systemFields.includes(f.name)
  )

  for (const sf of stringFields) {
    if (columns.length >= 4) break
    columns.push(sf)
  }

  // If we still only have the key, add a few non-string fields
  if (columns.length <= 1) {
    const otherFields = allFields.filter(
      (f) =>
        !keys.includes(f.name) &&
        !systemFields.includes(f.name) &&
        !columns.some((c) => c.name === f.name)
    )
    for (const of2 of otherFields) {
      if (columns.length >= 4) break
      columns.push(of2)
    }
  }

  return columns
})

const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / PAGE_SIZE)))
const canPrev = computed(() => currentPage.value > 0)
const canNext = computed(() => currentPage.value < totalPages.value - 1)

// Watch open to load data when the dialog opens
watch(
  () => props.open,
  async (isOpen) => {
    if (isOpen) {
      searchQuery.value = ''
      currentPage.value = 0
      rows.value = []
      totalCount.value = 0
      error.value = null
      await loadMetadata()
      await loadData()
    }
  },
  { immediate: true }
)

async function loadMetadata() {
  const { module: targetModule, entity: targetEntityName } = target.value
  try {
    entityMeta.value = await metadataStore.fetchEntity(targetModule, targetEntityName)
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to load entity metadata'
  }
}

async function loadData() {
  if (!entityMeta.value) return

  isLoading.value = true
  error.value = null

  try {
    const { module: targetModule, entity: targetEntityName } = target.value
    const response = await odataService.query<Record<string, unknown>>(
      targetModule,
      targetEntityName,
      {
        $search: searchQuery.value || undefined,
        $top: PAGE_SIZE,
        $skip: currentPage.value * PAGE_SIZE,
        $count: true
      }
    )
    rows.value = response.value ?? []
    totalCount.value = response['@odata.count'] ?? 0
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to load entities'
  } finally {
    isLoading.value = false
  }
}

let searchTimeout: ReturnType<typeof setTimeout> | null = null

function onSearchInput() {
  if (searchTimeout) clearTimeout(searchTimeout)
  searchTimeout = setTimeout(() => {
    currentPage.value = 0
    loadData()
  }, 300)
}

function prevPage() {
  if (canPrev.value) {
    currentPage.value--
    loadData()
  }
}

function nextPage() {
  if (canNext.value) {
    currentPage.value++
    loadData()
  }
}

function selectRow(row: Record<string, unknown>) {
  emit('select', row)
}

function close() {
  emit('close')
}

function getCellValue(row: Record<string, unknown>, field: FieldMetadata): string {
  const value = row[field.name]
  if (value === null || value === undefined) return '-'
  return String(value)
}
</script>

<template>
  <!-- Modal overlay -->
  <Teleport to="body">
    <div
      v-if="open"
      class="fixed inset-0 z-50 flex items-center justify-center"
    >
      <!-- Backdrop -->
      <div
        class="absolute inset-0 bg-black/50"
        @click="close"
      />

      <!-- Dialog -->
      <Card class="relative z-10 w-full max-w-2xl mx-4 max-h-[80vh] flex flex-col shadow-lg">
        <CardHeader class="pb-3">
          <div class="flex items-center justify-between">
            <CardTitle class="text-base">{{ title }}</CardTitle>
            <Button
              variant="ghost"
              size="icon"
              class="h-8 w-8"
              @click="close"
            >
              <X class="h-4 w-4" />
            </Button>
          </div>

          <!-- Search input -->
          <div class="relative mt-3">
            <Search class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
            <Input
              v-model="searchQuery"
              placeholder="Search..."
              class="pl-9"
              @input="onSearchInput"
            />
          </div>
        </CardHeader>

        <CardContent class="flex-1 overflow-auto pb-3">
          <!-- Loading state -->
          <div v-if="isLoading && rows.length === 0" class="flex items-center justify-center py-12">
            <Spinner size="md" />
          </div>

          <!-- Error state -->
          <p v-else-if="error" class="text-sm text-destructive text-center py-6">{{ error }}</p>

          <!-- Empty state -->
          <div v-else-if="rows.length === 0" class="text-center py-12">
            <p class="text-sm text-muted-foreground">No records found</p>
          </div>

          <!-- Results table -->
          <div v-else>
            <!-- Loading overlay for page changes -->
            <div v-if="isLoading" class="flex items-center justify-center py-2">
              <Spinner size="sm" />
            </div>

            <div class="overflow-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead
                      v-for="col in displayColumns"
                      :key="col.name"
                      class="text-xs"
                    >
                      {{ col.displayName || col.name }}
                    </TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  <TableRow
                    v-for="(row, idx) in rows"
                    :key="idx"
                    class="cursor-pointer hover:bg-muted/50"
                    @click="selectRow(row)"
                  >
                    <TableCell
                      v-for="col in displayColumns"
                      :key="col.name"
                      class="text-sm"
                    >
                      {{ getCellValue(row, col) }}
                    </TableCell>
                  </TableRow>
                </TableBody>
              </Table>
            </div>

            <!-- Pagination -->
            <div class="flex items-center justify-between pt-3 text-xs text-muted-foreground">
              <span>{{ totalCount }} record{{ totalCount !== 1 ? 's' : '' }} total</span>
              <div class="flex items-center gap-2">
                <Button
                  variant="outline"
                  size="icon"
                  class="h-7 w-7"
                  :disabled="!canPrev"
                  @click="prevPage"
                >
                  <ChevronLeft class="h-4 w-4" />
                </Button>
                <span>Page {{ currentPage + 1 }} of {{ totalPages }}</span>
                <Button
                  variant="outline"
                  size="icon"
                  class="h-7 w-7"
                  :disabled="!canNext"
                  @click="nextPage"
                >
                  <ChevronRight class="h-4 w-4" />
                </Button>
              </div>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  </Teleport>
</template>
