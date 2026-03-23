<script setup lang="ts">
import { ref } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import FeedList from '@/components/smart/FeedList.vue'
import type { FeedItem } from '@/components/smart/FeedList.vue'
import { ArrowLeft, MessageCircle } from 'lucide-vue-next'
import { useRouter } from 'vue-router'

const router = useRouter()

// ── Helper ───────────────────────────────────────────────────────────

let nextId = 100

function minutesAgo(n: number): string {
  return new Date(Date.now() - n * 60 * 1000).toISOString()
}

function hoursAgo(n: number): string {
  return new Date(Date.now() - n * 60 * 60 * 1000).toISOString()
}

function daysAgo(n: number): string {
  return new Date(Date.now() - n * 24 * 60 * 60 * 1000).toISOString()
}

// ── Demo 1: Interactive Feed ─────────────────────────────────────────

const interactiveItems = ref<FeedItem[]>([
  {
    id: '1',
    author: 'Alice Chen',
    avatar: 'AC',
    datetime: minutesAgo(3),
    content: 'Updated the API endpoint for the customer module. All tests are passing now.',
    liked: true,
    likeCount: 2,
    replyCount: 1,
  },
  {
    id: '2',
    author: 'Bob Martinez',
    avatar: 'BM',
    datetime: minutesAgo(18),
    content: 'Reviewed the PR, looks good overall. Left a minor comment about error handling in the validation layer.',
    liked: false,
    likeCount: 1,
    replyCount: 0,
  },
  {
    id: '3',
    author: 'You',
    datetime: hoursAgo(1),
    content: 'Deployed the latest build to staging. Please verify the order workflow before we push to production.',
    liked: false,
    likeCount: 3,
    replyCount: 2,
  },
  {
    id: '4',
    author: 'Diana Patel',
    avatar: 'DP',
    datetime: hoursAgo(4),
    content: 'The new dashboard charts are looking great. Performance is much better after switching to the aggregation pipeline.',
    liked: true,
    likeCount: 5,
    replyCount: 0,
  },
  {
    id: '5',
    author: 'Erik Johansson',
    avatar: 'EJ',
    datetime: daysAgo(1),
    content: 'Created the migration script for the new inventory schema. Ready for review.',
    liked: false,
    likeCount: 0,
    replyCount: 1,
  },
])

const lastAction = ref('')

function onInteractivePost(payload: { content: string }) {
  interactiveItems.value.unshift({
    id: String(nextId++),
    author: 'You',
    datetime: new Date().toISOString(),
    content: payload.content,
    liked: false,
    likeCount: 0,
    replyCount: 0,
  })
  lastAction.value = 'Posted a new comment'
}

function onInteractiveLike(payload: { itemId: string; liked: boolean }) {
  const item = interactiveItems.value.find(i => i.id === payload.itemId)
  if (item) {
    item.liked = payload.liked
    item.likeCount = (item.likeCount ?? 0) + (payload.liked ? 1 : -1)
  }
  lastAction.value = `${payload.liked ? 'Liked' : 'Unliked'} item ${payload.itemId}`
}

function onInteractiveReply(payload: { itemId: string }) {
  lastAction.value = `Reply clicked on item ${payload.itemId}`
}

function onInteractiveDelete(payload: { itemId: string }) {
  interactiveItems.value = interactiveItems.value.filter(i => i.id !== payload.itemId)
  lastAction.value = `Deleted item ${payload.itemId}`
}

// ── Demo 2: Character Limit ──────────────────────────────────────────

const limitItems = ref<FeedItem[]>([
  {
    id: '10',
    author: 'Grace Kim',
    avatar: 'GK',
    datetime: minutesAgo(45),
    content: 'Sprint retrospective notes: we should improve our code review turnaround time. Great job on delivering the reporting feature on schedule!',
    liked: true,
    likeCount: 4,
    replyCount: 0,
  },
])

function onLimitPost(payload: { content: string }) {
  limitItems.value.unshift({
    id: String(nextId++),
    author: 'You',
    datetime: new Date().toISOString(),
    content: payload.content,
    liked: false,
    likeCount: 0,
    replyCount: 0,
  })
}

// ── Demo 3: Readonly ─────────────────────────────────────────────────

const readonlyItems = ref<FeedItem[]>([
  {
    id: '20',
    author: 'System',
    avatar: 'SY',
    datetime: hoursAgo(2),
    content: 'Module "sales_order" compiled successfully (v2.1.0). 14 entities, 8 services registered.',
    likeCount: 0,
    replyCount: 0,
  },
  {
    id: '21',
    author: 'System',
    avatar: 'SY',
    datetime: hoursAgo(3),
    content: 'Schema migration applied: added "discount_percentage" column to order_item table.',
    likeCount: 0,
    replyCount: 0,
  },
  {
    id: '22',
    author: 'System',
    avatar: 'SY',
    datetime: daysAgo(1),
    content: 'Backup completed. Database snapshot stored: bmmdl_platform_20260209.sql.gz (124 MB).',
    likeCount: 0,
    replyCount: 0,
  },
])

// ── Demo 4: Empty State ──────────────────────────────────────────────

const emptyItems = ref<FeedItem[]>([])

// ── Demo 5: Single Author ────────────────────────────────────────────

const singleAuthorItems = ref<FeedItem[]>([
  {
    id: '30',
    author: 'You',
    datetime: minutesAgo(5),
    content: 'Fixed the timezone issue in the datetime picker component.',
    liked: false,
    likeCount: 0,
    replyCount: 0,
  },
  {
    id: '31',
    author: 'You',
    datetime: minutesAgo(25),
    content: 'Refactored the validation logic to use the new rule engine.',
    liked: true,
    likeCount: 1,
    replyCount: 0,
  },
  {
    id: '32',
    author: 'You',
    datetime: hoursAgo(2),
    content: 'Added unit tests for the deep insert handler. Coverage is now at 94%.',
    liked: false,
    likeCount: 2,
    replyCount: 0,
  },
])

function onSingleDelete(payload: { itemId: string }) {
  singleAuthorItems.value = singleAuthorItems.value.filter(i => i.id !== payload.itemId)
}
</script>

<template>
  <DefaultLayout>
    <div class="space-y-8">
      <!-- Header -->
      <div class="flex items-center gap-4">
        <Button variant="ghost" size="icon" @click="router.push('/showcase')">
          <ArrowLeft class="h-5 w-5" />
        </Button>
        <div>
          <h1 class="text-2xl font-bold tracking-tight flex items-center gap-2">
            <MessageCircle class="h-6 w-6" />
            Feed List
          </h1>
          <p class="text-muted-foreground mt-1">
            Social-style feed with input area, comments, likes, and actions.
          </p>
        </div>
      </div>

      <!-- Demo 1: Interactive Feed -->
      <Card>
        <CardHeader>
          <CardTitle>Interactive Feed</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Post new comments, like, reply, and delete your own items. All actions are fully functional.
          </p>
          <FeedList
            :items="interactiveItems"
            current-user="You"
            current-user-avatar="YO"
            @post="onInteractivePost"
            @like="onInteractiveLike"
            @reply="onInteractiveReply"
            @delete="onInteractiveDelete"
          />
          <div v-if="lastAction" class="mt-4 pt-3 border-t">
            <p class="text-xs text-muted-foreground">
              Last action: <Badge variant="secondary">{{ lastAction }}</Badge>
            </p>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 2: With Character Limit -->
      <Card>
        <CardHeader>
          <CardTitle>With Character Limit</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Input limited to 280 characters with a live counter. Post button disabled when over limit.
          </p>
          <FeedList
            :items="limitItems"
            :max-length="280"
            input-placeholder="What's on your mind? (280 chars max)"
            @post="onLimitPost"
            @like="onInteractiveLike"
          />
        </CardContent>
      </Card>

      <!-- Demo 3: Readonly Feed -->
      <Card>
        <CardHeader>
          <CardTitle>Readonly Feed</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Display-only audit trail. No input area and no action buttons.
          </p>
          <FeedList
            :items="readonlyItems"
            readonly
          />
        </CardContent>
      </Card>

      <!-- Demo 4: Empty State -->
      <Card>
        <CardHeader>
          <CardTitle>Empty State</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            When there are no items, a friendly empty state message is displayed.
          </p>
          <FeedList :items="emptyItems" />
        </CardContent>
      </Card>

      <!-- Demo 5: Single Author (Delete Buttons) -->
      <Card>
        <CardHeader>
          <CardTitle>Single Author</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            All items are from the current user, so delete buttons appear on hover for every item.
          </p>
          <FeedList
            :items="singleAuthorItems"
            current-user="You"
            @delete="onSingleDelete"
            @like="(p) => {
              const item = singleAuthorItems.find(i => i.id === p.itemId)
              if (item) { item.liked = p.liked; item.likeCount = (item.likeCount ?? 0) + (p.liked ? 1 : -1) }
            }"
          />
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
