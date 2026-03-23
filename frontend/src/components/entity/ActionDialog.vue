<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { Card, CardContent, CardHeader, CardTitle, CardFooter } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Spinner } from '@/components/ui/spinner'
import { X, Play } from 'lucide-vue-next'
import { odataService } from '@/services/odataService'
import type { ActionMetadata } from '@/types/metadata'

interface Props {
  open: boolean
  action: ActionMetadata | null
  module: string
  entitySet: string
  entityId?: string
  serviceName?: string
  mode: 'bound' | 'unbound'
  operationType?: 'action' | 'function'
}

const props = defineProps<Props>()

const emit = defineEmits<{
  close: []
  executed: [result: unknown]
}>()

const paramValues = ref<Record<string, string>>({})
const isExecuting = ref(false)
const result = ref<unknown>(null)
const error = ref<string | null>(null)

// Non-binding parameters (exclude the binding parameter for bound actions)
const inputParameters = computed(() => {
  if (!props.action) return []
  return props.action.parameters.filter(
    (p) => p.name !== props.action?.bindingParameter
  )
})

// Reset state when dialog opens or action changes
watch(
  () => [props.open, props.action],
  () => {
    if (props.open) {
      paramValues.value = {}
      result.value = null
      error.value = null
      isExecuting.value = false
    }
  }
)

function getInputType(type: string): string {
  switch (type) {
    case 'Integer':
    case 'Decimal':
      return 'number'
    case 'Boolean':
      return 'checkbox'
    case 'Date':
      return 'date'
    case 'DateTime':
    case 'Timestamp':
      return 'datetime-local'
    case 'Time':
      return 'time'
    default:
      return 'text'
  }
}

function buildParams(): Record<string, unknown> {
  const params: Record<string, unknown> = {}
  for (const param of inputParameters.value) {
    const raw = paramValues.value[param.name]
    if (raw === undefined || raw === '') continue
    switch (param.type) {
      case 'Integer':
        params[param.name] = parseInt(raw, 10)
        break
      case 'Decimal':
        params[param.name] = parseFloat(raw)
        break
      case 'Boolean':
        params[param.name] = raw === 'true' || raw === 'on'
        break
      default:
        params[param.name] = raw
    }
  }
  return params
}

async function handleExecute() {
  if (!props.action) return

  isExecuting.value = true
  error.value = null
  result.value = null

  try {
    const params = buildParams()
    let response: unknown

    if (props.mode === 'bound' && props.entityId) {
      response = await odataService.executeAction(
        props.module,
        props.entitySet,
        props.entityId,
        props.action.name,
        Object.keys(params).length > 0 ? params : undefined
      )
    } else if (props.operationType === 'function') {
      const svcName = props.serviceName || props.module
      response = await odataService.callFunction(
        svcName,
        props.action.name,
        Object.keys(params).length > 0 ? params : undefined
      )
    } else {
      const svcName = props.serviceName || props.module
      response = await odataService.executeUnboundAction(
        svcName,
        props.action.name,
        Object.keys(params).length > 0 ? params : undefined
      )
    }

    result.value = response
    emit('executed', response)
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Action execution failed'
  } finally {
    isExecuting.value = false
  }
}

function handleClose() {
  emit('close')
}

function handleOverlayClick(event: MouseEvent) {
  if (event.target === event.currentTarget) {
    handleClose()
  }
}
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open && action"
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
      @click="handleOverlayClick"
    >
      <Card
        class="w-full max-w-lg mx-4 max-h-[90vh] flex flex-col"
        role="dialog"
        aria-modal="true"
        :aria-label="action.name"
      >
        <CardHeader class="pb-3">
          <div class="flex items-center justify-between">
            <CardTitle class="text-lg">{{ action.name }}</CardTitle>
            <Button variant="ghost" size="icon" aria-label="Close" @click="handleClose">
              <X class="h-4 w-4" aria-hidden="true" />
            </Button>
          </div>
          <p class="text-sm text-muted-foreground">
            {{ mode === 'bound' ? 'Bound action' : operationType === 'function' ? 'Unbound function' : 'Unbound action' }}
            <span v-if="action.returnType"> &rarr; {{ action.returnType }}</span>
          </p>
        </CardHeader>

        <CardContent class="space-y-4 overflow-y-auto">
          <!-- Parameter form -->
          <div v-if="inputParameters.length > 0" class="space-y-3">
            <div v-for="param in inputParameters" :key="param.name" class="space-y-1.5">
              <Label :for="`param-${param.name}`">
                {{ param.name }}
                <span v-if="param.isRequired" class="text-destructive">*</span>
                <span class="text-xs text-muted-foreground ml-1">({{ param.type }})</span>
              </Label>
              <Input
                :id="`param-${param.name}`"
                :type="getInputType(param.type)"
                :placeholder="`Enter ${param.name}`"
                :required="param.isRequired"
                :modelValue="paramValues[param.name] ?? ''"
                @update:modelValue="paramValues[param.name] = String($event)"
              />
            </div>
          </div>

          <div v-else class="text-sm text-muted-foreground py-2">
            This {{ operationType === 'function' ? 'function' : 'action' }} has no parameters.
          </div>

          <!-- Error display -->
          <div v-if="error" class="rounded-md border border-destructive/50 bg-destructive/10 p-3">
            <p class="text-sm text-destructive">{{ error }}</p>
          </div>

          <!-- Result display -->
          <div v-if="result !== null" class="space-y-1.5">
            <Label>Result</Label>
            <pre class="bg-muted p-4 rounded text-xs overflow-auto max-h-60">{{ JSON.stringify(result, null, 2) }}</pre>
          </div>
        </CardContent>

        <CardFooter class="flex justify-end gap-2 pt-4">
          <Button variant="outline" @click="handleClose" :disabled="isExecuting">
            Cancel
          </Button>
          <Button @click="handleExecute" :disabled="isExecuting">
            <Spinner v-if="isExecuting" size="sm" class="mr-2" />
            <Play v-else class="mr-2 h-4 w-4" />
            Execute
          </Button>
        </CardFooter>
      </Card>
    </div>
  </Teleport>
</template>
