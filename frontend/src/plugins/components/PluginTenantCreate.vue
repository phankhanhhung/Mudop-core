<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useUiStore } from '@/stores/ui'
import api from '@/services/api'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardContent } from '@/components/ui/card'
import DynamicPage from '@/components/layout/DynamicPage.vue'
import DynamicPageHeader from '@/components/layout/DynamicPageHeader.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Spinner } from '@/components/ui/spinner'
import { Textarea } from '@/components/ui/textarea'
import { Select } from '@/components/ui/select'
import {
  ChevronRight,
  Plus,
  AlertCircle,
  Hash,
  FileText,
} from 'lucide-vue-next'

// ── Types ────────────────────────────────────────────────────────────────────

interface CreateTenantPayload {
  code: string
  name: string
  description?: string
  subscriptionTier: string
  maxUsers: number
}

const router = useRouter()
const uiStore = useUiStore()

// Form state
const code = ref('')
const name = ref('')
const description = ref('')
const subscriptionTier = ref('free')
const maxUsers = ref(10)

const isSubmitting = ref(false)
const submitError = ref<string | null>(null)
const fieldErrors = ref<Record<string, string>>({})

const tierOptions = ['free', 'standard', 'premium']

// Validation
const isValid = computed(() => {
  return code.value.trim().length > 0 && name.value.trim().length > 0
})

function validate(): boolean {
  fieldErrors.value = {}
  let valid = true

  if (!code.value.trim()) {
    fieldErrors.value.code = 'Code is required.'
    valid = false
  } else if (!/^[a-zA-Z][a-zA-Z0-9_-]*$/.test(code.value.trim())) {
    fieldErrors.value.code = 'Code must start with a letter and contain only letters, digits, hyphens, and underscores.'
    valid = false
  }

  if (!name.value.trim()) {
    fieldErrors.value.name = 'Name is required.'
    valid = false
  }

  if (maxUsers.value < 1) {
    fieldErrors.value.maxUsers = 'Max users must be at least 1.'
    valid = false
  }

  return valid
}

async function handleSubmit() {
  if (!validate()) return

  isSubmitting.value = true
  submitError.value = null

  const payload: CreateTenantPayload = {
    code: code.value.trim(),
    name: name.value.trim(),
    description: description.value.trim() || undefined,
    subscriptionTier: subscriptionTier.value,
    maxUsers: maxUsers.value,
  }

  try {
    await api.post('/tenants', payload)
    uiStore.success('Tenant Created', `Tenant "${payload.name}" has been created successfully.`)
    router.push('/admin/tenants')
  } catch (e) {
    if (e && typeof e === 'object' && 'response' in e) {
      const axiosErr = e as { response?: { data?: { message?: string; errors?: Record<string, string[]> } } }
      if (axiosErr.response?.data?.errors) {
        for (const [field, msgs] of Object.entries(axiosErr.response.data.errors)) {
          fieldErrors.value[field.toLowerCase()] = msgs[0]
        }
      }
      submitError.value = axiosErr.response?.data?.message || 'Failed to create tenant.'
    } else {
      submitError.value = e instanceof Error ? e.message : 'Failed to create tenant.'
    }
  } finally {
    isSubmitting.value = false
  }
}

function handleCancel() {
  router.push('/admin/tenants')
}

// Stats for header
const stats = computed(() => ({
  fields: 5,
  required: 2,
}))
</script>

<template>
  <DefaultLayout>
    <DynamicPage
      showBackButton
      class="min-h-[calc(100vh-10rem)]"
      @back="handleCancel"
    >
      <template #breadcrumb>
        <div class="flex items-center gap-2 text-sm text-muted-foreground">
          <button
            class="hover:text-foreground transition-colors"
            @click="router.push('/admin/tenants')"
          >
            Tenants
          </button>
          <ChevronRight class="h-3.5 w-3.5" />
          <span class="text-foreground font-medium">Create Tenant</span>
        </div>
      </template>

      <template #title>
        <div class="flex items-center gap-3">
          <div class="h-8 w-8 rounded-lg bg-emerald-500/10 flex items-center justify-center shrink-0">
            <Plus class="h-4 w-4 text-emerald-600" />
          </div>
          <div class="min-w-0">
            <h1 class="text-xl font-semibold truncate">Create Tenant</h1>
            <p class="text-sm text-muted-foreground truncate">
              Create a new tenant for multi-tenancy isolation.
            </p>
          </div>
        </div>
      </template>

      <template #headerActions>
        <Button variant="ghost" size="sm" @click="handleCancel">
          Cancel
        </Button>
      </template>

      <template #header>
        <DynamicPageHeader>
          <div class="flex items-center gap-3 px-4 py-3 rounded-lg bg-muted/30 border border-border/50">
            <Hash class="h-5 w-5 text-muted-foreground" />
            <div>
              <p class="text-xs text-muted-foreground uppercase tracking-wider">Fields</p>
              <p class="text-xl font-bold">{{ stats.fields }}</p>
            </div>
          </div>
          <div class="flex items-center gap-3 px-4 py-3 rounded-lg bg-muted/30 border border-border/50">
            <FileText class="h-5 w-5 text-amber-500" />
            <div>
              <p class="text-xs text-muted-foreground uppercase tracking-wider">Required</p>
              <p class="text-xl font-bold text-amber-600">{{ stats.required }}</p>
            </div>
          </div>
        </DynamicPageHeader>
      </template>

      <!-- Form -->
      <Card class="max-w-2xl">
        <CardContent class="p-6 space-y-6">
          <!-- Error -->
          <Alert v-if="submitError" variant="destructive">
            <AlertCircle class="h-4 w-4" />
            <AlertDescription>{{ submitError }}</AlertDescription>
          </Alert>

          <!-- Code -->
          <div class="space-y-2">
            <Label for="tenant-code">
              Code <span class="text-red-500">*</span>
            </Label>
            <Input
              id="tenant-code"
              v-model="code"
              placeholder="my-tenant"
              :disabled="isSubmitting"
              :class="fieldErrors.code ? 'border-destructive' : ''"
            />
            <p v-if="fieldErrors.code" class="text-xs text-destructive">{{ fieldErrors.code }}</p>
            <p v-else class="text-xs text-muted-foreground">
              Unique identifier for the tenant. Must start with a letter.
            </p>
          </div>

          <!-- Name -->
          <div class="space-y-2">
            <Label for="tenant-name">
              Name <span class="text-red-500">*</span>
            </Label>
            <Input
              id="tenant-name"
              v-model="name"
              placeholder="My Organization"
              :disabled="isSubmitting"
              :class="fieldErrors.name ? 'border-destructive' : ''"
            />
            <p v-if="fieldErrors.name" class="text-xs text-destructive">{{ fieldErrors.name }}</p>
          </div>

          <!-- Description -->
          <div class="space-y-2">
            <Label for="tenant-description">Description</Label>
            <Textarea
              id="tenant-description"
              :model-value="description"
              :disabled="isSubmitting"
              placeholder="Optional description..."
              :rows="3"
              @update:model-value="description = $event"
            />
          </div>

          <!-- Subscription Tier -->
          <div class="space-y-2">
            <Label for="tenant-tier">Subscription Tier</Label>
            <Select
              id="tenant-tier"
              :model-value="subscriptionTier"
              :disabled="isSubmitting"
              @update:model-value="subscriptionTier = String($event)"
            >
              <option v-for="tier in tierOptions" :key="tier" :value="tier" class="capitalize">
                {{ tier }}
              </option>
            </Select>
          </div>

          <!-- Max Users -->
          <div class="space-y-2">
            <Label for="tenant-max-users">Max Users</Label>
            <Input
              id="tenant-max-users"
              v-model.number="maxUsers"
              type="number"
              min="1"
              :disabled="isSubmitting"
              :class="fieldErrors.maxUsers ? 'border-destructive' : ''"
            />
            <p v-if="fieldErrors.maxUsers" class="text-xs text-destructive">{{ fieldErrors.maxUsers }}</p>
            <p v-else class="text-xs text-muted-foreground">
              Maximum number of users allowed in this tenant.
            </p>
          </div>

          <!-- Actions -->
          <div class="flex items-center gap-3 pt-4 border-t">
            <Button @click="handleSubmit" :disabled="isSubmitting || !isValid">
              <Spinner v-if="isSubmitting" size="sm" class="mr-2" />
              <Plus v-else class="mr-2 h-4 w-4" />
              Create Tenant
            </Button>
            <Button variant="outline" @click="handleCancel" :disabled="isSubmitting">
              Cancel
            </Button>
          </div>
        </CardContent>
      </Card>
    </DynamicPage>
  </DefaultLayout>
</template>
