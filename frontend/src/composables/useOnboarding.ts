import { ref, computed } from 'vue'

const ONBOARDING_KEY = 'bmmdl-onboarding-completed'
const WHATS_NEW_VERSION_KEY = 'bmmdl-whats-new-version'

export const CURRENT_VERSION = '1.0.0'

const completed = ref(localStorage.getItem(ONBOARDING_KEY) === 'true')
const currentStep = ref(0)

export function useOnboarding() {
  const isFirstRun = computed(() => !completed.value)

  function completeOnboarding() {
    completed.value = true
    localStorage.setItem(ONBOARDING_KEY, 'true')
  }

  function resetOnboarding() {
    completed.value = false
    currentStep.value = 0
    localStorage.removeItem(ONBOARDING_KEY)
  }

  function nextStep() {
    if (currentStep.value < 3) {
      currentStep.value++
    }
  }

  function prevStep() {
    if (currentStep.value > 0) {
      currentStep.value--
    }
  }

  // What's New tracking
  const lastSeenVersion = ref(localStorage.getItem(WHATS_NEW_VERSION_KEY) || '')
  const hasNewVersion = computed(() => lastSeenVersion.value !== CURRENT_VERSION)

  function dismissWhatsNew() {
    lastSeenVersion.value = CURRENT_VERSION
    localStorage.setItem(WHATS_NEW_VERSION_KEY, CURRENT_VERSION)
  }

  return {
    isFirstRun,
    currentStep,
    completeOnboarding,
    resetOnboarding,
    nextStep,
    prevStep,
    hasNewVersion,
    dismissWhatsNew,
    CURRENT_VERSION
  }
}
