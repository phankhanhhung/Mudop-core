<script setup lang="ts">
import { ref, computed } from 'vue'
import { Upload, Download, Trash2, File, Image } from 'lucide-vue-next'
import { odataService } from '@/services/odataService'
import { useConfirmDialog } from '@/composables/useConfirmDialog'
import { ConfirmDialog } from '@/components/common'

interface Props {
  module: string
  entitySet: string
  entityId: string
  mediaReadLink?: string
  mediaContentType?: string
  readonly?: boolean
}

const props = withDefaults(defineProps<Props>(), { readonly: false })
const emit = defineEmits<{
  uploaded: []
  deleted: []
}>()

const fileInput = ref<HTMLInputElement | null>(null)
const isDragging = ref(false)
const isUploading = ref(false)
const isDeleting = ref(false)
const previewUrl = ref<string | null>(null)
const error = ref<string | null>(null)

const confirmDialog = useConfirmDialog()

const isImage = computed(() => props.mediaContentType?.startsWith('image/'))
const hasMedia = computed(() => !!props.mediaReadLink)
const fileTypeLabel = computed(() => {
  if (!props.mediaContentType) return 'No file'
  const parts = props.mediaContentType.split('/')
  return parts[1]?.toUpperCase() || props.mediaContentType
})

function triggerUpload() {
  fileInput.value?.click()
}

async function handleFileSelect(event: Event) {
  const input = event.target as HTMLInputElement
  if (input.files?.[0]) {
    await uploadFile(input.files[0])
    input.value = '' // Reset for re-upload of same file
  }
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
  if (event.dataTransfer?.files?.[0]) {
    await uploadFile(event.dataTransfer.files[0])
  }
}

async function uploadFile(file: globalThis.File) {
  isUploading.value = true
  error.value = null
  try {
    await odataService.uploadMediaStream(props.module, props.entitySet, props.entityId, file)
    emit('uploaded')
  } catch (e: any) {
    error.value = e?.response?.data?.error?.message || e?.message || 'Upload failed'
  } finally {
    isUploading.value = false
  }
}

async function downloadMedia() {
  try {
    const { blob, contentType } = await odataService.getMediaStream(
      props.module, props.entitySet, props.entityId
    )
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${props.entitySet}-${props.entityId}.${contentType.split('/')[1] || 'bin'}`
    a.click()
    URL.revokeObjectURL(url)
  } catch (e: any) {
    error.value = e?.message || 'Download failed'
  }
}

async function loadPreview() {
  if (!isImage.value || !hasMedia.value) return
  try {
    const { blob } = await odataService.getMediaStream(
      props.module, props.entitySet, props.entityId
    )
    previewUrl.value = URL.createObjectURL(blob)
  } catch {
    // Preview not available
  }
}

async function deleteMedia() {
  const confirmed = await confirmDialog.confirm({
    title: 'Delete Media',
    description: 'Are you sure you want to delete the media content? This action cannot be undone.',
    confirmLabel: 'Delete',
    variant: 'destructive'
  })
  if (!confirmed) return
  isDeleting.value = true
  error.value = null
  try {
    await odataService.deleteMediaStream(props.module, props.entitySet, props.entityId)
    previewUrl.value = null
    emit('deleted')
  } catch (e: any) {
    error.value = e?.message || 'Delete failed'
  } finally {
    isDeleting.value = false
  }
}

// Load preview on mount if image
if (props.mediaReadLink && props.mediaContentType?.startsWith('image/')) {
  loadPreview()
}
</script>

<template>
  <div class="space-y-3">
    <!-- Error message -->
    <div v-if="error" class="text-sm text-red-600 bg-red-50 border border-red-200 rounded p-2">
      {{ error }}
    </div>

    <!-- Media preview / upload zone -->
    <div v-if="hasMedia" class="border rounded-lg p-4 bg-gray-50">
      <!-- Image preview -->
      <div v-if="isImage && previewUrl" class="mb-3">
        <img :src="previewUrl" alt="Media preview" class="max-h-64 rounded border object-contain" />
      </div>

      <!-- File info -->
      <div class="flex items-center gap-3">
        <component :is="isImage ? Image : File" class="h-5 w-5 text-gray-500" />
        <span class="text-sm text-gray-700">{{ fileTypeLabel }}</span>

        <div class="flex gap-2 ml-auto">
          <button
            @click="downloadMedia"
            class="inline-flex items-center gap-1 px-3 py-1.5 text-sm bg-white border rounded-md hover:bg-gray-50"
          >
            <Download class="h-4 w-4" /> Download
          </button>
          <button
            v-if="!readonly"
            @click="triggerUpload"
            :disabled="isUploading"
            class="inline-flex items-center gap-1 px-3 py-1.5 text-sm bg-white border rounded-md hover:bg-gray-50"
          >
            <Upload class="h-4 w-4" /> Replace
          </button>
          <button
            v-if="!readonly"
            @click="deleteMedia"
            :disabled="isDeleting"
            class="inline-flex items-center gap-1 px-3 py-1.5 text-sm text-red-600 bg-white border border-red-200 rounded-md hover:bg-red-50"
          >
            <Trash2 class="h-4 w-4" /> Delete
          </button>
        </div>
      </div>
    </div>

    <!-- Drop zone (no media or for upload) -->
    <div
      v-if="!hasMedia && !readonly"
      @dragover="handleDragOver"
      @dragleave="handleDragLeave"
      @drop="handleDrop"
      @click="triggerUpload"
      :class="[
        'border-2 border-dashed rounded-lg p-8 text-center cursor-pointer transition-colors',
        isDragging ? 'border-blue-400 bg-blue-50' : 'border-gray-300 hover:border-gray-400 hover:bg-gray-50'
      ]"
    >
      <Upload class="h-8 w-8 mx-auto text-gray-400 mb-2" />
      <p class="text-sm text-gray-600">
        <span v-if="isUploading">Uploading...</span>
        <span v-else>Drop a file here or click to upload</span>
      </p>
    </div>

    <!-- Hidden file input -->
    <input
      ref="fileInput"
      type="file"
      class="hidden"
      @change="handleFileSelect"
    />

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
  </div>
</template>
