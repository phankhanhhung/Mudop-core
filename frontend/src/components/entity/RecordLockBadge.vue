<script setup lang="ts">
import { computed } from 'vue'
import { Lock } from 'lucide-vue-next'
import type { LockState } from '@/composables/useRecordLock'

const props = defineProps<{
  lockState: LockState
  currentUserId: string
}>()

function formatRelativeTime(iso: string | undefined): string {
  if (!iso) return ''
  const date = new Date(iso)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffMin = Math.floor(diffMs / 60000)
  if (diffMin < 1) return 'just now'
  if (diffMin < 60) return `${diffMin}m ago`
  const diffHr = Math.floor(diffMin / 60)
  if (diffHr < 24) return `${diffHr}h ago`
  return `${Math.floor(diffHr / 24)}d ago`
}

const timeAgo = computed(() => formatRelativeTime(props.lockState.startedAt))
</script>

<template>
  <span
    v-if="lockState.locked"
    class="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium"
    :class="lockState.isMe
      ? 'bg-amber-100 text-amber-700'
      : 'bg-red-100 text-red-700'"
  >
    <Lock class="h-3 w-3 shrink-0" />
    <span v-if="lockState.isMe">Editing (you)</span>
    <span v-else>{{ lockState.displayName }} is editing</span>
    <span v-if="timeAgo" class="opacity-70">· {{ timeAgo }}</span>
  </span>
</template>
