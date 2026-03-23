<script setup lang="ts">
import { ref, computed } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import UploadCollection from '@/components/smart/UploadCollection.vue'
import type { UploadFileItem } from '@/composables/useUploadCollection'
import {
  Upload,
  ArrowLeft,
  Settings,
  ImageIcon,
  FileIcon,
  Zap,
  Hash,
} from 'lucide-vue-next'

// ─── Configuration state ──────────────────────────────────────────────────

const maxFilesOption = ref<number>(10)
const maxFileSizeMB = ref<number>(50)
const imagesOnly = ref(false)
const autoUpload = ref(false)

const maxFileSize = computed(() => maxFileSizeMB.value * 1024 * 1024)

const allowedTypes = computed(() => {
  if (imagesOnly.value) return ['image/*']
  return []
})

// ─── Mock upload function ─────────────────────────────────────────────────

function mockUploadFn(file: File, onProgress: (pct: number) => void): Promise<string> {
  return new Promise((resolve, reject) => {
    let progress = 0
    const interval = setInterval(() => {
      progress += 10
      onProgress(Math.min(progress, 100))

      if (progress >= 100) {
        clearInterval(interval)
        // ~20% failure rate
        if (Math.random() < 0.2) {
          reject(new Error(`Upload failed for ${file.name}: server error`))
        } else {
          resolve(`https://storage.example.com/files/${encodeURIComponent(file.name)}`)
        }
      }
    }, 200)
  })
}

// ─── Event handlers ───────────────────────────────────────────────────────

const uploadLog = ref<string[]>([])

function onUploadComplete(item: UploadFileItem) {
  const entry = `Completed: ${item.name} -> ${item.uploadedUrl}`
  if (!uploadLog.value.includes(entry)) {
    uploadLog.value.unshift(entry)
  }
  if (uploadLog.value.length > 20) uploadLog.value.pop()
}

function onUploadError(item: UploadFileItem) {
  const entry = `Error: ${item.name} - ${item.error}`
  if (!uploadLog.value.includes(entry)) {
    uploadLog.value.unshift(entry)
  }
  if (uploadLog.value.length > 20) uploadLog.value.pop()
}

function onFilesChange(_items: UploadFileItem[]) {
  // Track total files for display
}

function clearLog() {
  uploadLog.value = []
}
</script>

<template>
  <DefaultLayout>
    <div class="space-y-8 pb-12">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div>
          <div class="flex items-center gap-2 mb-1">
            <router-link to="/showcase" class="text-muted-foreground hover:text-foreground transition-colors">
              <ArrowLeft class="h-5 w-5" />
            </router-link>
            <h1 class="text-3xl font-bold text-foreground">Upload Collection</h1>
          </div>
          <p class="text-muted-foreground">
            File upload component with drag-and-drop, progress tracking, validation, and list/grid layouts
          </p>
        </div>
      </div>

      <!-- ================================================================ -->
      <!-- Configuration Panel                                               -->
      <!-- ================================================================ -->
      <Card>
        <CardHeader>
          <div class="flex items-center gap-3">
            <div class="flex h-10 w-10 items-center justify-center rounded-lg bg-primary/10">
              <Settings class="h-5 w-5 text-primary" />
            </div>
            <div>
              <CardTitle>Configuration</CardTitle>
              <p class="text-sm text-muted-foreground mt-0.5">
                Adjust settings to see how the component adapts
              </p>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6">
            <!-- Max Files -->
            <div>
              <label class="text-xs font-medium text-muted-foreground mb-2 block">
                <Hash class="h-3.5 w-3.5 inline mr-1" />
                Max Files
              </label>
              <div class="flex gap-1.5">
                <Button
                  v-for="opt in [5, 10, 999]"
                  :key="opt"
                  size="sm"
                  :variant="maxFilesOption === opt ? 'default' : 'outline'"
                  @click="maxFilesOption = opt"
                >
                  {{ opt === 999 ? 'Unlimited' : opt }}
                </Button>
              </div>
            </div>

            <!-- Max File Size -->
            <div>
              <label class="text-xs font-medium text-muted-foreground mb-2 block">
                <FileIcon class="h-3.5 w-3.5 inline mr-1" />
                Max File Size
              </label>
              <div class="flex gap-1.5">
                <Button
                  v-for="opt in [5, 10, 50]"
                  :key="opt"
                  size="sm"
                  :variant="maxFileSizeMB === opt ? 'default' : 'outline'"
                  @click="maxFileSizeMB = opt"
                >
                  {{ opt }}MB
                </Button>
              </div>
            </div>

            <!-- File Type Filter -->
            <div>
              <label class="text-xs font-medium text-muted-foreground mb-2 block">
                <ImageIcon class="h-3.5 w-3.5 inline mr-1" />
                Allowed Types
              </label>
              <div class="flex gap-1.5">
                <Button
                  size="sm"
                  :variant="!imagesOnly ? 'default' : 'outline'"
                  @click="imagesOnly = false"
                >
                  All Files
                </Button>
                <Button
                  size="sm"
                  :variant="imagesOnly ? 'default' : 'outline'"
                  @click="imagesOnly = true"
                >
                  Images Only
                </Button>
              </div>
            </div>

            <!-- Auto Upload -->
            <div>
              <label class="text-xs font-medium text-muted-foreground mb-2 block">
                <Zap class="h-3.5 w-3.5 inline mr-1" />
                Auto Upload
              </label>
              <div class="flex gap-1.5">
                <Button
                  size="sm"
                  :variant="!autoUpload ? 'default' : 'outline'"
                  @click="autoUpload = false"
                >
                  Manual
                </Button>
                <Button
                  size="sm"
                  :variant="autoUpload ? 'default' : 'outline'"
                  @click="autoUpload = true"
                >
                  Auto
                </Button>
              </div>
            </div>
          </div>

          <div class="mt-4 flex flex-wrap gap-2">
            <Badge variant="secondary">
              Max {{ maxFilesOption === 999 ? 'unlimited' : maxFilesOption }} files
            </Badge>
            <Badge variant="secondary">
              {{ maxFileSizeMB }}MB limit
            </Badge>
            <Badge :variant="imagesOnly ? 'default' : 'secondary'">
              {{ imagesOnly ? 'Images only' : 'All file types' }}
            </Badge>
            <Badge :variant="autoUpload ? 'default' : 'secondary'">
              {{ autoUpload ? 'Auto upload' : 'Manual upload' }}
            </Badge>
          </div>
        </CardContent>
      </Card>

      <!-- ================================================================ -->
      <!-- Upload Collection Demo                                            -->
      <!-- ================================================================ -->
      <Card>
        <CardHeader>
          <div class="flex items-center gap-3">
            <div class="flex h-10 w-10 items-center justify-center rounded-lg bg-primary/10">
              <Upload class="h-5 w-5 text-primary" />
            </div>
            <div>
              <CardTitle>Upload Collection</CardTitle>
              <p class="text-sm text-muted-foreground mt-0.5">
                Drag and drop files or click to browse. Toggle between list and grid views.
                <Badge variant="secondary" class="text-xs ml-1">~20% simulated failure rate</Badge>
              </p>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <UploadCollection
            :key="`${maxFilesOption}-${maxFileSizeMB}-${imagesOnly}-${autoUpload}`"
            title="Attachments"
            :max-files="maxFilesOption"
            :max-file-size="maxFileSize"
            :allowed-types="allowedTypes"
            :auto-upload="autoUpload"
            :upload-fn="mockUploadFn"
            @upload-complete="onUploadComplete"
            @upload-error="onUploadError"
            @files-change="onFilesChange"
          />
        </CardContent>
      </Card>

      <!-- ================================================================ -->
      <!-- Event Log                                                         -->
      <!-- ================================================================ -->
      <Card v-if="uploadLog.length > 0">
        <CardHeader>
          <div class="flex items-center justify-between">
            <CardTitle class="text-base">Event Log</CardTitle>
            <Button variant="ghost" size="sm" @click="clearLog">Clear</Button>
          </div>
        </CardHeader>
        <CardContent>
          <div class="rounded-md bg-muted p-3 max-h-48 overflow-y-auto font-mono text-xs space-y-1">
            <div
              v-for="(entry, idx) in uploadLog"
              :key="idx"
              :class="entry.startsWith('Error') ? 'text-destructive' : 'text-emerald-600 dark:text-emerald-400'"
            >
              {{ entry }}
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
