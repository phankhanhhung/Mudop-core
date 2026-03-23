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
import { Textarea } from '@/components/ui/textarea'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Spinner } from '@/components/ui/spinner'
import { AlertCircle, Building2, X } from 'lucide-vue-next'
import { useTenantStore } from '@/stores/tenant'
import type { Tenant } from '@/types/tenant'

interface Props {
  open: boolean
  tenant?: Tenant
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  saved: [tenant: Tenant]
}>()

const { t } = useI18n()
const tenantStore = useTenantStore()

const isEditMode = computed(() => !!props.tenant)
const title = computed(() =>
  isEditMode.value ? t('tenant.dialog.editTitle') : t('tenant.dialog.createTitle')
)

const code = ref('')
const name = ref('')
const description = ref('')

const isSaving = ref(false)
const error = ref<string | null>(null)
const codeInput = ref<HTMLInputElement | null>(null)

watch(() => props.open, (open) => {
  if (open) {
    error.value = null
    if (props.tenant) {
      code.value = props.tenant.code
      name.value = props.tenant.name
      description.value = props.tenant.description || ''
    } else {
      code.value = ''
      name.value = ''
      description.value = ''
    }
    nextTick(() => codeInput.value?.focus())
  }
})

function getInitials(): string {
  if (name.value.trim()) {
    const words = name.value.trim().split(/\s+/)
    if (words.length >= 2) {
      return (words[0][0] + words[1][0]).toUpperCase()
    }
    return name.value.substring(0, 2).toUpperCase()
  }
  if (code.value.trim()) {
    return code.value.substring(0, 2).toUpperCase()
  }
  return '?'
}

async function save() {
  error.value = null

  if (!code.value.trim()) {
    error.value = t('tenant.dialog.codeRequired')
    return
  }
  if (!name.value.trim()) {
    error.value = t('tenant.dialog.nameRequired')
    return
  }

  isSaving.value = true
  try {
    let tenant: Tenant
    if (isEditMode.value && props.tenant) {
      tenant = await tenantStore.updateTenant(props.tenant.id, {
        name: name.value.trim(),
        description: description.value.trim() || undefined
      })
    } else {
      tenant = await tenantStore.createTenant({
        code: code.value.trim(),
        name: name.value.trim(),
        description: description.value.trim() || undefined
      })
    }
    emit('saved', tenant)
    emit('update:open', false)
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('tenant.dialog.failedToSave')
  } finally {
    isSaving.value = false
  }
}

function onOpenChange(value: boolean) {
  emit('update:open', value)
}
</script>

<template>
  <DialogRoot :open="open" @update:open="onOpenChange">
    <DialogPortal>
      <DialogOverlay
        class="fixed inset-0 z-50 bg-black/50 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0"
      />
      <DialogContent
        class="fixed left-1/2 top-1/2 z-50 w-full max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-lg border bg-background shadow-lg duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[state=closed]:slide-out-to-left-1/2 data-[state=closed]:slide-out-to-top-[48%] data-[state=open]:slide-in-from-left-1/2 data-[state=open]:slide-in-from-top-[48%]"
      >
        <!-- Header with icon preview -->
        <div class="flex items-start justify-between p-6 pb-0">
          <div class="flex items-center gap-3">
            <div class="h-11 w-11 rounded-lg bg-primary/10 flex items-center justify-center text-primary text-sm font-semibold">
              {{ getInitials() }}
            </div>
            <div>
              <DialogTitle class="text-lg font-semibold text-foreground">
                {{ title }}
              </DialogTitle>
              <DialogDescription class="text-sm text-muted-foreground">
                {{ isEditMode ? $t('tenant.dialog.editDescription') : $t('tenant.dialog.createDescription') }}
              </DialogDescription>
            </div>
          </div>
          <DialogClose
            class="rounded-sm opacity-70 ring-offset-background transition-opacity hover:opacity-100 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
          >
            <X class="h-4 w-4" />
          </DialogClose>
        </div>

        <Alert v-if="error" variant="destructive" class="mx-6 mt-4">
          <AlertCircle class="h-4 w-4" />
          <AlertDescription>{{ error }}</AlertDescription>
        </Alert>

        <form class="p-6 space-y-5" @submit.prevent="save">
          <!-- Tenant Code -->
          <div class="space-y-2">
            <Label for="dialog-code" class="text-sm font-medium">
              {{ $t('tenant.create.code') }}
              <span class="text-destructive">*</span>
            </Label>
            <div class="relative">
              <Building2 class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                id="dialog-code"
                ref="codeInput"
                v-model="code"
                :placeholder="$t('tenant.create.codePlaceholder')"
                class="pl-9"
                :disabled="isSaving || isEditMode"
                pattern="^[a-z0-9-]+$"
              />
            </div>
            <p class="text-xs text-muted-foreground">
              {{ $t('tenant.create.codeHint') }}
            </p>
          </div>

          <!-- Tenant Name -->
          <div class="space-y-2">
            <Label for="dialog-name" class="text-sm font-medium">
              {{ $t('tenant.create.name') }}
              <span class="text-destructive">*</span>
            </Label>
            <Input
              id="dialog-name"
              v-model="name"
              :placeholder="$t('tenant.create.namePlaceholder')"
              :disabled="isSaving"
            />
            <p class="text-xs text-muted-foreground">
              {{ $t('tenant.create.nameHint') }}
            </p>
          </div>

          <!-- Description -->
          <div class="space-y-2">
            <Label for="dialog-description" class="text-sm font-medium">{{ $t('tenant.create.description') }}</Label>
            <Textarea
              id="dialog-description"
              v-model="description"
              :placeholder="$t('tenant.create.descriptionPlaceholder')"
              :disabled="isSaving"
              :rows="3"
            />
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
