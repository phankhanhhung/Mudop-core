<template>
  <div :class="cn('relative', props.class)">
    <!-- Empty state -->
    <div
      v-if="entries.length === 0"
      class="flex flex-col items-center justify-center py-12 text-muted-foreground"
    >
      <Clock class="h-12 w-12 mb-3 opacity-20" />
      <p class="text-sm">No compilations yet</p>
    </div>

    <!-- Timeline entries -->
    <div
      v-else
      class="relative max-h-[600px] overflow-y-auto pr-2 space-y-6"
    >
      <!-- Timeline line -->
      <div class="absolute left-3 top-0 bottom-0 w-px bg-border" />

      <!-- Entry -->
      <div
        v-for="(entry, index) in sortedEntries"
        :key="entry.id"
        class="relative pl-10 animate-in fade-in slide-in-from-left-2 duration-300"
        :style="{ animationDelay: `${index * 50}ms` }"
      >
        <!-- Timeline node -->
        <div
          class="absolute left-0 top-1 flex h-6 w-6 items-center justify-center rounded-full border-2 bg-background"
          :class="entry.success ? 'border-green-500' : 'border-red-500'"
        >
          <CheckCircle
            v-if="entry.success"
            class="h-4 w-4 text-green-500"
          />
          <XCircle
            v-else
            class="h-4 w-4 text-red-500"
          />
        </div>

        <!-- Entry card -->
        <div class="rounded-lg border bg-card p-4 shadow-sm">
          <!-- Header -->
          <div class="flex items-start justify-between gap-2 mb-3">
            <div>
              <div class="flex items-center gap-2">
                <h3 class="font-semibold text-sm">{{ entry.moduleName }}</h3>
                <Badge :variant="entry.success ? 'default' : 'destructive'">
                  {{ entry.success ? 'Success' : 'Failed' }}
                </Badge>
                <Badge v-if="entry.warnings.length > 0" variant="outline" class="border-amber-500 text-amber-600">
                  <AlertTriangle class="h-3 w-3 mr-1" />
                  {{ entry.warnings.length }}
                </Badge>
              </div>
              <p class="text-xs text-muted-foreground mt-1">
                {{ formatRelativeTime(entry.timestamp) }}
              </p>
            </div>
          </div>

          <!-- Stats row -->
          <div class="flex items-center gap-4 text-xs text-muted-foreground mb-3">
            <span>{{ entry.entityCount }} entities</span>
            <span>{{ entry.serviceCount }} services</span>
            <span>{{ entry.enumCount }} enums</span>
            <span class="flex items-center gap-1">
              <Clock class="h-3 w-3" />
              {{ entry.compilationTime }}
            </span>
          </div>

          <!-- Version info -->
          <div v-if="entry.versionInfo" class="mb-3 space-y-2">
            <div class="flex items-center gap-2 flex-wrap">
              <Badge variant="secondary">v{{ entry.versionInfo.version }}</Badge>
              <Badge variant="outline">{{ entry.versionInfo.changeCategory }}</Badge>
              <span class="text-xs text-muted-foreground">
                {{ entry.versionInfo.totalChanges }} changes
              </span>
              <Badge
                v-if="entry.versionInfo.hasBreakingChanges"
                variant="destructive"
                class="text-xs"
              >
                Breaking Changes
              </Badge>
            </div>
            <div v-if="entry.versionInfo.migrationSql" class="text-xs text-muted-foreground">
              Migration script generated
            </div>
          </div>

          <!-- Schema result -->
          <div v-if="entry.schemaResult" class="mb-3">
            <p class="text-xs text-muted-foreground">
              Schema: {{ entry.schemaResult }}
            </p>
          </div>

          <!-- Errors (expandable) -->
          <div v-if="entry.errors.length > 0" class="mt-3">
            <Button
              variant="ghost"
              size="sm"
              class="h-auto p-0 text-xs text-red-600 hover:text-red-700 hover:bg-transparent"
              @click="toggleErrors(entry.id)"
            >
              <ChevronDown v-if="!expandedErrors.has(entry.id)" class="h-3 w-3 mr-1" />
              <ChevronUp v-else class="h-3 w-3 mr-1" />
              {{ entry.errors.length }} {{ entry.errors.length === 1 ? 'error' : 'errors' }}
            </Button>
            <div
              v-if="expandedErrors.has(entry.id)"
              class="mt-2 space-y-1 rounded-md bg-red-50 dark:bg-red-950/20 p-3 border border-red-200 dark:border-red-900"
            >
              <div
                v-for="(error, errorIndex) in entry.errors"
                :key="errorIndex"
                class="text-xs text-red-700 dark:text-red-400 font-mono"
              >
                {{ error }}
              </div>
            </div>
          </div>

          <!-- Warnings (always visible if present) -->
          <div v-if="entry.warnings.length > 0" class="mt-3">
            <div class="space-y-1 rounded-md bg-amber-50 dark:bg-amber-950/20 p-3 border border-amber-200 dark:border-amber-900">
              <div
                v-for="(warning, warningIndex) in entry.warnings"
                :key="warningIndex"
                class="text-xs text-amber-700 dark:text-amber-400 font-mono"
              >
                {{ warning }}
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { CheckCircle, XCircle, AlertTriangle, Clock, ChevronDown, ChevronUp } from 'lucide-vue-next'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

export interface CompilationEntry {
  id: string
  moduleName: string
  timestamp: Date
  success: boolean
  entityCount: number
  serviceCount: number
  enumCount: number
  compilationTime: string
  errors: string[]
  warnings: string[]
  schemaResult?: string
  versionInfo?: {
    version: string
    changeCategory: string
    totalChanges: number
    hasBreakingChanges: boolean
    migrationSql?: string
  }
}

interface Props {
  entries: CompilationEntry[]
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  entries: () => [],
  class: ''
})

// Sort entries by timestamp descending (most recent first)
const sortedEntries = computed(() => {
  return [...props.entries].sort((a, b) => b.timestamp.getTime() - a.timestamp.getTime())
})

// Track expanded error sections
const expandedErrors = ref(new Set<string>())

const toggleErrors = (entryId: string) => {
  if (expandedErrors.value.has(entryId)) {
    expandedErrors.value.delete(entryId)
  } else {
    expandedErrors.value.add(entryId)
  }
}

// Format relative time (simple implementation)
const formatRelativeTime = (date: Date): string => {
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffSec = Math.floor(diffMs / 1000)
  const diffMin = Math.floor(diffSec / 60)
  const diffHour = Math.floor(diffMin / 60)
  const diffDay = Math.floor(diffHour / 24)

  if (diffSec < 60) {
    return 'just now'
  } else if (diffMin < 60) {
    return `${diffMin} min ago`
  } else if (diffHour < 24) {
    return `${diffHour} hour${diffHour === 1 ? '' : 's'} ago`
  } else {
    return `${diffDay} day${diffDay === 1 ? '' : 's'} ago`
  }
}
</script>
