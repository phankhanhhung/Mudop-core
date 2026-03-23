<script setup lang="ts">
import { ref, computed } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Checkbox } from '@/components/ui/checkbox'
import { Select } from '@/components/ui/select'
import Wizard from '@/components/smart/Wizard.vue'
import type { WizardStep } from '@/composables/useWizard'
import MessageStrip from '@/components/smart/MessageStrip.vue'
import { ArrowLeft, Wand2, User, MapPin, Settings, FileCheck } from 'lucide-vue-next'
import { useRouter } from 'vue-router'

const router = useRouter()

// ─── Form data ──────────────────────────────────────────────────────────

const formData = ref({
  name: '',
  email: '',
  street: '',
  city: '',
  country: '',
  theme: 'light',
  notifyEmail: true,
  notifySms: false,
  notifyPush: true,
})

const showSuccess = ref(false)

// ─── Validation helpers ─────────────────────────────────────────────────

const personalErrors = ref<string[]>([])
const addressErrors = ref<string[]>([])

function validatePersonal(): boolean {
  const errors: string[] = []
  if (!formData.value.name.trim()) errors.push('Name is required.')
  if (!formData.value.email.trim()) errors.push('Email is required.')
  else if (!formData.value.email.includes('@')) errors.push('Email must contain "@".')
  personalErrors.value = errors
  return errors.length === 0
}

function validateAddress(): boolean {
  const errors: string[] = []
  if (!formData.value.street.trim()) errors.push('Street is required.')
  if (!formData.value.city.trim()) errors.push('City is required.')
  if (!formData.value.country.trim()) errors.push('Country is required.')
  addressErrors.value = errors
  return errors.length === 0
}

// ─── Step definitions ───────────────────────────────────────────────────

const steps = computed<WizardStep[]>(() => [
  {
    key: 'personal',
    title: 'Personal Info',
    subtitle: 'Name and email',
    icon: User,
    validate: validatePersonal,
  },
  {
    key: 'address',
    title: 'Address',
    subtitle: 'Street, city, country',
    icon: MapPin,
    validate: validateAddress,
  },
  {
    key: 'preferences',
    title: 'Preferences',
    subtitle: 'Theme and notifications',
    icon: Settings,
    optional: true,
  },
  {
    key: 'review',
    title: 'Review',
    subtitle: 'Confirm your details',
    icon: FileCheck,
  },
])

// ─── Handlers ───────────────────────────────────────────────────────────

function onComplete() {
  showSuccess.value = true
}

const themeLabel = computed(() =>
  formData.value.theme === 'light' ? 'Light' : formData.value.theme === 'dark' ? 'Dark' : 'System'
)

const notificationSummary = computed(() => {
  const items: string[] = []
  if (formData.value.notifyEmail) items.push('Email')
  if (formData.value.notifySms) items.push('SMS')
  if (formData.value.notifyPush) items.push('Push')
  return items.length > 0 ? items.join(', ') : 'None'
})
</script>

<template>
  <DefaultLayout>
    <div class="space-y-8">
      <!-- Header -->
      <div class="flex items-center gap-4">
        <Button variant="ghost" size="icon" @click="router.push('/showcase')">
          <ArrowLeft class="h-5 w-5" />
        </Button>
        <div>
          <h1 class="text-2xl font-bold tracking-tight flex items-center gap-2">
            <Wand2 class="h-6 w-6" />
            Wizard / Step Form
          </h1>
          <p class="text-muted-foreground mt-1">
            Multi-step form with progress indicator, per-step validation, and linear navigation.
          </p>
        </div>
      </div>

      <!-- Success message -->
      <MessageStrip
        v-if="showSuccess"
        type="success"
        title="Registration Complete"
        description="All steps have been completed successfully. Your data has been submitted."
        :closable="true"
        @close="showSuccess = false"
      />

      <!-- Demo: Horizontal wizard with progress bar -->
      <Card>
        <CardHeader>
          <CardTitle>Registration Wizard</CardTitle>
        </CardHeader>
        <CardContent>
          <Wizard
            :steps="steps"
            :linear="true"
            show-progress-bar
            variant="horizontal"
            @complete="onComplete"
          >
            <!-- Step 1: Personal Info -->
            <template #step-personal="{ step }">
              <div class="space-y-4 max-w-lg">
                <h3 class="text-lg font-semibold">{{ step.title }}</h3>
                <p class="text-sm text-muted-foreground">
                  Please enter your name and email address to get started.
                </p>

                <MessageStrip
                  v-if="personalErrors.length > 0"
                  type="error"
                  title="Validation Error"
                  :description="personalErrors.join(' ')"
                  :closable="false"
                />

                <div class="space-y-3">
                  <div class="space-y-1.5">
                    <Label for="wizard-name">Full Name *</Label>
                    <Input
                      id="wizard-name"
                      v-model="formData.name"
                      placeholder="Enter your full name"
                    />
                  </div>
                  <div class="space-y-1.5">
                    <Label for="wizard-email">Email Address *</Label>
                    <Input
                      id="wizard-email"
                      v-model="formData.email"
                      type="email"
                      placeholder="you@example.com"
                    />
                  </div>
                </div>
              </div>
            </template>

            <!-- Step 2: Address -->
            <template #step-address="{ step }">
              <div class="space-y-4 max-w-lg">
                <h3 class="text-lg font-semibold">{{ step.title }}</h3>
                <p class="text-sm text-muted-foreground">
                  Enter your mailing address for correspondence.
                </p>

                <MessageStrip
                  v-if="addressErrors.length > 0"
                  type="error"
                  title="Validation Error"
                  :description="addressErrors.join(' ')"
                  :closable="false"
                />

                <div class="space-y-3">
                  <div class="space-y-1.5">
                    <Label for="wizard-street">Street Address *</Label>
                    <Input
                      id="wizard-street"
                      v-model="formData.street"
                      placeholder="123 Main Street"
                    />
                  </div>
                  <div class="grid grid-cols-2 gap-3">
                    <div class="space-y-1.5">
                      <Label for="wizard-city">City *</Label>
                      <Input
                        id="wizard-city"
                        v-model="formData.city"
                        placeholder="San Francisco"
                      />
                    </div>
                    <div class="space-y-1.5">
                      <Label for="wizard-country">Country *</Label>
                      <Input
                        id="wizard-country"
                        v-model="formData.country"
                        placeholder="United States"
                      />
                    </div>
                  </div>
                </div>
              </div>
            </template>

            <!-- Step 3: Preferences -->
            <template #step-preferences="{ step }">
              <div class="space-y-4 max-w-lg">
                <h3 class="text-lg font-semibold">{{ step.title }}</h3>
                <p class="text-sm text-muted-foreground">
                  Customize your experience. This step is optional.
                </p>

                <div class="space-y-5">
                  <div class="space-y-1.5">
                    <Label for="wizard-theme">Theme</Label>
                    <Select
                      id="wizard-theme"
                      :model-value="formData.theme"
                      @update:model-value="(v: string | number) => formData.theme = String(v)"
                    >
                      <option value="light">Light</option>
                      <option value="dark">Dark</option>
                      <option value="system">System</option>
                    </Select>
                  </div>

                  <div class="space-y-3">
                    <Label>Notifications</Label>
                    <div class="flex items-center gap-3">
                      <Checkbox v-model="formData.notifyEmail" />
                      <span class="text-sm">Email notifications</span>
                    </div>
                    <div class="flex items-center gap-3">
                      <Checkbox v-model="formData.notifySms" />
                      <span class="text-sm">SMS notifications</span>
                    </div>
                    <div class="flex items-center gap-3">
                      <Checkbox v-model="formData.notifyPush" />
                      <span class="text-sm">Push notifications</span>
                    </div>
                  </div>
                </div>
              </div>
            </template>

            <!-- Step 4: Review -->
            <template #step-review>
              <div class="space-y-4 max-w-lg">
                <h3 class="text-lg font-semibold">Review Your Information</h3>
                <p class="text-sm text-muted-foreground">
                  Please verify the details below before completing registration.
                </p>

                <div class="rounded-lg border divide-y">
                  <div class="p-4 space-y-2">
                    <h4 class="text-sm font-medium text-muted-foreground">Personal Info</h4>
                    <div class="grid grid-cols-[120px_1fr] gap-y-1 text-sm">
                      <span class="text-muted-foreground">Name</span>
                      <span class="font-medium">{{ formData.name || '-' }}</span>
                      <span class="text-muted-foreground">Email</span>
                      <span class="font-medium">{{ formData.email || '-' }}</span>
                    </div>
                  </div>

                  <div class="p-4 space-y-2">
                    <h4 class="text-sm font-medium text-muted-foreground">Address</h4>
                    <div class="grid grid-cols-[120px_1fr] gap-y-1 text-sm">
                      <span class="text-muted-foreground">Street</span>
                      <span class="font-medium">{{ formData.street || '-' }}</span>
                      <span class="text-muted-foreground">City</span>
                      <span class="font-medium">{{ formData.city || '-' }}</span>
                      <span class="text-muted-foreground">Country</span>
                      <span class="font-medium">{{ formData.country || '-' }}</span>
                    </div>
                  </div>

                  <div class="p-4 space-y-2">
                    <h4 class="text-sm font-medium text-muted-foreground">Preferences</h4>
                    <div class="grid grid-cols-[120px_1fr] gap-y-1 text-sm">
                      <span class="text-muted-foreground">Theme</span>
                      <span class="font-medium">{{ themeLabel }}</span>
                      <span class="text-muted-foreground">Notifications</span>
                      <span class="font-medium">{{ notificationSummary }}</span>
                    </div>
                  </div>
                </div>
              </div>
            </template>
          </Wizard>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
