<script setup lang="ts">
import { ref, computed, nextTick, watch } from 'vue'
import {
  DialogRoot,
  DialogPortal,
  DialogOverlay,
  DialogContent,
  DialogTitle,
  DialogDescription
} from 'radix-vue'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import {
  Info,
  AlertTriangle,
  XCircle,
  CheckCircle2,
  HelpCircle,
  ChevronDown,
  X
} from 'lucide-vue-next'
import {
  type MessageBoxType,
  type MessageBoxAction,
  getDefaultActions
} from '@/composables/useMessageBox'

interface Props {
  open: boolean
  type?: MessageBoxType
  title: string
  message: string
  details?: string
  actions?: MessageBoxAction[]
  showCloseButton?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  type: 'info',
  showCloseButton: true
})

const emit = defineEmits<{
  action: [key: string]
  'update:open': [value: boolean]
}>()

const showDetails = ref(false)
const autoFocusRef = ref<InstanceType<typeof Button> | null>(null)

const resolvedActions = computed<MessageBoxAction[]>(() => {
  if (props.actions && props.actions.length > 0) {
    return props.actions
  }
  return getDefaultActions(props.type ?? 'info')
})

const iconConfig = computed(() => {
  const configs: Record<MessageBoxType, {
    icon: typeof Info
    bgClass: string
  }> = {
    info: {
      icon: Info,
      bgClass: 'bg-blue-100 text-blue-600 dark:bg-blue-900/30 dark:text-blue-400'
    },
    warning: {
      icon: AlertTriangle,
      bgClass: 'bg-amber-100 text-amber-600 dark:bg-amber-900/30 dark:text-amber-400'
    },
    error: {
      icon: XCircle,
      bgClass: 'bg-red-100 text-red-600 dark:bg-red-900/30 dark:text-red-400'
    },
    success: {
      icon: CheckCircle2,
      bgClass: 'bg-emerald-100 text-emerald-600 dark:bg-emerald-900/30 dark:text-emerald-400'
    },
    confirm: {
      icon: HelpCircle,
      bgClass: 'bg-blue-100 text-blue-600 dark:bg-blue-900/30 dark:text-blue-400'
    }
  }
  return configs[props.type ?? 'info']
})

function onAction(key: string) {
  emit('action', key)
  emit('update:open', false)
}

function onOpenChange(value: boolean) {
  emit('update:open', value)
  if (!value) {
    emit('action', '')
  }
}

// Reset details expansion when dialog opens
watch(() => props.open, (newVal) => {
  if (newVal) {
    showDetails.value = false
    nextTick(() => {
      // Focus the auto-focus button if one exists
      const focusAction = resolvedActions.value.find(a => a.autoFocus)
      if (focusAction && autoFocusRef.value) {
        (autoFocusRef.value as unknown as { $el: HTMLElement }).$el?.focus()
      }
    })
  }
})
</script>

<template>
  <DialogRoot :open="open" @update:open="onOpenChange">
    <DialogPortal>
      <DialogOverlay
        class="fixed inset-0 z-50 bg-black/50 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0"
      />
      <DialogContent
        class="fixed left-1/2 top-1/2 z-50 w-full max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-lg border bg-background p-6 shadow-lg duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[state=closed]:slide-out-to-left-1/2 data-[state=closed]:slide-out-to-top-[48%] data-[state=open]:slide-in-from-left-1/2 data-[state=open]:slide-in-from-top-[48%]"
      >
        <!-- Close button (top right) -->
        <button
          v-if="showCloseButton"
          class="absolute right-4 top-4 rounded-sm opacity-70 ring-offset-background transition-opacity hover:opacity-100 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
          @click="onAction('')"
        >
          <X class="h-4 w-4" />
          <span class="sr-only">Close</span>
        </button>

        <!-- Main content row: icon + text -->
        <div class="flex gap-4">
          <!-- Type icon -->
          <div class="flex-shrink-0 mt-0.5">
            <div
              :class="cn(
                'flex h-10 w-10 items-center justify-center rounded-full',
                iconConfig.bgClass
              )"
            >
              <component :is="iconConfig.icon" class="h-5 w-5" />
            </div>
          </div>

          <!-- Content -->
          <div class="flex-1 min-w-0">
            <DialogTitle class="text-lg font-semibold text-foreground leading-tight pr-6">
              {{ title }}
            </DialogTitle>
            <DialogDescription class="mt-1.5 text-sm text-muted-foreground">
              {{ message }}
            </DialogDescription>

            <!-- Expandable details -->
            <div v-if="details" class="mt-3">
              <button
                class="inline-flex items-center gap-1 text-sm text-primary hover:text-primary/80 transition-colors"
                @click="showDetails = !showDetails"
              >
                <ChevronDown
                  class="h-4 w-4 transition-transform duration-200"
                  :class="{ 'rotate-180': showDetails }"
                />
                {{ showDetails ? 'Hide Details' : 'Show Details' }}
              </button>
              <pre
                v-if="showDetails"
                class="mt-2 text-xs bg-muted p-3 rounded-md overflow-auto max-h-40 whitespace-pre-wrap break-words font-mono"
              >{{ details }}</pre>
            </div>
          </div>
        </div>

        <!-- Actions footer -->
        <div class="mt-6 flex justify-end gap-2">
          <Button
            v-for="action in resolvedActions"
            :key="action.key"
            :variant="action.variant ?? 'default'"
            :ref="action.autoFocus ? (el: unknown) => { autoFocusRef = el as InstanceType<typeof Button> } : undefined"
            @click="onAction(action.key)"
          >
            {{ action.label }}
          </Button>
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
