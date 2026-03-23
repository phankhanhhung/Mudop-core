import { ref, computed, type Ref, type ComputedRef } from 'vue'

export type UploadStatus = 'pending' | 'uploading' | 'complete' | 'error'

export interface UploadFileItem {
  id: string
  file: File
  name: string
  size: number
  type: string
  status: UploadStatus
  progress: number // 0-100
  error?: string
  thumbnailUrl?: string
  uploadedUrl?: string
}

export interface UseUploadCollectionOptions {
  maxFiles?: number
  maxFileSize?: number // bytes
  allowedTypes?: string[] // mime type patterns like 'image/*', 'application/pdf'
  autoUpload?: boolean
  uploadFn?: (file: File, onProgress: (pct: number) => void) => Promise<string>
}

export interface UseUploadCollectionReturn {
  items: Ref<UploadFileItem[]>
  isUploading: ComputedRef<boolean>
  pendingCount: ComputedRef<number>
  completedCount: ComputedRef<number>
  errorCount: ComputedRef<number>
  addFiles: (files: FileList | File[]) => string[]
  removeFile: (id: string) => void
  retryFile: (id: string) => Promise<void>
  startUpload: () => Promise<void>
  clearCompleted: () => void
  clearAll: () => void
}

function generateId(): string {
  return `upload-${Date.now()}-${Math.random().toString(36).slice(2, 9)}`
}

function matchesMimePattern(mimeType: string, pattern: string): boolean {
  if (pattern === '*' || pattern === '*/*') return true
  if (pattern.endsWith('/*')) {
    const prefix = pattern.slice(0, -2)
    return mimeType.startsWith(prefix + '/')
  }
  return mimeType === pattern
}

function generateThumbnail(file: File): Promise<string | undefined> {
  return new Promise((resolve) => {
    if (!file.type.startsWith('image/')) {
      resolve(undefined)
      return
    }
    const reader = new FileReader()
    reader.onload = (e) => resolve(e.target?.result as string)
    reader.onerror = () => resolve(undefined)
    reader.readAsDataURL(file)
  })
}

export function useUploadCollection(options: UseUploadCollectionOptions = {}): UseUploadCollectionReturn {
  const {
    maxFiles = Infinity,
    maxFileSize = 50 * 1024 * 1024, // 50MB default
    allowedTypes = [],
    autoUpload = false,
    uploadFn,
  } = options

  const items = ref<UploadFileItem[]>([])

  const isUploading = computed(() => items.value.some(i => i.status === 'uploading'))
  const pendingCount = computed(() => items.value.filter(i => i.status === 'pending').length)
  const completedCount = computed(() => items.value.filter(i => i.status === 'complete').length)
  const errorCount = computed(() => items.value.filter(i => i.status === 'error').length)

  function validateFile(file: File): string | null {
    if (file.size > maxFileSize) {
      const maxMB = (maxFileSize / (1024 * 1024)).toFixed(1)
      return `File exceeds ${maxMB}MB limit`
    }
    if (allowedTypes.length > 0) {
      const matches = allowedTypes.some(pattern => matchesMimePattern(file.type, pattern))
      if (!matches) return `File type ${file.type || 'unknown'} is not allowed`
    }
    return null
  }

  function addFiles(files: FileList | File[]): string[] {
    const fileArray = Array.from(files)
    const currentCount = items.value.length
    const available = maxFiles - currentCount
    if (available <= 0) return []

    const toAdd = fileArray.slice(0, available)
    const addedIds: string[] = []

    for (const file of toAdd) {
      const validationError = validateFile(file)
      const id = generateId()

      const item: UploadFileItem = {
        id,
        file,
        name: file.name,
        size: file.size,
        type: file.type,
        status: validationError ? 'error' : 'pending',
        progress: 0,
        error: validationError ?? undefined,
      }

      // Generate thumbnail for images
      if (file.type.startsWith('image/')) {
        generateThumbnail(file).then(url => {
          const found = items.value.find(i => i.id === id)
          if (found) found.thumbnailUrl = url
        })
      }

      items.value.push(item)
      addedIds.push(id)
    }

    if (autoUpload && uploadFn) {
      startUpload()
    }

    return addedIds
  }

  function removeFile(id: string) {
    const idx = items.value.findIndex(i => i.id === id)
    if (idx >= 0) {
      items.value.splice(idx, 1)
    }
  }

  async function uploadSingleFile(item: UploadFileItem) {
    if (!uploadFn) return
    item.status = 'uploading'
    item.progress = 0
    item.error = undefined

    try {
      const url = await uploadFn(item.file, (pct) => {
        item.progress = Math.min(100, Math.max(0, pct))
      })
      item.status = 'complete'
      item.progress = 100
      item.uploadedUrl = url
    } catch (e: unknown) {
      item.status = 'error'
      item.error = e instanceof Error ? e.message : 'Upload failed'
    }
  }

  async function startUpload() {
    const pending = items.value.filter(i => i.status === 'pending')
    // Upload sequentially to avoid overwhelming the server
    for (const item of pending) {
      await uploadSingleFile(item)
    }
  }

  async function retryFile(id: string) {
    const item = items.value.find(i => i.id === id)
    if (item && item.status === 'error') {
      item.status = 'pending'
      item.progress = 0
      item.error = undefined
      await uploadSingleFile(item)
    }
  }

  function clearCompleted() {
    items.value = items.value.filter(i => i.status !== 'complete')
  }

  function clearAll() {
    items.value = []
  }

  return {
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
    clearAll,
  }
}
