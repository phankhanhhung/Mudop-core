<script setup lang="ts">
import { ref, computed, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { DraftManager } from '@/odata/DraftManager'
import type { DraftInstance } from '@/odata/types'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { FileEdit, X, Clock, RotateCcw } from 'lucide-vue-next'

interface Props {
  module: string
  entitySet: string
  entityKey?: string
}

const props = defineProps<Props>()
const router = useRouter()

// Access shared draft manager (same singleton pattern as useDraft)
let draftManager: DraftManager | null = null
function getDraftManager(): DraftManager {
  if (!draftManager) {
    draftManager = new DraftManager()
  }
  return draftManager
}

const dm = getDraftManager()
const showPopover = ref(false)

// Poll for draft state changes (lightweight since it reads from reactive Map)
const pollTimer = ref<ReturnType<typeof setInterval> | null>(null)
const tick = ref(0)

pollTimer.value = setInterval(() => {
  tick.value++
}, 2000)

onUnmounted(() => {
  if (pollTimer.value) {
    clearInterval(pollTimer.value)
  }
})

const hasDraft = computed(() => {
  // eslint-disable-next-line @typescript-eslint/no-unused-expressions
  tick.value // reactive dependency for polling
  if (!props.entityKey) return false
  return !!dm.getDraftForEntity(props.module, props.entitySet, props.entityKey)
})

const drafts = computed<DraftInstance[]>(() => {
  // eslint-disable-next-line @typescript-eslint/no-unused-expressions
  tick.value
  return dm.getDraftsFor(props.module, props.entitySet)
})

const draftCount = computed(() => drafts.value.length)

function formatTimeAgo(date: Date): string {
  const now = Date.now()
  const diff = now - date.getTime()
  const seconds = Math.floor(diff / 1000)
  const minutes = Math.floor(seconds / 60)
  const hours = Math.floor(minutes / 60)

  if (seconds < 60) return 'Just now'
  if (minutes < 60) return `${minutes}m ago`
  if (hours < 24) return `${hours}h ago`
  return `${Math.floor(hours / 24)}d ago`
}

function handleResume(draft: DraftInstance) {
  showPopover.value = false
  if (draft.entityKey) {
    router.push(`/odata/${draft.module}/${draft.entitySet}/${draft.entityKey}/edit`)
  } else {
    router.push(`/odata/${draft.module}/${draft.entitySet}/create`)
  }
}

function handleDiscard(draft: DraftInstance) {
  dm.discard(draft.draftKey)
  tick.value++ // force re-compute
  if (draftCount.value === 0) {
    showPopover.value = false
  }
}

function closePopover() {
  showPopover.value = false
}
</script>

<template>
  <!-- Single entity draft indicator -->
  <Badge
    v-if="entityKey && hasDraft"
    variant="outline"
    class="bg-amber-50 text-amber-700 border-amber-200 dark:bg-amber-950 dark:text-amber-300 dark:border-amber-800"
  >
    <FileEdit class="h-3 w-3 mr-1" />
    Draft
  </Badge>

  <!-- Entity set draft count -->
  <div v-else-if="!entityKey && draftCount > 0" class="relative">
    <Button variant="outline" size="sm" @click="showPopover = !showPopover">
      <FileEdit class="h-4 w-4 mr-1" />
      {{ draftCount }} {{ draftCount === 1 ? 'draft' : 'drafts' }}
    </Button>

    <!-- Popover listing drafts -->
    <div
      v-if="showPopover"
      class="absolute right-0 top-full mt-1 z-50 w-80 rounded-md border bg-popover p-4 shadow-md"
    >
      <div class="flex items-center justify-between mb-2">
        <h4 class="font-medium text-sm">Unsaved Drafts</h4>
        <Button variant="ghost" size="sm" class="h-6 w-6 p-0" @click="closePopover">
          <X class="h-3 w-3" />
        </Button>
      </div>

      <div
        v-for="draft in drafts"
        :key="draft.draftKey"
        class="flex items-center justify-between py-2 border-b last:border-0"
      >
        <div class="min-w-0 flex-1">
          <p class="text-sm font-medium truncate">
            {{ draft.entityKey ? 'Edit: ' + draft.entityKey.substring(0, 8) + '...' : 'New record' }}
          </p>
          <p class="text-xs text-muted-foreground flex items-center gap-1">
            <Clock class="h-3 w-3" />
            {{ formatTimeAgo(draft.lastSaved || draft.createdAt) }}
          </p>
        </div>
        <div class="flex gap-1 shrink-0 ml-2">
          <Button size="sm" variant="ghost" class="h-7 px-2 text-xs" @click="handleResume(draft)">
            <RotateCcw class="h-3 w-3 mr-1" />
            Resume
          </Button>
          <Button
            size="sm"
            variant="ghost"
            class="h-7 px-2 text-xs text-destructive hover:text-destructive"
            @click="handleDiscard(draft)"
          >
            <X class="h-3 w-3 mr-1" />
            Discard
          </Button>
        </div>
      </div>

      <div v-if="draftCount === 0" class="text-sm text-muted-foreground text-center py-2">
        No drafts
      </div>
    </div>
  </div>
</template>
