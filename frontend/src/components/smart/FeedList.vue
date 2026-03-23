<script setup lang="ts">
import { ref, computed, nextTick } from 'vue'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { Heart, MessageCircle, Trash2, Send } from 'lucide-vue-next'

export interface FeedItem {
  id: string
  author: string
  avatar?: string
  datetime: string
  content: string
  liked?: boolean
  likeCount?: number
  replyCount?: number
}

interface Props {
  items: FeedItem[]
  showInput?: boolean
  inputPlaceholder?: string
  currentUser?: string
  currentUserAvatar?: string
  maxLength?: number
  readonly?: boolean
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  showInput: true,
  inputPlaceholder: 'Write a comment...',
  currentUser: 'You',
  currentUserAvatar: undefined,
  maxLength: undefined,
  readonly: false,
})

const emit = defineEmits<{
  post: [payload: { content: string }]
  like: [payload: { itemId: string; liked: boolean }]
  reply: [payload: { itemId: string }]
  delete: [payload: { itemId: string }]
}>()

// ── Input state ──────────────────────────────────────────────────────

const inputText = ref('')
const textareaRef = ref<HTMLTextAreaElement | null>(null)

const charCount = computed(() => inputText.value.length)
const isOverLimit = computed(() => props.maxLength != null && charCount.value > props.maxLength)
const canPost = computed(() => inputText.value.trim().length > 0 && !isOverLimit.value)

function autoResize() {
  const el = textareaRef.value
  if (!el) return
  el.style.height = 'auto'
  el.style.height = el.scrollHeight + 'px'
}

function handlePost() {
  if (!canPost.value) return
  emit('post', { content: inputText.value.trim() })
  inputText.value = ''
  nextTick(autoResize)
}

function onInputKeydown(e: KeyboardEvent) {
  if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
    e.preventDefault()
    handlePost()
  }
}

// ── Like toggle ──────────────────────────────────────────────────────

function toggleLike(item: FeedItem) {
  emit('like', { itemId: item.id, liked: !item.liked })
}

// ── Relative time formatting ─────────────────────────────────────────

function formatRelativeTime(iso: string): string {
  const date = new Date(iso)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffSec = Math.floor(diffMs / 1000)
  const diffMin = Math.floor(diffSec / 60)
  const diffHour = Math.floor(diffMin / 60)
  const diffDay = Math.floor(diffHour / 24)

  if (diffSec < 60) return 'just now'
  if (diffMin < 60) return `${diffMin} min ago`
  if (diffHour < 24) return `${diffHour} hour${diffHour > 1 ? 's' : ''} ago`
  if (diffDay < 7) return `${diffDay} day${diffDay > 1 ? 's' : ''} ago`

  return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
}

// ── Avatar initials ──────────────────────────────────────────────────

function getInitials(text?: string): string {
  if (!text) return '?'
  const parts = text.trim().split(/\s+/)
  if (parts.length === 1) return parts[0].charAt(0).toUpperCase()
  return (parts[0].charAt(0) + parts[parts.length - 1].charAt(0)).toUpperCase()
}
</script>

<template>
  <div :class="cn('space-y-4', props.class)">
    <!-- Input area -->
    <div
      v-if="showInput && !readonly"
      class="flex gap-3"
    >
      <div
        class="h-9 w-9 rounded-full bg-primary text-primary-foreground flex items-center justify-center text-sm font-medium shrink-0"
      >
        {{ currentUserAvatar || getInitials(currentUser) }}
      </div>
      <div class="flex-1 space-y-2">
        <textarea
          ref="textareaRef"
          v-model="inputText"
          :placeholder="inputPlaceholder"
          rows="1"
          class="flex w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 resize-none overflow-hidden"
          @input="autoResize"
          @keydown="onInputKeydown"
        />
        <div class="flex items-center justify-between">
          <span
            v-if="maxLength != null"
            class="text-xs"
            :class="isOverLimit ? 'text-destructive font-medium' : 'text-muted-foreground'"
          >
            {{ charCount }} / {{ maxLength }}
          </span>
          <span v-else />
          <Button
            size="sm"
            :disabled="!canPost"
            @click="handlePost"
          >
            <Send class="h-4 w-4 mr-1.5" />
            Post
          </Button>
        </div>
      </div>
    </div>

    <!-- Divider between input and list -->
    <div
      v-if="showInput && !readonly && items.length > 0"
      class="border-t"
    />

    <!-- Empty state -->
    <div
      v-if="items.length === 0"
      class="flex flex-col items-center justify-center py-12 text-muted-foreground"
    >
      <MessageCircle class="h-10 w-10 mb-3 opacity-40" />
      <p class="text-sm">No comments yet</p>
      <p v-if="showInput && !readonly" class="text-xs mt-1">Be the first to comment</p>
    </div>

    <!-- Feed items -->
    <div
      v-for="item in items"
      :key="item.id"
      class="flex gap-3 group"
    >
      <!-- Avatar -->
      <div
        class="h-9 w-9 rounded-full bg-muted text-muted-foreground flex items-center justify-center text-sm font-medium shrink-0"
      >
        {{ item.avatar || getInitials(item.author) }}
      </div>

      <!-- Content -->
      <div class="flex-1 min-w-0">
        <div class="flex items-baseline gap-2">
          <span class="text-sm font-medium">{{ item.author }}</span>
          <span class="text-xs text-muted-foreground">{{ formatRelativeTime(item.datetime) }}</span>
        </div>
        <p class="text-sm mt-0.5 whitespace-pre-wrap break-words">{{ item.content }}</p>

        <!-- Actions -->
        <div v-if="!readonly" class="flex items-center gap-4 mt-1.5">
          <!-- Like -->
          <button
            type="button"
            class="flex items-center gap-1 text-xs transition-colors"
            :class="item.liked ? 'text-rose-500' : 'text-muted-foreground hover:text-rose-500'"
            @click="toggleLike(item)"
          >
            <Heart
              class="h-3.5 w-3.5 transition-all"
              :class="item.liked ? 'fill-current scale-110' : ''"
            />
            <span v-if="(item.likeCount ?? 0) > 0">{{ item.likeCount }}</span>
          </button>

          <!-- Reply -->
          <button
            type="button"
            class="flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground transition-colors"
            @click="emit('reply', { itemId: item.id })"
          >
            <MessageCircle class="h-3.5 w-3.5" />
            <span v-if="(item.replyCount ?? 0) > 0">{{ item.replyCount }}</span>
          </button>

          <!-- Delete (only for current user's items) -->
          <button
            v-if="item.author === currentUser"
            type="button"
            class="flex items-center gap-1 text-xs text-muted-foreground hover:text-destructive transition-colors opacity-0 group-hover:opacity-100"
            @click="emit('delete', { itemId: item.id })"
          >
            <Trash2 class="h-3.5 w-3.5" />
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
