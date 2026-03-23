<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  DialogRoot,
  DialogPortal,
  DialogOverlay,
  DialogContent,
  DialogTitle,
  DialogDescription,
  DialogClose
} from 'radix-vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Spinner } from '@/components/ui/spinner'
import { AlertCircle, Eye, EyeOff, X, Webhook } from 'lucide-vue-next'
import { integrationService } from '@/services/integrationService'
import type { WebhookConfig, CreateWebhookRequest } from '@/services/integrationService'
import { useUiStore } from '@/stores/ui'

interface Props {
  open: boolean
  webhook?: WebhookConfig | null
}

const props = defineProps<Props>()

const emit = defineEmits<{
  close: []
  saved: [webhook: WebhookConfig]
}>()

const { t } = useI18n()
const uiStore = useUiStore()

const isEditMode = computed(() => !!props.webhook)
const title = computed(() =>
  isEditMode.value
    ? t('integration.webhook.editTitle')
    : t('integration.webhook.createTitle')
)

interface FormState {
  name: string
  targetUrl: string
  secret: string
  eventFilter: string[]
  isActive: boolean
}

const form = ref<FormState>({
  name: '',
  targetUrl: '',
  secret: '',
  eventFilter: [],
  isActive: true
})

const tagInput = ref('')
const showSecret = ref(false)
const isSaving = ref(false)
const validationError = ref<string | null>(null)
const nameInput = ref<HTMLInputElement | null>(null)

watch(
  () => props.open,
  (open) => {
    if (open) {
      validationError.value = null
      showSecret.value = false
      tagInput.value = ''
      if (props.webhook) {
        form.value = {
          name: props.webhook.name,
          targetUrl: props.webhook.targetUrl,
          secret: '',       // server never returns the secret
          eventFilter: [...props.webhook.eventFilter],
          isActive: props.webhook.isActive
        }
      } else {
        form.value = {
          name: '',
          targetUrl: '',
          secret: '',
          eventFilter: [],
          isActive: true
        }
      }
      nextTick(() => nameInput.value?.focus())
    }
  }
)

function addTag() {
  const raw = tagInput.value.trim().replace(/,$/, '').trim()
  if (raw && !form.value.eventFilter.includes(raw)) {
    form.value.eventFilter.push(raw)
  }
  tagInput.value = ''
}

function removeTag(index: number) {
  form.value.eventFilter.splice(index, 1)
}

function validate(): boolean {
  if (!form.value.name.trim()) {
    validationError.value = t('integration.webhook.nameRequired')
    return false
  }
  const url = form.value.targetUrl.trim()
  if (!url) {
    validationError.value = t('integration.webhook.urlRequired')
    return false
  }
  if (!url.startsWith('http://') && !url.startsWith('https://')) {
    validationError.value = t('integration.webhook.urlInvalid')
    return false
  }
  return true
}

async function save() {
  validationError.value = null
  if (!validate()) return

  isSaving.value = true
  try {
    const payload: CreateWebhookRequest = {
      name: form.value.name.trim(),
      targetUrl: form.value.targetUrl.trim(),
      eventFilter: form.value.eventFilter,
      isActive: form.value.isActive
    }
    if (form.value.secret) {
      payload.secret = form.value.secret
    }

    let result: WebhookConfig
    if (isEditMode.value && props.webhook) {
      result = await integrationService.updateWebhook(props.webhook.id, payload)
    } else {
      result = await integrationService.createWebhook(payload)
    }
    emit('saved', result)
    emit('close')
  } catch (e) {
    uiStore.error(
      t('integration.webhook.saveFailed'),
      e instanceof Error ? e.message : undefined
    )
  } finally {
    isSaving.value = false
  }
}

function onOpenChange(value: boolean) {
  if (!value) emit('close')
}
</script>

<template>
  <DialogRoot :open="open" @update:open="onOpenChange">
    <DialogPortal>
      <DialogOverlay
        class="fixed inset-0 z-50 bg-black/50 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0"
      />
      <DialogContent
        class="fixed left-1/2 top-1/2 z-50 w-full max-w-[520px] -translate-x-1/2 -translate-y-1/2 rounded-lg border bg-background shadow-lg duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[state=closed]:slide-out-to-left-1/2 data-[state=closed]:slide-out-to-top-[48%] data-[state=open]:slide-in-from-left-1/2 data-[state=open]:slide-in-from-top-[48%]"
      >
        <!-- Header -->
        <div class="flex items-start justify-between p-6 pb-0">
          <div class="flex items-center gap-3">
            <div class="h-11 w-11 rounded-full bg-primary/10 flex items-center justify-center text-primary">
              <Webhook class="h-5 w-5" />
            </div>
            <div>
              <DialogTitle class="text-lg font-semibold text-foreground">
                {{ title }}
              </DialogTitle>
              <DialogDescription class="text-sm text-muted-foreground">
                {{
                  isEditMode
                    ? $t('integration.webhook.editDescription')
                    : $t('integration.webhook.createDescription')
                }}
              </DialogDescription>
            </div>
          </div>
          <DialogClose
            class="rounded-sm opacity-70 ring-offset-background transition-opacity hover:opacity-100 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
          >
            <X class="h-4 w-4" />
          </DialogClose>
        </div>

        <Alert v-if="validationError" variant="destructive" class="mx-6 mt-4">
          <AlertCircle class="h-4 w-4" />
          <AlertDescription>{{ validationError }}</AlertDescription>
        </Alert>

        <form class="p-6 space-y-5" @submit.prevent="save">
          <!-- Name -->
          <div class="space-y-2">
            <Label for="webhook-name" class="text-sm font-medium">
              {{ $t('integration.webhook.name') }}
              <span class="text-destructive">*</span>
            </Label>
            <Input
              id="webhook-name"
              ref="nameInput"
              v-model="form.name"
              :placeholder="$t('integration.webhook.namePlaceholder')"
              :disabled="isSaving"
            />
          </div>

          <!-- Target URL -->
          <div class="space-y-2">
            <Label for="webhook-url" class="text-sm font-medium">
              {{ $t('integration.webhook.url') }}
              <span class="text-destructive">*</span>
            </Label>
            <Input
              id="webhook-url"
              v-model="form.targetUrl"
              type="url"
              placeholder="https://example.com/webhook"
              :disabled="isSaving"
            />
          </div>

          <!-- Secret -->
          <div class="space-y-2">
            <Label for="webhook-secret" class="text-sm font-medium">
              {{ $t('integration.webhook.secret') }}
            </Label>
            <div class="relative">
              <Input
                id="webhook-secret"
                v-model="form.secret"
                :type="showSecret ? 'text' : 'password'"
                :placeholder="
                  isEditMode
                    ? $t('integration.webhook.secretPlaceholderEdit')
                    : $t('integration.webhook.secretPlaceholderCreate')
                "
                :disabled="isSaving"
                class="pr-10"
              />
              <button
                type="button"
                tabindex="-1"
                class="absolute right-2 top-1/2 -translate-y-1/2 p-1 rounded hover:bg-muted focus:outline-none"
                @click="showSecret = !showSecret"
              >
                <Eye v-if="!showSecret" class="h-4 w-4 text-gray-400" />
                <EyeOff v-else class="h-4 w-4 text-gray-400" />
              </button>
            </div>
          </div>

          <!-- Event Filter -->
          <div class="space-y-2">
            <Label class="text-sm font-medium">
              {{ $t('integration.webhook.eventFilter') }}
            </Label>
            <div class="rounded-md border border-input bg-background p-2">
              <div v-if="form.eventFilter.length > 0" class="flex flex-wrap gap-1 mb-1">
                <span
                  v-for="(tag, i) in form.eventFilter"
                  :key="i"
                  class="inline-flex items-center gap-1 px-2 py-0.5 bg-blue-100 dark:bg-blue-900/40 text-blue-800 dark:text-blue-200 text-xs rounded-full"
                >
                  {{ tag }}
                  <button
                    type="button"
                    class="hover:text-red-500 focus:outline-none leading-none"
                    :aria-label="`Remove ${tag}`"
                    @click="removeTag(i)"
                  >
                    &times;
                  </button>
                </span>
              </div>
              <input
                v-model="tagInput"
                type="text"
                :placeholder="$t('integration.webhook.eventFilterPlaceholder')"
                :disabled="isSaving"
                class="w-full border-0 bg-transparent text-sm text-gray-900 dark:text-gray-100 placeholder:text-muted-foreground focus:outline-none focus:ring-0 p-0"
                @keydown.enter.prevent="addTag"
                @keydown.188.prevent="addTag"
              />
            </div>
            <p class="text-xs text-muted-foreground">
              {{ $t('integration.webhook.eventFilterHint') }}
            </p>
          </div>

          <!-- Active toggle -->
          <div class="flex items-center justify-between rounded-lg border p-3 bg-muted/30">
            <div>
              <p class="text-sm font-medium">{{ $t('integration.webhook.active') }}</p>
              <p class="text-xs text-muted-foreground">{{ $t('integration.webhook.activeDescription') }}</p>
            </div>
            <button
              type="button"
              role="switch"
              :aria-checked="form.isActive"
              :disabled="isSaving"
              class="relative inline-flex h-6 w-11 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
              :class="form.isActive ? 'bg-primary' : 'bg-input'"
              @click="form.isActive = !form.isActive"
            >
              <span
                class="pointer-events-none inline-block h-5 w-5 rounded-full bg-background shadow-lg ring-0 transition-transform duration-200"
                :class="form.isActive ? 'translate-x-5' : 'translate-x-0'"
              />
            </button>
          </div>

          <!-- Footer -->
          <div class="flex justify-end gap-3 pt-2 border-t">
            <DialogClose as-child>
              <Button variant="outline" type="button" :disabled="isSaving">
                {{ $t('common.cancel') }}
              </Button>
            </DialogClose>
            <Button type="submit" :disabled="isSaving">
              <Spinner v-if="isSaving" size="sm" class="mr-2" />
              {{ isEditMode ? $t('common.update') : $t('common.create') }}
            </Button>
          </div>
        </form>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
