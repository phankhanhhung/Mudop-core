<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import UploadCollectionItem from './UploadCollectionItem.vue'
import { useUploadCollection } from '@/composables/useUploadCollection'
import type { UploadFileItem } from '@/composables/useUploadCollection'
import {
  Upload,
  LayoutList,
  LayoutGrid,
  Trash2,
  CloudUpload,
} from 'lucide-vue-next'

interface Props {
  layout?: 'list' | 'grid'
  maxFiles?: number
  maxFileSize?: number
  allowedTypes?: string[]
  readonly?: boolean
  autoUpload?: boolean
  title?: string
  uploadFn?: (file: File, onProgress: (pct: number) => void) => Promise<string>
}

const props = withDefaults(defineProps<Props>(), {
  layout: 'list',
  maxFiles: 10,
  maxFileSize: 50 * 1024 * 1024,
  allowedTypes: () => [],
  readonly: false,
  autoUpload: false,
  title: 'Upload Collection',
})

const emit = defineEmits<{
  'upload-complete': [item: UploadFileItem]
  'upload-error': [item: UploadFileItem]
  'files-change': [items: UploadFileItem[]]
}>()

const currentLayout = ref(props.layout)
const fileInputRef = ref<HTMLInputElement | null>(null)
const isDragging = ref(false)
let dragCounter = 0

const {
  items,
  isUploading,
  pendingCount,
  completedCount,
  errorCount,
  addFiles,
  removeFile,
  retryFile,
  startUpload,
  clearCompleted,
} = useUploadCollection({
  maxFiles: props.maxFiles,
  maxFileSize: props.maxFileSize,
  allowedTypes: props.allowedTypes,
  autoUpload: props.autoUpload,
  uploadFn: props.uploadFn,
})

const totalCount = computed(() => items.value.length)
const acceptString = computed(() => {
  if (props.allowedTypes.length === 0) return undefined
  return props.allowedTypes.join(',')
})

watch(items, (val) => {
  emit('files-change', val)
}, { deep: true })

// Watch for completed/errored items to emit events
watch(items, (val) => {
  for (const item of val) {
    if (item.status === 'complete' && item.progress === 100) {
      emit('upload-complete', item)
    }
  }
}, { deep: true })

function handleFileInput(event: Event) {
  const input = event.target as HTMLInputElement
  if (input.files && input.files.length > 0) {
    addFiles(input.files)
    input.value = ''
  }
}

function openFilePicker() {
  fileInputRef.value?.click()
}

function handleDragEnter(e: DragEvent) {
  e.preventDefault()
  dragCounter++
  if (e.dataTransfer?.types.includes('Files')) {
    isDragging.value = true
  }
}

function handleDragLeave(e: DragEvent) {
  e.preventDefault()
  dragCounter--
  if (dragCounter === 0) {
    isDragging.value = false
  }
}

function handleDragOver(e: DragEvent) {
  e.preventDefault()
}

function handleDrop(e: DragEvent) {
  e.preventDefault()
  isDragging.value = false
  dragCounter = 0
  if (e.dataTransfer?.files && e.dataTransfer.files.length > 0) {
    addFiles(e.dataTransfer.files)
  }
}

function handleRemove(id: string) {
  removeFile(id)
}

async function handleRetry(id: string) {
  await retryFile(id)
}

function handleDownload(id: string) {
  const item = items.value.find(i => i.id === id)
  if (item?.uploadedUrl) {
    window.open(item.uploadedUrl, '_blank')
  }
}

async function handleUploadAll() {
  await startUpload()
}
</script>

<template>
  <div class="space-y-3">
    <!-- Toolbar -->
    <div class="flex items-center justify-between flex-wrap gap-2">
      <div class="flex items-center gap-2">
        <h3 class="text-sm font-semibold text-foreground">{{ title }}</h3>
        <Badge v-if="totalCount > 0" variant="secondary">
          {{ totalCount }} file{{ totalCount === 1 ? '' : 's' }}
        </Badge>
        <Badge v-if="pendingCount > 0" variant="outline">
          {{ pendingCount }} pending
        </Badge>
        <Badge v-if="errorCount > 0" variant="destructive">
          {{ errorCount }} error{{ errorCount === 1 ? '' : 's' }}
        </Badge>
      </div>
      <div class="flex items-center gap-1.5">
        <!-- Layout toggle -->
        <Button
          variant="ghost"
          size="icon"
          class="h-8 w-8"
          :class="currentLayout === 'list' ? 'bg-muted' : ''"
          title="List view"
          @click="currentLayout = 'list'"
        >
          <LayoutList class="h-4 w-4" />
        </Button>
        <Button
          variant="ghost"
          size="icon"
          class="h-8 w-8"
          :class="currentLayout === 'grid' ? 'bg-muted' : ''"
          title="Grid view"
          @click="currentLayout = 'grid'"
        >
          <LayoutGrid class="h-4 w-4" />
        </Button>

        <div v-if="!readonly && (pendingCount > 0 || completedCount > 0)" class="h-4 w-px bg-border mx-1" />

        <!-- Upload All -->
        <Button
          v-if="!autoUpload && pendingCount > 0 && !readonly && uploadFn"
          size="sm"
          :disabled="isUploading"
          @click="handleUploadAll"
        >
          <CloudUpload class="h-4 w-4 mr-1.5" />
          Upload All
        </Button>

        <!-- Clear completed -->
        <Button
          v-if="completedCount > 0"
          variant="outline"
          size="sm"
          @click="clearCompleted"
        >
          <Trash2 class="h-4 w-4 mr-1.5" />
          Clear Completed
        </Button>
      </div>
    </div>

    <!-- Drop zone -->
    <div
      v-if="!readonly"
      :class="cn(
        'border-2 border-dashed rounded-lg p-8 text-center transition-colors cursor-pointer',
        isDragging
          ? 'border-primary bg-primary/5'
          : 'border-muted-foreground/25 hover:border-muted-foreground/50'
      )"
      @click="openFilePicker"
      @dragenter="handleDragEnter"
      @dragleave="handleDragLeave"
      @dragover="handleDragOver"
      @drop="handleDrop"
    >
      <Upload class="h-8 w-8 mx-auto text-muted-foreground/60 mb-2" />
      <p class="text-sm text-muted-foreground">
        <span v-if="isDragging" class="text-primary font-medium">Drop files here</span>
        <span v-else>Drop files here or <span class="text-primary font-medium underline underline-offset-2">click to browse</span></span>
      </p>
      <p class="text-xs text-muted-foreground/60 mt-1">
        <template v-if="maxFiles < Infinity">Max {{ maxFiles }} files</template>
        <template v-if="maxFiles < Infinity && maxFileSize < Infinity"> &middot; </template>
        <template v-if="maxFileSize < Infinity">Up to {{ (maxFileSize / (1024 * 1024)).toFixed(0) }}MB each</template>
      </p>
      <input
        ref="fileInputRef"
        type="file"
        multiple
        :accept="acceptString"
        class="hidden"
        @change="handleFileInput"
        @click.stop
      />
    </div>

    <!-- File list (list mode) -->
    <div v-if="items.length > 0 && currentLayout === 'list'" class="rounded-lg border divide-y">
      <UploadCollectionItem
        v-for="item in items"
        :key="item.id"
        :item="item"
        layout="list"
        :readonly="readonly"
        @remove="handleRemove"
        @retry="handleRetry"
        @download="handleDownload"
      />
    </div>

    <!-- File grid (grid mode) -->
    <div
      v-if="items.length > 0 && currentLayout === 'grid'"
      class="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6 gap-4"
    >
      <UploadCollectionItem
        v-for="item in items"
        :key="item.id"
        :item="item"
        layout="grid"
        :readonly="readonly"
        @remove="handleRemove"
        @retry="handleRetry"
        @download="handleDownload"
      />
    </div>

    <!-- Summary bar -->
    <div
      v-if="items.length > 0"
      class="flex items-center gap-3 text-xs text-muted-foreground px-1"
    >
      <span>{{ totalCount }} file{{ totalCount === 1 ? '' : 's' }}</span>
      <span v-if="pendingCount > 0">&middot; {{ pendingCount }} pending</span>
      <span v-if="isUploading">&middot; Uploading...</span>
      <span v-if="completedCount > 0">&middot; {{ completedCount }} complete</span>
      <span v-if="errorCount > 0" class="text-destructive">&middot; {{ errorCount }} error{{ errorCount === 1 ? '' : 's' }}</span>
    </div>
  </div>
</template>
