<script setup lang="ts">
import { ref, computed } from 'vue'
import type { BatchOperationType, BatchQueueItem } from '@/types/batch'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { Plus } from 'lucide-vue-next'

interface Props {
  entitySets: string[]
  existingIds?: string[]
}

const props = withDefaults(defineProps<Props>(), {
  existingIds: () => []
})

const emit = defineEmits<{
  add: [item: Omit<BatchQueueItem, 'status'>]
}>()

const method = ref<BatchOperationType>('GET')
const entitySet = ref('')
const entityId = ref('')
const bodyText = ref('')
const dependsOnText = ref('')
const jsonError = ref<string | null>(null)

const methods: BatchOperationType[] = ['GET', 'POST', 'PATCH', 'DELETE']

const needsEntityId = computed(() => {
  return method.value === 'GET' || method.value === 'PATCH' || method.value === 'DELETE'
})

const needsBody = computed(() => {
  return method.value === 'POST' || method.value === 'PATCH'
})

function validateJson(text: string): Record<string, unknown> | null {
  if (!text.trim()) return null
  try {
    const parsed = JSON.parse(text)
    jsonError.value = null
    return parsed as Record<string, unknown>
  } catch (e) {
    jsonError.value = e instanceof Error ? e.message : 'Invalid JSON'
    return null
  }
}

function resetForm() {
  method.value = 'GET'
  entitySet.value = ''
  entityId.value = ''
  bodyText.value = ''
  dependsOnText.value = ''
  jsonError.value = null
}

function handleAdd() {
  if (!entitySet.value) return

  // Validate body JSON if needed
  let body: Record<string, unknown> | undefined
  if (needsBody.value && bodyText.value.trim()) {
    const parsed = validateJson(bodyText.value)
    if (jsonError.value) return
    body = parsed ?? undefined
  }

  // Parse dependencies
  let dependsOn: string[] | undefined
  if (dependsOnText.value.trim()) {
    dependsOn = dependsOnText.value
      .split(',')
      .map((s) => s.trim())
      .filter((s) => s.length > 0)
    if (dependsOn.length === 0) dependsOn = undefined
  }

  const item: Omit<BatchQueueItem, 'status'> = {
    id: crypto.randomUUID().slice(0, 8),
    method: method.value,
    entitySet: entitySet.value,
    ...(needsEntityId.value && entityId.value ? { entityId: entityId.value } : {}),
    ...(body ? { body } : {}),
    ...(dependsOn ? { dependsOn } : {})
  }

  emit('add', item)
  resetForm()
}
</script>

<template>
  <Card>
    <CardHeader>
      <div class="flex items-center gap-2">
        <Plus class="h-5 w-5" />
        <CardTitle>Add Operation</CardTitle>
      </div>
    </CardHeader>
    <CardContent class="space-y-4">
      <!-- Method + Entity Set row -->
      <div class="grid gap-4 sm:grid-cols-2">
        <div class="space-y-2">
          <Label for="batch-method">Method</Label>
          <Select id="batch-method" v-model="method">
            <option v-for="m in methods" :key="m" :value="m">{{ m }}</option>
          </Select>
        </div>
        <div class="space-y-2">
          <Label for="batch-entity-set">Entity Set</Label>
          <Select
            id="batch-entity-set"
            v-model="entitySet"
            placeholder="Select entity set..."
          >
            <option v-for="es in entitySets" :key="es" :value="es">{{ es }}</option>
          </Select>
        </div>
      </div>

      <!-- Entity ID (conditional) -->
      <div v-if="needsEntityId" class="space-y-2">
        <Label for="batch-entity-id">Entity ID</Label>
        <Input
          id="batch-entity-id"
          v-model="entityId"
          placeholder="e.g. 550e8400-e29b-41d4-a716-446655440000"
        />
        <p class="text-xs text-muted-foreground">
          Required for {{ method }} on a specific entity
        </p>
      </div>

      <!-- Request Body (conditional) -->
      <div v-if="needsBody" class="space-y-2">
        <Label for="batch-body">Request Body (JSON)</Label>
        <Textarea
          id="batch-body"
          v-model="bodyText"
          placeholder='{ "Name": "Example", "Status": "Active" }'
          :rows="5"
          class="font-mono text-sm"
        />
        <p v-if="jsonError" class="text-xs text-destructive">{{ jsonError }}</p>
      </div>

      <!-- Dependencies -->
      <div class="space-y-2">
        <Label for="batch-depends">Dependencies (comma-separated IDs)</Label>
        <Input
          id="batch-depends"
          v-model="dependsOnText"
          placeholder="e.g. a1b2c3d4, e5f6g7h8"
        />
        <p v-if="props.existingIds.length > 0" class="text-xs text-muted-foreground">
          Available IDs: {{ props.existingIds.join(', ') }}
        </p>
      </div>

      <!-- Add button -->
      <Button
        class="w-full"
        :disabled="!entitySet"
        @click="handleAdd"
      >
        <Plus class="mr-2 h-4 w-4" />
        Add to Queue
      </Button>
    </CardContent>
  </Card>
</template>
