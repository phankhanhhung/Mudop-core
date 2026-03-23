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
import { AlertCircle, Shield, X, FileText } from 'lucide-vue-next'
import { roleService } from '@/services/roleService'
import type { Role } from '@/types/role'

interface Props {
  open: boolean
  role?: Role
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  saved: []
}>()

const { t } = useI18n()

const isEditMode = computed(() => !!props.role)
const title = computed(() => isEditMode.value ? t('admin.roles.dialog.editTitle') : t('admin.roles.dialog.createTitle'))

const name = ref('')
const description = ref('')

const isSaving = ref(false)
const error = ref<string | null>(null)
const nameInput = ref<HTMLInputElement | null>(null)

watch(() => props.open, (open) => {
  if (open) {
    error.value = null
    if (props.role) {
      name.value = props.role.Name
      description.value = props.role.Description || ''
    } else {
      name.value = ''
      description.value = ''
    }
    nextTick(() => nameInput.value?.focus())
  }
})

async function save() {
  error.value = null

  if (!name.value.trim()) {
    error.value = t('admin.roles.dialog.nameRequired')
    return
  }

  isSaving.value = true
  try {
    if (isEditMode.value && props.role) {
      await roleService.updateRole(props.role.Id, {
        Name: name.value.trim(),
        Description: description.value.trim() || undefined
      })
    } else {
      await roleService.createRole({
        Name: name.value.trim(),
        Description: description.value.trim() || undefined
      })
    }
    emit('saved')
    emit('update:open', false)
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('admin.roles.dialog.failedToSave')
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
        class="fixed left-1/2 top-1/2 z-50 w-full max-w-md -translate-x-1/2 -translate-y-1/2 rounded-lg border bg-background shadow-lg duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[state=closed]:slide-out-to-left-1/2 data-[state=closed]:slide-out-to-top-[48%] data-[state=open]:slide-in-from-left-1/2 data-[state=open]:slide-in-from-top-[48%]"
      >
        <!-- Header -->
        <div class="flex items-start justify-between p-6 pb-0">
          <div class="flex items-center gap-3">
            <div class="h-11 w-11 rounded-lg bg-primary/10 flex items-center justify-center">
              <Shield class="h-5 w-5 text-primary" />
            </div>
            <div>
              <DialogTitle class="text-lg font-semibold text-foreground">
                {{ title }}
              </DialogTitle>
              <DialogDescription class="text-sm text-muted-foreground">
                {{ isEditMode ? $t('admin.roles.dialog.editDescription') : $t('admin.roles.dialog.createDescription') }}
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
          <!-- Role name -->
          <div class="space-y-2">
            <Label for="role-name" class="text-sm font-medium">
              {{ $t('admin.roles.dialog.name') }}
              <span class="text-destructive">*</span>
            </Label>
            <div class="relative">
              <Shield class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                id="role-name"
                ref="nameInput"
                v-model="name"
                :placeholder="$t('admin.roles.dialog.namePlaceholder')"
                class="pl-9"
                :disabled="isSaving"
              />
            </div>
          </div>

          <!-- Description -->
          <div class="space-y-2">
            <Label for="role-description" class="text-sm font-medium">{{ $t('admin.roles.dialog.description') }}</Label>
            <div class="relative">
              <FileText class="absolute left-3 top-3 h-4 w-4 text-muted-foreground" />
              <textarea
                id="role-description"
                v-model="description"
                :placeholder="$t('admin.roles.dialog.descriptionPlaceholder')"
                :disabled="isSaving"
                rows="3"
                class="flex w-full rounded-md border border-input bg-background pl-9 pr-3 py-2 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50 resize-none"
              />
            </div>
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
