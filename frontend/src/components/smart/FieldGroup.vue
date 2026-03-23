<script setup lang="ts">
import { ref, type Component } from 'vue'
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { ChevronDown } from 'lucide-vue-next'

interface Props {
  title: string
  description?: string
  collapsed?: boolean
  fieldCount?: number
  errorCount?: number
  icon?: Component
}

const props = withDefaults(defineProps<Props>(), {
  collapsed: false,
  fieldCount: 0,
  errorCount: 0,
})

const isCollapsed = ref(props.collapsed)

function toggleCollapsed() {
  isCollapsed.value = !isCollapsed.value
}
</script>

<template>
  <Card>
    <CardHeader class="cursor-pointer select-none py-4" @click="toggleCollapsed">
      <div class="flex items-center justify-between">
        <div class="flex items-center gap-2">
          <component v-if="props.icon" :is="props.icon" class="h-4 w-4 text-muted-foreground" />
          <CardTitle class="text-base">{{ title }}</CardTitle>
          <Badge v-if="fieldCount" variant="secondary" class="text-xs">
            {{ fieldCount }}
          </Badge>
          <Badge v-if="errorCount && errorCount > 0" variant="destructive" class="text-xs">
            {{ errorCount }} {{ errorCount === 1 ? 'error' : 'errors' }}
          </Badge>
        </div>
        <ChevronDown
          class="h-4 w-4 text-muted-foreground transition-transform duration-200"
          :class="{ '-rotate-180': !isCollapsed }"
        />
      </div>
      <CardDescription v-if="description">{{ description }}</CardDescription>
    </CardHeader>
    <CardContent v-show="!isCollapsed">
      <slot />
    </CardContent>
  </Card>
</template>
