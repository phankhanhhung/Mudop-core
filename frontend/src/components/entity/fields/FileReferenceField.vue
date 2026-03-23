<script setup lang="ts">
import { ref, computed } from 'vue'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Spinner } from '@/components/ui/spinner'
import { Alert, AlertDescription } from '@/components/ui/alert'
import {
  Upload,
  Download,
  Trash2,
  File as FileIcon,
  FileText,
  Image as ImageIcon,
  AlertCircle
} from 'lucide-vue-next'
import type { FileReferenceInfo, UploadResult } from '@/types/file'
import { formatFileSize, isImageType, getFileNameFromKey } from '@/utils/fileUtils'
import fileService from '@/services/fileService'
import { formatDate } from '@/utils/formatting'

interface Props {
  fieldName: string
  displayName?: string
  module: string
  entitySet: string
  entityId: string
  fileInfo?: FileReferenceInfo
  readonly?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  readonly: false
})

const emit = defineEmits<{
  uploaded: [result: UploadResult]
  deleted: []
}>()

const isUploading = ref(false)
const isDeleting = ref(false)
const error = ref<string | null>(null)
const isDragging = ref(false)
const fileInput = ref<HTMLInputElement | null>(null)

const hasFile = computed(() => {
  return props.fileInfo?.key && props.fileInfo?.bucket
})

const fileName = computed(() => {
  if (!props.fileInfo?.key) return ''
  return getFileNameFromKey(props.fileInfo.key)
})

const fileIconComponent = computed(() => {
  const mimeType = props.fileInfo?.mimeType || ''
  if (mimeType.startsWith('image/')) return ImageIcon
  if (mimeType === 'application/pdf' || mimeType.includes('document') || mimeType.includes('word')) return FileText
  return FileIcon
})

const isImage = computed(() => {
  return props.fileInfo?.mimeType ? isImageType(props.fileInfo.mimeType) : false
})

const downloadUrl = computed(() => {
  if (!hasFile.value) return ''
  return fileService.getDownloadUrl(props.module, props.entitySet, props.entityId, props.fieldName)
})

function triggerFileInput() {
  fileInput.value?.click()
}

async function handleFileSelect(event: Event) {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  if (file) {
    await uploadFile(file)
  }
  // Reset input so the same file can be selected again
  target.value = ''
}

function handleDragOver(event: DragEvent) {
  event.preventDefault()
  isDragging.value = true
}

function handleDragLeave() {
  isDragging.value = false
}

async function handleDrop(event: DragEvent) {
  event.preventDefault()
  isDragging.value = false
  const file = event.dataTransfer?.files?.[0]
  if (file) {
    await uploadFile(file)
  }
}

async function uploadFile(file: File) {
  // Validate file size (50MB)
  if (file.size > 50 * 1024 * 1024) {
    error.value = 'File exceeds the 50MB size limit'
    return
  }

  error.value = null
  isUploading.value = true

  try {
    const result = await fileService.upload(
      props.module,
      props.entitySet,
      props.entityId,
      props.fieldName,
      file
    )
    emit('uploaded', result)
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Upload failed'
  } finally {
    isUploading.value = false
  }
}

async function handleDownload() {
  error.value = null
  try {
    const { blob, fileName: name } = await fileService.download(
      props.module,
      props.entitySet,
      props.entityId,
      props.fieldName
    )
    // Trigger browser download
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = name
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Download failed'
  }
}

async function handleDelete() {
  error.value = null
  isDeleting.value = true
  try {
    await fileService.delete(
      props.module,
      props.entitySet,
      props.entityId,
      props.fieldName
    )
    emit('deleted')
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Delete failed'
  } finally {
    isDeleting.value = false
  }
}
</script>

<template>
  <div class="space-y-2">
    <Label>
      {{ displayName || fieldName }}
    </Label>

    <!-- Error display -->
    <Alert v-if="error" variant="destructive" class="py-2">
      <AlertCircle class="h-4 w-4" />
      <AlertDescription class="text-sm">{{ error }}</AlertDescription>
    </Alert>

    <!-- File exists: show info + actions -->
    <div v-if="hasFile" class="border rounded-lg p-4 space-y-3">
      <!-- Image preview -->
      <div v-if="isImage && downloadUrl" class="flex justify-center">
        <img
          :src="downloadUrl"
          :alt="fileName"
          class="max-h-48 rounded border object-contain"
        />
      </div>

      <!-- File info -->
      <div class="flex items-center gap-3">
        <component :is="fileIconComponent" class="h-8 w-8 text-muted-foreground shrink-0" />
        <div class="min-w-0 flex-1">
          <p class="text-sm font-medium truncate" :title="fileName">{{ fileName }}</p>
          <p class="text-xs text-muted-foreground">
            {{ fileInfo?.mimeType }}
            <span v-if="fileInfo?.size"> &middot; {{ formatFileSize(fileInfo.size) }}</span>
            <span v-if="fileInfo?.uploadedAt">
              &middot; {{ formatDate(fileInfo.uploadedAt) }}
            </span>
          </p>
        </div>
      </div>

      <!-- Actions -->
      <div class="flex gap-2">
        <Button variant="outline" size="sm" @click="handleDownload">
          <Download class="h-4 w-4 mr-1" />
          Download
        </Button>
        <Button
          v-if="!readonly"
          variant="outline"
          size="sm"
          class="text-destructive hover:text-destructive"
          :disabled="isDeleting"
          @click="handleDelete"
        >
          <Spinner v-if="isDeleting" size="sm" class="mr-1" />
          <Trash2 v-else class="h-4 w-4 mr-1" />
          Delete
        </Button>
      </div>
    </div>

    <!-- No file: upload area -->
    <div
      v-else-if="!readonly && entityId"
      class="border-2 border-dashed rounded-lg p-6 text-center cursor-pointer transition-colors"
      :class="{
        'border-primary bg-primary/5': isDragging,
        'border-muted-foreground/25 hover:border-muted-foreground/50': !isDragging
      }"
      @click="triggerFileInput"
      @dragover="handleDragOver"
      @dragleave="handleDragLeave"
      @drop="handleDrop"
    >
      <input
        ref="fileInput"
        type="file"
        class="hidden"
        @change="handleFileSelect"
      />
      <div v-if="isUploading" class="flex flex-col items-center gap-2">
        <Spinner size="lg" />
        <p class="text-sm text-muted-foreground">Uploading...</p>
      </div>
      <div v-else class="flex flex-col items-center gap-2">
        <Upload class="h-8 w-8 text-muted-foreground" />
        <p class="text-sm text-muted-foreground">
          Click to upload or drag and drop
        </p>
        <p class="text-xs text-muted-foreground">
          Maximum file size: 50MB
        </p>
      </div>
    </div>

    <!-- No file, readonly mode -->
    <div v-else-if="readonly || !entityId" class="border rounded-lg p-4 text-center">
      <p class="text-sm text-muted-foreground">
        {{ !entityId ? 'Save the record first to upload files' : 'No file uploaded' }}
      </p>
    </div>
  </div>
</template>
