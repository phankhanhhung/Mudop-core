<script setup lang="ts">
import { computed } from 'vue'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import type { UploadFileItem } from '@/composables/useUploadCollection'
import {
  X,
  RefreshCw,
  Download,
  FileIcon,
  FileText,
  ImageIcon,
  FileArchive,
  FileSpreadsheet,
  CheckCircle2,
  AlertCircle,
  Loader2,
} from 'lucide-vue-next'

interface Props {
  item: UploadFileItem
  layout: 'list' | 'grid'
  readonly?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  readonly: false,
})

const emit = defineEmits<{
  remove: [id: string]
  retry: [id: string]
  download: [id: string]
}>()

const fileIcon = computed(() => {
  const t = props.item.type
  if (t.startsWith('image/')) return ImageIcon
  if (t === 'application/pdf') return FileText
  if (t.includes('spreadsheet') || t.includes('excel') || t === 'text/csv') return FileSpreadsheet
  if (t.includes('zip') || t.includes('archive') || t.includes('compressed')) return FileArchive
  return FileIcon
})

const formattedSize = computed(() => {
  const bytes = props.item.size
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
})

const statusText = computed(() => {
  switch (props.item.status) {
    case 'pending': return 'Ready'
    case 'uploading': return `Uploading... ${props.item.progress}%`
    case 'complete': return 'Complete'
    case 'error': return props.item.error || 'Error'
  }
})

const statusColor = computed(() => {
  switch (props.item.status) {
    case 'pending': return 'text-muted-foreground'
    case 'uploading': return 'text-primary'
    case 'complete': return 'text-emerald-600 dark:text-emerald-400'
    case 'error': return 'text-destructive'
  }
})

const statusIcon = computed(() => {
  switch (props.item.status) {
    case 'uploading': return Loader2
    case 'complete': return CheckCircle2
    case 'error': return AlertCircle
    default: return null
  }
})

const progressBarColor = computed(() => {
  switch (props.item.status) {
    case 'uploading': return 'bg-primary'
    case 'error': return 'bg-destructive'
    case 'complete': return 'bg-emerald-500'
    default: return 'bg-muted-foreground/30'
  }
})
</script>

<template>
  <!-- List layout -->
  <div
    v-if="layout === 'list'"
    class="group relative flex items-center gap-3 px-3 py-2.5 hover:bg-muted/50 transition-colors"
  >
    <!-- Thumbnail / Icon -->
    <div class="flex-shrink-0 h-10 w-10 rounded-md bg-muted flex items-center justify-center overflow-hidden">
      <img
        v-if="item.thumbnailUrl"
        :src="item.thumbnailUrl"
        :alt="item.name"
        class="h-full w-full object-cover"
      />
      <component
        :is="fileIcon"
        v-else
        class="h-5 w-5 text-muted-foreground"
      />
    </div>

    <!-- Info -->
    <div class="flex-1 min-w-0">
      <div class="flex items-center gap-2">
        <p class="text-sm font-medium text-foreground truncate">{{ item.name }}</p>
        <span class="text-xs text-muted-foreground flex-shrink-0">{{ formattedSize }}</span>
      </div>
      <div class="flex items-center gap-1.5 mt-0.5">
        <component
          :is="statusIcon"
          v-if="statusIcon"
          :class="cn('h-3.5 w-3.5', statusColor, item.status === 'uploading' ? 'animate-spin' : '')"
        />
        <span :class="cn('text-xs', statusColor)">{{ statusText }}</span>
      </div>
    </div>

    <!-- Progress bar -->
    <div
      v-if="item.status === 'uploading' || (item.status === 'error' && item.progress > 0)"
      class="absolute bottom-0 left-0 right-0 h-0.5 bg-muted"
    >
      <div
        :class="cn('h-full transition-all duration-300', progressBarColor)"
        :style="{ width: `${item.progress}%` }"
      />
    </div>

    <!-- Actions -->
    <div class="flex items-center gap-1 flex-shrink-0">
      <Button
        v-if="item.status === 'error' && !readonly"
        variant="ghost"
        size="icon"
        class="h-8 w-8"
        title="Retry upload"
        @click="emit('retry', item.id)"
      >
        <RefreshCw class="h-4 w-4" />
      </Button>
      <Button
        v-if="item.status === 'complete'"
        variant="ghost"
        size="icon"
        class="h-8 w-8"
        title="Download"
        @click="emit('download', item.id)"
      >
        <Download class="h-4 w-4" />
      </Button>
      <Button
        v-if="!readonly"
        variant="ghost"
        size="icon"
        class="h-8 w-8 text-muted-foreground hover:text-destructive"
        title="Remove"
        @click="emit('remove', item.id)"
      >
        <X class="h-4 w-4" />
      </Button>
    </div>
  </div>

  <!-- Grid layout -->
  <div
    v-else
    class="group relative flex flex-col rounded-lg border bg-card overflow-hidden w-36"
  >
    <!-- Thumbnail / Icon area -->
    <div class="relative h-24 bg-muted flex items-center justify-center overflow-hidden">
      <img
        v-if="item.thumbnailUrl"
        :src="item.thumbnailUrl"
        :alt="item.name"
        class="h-full w-full object-cover"
      />
      <component
        :is="fileIcon"
        v-else
        class="h-10 w-10 text-muted-foreground/60"
      />

      <!-- Upload progress overlay -->
      <div
        v-if="item.status === 'uploading'"
        class="absolute inset-0 bg-background/70 flex flex-col items-center justify-center gap-1"
      >
        <Loader2 class="h-6 w-6 text-primary animate-spin" />
        <span class="text-xs font-medium text-primary">{{ item.progress }}%</span>
      </div>

      <!-- Error overlay -->
      <div
        v-if="item.status === 'error'"
        class="absolute inset-0 bg-destructive/10 flex items-center justify-center"
      >
        <AlertCircle class="h-6 w-6 text-destructive" />
      </div>

      <!-- Complete overlay -->
      <div
        v-if="item.status === 'complete'"
        class="absolute top-1.5 right-1.5"
      >
        <CheckCircle2 class="h-5 w-5 text-emerald-500" />
      </div>

      <!-- Remove button (top-right, shown on hover) -->
      <Button
        v-if="!readonly"
        variant="ghost"
        size="icon"
        class="absolute top-1 left-1 h-6 w-6 bg-background/80 opacity-0 group-hover:opacity-100 transition-opacity"
        @click="emit('remove', item.id)"
      >
        <X class="h-3.5 w-3.5" />
      </Button>
    </div>

    <!-- Progress bar -->
    <div
      v-if="item.status === 'uploading'"
      class="h-1 bg-muted"
    >
      <div
        class="h-full bg-primary transition-all duration-300"
        :style="{ width: `${item.progress}%` }"
      />
    </div>

    <!-- Name + actions -->
    <div class="p-2">
      <p class="text-xs font-medium text-foreground truncate" :title="item.name">{{ item.name }}</p>
      <p class="text-[10px] text-muted-foreground mt-0.5">{{ formattedSize }}</p>
      <div class="flex items-center gap-1 mt-1.5">
        <Button
          v-if="item.status === 'error' && !readonly"
          variant="ghost"
          size="icon"
          class="h-6 w-6"
          title="Retry"
          @click="emit('retry', item.id)"
        >
          <RefreshCw class="h-3 w-3" />
        </Button>
        <Button
          v-if="item.status === 'complete'"
          variant="ghost"
          size="icon"
          class="h-6 w-6"
          title="Download"
          @click="emit('download', item.id)"
        >
          <Download class="h-3 w-3" />
        </Button>
      </div>
    </div>
  </div>
</template>
