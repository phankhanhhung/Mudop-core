<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { Button } from '@/components/ui/button'

// ── Props ──

interface Props {
  currentPage: number
  pageSize: number
  totalCount: number
}

const props = withDefaults(defineProps<Props>(), {
  currentPage: 1,
  pageSize: 20,
  totalCount: 0,
})

// ── Emits ──

const emit = defineEmits<{
  'page-change': [page: number]
  'page-size-change': [size: number]
}>()

// ── Internal page size (mirrors prop, allows local select binding) ──

const internalPageSize = ref(props.pageSize)

watch(() => props.pageSize, (val) => {
  internalPageSize.value = val
})

// ── Computed ──

const totalPages = computed(() => {
  if (props.totalCount <= 0 || internalPageSize.value <= 0) return 1
  return Math.ceil(props.totalCount / internalPageSize.value)
})

// ── Handlers ──

function handlePageSizeChange(): void {
  emit('page-size-change', internalPageSize.value)
}
</script>

<template>
  <div class="flex items-center justify-between">
    <div class="flex items-center gap-2">
      <span class="text-sm text-muted-foreground">Rows per page:</span>
      <select
        v-model.number="internalPageSize"
        class="h-8 rounded-md border bg-background px-2 text-sm"
        @change="handlePageSizeChange"
      >
        <option :value="10">10</option>
        <option :value="20">20</option>
        <option :value="50">50</option>
        <option :value="100">100</option>
      </select>
    </div>
    <div class="flex items-center gap-2">
      <span class="text-sm text-muted-foreground">
        Page {{ currentPage }} of {{ totalPages }}
      </span>
      <Button
        size="sm"
        variant="outline"
        :disabled="currentPage <= 1"
        @click="emit('page-change', currentPage - 1)"
      >
        Previous
      </Button>
      <Button
        size="sm"
        variant="outline"
        :disabled="currentPage >= totalPages"
        @click="emit('page-change', currentPage + 1)"
      >
        Next
      </Button>
    </div>
  </div>
</template>
