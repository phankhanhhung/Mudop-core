<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Spinner } from '@/components/ui/spinner'
import FeedList from '@/components/smart/FeedList.vue'
import type { FeedItem } from '@/components/smart/FeedList.vue'
import { commentService } from '@/services/commentService'
import type { Comment } from '@/services/commentService'
import { useSignalR } from '@/composables/useSignalR'
import { useUiStore } from '@/stores/ui'

const props = defineProps<{
  module: string
  entityType: string
  entityId: string
  currentUserId: string
  currentUserName: string
  availableUsers?: string[]
}>()

const uiStore = useUiStore()
const { joinRecordGroup, onNewComment } = useSignalR()

const comments = ref<Comment[]>([])
const loading = ref(false)

const recordKey = computed(() => `${props.module}/${props.entityType}/${props.entityId}`)

const feedItems = computed<FeedItem[]>(() =>
  comments.value.map((c) => ({
    id: c.id,
    author: c.authorName,
    datetime: c.createdAt,
    content: c.content,
    liked: c.likedBy.includes(props.currentUserId),
    likeCount: c.likedBy.length,
  }))
)

async function loadComments(): Promise<void> {
  loading.value = true
  try {
    comments.value = await commentService.listComments(
      props.module,
      props.entityType,
      props.entityId
    )
  } catch (e) {
    uiStore.error('Failed to load comments', e instanceof Error ? e.message : undefined)
  } finally {
    loading.value = false
  }
}

async function handlePost(payload: { content: string }): Promise<void> {
  const mentionRegex = /\B@(\w+)/g
  const mentions: string[] = []
  let match: RegExpExecArray | null
  while ((match = mentionRegex.exec(payload.content)) !== null) {
    mentions.push(match[1])
  }

  try {
    const created = await commentService.createComment(
      props.module,
      props.entityType,
      props.entityId,
      payload.content,
      mentions
    )
    comments.value.unshift(created)
    uiStore.success('Comment posted')
  } catch (e) {
    uiStore.error('Failed to post comment', e instanceof Error ? e.message : undefined)
  }
}

async function handleLike(payload: { itemId: string; liked: boolean }): Promise<void> {
  try {
    const updated = await commentService.toggleLike(
      props.module,
      props.entityType,
      props.entityId,
      payload.itemId
    )
    const idx = comments.value.findIndex((c) => c.id === payload.itemId)
    if (idx !== -1) {
      comments.value[idx] = updated
    }
  } catch (e) {
    uiStore.error('Failed to update like', e instanceof Error ? e.message : undefined)
  }
}

async function handleDelete(payload: { itemId: string }): Promise<void> {
  try {
    await commentService.deleteComment(
      props.module,
      props.entityType,
      props.entityId,
      payload.itemId
    )
    comments.value = comments.value.filter((c) => c.id !== payload.itemId)
    uiStore.success('Comment deleted')
  } catch (e) {
    uiStore.error('Failed to delete comment', e instanceof Error ? e.message : undefined)
  }
}

// SignalR real-time updates
onNewComment((n) => {
  if (n.recordKey === recordKey.value && n.authorId !== props.currentUserId) {
    const alreadyExists = comments.value.some((c) => c.id === n.commentId)
    if (!alreadyExists) {
      comments.value.unshift({
        id: n.commentId,
        authorId: n.authorId,
        authorName: n.authorName,
        content: n.content,
        mentions: n.mentions,
        likedBy: [],
        createdAt: n.createdAt,
      })
    }
  }
})

onMounted(async () => {
  await joinRecordGroup(recordKey.value)
  await loadComments()
})
</script>

<template>
  <Card>
    <CardHeader class="pb-3">
      <CardTitle class="text-base">Comments</CardTitle>
    </CardHeader>
    <CardContent>
      <!-- Loading state -->
      <div v-if="loading" class="flex justify-center py-8">
        <Spinner size="md" aria-label="Loading comments" />
      </div>

      <!-- Feed -->
      <FeedList
        v-else
        :items="feedItems"
        :current-user="currentUserName"
        input-placeholder="Write a comment... Use @mention to notify someone"
        @post="handlePost"
        @like="handleLike"
        @delete="handleDelete"
      />
    </CardContent>
  </Card>
</template>
