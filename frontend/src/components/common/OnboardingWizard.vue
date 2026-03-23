<script setup lang="ts">
import { ref, watch, nextTick } from 'vue'
import { useRouter } from 'vue-router'
import { useOnboarding } from '@/composables/useOnboarding'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import {
  Sparkles,
  Building2,
  Database,
  CheckCircle2,
  ArrowRight,
  ArrowLeft
} from 'lucide-vue-next'

const router = useRouter()
const {
  isFirstRun,
  currentStep,
  completeOnboarding,
  nextStep,
  prevStep
} = useOnboarding()

const stepIcons = [Sparkles, Building2, Database, CheckCircle2]
const dialogRef = ref<HTMLElement | null>(null)

// Focus the dialog when it opens so keyboard/screen-reader users land in it
watch(isFirstRun, (val) => {
  if (val) nextTick(() => dialogRef.value?.focus())
}, { immediate: true })

function handleGetStarted() {
  completeOnboarding()
}

function handleSkip() {
  completeOnboarding()
}

function goToTenants() {
  completeOnboarding()
  router.push('/tenants')
}
</script>

<template>
  <Teleport to="body">
    <Transition
      enter-active-class="transition ease-out duration-200"
      enter-from-class="opacity-0"
      enter-to-class="opacity-100"
      leave-active-class="transition ease-in duration-150"
      leave-from-class="opacity-100"
      leave-to-class="opacity-0"
    >
      <div
        v-if="isFirstRun"
        class="fixed inset-0 z-[100] flex items-center justify-center bg-background/80 backdrop-blur-sm"
        @keydown.escape="handleSkip"
      >
        <div
          ref="dialogRef"
          role="dialog"
          aria-modal="true"
          aria-labelledby="onboarding-title"
          tabindex="-1"
          class="w-full max-w-lg mx-4 outline-none"
          @keydown.escape.stop="handleSkip"
        >
        <Card class="shadow-xl">
          <CardContent class="p-8">
            <!-- Step content -->
            <div class="text-center">
              <div class="mx-auto mb-6 flex h-16 w-16 items-center justify-center rounded-full bg-primary/10">
                <component
                  :is="stepIcons[currentStep]"
                  class="h-8 w-8 text-primary"
                />
              </div>

              <!-- Step 0: Welcome -->
              <template v-if="currentStep === 0">
                <h2 id="onboarding-title" class="text-2xl font-bold tracking-tight">
                  {{ $t('onboarding.welcome.title') }}
                </h2>
                <p class="mt-2 text-muted-foreground">
                  {{ $t('onboarding.welcome.description') }}
                </p>
                <div class="mt-6 rounded-lg bg-muted/50 p-4 text-sm text-muted-foreground">
                  {{ $t('onboarding.welcome.hint') }}
                </div>
              </template>

              <!-- Step 1: Select Tenant -->
              <template v-if="currentStep === 1">
                <h2 id="onboarding-title" class="text-2xl font-bold tracking-tight">
                  {{ $t('onboarding.tenant.title') }}
                </h2>
                <p class="mt-2 text-muted-foreground">
                  {{ $t('onboarding.tenant.description') }}
                </p>
                <Button
                  variant="outline"
                  class="mt-6"
                  @click="goToTenants"
                >
                  <Building2 class="mr-2 h-4 w-4" />
                  {{ $t('onboarding.tenant.action') }}
                </Button>
              </template>

              <!-- Step 2: Browse Entities -->
              <template v-if="currentStep === 2">
                <h2 id="onboarding-title" class="text-2xl font-bold tracking-tight">
                  {{ $t('onboarding.entities.title') }}
                </h2>
                <p class="mt-2 text-muted-foreground">
                  {{ $t('onboarding.entities.description') }}
                </p>
                <div class="mt-6 rounded-lg bg-muted/50 p-4 text-sm text-muted-foreground">
                  {{ $t('onboarding.entities.hint') }}
                </div>
              </template>

              <!-- Step 3: Complete -->
              <template v-if="currentStep === 3">
                <h2 id="onboarding-title" class="text-2xl font-bold tracking-tight">
                  {{ $t('onboarding.complete.title') }}
                </h2>
                <p class="mt-2 text-muted-foreground">
                  {{ $t('onboarding.complete.description') }}
                </p>
              </template>
            </div>

            <!-- Progress dots -->
            <div class="mt-8 flex justify-center gap-2">
              <span
                v-for="step in 4"
                :key="step - 1"
                class="h-2 w-2 rounded-full transition-colors"
                :class="step - 1 === currentStep ? 'bg-primary' : 'bg-muted-foreground/30'"
              />
            </div>

            <!-- Navigation -->
            <div class="mt-6 flex items-center justify-between">
              <Button
                v-if="currentStep > 0"
                variant="ghost"
                @click="prevStep"
              >
                <ArrowLeft class="mr-2 h-4 w-4" />
                {{ $t('common.back') }}
              </Button>
              <Button
                v-else
                variant="ghost"
                class="text-muted-foreground text-sm"
                @click="handleSkip"
              >
                {{ $t('onboarding.skip') }}
              </Button>

              <Button
                v-if="currentStep < 3"
                @click="nextStep"
              >
                {{ $t('common.next') }}
                <ArrowRight class="ml-2 h-4 w-4" />
              </Button>
              <Button
                v-else
                @click="handleGetStarted"
              >
                {{ $t('onboarding.complete.action') }}
              </Button>
            </div>
          </CardContent>
        </Card>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>
