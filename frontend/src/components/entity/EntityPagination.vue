<script setup lang="ts">
import { computed } from 'vue'
import { Button } from '@/components/ui/button'
import { Select } from '@/components/ui/select'
import { ChevronLeft, ChevronRight, ChevronsLeft, ChevronsRight } from 'lucide-vue-next'

interface Props {
  currentPage: number
  totalPages: number
  pageSize: number
  totalCount: number
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:currentPage': [page: number]
  'update:pageSize': [size: number]
}>()

const pageSizeOptions = [10, 20, 50, 100]

const startRecord = computed(() => {
  if (props.totalCount === 0) return 0
  return (props.currentPage - 1) * props.pageSize + 1
})

const endRecord = computed(() => {
  return Math.min(props.currentPage * props.pageSize, props.totalCount)
})

const canGoPrevious = computed(() => props.currentPage > 1)
const canGoNext = computed(() => props.currentPage < props.totalPages)

function goToPage(page: number) {
  emit('update:currentPage', page)
}

function goToFirst() {
  goToPage(1)
}

function goToPrevious() {
  if (canGoPrevious.value) {
    goToPage(props.currentPage - 1)
  }
}

function goToNext() {
  if (canGoNext.value) {
    goToPage(props.currentPage + 1)
  }
}

function goToLast() {
  goToPage(props.totalPages)
}

function handlePageSizeChange(event: Event) {
  const target = event.target as HTMLSelectElement
  emit('update:pageSize', Number(target.value))
}
</script>

<template>
  <div class="flex flex-col sm:flex-row items-center justify-between gap-3 sm:gap-4 py-4">
    <!-- Page size selector - hidden on mobile -->
    <div class="hidden sm:flex items-center gap-2">
      <span class="text-sm text-muted-foreground">Rows per page</span>
      <Select
        :modelValue="pageSize"
        @change="handlePageSizeChange"
        class="w-20"
      >
        <option v-for="size in pageSizeOptions" :key="size" :value="size">
          {{ size }}
        </option>
      </Select>
    </div>

    <!-- Record info - compact on mobile -->
    <div class="text-xs sm:text-sm text-muted-foreground">
      <span class="hidden sm:inline">Showing {{ startRecord }} to {{ endRecord }} of {{ totalCount }} records</span>
      <span class="sm:hidden">{{ startRecord }}-{{ endRecord }} of {{ totalCount }}</span>
    </div>

    <!-- Page navigation -->
    <div class="flex items-center gap-1">
      <Button
        variant="outline"
        size="icon"
        class="h-8 w-8 sm:h-9 sm:w-9"
        :disabled="!canGoPrevious"
        @click="goToFirst"
        title="First page"
        aria-label="First page"
      >
        <ChevronsLeft class="h-4 w-4" aria-hidden="true" />
      </Button>
      <Button
        variant="outline"
        size="icon"
        class="h-8 w-8 sm:h-9 sm:w-9"
        :disabled="!canGoPrevious"
        @click="goToPrevious"
        title="Previous page"
        aria-label="Previous page"
      >
        <ChevronLeft class="h-4 w-4" aria-hidden="true" />
      </Button>

      <span class="px-2 sm:px-4 text-xs sm:text-sm whitespace-nowrap">
        {{ currentPage }} / {{ totalPages }}
      </span>

      <Button
        variant="outline"
        size="icon"
        class="h-8 w-8 sm:h-9 sm:w-9"
        :disabled="!canGoNext"
        @click="goToNext"
        title="Next page"
        aria-label="Next page"
      >
        <ChevronRight class="h-4 w-4" aria-hidden="true" />
      </Button>
      <Button
        variant="outline"
        size="icon"
        class="h-8 w-8 sm:h-9 sm:w-9"
        :disabled="!canGoNext"
        @click="goToLast"
        title="Last page"
        aria-label="Last page"
      >
        <ChevronsRight class="h-4 w-4" aria-hidden="true" />
      </Button>
    </div>
  </div>
</template>
