<script setup lang="ts">
import { watch } from 'vue'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { useWizard, type WizardStep } from '@/composables/useWizard'
import { Check, ChevronLeft, ChevronRight } from 'lucide-vue-next'

interface Props {
  steps: WizardStep[]
  linear?: boolean
  showProgressBar?: boolean
  variant?: 'horizontal' | 'vertical'
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  linear: true,
  showProgressBar: false,
  variant: 'horizontal',
})

const emit = defineEmits<{
  'complete': []
  'step-change': [index: number]
}>()

const {
  steps: wizardSteps,
  currentStepIndex,
  currentStep,
  isFirstStep,
  isLastStep,
  completedSteps,
  isStepAccessible,
  progress,
  goToStep,
  next,
  previous,
  complete: wizardComplete,
} = useWizard({
  steps: () => props.steps,
  linear: props.linear,
  onComplete: () => emit('complete'),
})

watch(currentStepIndex, (index) => {
  emit('step-change', index)
})

function isStepCompleted(key: string): boolean {
  return completedSteps.value.has(key)
}

function isStepActive(index: number): boolean {
  return currentStepIndex.value === index
}

async function handleStepClick(index: number) {
  if (isStepAccessible(index)) {
    await goToStep(index)
  }
}
</script>

<template>
  <div :class="cn('flex flex-col', props.class)">
    <!-- Step indicator -->
    <div
      :class="cn(
        variant === 'horizontal'
          ? 'flex items-start justify-between'
          : 'flex flex-col gap-2',
        'mb-6'
      )"
    >
      <template v-for="(step, index) in wizardSteps" :key="step.key">
        <div
          :class="cn(
            'flex items-center',
            variant === 'horizontal' ? 'flex-1' : '',
            variant === 'horizontal' && index === wizardSteps.length - 1 ? 'flex-none' : ''
          )"
        >
          <!-- Step circle + label group -->
          <button
            type="button"
            :class="cn(
              'flex items-center gap-3 group',
              isStepAccessible(index) ? 'cursor-pointer' : 'cursor-default',
            )"
            :disabled="!isStepAccessible(index)"
            @click="handleStepClick(index)"
          >
            <!-- Circle -->
            <div
              :class="cn(
                'flex h-8 w-8 shrink-0 items-center justify-center rounded-full border-2 text-sm font-medium transition-colors',
                isStepCompleted(step.key) && !isStepActive(index)
                  ? 'border-primary bg-primary text-primary-foreground'
                  : isStepActive(index)
                    ? 'border-primary bg-primary text-primary-foreground'
                    : 'border-muted-foreground/30 bg-background text-muted-foreground',
                isStepAccessible(index) && !isStepActive(index) && !isStepCompleted(step.key)
                  ? 'group-hover:border-primary/50'
                  : '',
              )"
            >
              <Check v-if="isStepCompleted(step.key) && !isStepActive(index)" class="h-4 w-4" />
              <component
                v-else-if="step.icon"
                :is="step.icon"
                class="h-4 w-4"
              />
              <span v-else>{{ index + 1 }}</span>
            </div>

            <!-- Label (hidden on mobile for horizontal variant) -->
            <div :class="cn(variant === 'horizontal' ? 'hidden md:block' : '')">
              <div
                :class="cn(
                  'text-sm font-medium leading-tight',
                  isStepActive(index) ? 'text-foreground' : 'text-muted-foreground',
                  isStepCompleted(step.key) && !isStepActive(index) ? 'text-foreground' : '',
                )"
              >
                {{ step.title }}
              </div>
              <div v-if="step.subtitle" class="text-xs text-muted-foreground mt-0.5">
                {{ step.subtitle }}
              </div>
            </div>
          </button>

          <!-- Connector line (not after last step) -->
          <div
            v-if="variant === 'horizontal' && index < wizardSteps.length - 1"
            :class="cn(
              'mx-3 h-0.5 flex-1 transition-colors',
              isStepCompleted(step.key)
                ? 'bg-primary'
                : 'bg-muted-foreground/20',
            )"
          />
        </div>

        <!-- Vertical connector -->
        <div
          v-if="variant === 'vertical' && index < wizardSteps.length - 1"
          :class="cn(
            'ml-[15px] h-6 w-0.5 transition-colors',
            isStepCompleted(step.key)
              ? 'bg-primary'
              : 'bg-muted-foreground/20',
          )"
        />
      </template>
    </div>

    <!-- Progress bar -->
    <div
      v-if="showProgressBar"
      class="mb-6"
    >
      <div class="flex items-center justify-between text-xs text-muted-foreground mb-1.5">
        <span>Step {{ currentStepIndex + 1 }} of {{ wizardSteps.length }}</span>
        <span>{{ progress }}% complete</span>
      </div>
      <div class="h-1.5 w-full rounded-full bg-muted overflow-hidden">
        <div
          class="h-full rounded-full bg-primary transition-all duration-300 ease-in-out"
          :style="{ width: progress + '%' }"
        />
      </div>
    </div>

    <!-- Step content -->
    <div class="min-h-[300px] mb-6">
      <slot
        :name="'step-' + currentStep.key"
        :step="currentStep"
        :index="currentStepIndex"
      />
      <slot
        v-if="!$slots['step-' + currentStep.key]"
        :step="currentStep"
        :index="currentStepIndex"
      />
    </div>

    <!-- Navigation footer -->
    <div class="flex items-center border-t pt-4">
      <Button
        variant="outline"
        :disabled="isFirstStep"
        @click="previous"
      >
        <ChevronLeft class="h-4 w-4 mr-1" />
        Previous
      </Button>
      <div class="flex-1" />
      <Button
        v-if="!isLastStep"
        @click="next"
      >
        Next
        <ChevronRight class="h-4 w-4 ml-1" />
      </Button>
      <Button
        v-else
        variant="default"
        @click="wizardComplete"
      >
        <Check class="h-4 w-4 mr-1" />
        Complete
      </Button>
    </div>
  </div>
</template>
