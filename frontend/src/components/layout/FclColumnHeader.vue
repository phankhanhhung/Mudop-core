<script setup lang="ts">
import { ArrowLeft, Maximize2, X } from 'lucide-vue-next'
import Button from '@/components/ui/button/Button.vue'

interface Props {
  title: string
  subtitle?: string
  showBack?: boolean
  showClose?: boolean
  showFullscreen?: boolean
}

withDefaults(defineProps<Props>(), {
  subtitle: undefined,
  showBack: false,
  showClose: false,
  showFullscreen: false,
})

const emit = defineEmits<{
  back: []
  close: []
  fullscreen: []
}>()
</script>

<template>
  <div class="flex items-center justify-between px-4 py-3 border-b border-border bg-background sticky top-0 z-10">
    <div class="flex items-center gap-2 min-w-0">
      <Button
        v-if="showBack"
        variant="ghost"
        size="icon"
        class="h-8 w-8 flex-shrink-0"
        @click="emit('back')"
      >
        <ArrowLeft class="h-4 w-4" />
      </Button>
      <div class="min-w-0">
        <h2 class="text-lg font-semibold truncate">{{ title }}</h2>
        <p v-if="subtitle" class="text-sm text-muted-foreground truncate">{{ subtitle }}</p>
      </div>
    </div>
    <div class="flex items-center gap-1 flex-shrink-0">
      <Button
        v-if="showFullscreen"
        variant="ghost"
        size="icon"
        class="h-8 w-8"
        @click="emit('fullscreen')"
      >
        <Maximize2 class="h-4 w-4" />
      </Button>
      <Button
        v-if="showClose"
        variant="ghost"
        size="icon"
        class="h-8 w-8"
        @click="emit('close')"
      >
        <X class="h-4 w-4" />
      </Button>
    </div>
  </div>
</template>
