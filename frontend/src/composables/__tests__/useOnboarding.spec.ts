import { describe, it, expect, beforeEach } from 'vitest'
import { useOnboarding, CURRENT_VERSION } from '../useOnboarding'

describe('useOnboarding', () => {
  beforeEach(() => {
    localStorage.clear()
    // Reset module-level singletons: completed.value = false, currentStep.value = 0
    const { resetOnboarding } = useOnboarding()
    resetOnboarding()
  })

  describe('isFirstRun', () => {
    it('isFirstRun is true when localStorage key is absent', () => {
      const { isFirstRun } = useOnboarding()
      expect(isFirstRun.value).toBe(true)
    })

    it('isFirstRun is false when localStorage key is "true"', () => {
      localStorage.setItem('bmmdl-onboarding-completed', 'true')
      const { completeOnboarding, isFirstRun } = useOnboarding()
      completeOnboarding()
      expect(isFirstRun.value).toBe(false)
    })
  })

  describe('completeOnboarding', () => {
    it('completeOnboarding sets isFirstRun to false', () => {
      const { completeOnboarding, isFirstRun } = useOnboarding()
      expect(isFirstRun.value).toBe(true)
      completeOnboarding()
      expect(isFirstRun.value).toBe(false)
    })

    it('completeOnboarding writes to localStorage', () => {
      const { completeOnboarding } = useOnboarding()
      completeOnboarding()
      expect(localStorage.getItem('bmmdl-onboarding-completed')).toBe('true')
    })
  })

  describe('resetOnboarding', () => {
    it('resetOnboarding sets isFirstRun to true', () => {
      const { completeOnboarding, resetOnboarding, isFirstRun } = useOnboarding()
      completeOnboarding()
      expect(isFirstRun.value).toBe(false)
      resetOnboarding()
      expect(isFirstRun.value).toBe(true)
    })

    it('resetOnboarding removes localStorage key', () => {
      const { completeOnboarding, resetOnboarding } = useOnboarding()
      completeOnboarding()
      expect(localStorage.getItem('bmmdl-onboarding-completed')).toBe('true')
      resetOnboarding()
      expect(localStorage.getItem('bmmdl-onboarding-completed')).toBeNull()
    })

    it('resetOnboarding resets currentStep to 0', () => {
      const { nextStep, resetOnboarding, currentStep } = useOnboarding()
      nextStep()
      nextStep()
      expect(currentStep.value).toBe(2)
      resetOnboarding()
      expect(currentStep.value).toBe(0)
    })
  })

  describe('nextStep', () => {
    it('nextStep increments currentStep', () => {
      const { nextStep, currentStep } = useOnboarding()
      expect(currentStep.value).toBe(0)
      nextStep()
      expect(currentStep.value).toBe(1)
    })

    it('nextStep does not exceed 3', () => {
      const { nextStep, currentStep } = useOnboarding()
      nextStep()
      nextStep()
      nextStep()
      nextStep()
      nextStep()
      expect(currentStep.value).toBe(3)
    })
  })

  describe('prevStep', () => {
    it('prevStep decrements currentStep', () => {
      const { nextStep, prevStep, currentStep } = useOnboarding()
      nextStep()
      nextStep()
      expect(currentStep.value).toBe(2)
      prevStep()
      expect(currentStep.value).toBe(1)
    })

    it('prevStep does not go below 0', () => {
      const { prevStep, currentStep } = useOnboarding()
      expect(currentStep.value).toBe(0)
      prevStep()
      prevStep()
      expect(currentStep.value).toBe(0)
    })
  })

  describe("What's New", () => {
    it('hasNewVersion is true when lastSeenVersion does not match CURRENT_VERSION', () => {
      // localStorage has no whats-new version key after clear+reset
      const { hasNewVersion } = useOnboarding()
      expect(hasNewVersion.value).toBe(true)
    })

    it('hasNewVersion is false when localStorage has current version', () => {
      localStorage.setItem('bmmdl-whats-new-version', CURRENT_VERSION)
      const { dismissWhatsNew, hasNewVersion } = useOnboarding()
      dismissWhatsNew()
      expect(hasNewVersion.value).toBe(false)
    })

    it('dismissWhatsNew sets hasNewVersion to false', () => {
      const { dismissWhatsNew, hasNewVersion } = useOnboarding()
      expect(hasNewVersion.value).toBe(true)
      dismissWhatsNew()
      expect(hasNewVersion.value).toBe(false)
    })
  })

  describe('CURRENT_VERSION', () => {
    it('CURRENT_VERSION is exported as "1.0.0"', () => {
      expect(CURRENT_VERSION).toBe('1.0.0')
    })
  })
})
