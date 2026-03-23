<script setup lang="ts">
import { ref, watch } from 'vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Badge } from '@/components/ui/badge'
import { Checkbox } from '@/components/ui/checkbox'
import { Clock, RotateCcw } from 'lucide-vue-next'
import type { TemporalState } from '@/composables/useTemporal'

interface Props {
  modelValue: TemporalState
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:modelValue': [value: TemporalState]
  apply: []
}>()

const localAsOf = ref(props.modelValue.asOf ?? '')
const localValidAt = ref(props.modelValue.validAt ?? '')
const localIncludeHistory = ref(props.modelValue.includeHistory)

watch(
  () => props.modelValue,
  (newVal) => {
    localAsOf.value = newVal.asOf ?? ''
    localValidAt.value = newVal.validAt ?? ''
    localIncludeHistory.value = newVal.includeHistory
  }
)

function emitUpdate() {
  emit('update:modelValue', {
    asOf: localAsOf.value || null,
    validAt: localValidAt.value || null,
    includeHistory: localIncludeHistory.value
  })
}

function handleApply() {
  emitUpdate()
  emit('apply')
}

function handleReset() {
  localAsOf.value = ''
  localValidAt.value = ''
  localIncludeHistory.value = false
  emit('update:modelValue', {
    asOf: null,
    validAt: null,
    includeHistory: false
  })
  emit('apply')
}

const isActive = (() => {
  return () => !!localAsOf.value || !!localValidAt.value || localIncludeHistory.value
})()
</script>

<template>
  <div
    class="rounded-lg border p-3 mb-3"
    :class="isActive() ? 'border-blue-400 bg-blue-50 dark:bg-blue-950/30 dark:border-blue-700' : 'border-border bg-muted/30'"
  >
    <div class="flex items-center gap-4 flex-wrap">
      <!-- Icon + label -->
      <div class="flex items-center gap-2 shrink-0">
        <Clock class="h-4 w-4 text-muted-foreground" />
        <span class="text-sm font-medium">Time Travel</span>
        <Badge v-if="isActive()" variant="default" class="text-xs">Active</Badge>
      </div>

      <!-- As Of -->
      <div class="flex items-center gap-1.5">
        <Label class="text-xs text-muted-foreground whitespace-nowrap">As Of</Label>
        <Input
          v-model="localAsOf"
          type="datetime-local"
          class="h-8 w-48 text-xs"
          placeholder="Point in time"
        />
      </div>

      <!-- Valid At -->
      <div class="flex items-center gap-1.5">
        <Label class="text-xs text-muted-foreground whitespace-nowrap">Valid At</Label>
        <Input
          v-model="localValidAt"
          type="date"
          class="h-8 w-36 text-xs"
        />
      </div>

      <!-- Include History -->
      <div class="flex items-center gap-1.5">
        <Checkbox
          :model-value="localIncludeHistory"
          @update:model-value="localIncludeHistory = $event"
        />
        <Label class="text-xs text-muted-foreground whitespace-nowrap cursor-pointer" @click="localIncludeHistory = !localIncludeHistory">
          Include History
        </Label>
      </div>

      <!-- Actions -->
      <div class="flex items-center gap-1.5 ml-auto">
        <Button variant="default" size="sm" @click="handleApply">
          Apply
        </Button>
        <Button variant="ghost" size="sm" @click="handleReset" :disabled="!isActive()">
          <RotateCcw class="mr-1 h-3.5 w-3.5" />
          Reset
        </Button>
      </div>
    </div>
  </div>
</template>
