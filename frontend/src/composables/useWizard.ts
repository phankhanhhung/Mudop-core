import { ref, computed, toValue, type Ref, type ComputedRef, type MaybeRefOrGetter, type Component } from 'vue'

export interface WizardStep {
  key: string
  title: string
  subtitle?: string
  icon?: Component
  optional?: boolean
  validate?: () => boolean | Promise<boolean>
}

export interface UseWizardOptions {
  steps: MaybeRefOrGetter<WizardStep[]>
  linear?: boolean
  onComplete?: () => void | Promise<void>
}

export interface UseWizardReturn {
  steps: ComputedRef<WizardStep[]>
  currentStepIndex: Ref<number>
  currentStep: ComputedRef<WizardStep>
  isFirstStep: ComputedRef<boolean>
  isLastStep: ComputedRef<boolean>
  completedSteps: Ref<Set<string>>
  isStepAccessible: (index: number) => boolean
  progress: ComputedRef<number>
  goToStep: (index: number) => Promise<boolean>
  next: () => Promise<boolean>
  previous: () => void
  complete: () => Promise<void>
}

export function useWizard(options: UseWizardOptions): UseWizardReturn {
  const { linear = true, onComplete } = options

  const currentStepIndex = ref(0)
  const completedSteps = ref<Set<string>>(new Set())

  const steps = computed(() => toValue(options.steps))

  const currentStep = computed(() => steps.value[currentStepIndex.value])

  const isFirstStep = computed(() => currentStepIndex.value === 0)

  const isLastStep = computed(() => currentStepIndex.value === steps.value.length - 1)

  const progress = computed(() => {
    if (steps.value.length === 0) return 0
    return Math.round((completedSteps.value.size / steps.value.length) * 100)
  })

  function isStepAccessible(index: number): boolean {
    if (index < 0 || index >= steps.value.length) return false
    if (!linear) return true

    // In linear mode: can go back to completed steps or to the next uncompleted step
    if (index <= currentStepIndex.value) return true

    // Allow going forward only if all previous steps are completed
    for (let i = 0; i < index; i++) {
      const step = steps.value[i]
      if (!completedSteps.value.has(step.key) && !step.optional) {
        return false
      }
    }
    return true
  }

  async function validateCurrentStep(): Promise<boolean> {
    const step = currentStep.value
    if (!step.validate) return true
    try {
      return await step.validate()
    } catch {
      return false
    }
  }

  async function goToStep(index: number): Promise<boolean> {
    if (index < 0 || index >= steps.value.length) return false
    if (!isStepAccessible(index)) return false

    // When moving forward, validate the current step
    if (index > currentStepIndex.value) {
      const valid = await validateCurrentStep()
      if (!valid) return false
      completedSteps.value = new Set([...completedSteps.value, currentStep.value.key])
    }

    currentStepIndex.value = index
    return true
  }

  async function next(): Promise<boolean> {
    if (isLastStep.value) return false
    return goToStep(currentStepIndex.value + 1)
  }

  function previous(): void {
    if (isFirstStep.value) return
    currentStepIndex.value = currentStepIndex.value - 1
  }

  async function complete(): Promise<void> {
    const valid = await validateCurrentStep()
    if (!valid) return
    completedSteps.value = new Set([...completedSteps.value, currentStep.value.key])
    if (onComplete) {
      await onComplete()
    }
  }

  return {
    steps,
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
    complete,
  }
}
