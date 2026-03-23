<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import {
  DialogRoot,
  DialogPortal,
  DialogOverlay,
  DialogContent,
  DialogTitle,
  DialogDescription,
  DialogClose
} from 'radix-vue'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Spinner } from '@/components/ui/spinner'
import { X, GitCompare } from 'lucide-vue-next'
import { odataService } from '@/services/odataService'
import VersionDiff from './VersionDiff.vue'
import type { FieldMetadata } from '@/types/metadata'
import { formatDateTime } from '@/utils/formatting'

interface Props {
  open: boolean
  module: string
  entitySet: string
  entityId: string
  fields: FieldMetadata[]
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  'select-version': [version: Record<string, unknown>]
}>()

const versions = ref<Record<string, unknown>[]>([])
const isLoading = ref(false)
const error = ref<string | null>(null)

// Two-selection mode for diff
const selectedVersions = ref<number[]>([])
const showDiff = ref(false)

const version1 = computed(() => {
  if (selectedVersions.value.length < 2) return null
  return versions.value[selectedVersions.value[0]]
})

const version2 = computed(() => {
  if (selectedVersions.value.length < 2) return null
  return versions.value[selectedVersions.value[1]]
})

watch(
  () => props.open,
  async (isOpen) => {
    if (isOpen) {
      selectedVersions.value = []
      showDiff.value = false
      await loadVersions()
    }
  }
)

async function loadVersions() {
  isLoading.value = true
  error.value = null
  try {
    const result = await odataService.getVersions<Record<string, unknown>>(
      props.module,
      props.entitySet,
      props.entityId
    )
    // Sort newest first
    versions.value = result.sort((a, b) => {
      const aTime = String(a['SystemStart'] ?? a['system_start'] ?? '')
      const bTime = String(b['SystemStart'] ?? b['system_start'] ?? '')
      return bTime.localeCompare(aTime)
    })
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to load version history'
  } finally {
    isLoading.value = false
  }
}

function toggleVersionSelect(index: number) {
  const pos = selectedVersions.value.indexOf(index)
  if (pos >= 0) {
    selectedVersions.value.splice(pos, 1)
    showDiff.value = false
  } else {
    if (selectedVersions.value.length >= 2) {
      // Replace the oldest selection
      selectedVersions.value.shift()
    }
    selectedVersions.value.push(index)
  }
}

function isSelected(index: number): boolean {
  return selectedVersions.value.includes(index)
}

function handleCompare() {
  if (selectedVersions.value.length === 2) {
    showDiff.value = true
  }
}

function handleSelectVersion(version: Record<string, unknown>) {
  emit('select-version', version)
}

function formatTimestamp(value: unknown): string {
  if (!value) return '-'
  return formatDateTime(String(value))
}

function isCurrent(version: Record<string, unknown>): boolean {
  const end = version['SystemEnd'] ?? version['system_end']
  if (!end) return true
  // Check if system_end is max value (9999-12-31 or similar)
  const endStr = String(end)
  return endStr.startsWith('9999') || endStr.startsWith('infinity')
}

function onOpenChange(value: boolean) {
  emit('update:open', value)
}
</script>

<template>
  <DialogRoot :open="open" @update:open="onOpenChange">
    <DialogPortal>
      <DialogOverlay
        class="fixed inset-0 z-50 bg-black/50 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0"
      />
      <DialogContent
        class="fixed left-1/2 top-1/2 z-50 w-full max-w-2xl max-h-[85vh] -translate-x-1/2 -translate-y-1/2 rounded-lg border bg-background shadow-lg duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[state=closed]:slide-out-to-left-1/2 data-[state=closed]:slide-out-to-top-[48%] data-[state=open]:slide-in-from-left-1/2 data-[state=open]:slide-in-from-top-[48%] flex flex-col"
      >
        <!-- Header -->
        <div class="flex items-center justify-between p-6 pb-4 border-b">
          <div>
            <DialogTitle class="text-lg font-semibold text-foreground">
              Version History
            </DialogTitle>
            <DialogDescription class="text-sm text-muted-foreground mt-1">
              Select two versions to compare changes
            </DialogDescription>
          </div>
          <div class="flex items-center gap-2">
            <Button
              v-if="selectedVersions.length === 2"
              variant="default"
              size="sm"
              @click="handleCompare"
            >
              <GitCompare class="mr-1.5 h-3.5 w-3.5" />
              Compare
            </Button>
            <DialogClose as-child>
              <Button variant="ghost" size="icon">
                <X class="h-4 w-4" />
              </Button>
            </DialogClose>
          </div>
        </div>

        <!-- Content -->
        <div class="flex-1 overflow-y-auto p-6">
          <!-- Loading -->
          <div v-if="isLoading" class="flex items-center justify-center py-12">
            <Spinner size="lg" />
          </div>

          <!-- Error -->
          <div v-else-if="error" class="text-center py-12">
            <p class="text-sm text-destructive">{{ error }}</p>
            <Button class="mt-3" variant="outline" size="sm" @click="loadVersions">
              Retry
            </Button>
          </div>

          <!-- Empty -->
          <div v-else-if="versions.length === 0" class="text-center py-12">
            <p class="text-sm text-muted-foreground">No version history available.</p>
          </div>

          <!-- Diff view -->
          <div v-else-if="showDiff && version1 && version2">
            <div class="flex items-center justify-between mb-4">
              <h3 class="text-sm font-medium">Comparing versions</h3>
              <Button variant="ghost" size="sm" @click="showDiff = false">
                Back to list
              </Button>
            </div>
            <VersionDiff
              :version1="version1"
              :version2="version2"
              :fields="fields"
            />
          </div>

          <!-- Version list -->
          <div v-else class="space-y-2">
            <div
              v-for="(version, index) in versions"
              :key="index"
              class="flex items-center gap-3 p-3 rounded-lg border cursor-pointer transition-colors"
              :class="{
                'border-primary bg-primary/5': isSelected(index),
                'hover:bg-muted/50': !isSelected(index)
              }"
              @click="toggleVersionSelect(index)"
            >
              <!-- Selection indicator -->
              <div
                class="w-6 h-6 rounded-full border-2 flex items-center justify-center shrink-0 text-xs font-medium"
                :class="isSelected(index) ? 'border-primary bg-primary text-primary-foreground' : 'border-muted-foreground/30'"
              >
                <span v-if="isSelected(index)">{{ selectedVersions.indexOf(index) + 1 }}</span>
              </div>

              <!-- Version info -->
              <div class="flex-1 min-w-0">
                <div class="flex items-center gap-2">
                  <span class="text-sm font-medium">
                    {{ formatTimestamp(version['SystemStart'] ?? version['system_start']) }}
                  </span>
                  <Badge v-if="isCurrent(version)" variant="default" class="text-xs">
                    Current
                  </Badge>
                </div>
                <div class="text-xs text-muted-foreground mt-0.5">
                  <span v-if="version['SystemEnd'] ?? version['system_end']">
                    Valid until {{ formatTimestamp(version['SystemEnd'] ?? version['system_end']) }}
                  </span>
                </div>
              </div>

              <!-- View button -->
              <Button
                variant="ghost"
                size="sm"
                @click.stop="handleSelectVersion(version)"
              >
                View
              </Button>
            </div>
          </div>
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
