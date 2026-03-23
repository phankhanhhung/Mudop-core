<script setup lang="ts">
import type { BatchQueueItem } from '@/types/batch'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { X, ArrowUp, ArrowDown, ListOrdered } from 'lucide-vue-next'

interface Props {
  items: BatchQueueItem[]
}

defineProps<Props>()

const emit = defineEmits<{
  remove: [id: string]
  reorder: [fromIndex: number, toIndex: number]
}>()

const methodColors: Record<string, string> = {
  GET: 'bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-200',
  POST: 'bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-200',
  PATCH: 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900 dark:text-yellow-200',
  DELETE: 'bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-200'
}

function moveUp(index: number) {
  if (index > 0) {
    emit('reorder', index, index - 1)
  }
}

function moveDown(index: number, total: number) {
  if (index < total - 1) {
    emit('reorder', index, index + 1)
  }
}
</script>

<template>
  <Card>
    <CardHeader>
      <div class="flex items-center gap-2">
        <ListOrdered class="h-5 w-5" />
        <CardTitle>Request Queue</CardTitle>
        <Badge variant="secondary" class="ml-auto">{{ items.length }}</Badge>
      </div>
    </CardHeader>
    <CardContent>
      <!-- Empty state -->
      <div
        v-if="items.length === 0"
        class="flex flex-col items-center justify-center py-12 text-muted-foreground"
      >
        <ListOrdered class="h-10 w-10 mb-3 opacity-40" />
        <p class="text-sm">Queue is empty</p>
        <p class="text-xs mt-1">Add operations using the form below</p>
      </div>

      <!-- Queue items -->
      <div v-else class="space-y-2">
        <div
          v-for="(item, index) in items"
          :key="item.id"
          class="flex items-center gap-3 rounded-lg border p-3 transition-colors"
          :class="{
            'border-green-300 bg-green-50 dark:bg-green-950/30': item.status === 'success',
            'border-red-300 bg-red-50 dark:bg-red-950/30': item.status === 'error',
            'border-border bg-background': item.status === 'pending'
          }"
        >
          <!-- Index -->
          <span class="text-xs font-mono text-muted-foreground w-6 text-right shrink-0">
            #{{ index + 1 }}
          </span>

          <!-- Method badge -->
          <span
            class="inline-flex items-center rounded-md px-2 py-0.5 text-xs font-bold shrink-0"
            :class="methodColors[item.method]"
          >
            {{ item.method }}
          </span>

          <!-- Entity set + ID -->
          <div class="flex-1 min-w-0">
            <div class="flex items-center gap-1.5">
              <span class="font-medium text-sm truncate">{{ item.entitySet }}</span>
              <span v-if="item.entityId" class="text-xs text-muted-foreground truncate">
                / {{ item.entityId }}
              </span>
            </div>

            <!-- Dependencies -->
            <div v-if="item.dependsOn && item.dependsOn.length > 0" class="flex flex-wrap gap-1 mt-1">
              <span class="text-xs text-muted-foreground">depends on:</span>
              <Badge
                v-for="dep in item.dependsOn"
                :key="dep"
                variant="outline"
                class="text-[10px] px-1.5 py-0"
              >
                {{ dep }}
              </Badge>
            </div>

            <!-- Response status -->
            <div v-if="item.response" class="mt-1">
              <Badge
                :variant="item.status === 'success' ? 'default' : 'destructive'"
                class="text-[10px]"
              >
                {{ item.response.status }}
              </Badge>
            </div>
          </div>

          <!-- Reorder buttons -->
          <div class="flex flex-col gap-0.5 shrink-0">
            <Button
              variant="ghost"
              size="icon"
              class="h-6 w-6"
              :disabled="index === 0"
              @click="moveUp(index)"
            >
              <ArrowUp class="h-3 w-3" />
            </Button>
            <Button
              variant="ghost"
              size="icon"
              class="h-6 w-6"
              :disabled="index === items.length - 1"
              @click="moveDown(index, items.length)"
            >
              <ArrowDown class="h-3 w-3" />
            </Button>
          </div>

          <!-- Remove button -->
          <Button
            variant="ghost"
            size="icon"
            class="h-7 w-7 text-muted-foreground hover:text-destructive shrink-0"
            @click="emit('remove', item.id)"
          >
            <X class="h-4 w-4" />
          </Button>
        </div>
      </div>
    </CardContent>
  </Card>
</template>
