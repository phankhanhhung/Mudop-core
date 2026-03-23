import { ref, computed, onUnmounted } from 'vue'
import { adminService, type CompileResponse } from '@/services/adminService'
import type { EditorMarker } from '@/components/admin/BmmdlCodeEditor.vue'

export interface AutoValidationOptions {
  debounceMs?: number
  enabled?: boolean
}

export interface ValidationResult {
  success: boolean
  errors: string[]
  warnings: string[]
  timestamp: Date
}

export type ValidationStatus = 'idle' | 'validating' | 'valid' | 'invalid' | 'disabled'

/**
 * Composable for debounced auto-validation of BMMDL source code.
 *
 * Provides reactive state for validation status, errors/warnings, and editor markers.
 * Skips validation if source is empty, unchanged, or validation is disabled.
 *
 * @example
 * const { validate, markers, status, errorCount } = useAutoValidation({ debounceMs: 1000 })
 * watch(() => source.value, (newSource) => validate(newSource, moduleName.value))
 */
export function useAutoValidation(options: AutoValidationOptions = {}) {
  const debounceMs = options.debounceMs ?? 1500
  const enabled = ref(options.enabled ?? true)

  // ── State ──

  const isValidating = ref(false)
  const lastValidation = ref<ValidationResult | null>(null)

  // Cache to avoid redundant validation
  let lastValidatedSource = ''
  let lastValidatedModule = ''

  // Debounce timer and generation counter for cancellation
  let debounceTimer: ReturnType<typeof setTimeout> | null = null
  let validationGeneration = 0

  // ── Computed ──

  const status = computed<ValidationStatus>(() => {
    if (!enabled.value) return 'disabled'
    if (isValidating.value) return 'validating'
    if (!lastValidation.value) return 'idle'
    return lastValidation.value.success ? 'valid' : 'invalid'
  })

  const errorCount = computed(() => lastValidation.value?.errors.length ?? 0)
  const warningCount = computed(() => lastValidation.value?.warnings.length ?? 0)

  const markers = computed<EditorMarker[]>(() => {
    if (!lastValidation.value) return []

    const result: EditorMarker[] = []

    // Parse errors
    for (const error of lastValidation.value.errors) {
      const marker = parseMarker(error, 'error')
      if (marker) result.push(marker)
    }

    // Parse warnings
    for (const warning of lastValidation.value.warnings) {
      const marker = parseMarker(warning, 'warning')
      if (marker) result.push(marker)
    }

    return result
  })

  // ── Marker Parsing ──

  /**
   * Parse line/column from error/warning message.
   * Pattern: /[Ll]ine\s+(\d+)(?:,?\s*[Cc]ol(?:umn)?\s+(\d+))?/
   */
  function parseMarker(message: string, severity: 'error' | 'warning'): EditorMarker | null {
    const lineColRegex = /[Ll]ine\s+(\d+)(?:,?\s*[Cc]ol(?:umn)?\s+(\d+))?/
    const match = lineColRegex.exec(message)

    if (!match) {
      // No line/column info - create marker at line 1
      return {
        line: 1,
        column: 1,
        message,
        severity,
      }
    }

    const line = parseInt(match[1], 10)
    const column = match[2] ? parseInt(match[2], 10) : 1

    return {
      line,
      column,
      message,
      severity,
    }
  }

  // ── Validation Methods ──

  /**
   * Debounced validation trigger.
   * Skips if source is empty, unchanged, or validation is disabled.
   */
  function validate(source: string, moduleName: string): void {
    // Cancel pending validation
    if (debounceTimer) {
      clearTimeout(debounceTimer)
      debounceTimer = null
    }

    // Skip if disabled
    if (!enabled.value) return

    // Skip if empty
    if (!source.trim() || !moduleName.trim()) {
      reset()
      return
    }

    // Skip if unchanged (cache hit)
    if (source === lastValidatedSource && moduleName === lastValidatedModule) {
      return
    }

    // Schedule debounced validation
    debounceTimer = setTimeout(() => {
      validateNow(source, moduleName)
    }, debounceMs)
  }

  /**
   * Immediate validation (no debounce).
   * Returns a promise that resolves when validation completes.
   */
  async function validateNow(source: string, moduleName: string): Promise<void> {
    // Skip if disabled
    if (!enabled.value) return

    // Skip if empty
    if (!source.trim() || !moduleName.trim()) {
      reset()
      return
    }

    // Skip if unchanged (cache hit)
    if (source === lastValidatedSource && moduleName === lastValidatedModule) {
      return
    }

    // Increment generation to detect stale requests
    validationGeneration++
    const currentGeneration = validationGeneration

    isValidating.value = true

    try {
      const response: CompileResponse = await adminService.validate(source, moduleName)

      // Ignore stale results
      if (currentGeneration !== validationGeneration) {
        return
      }

      // Update cache
      lastValidatedSource = source
      lastValidatedModule = moduleName

      // Update validation result
      lastValidation.value = {
        success: response.success,
        errors: response.errors || [],
        warnings: response.warnings || [],
        timestamp: new Date(),
      }
    } catch (error) {
      // Ignore stale results
      if (currentGeneration !== validationGeneration) {
        return
      }

      // Network error or API failure - store as error
      const errorMessage = error instanceof Error ? error.message : 'Validation request failed'
      lastValidation.value = {
        success: false,
        errors: [errorMessage],
        warnings: [],
        timestamp: new Date(),
      }
    } finally {
      // Only clear isValidating if this is the latest request
      if (currentGeneration === validationGeneration) {
        isValidating.value = false
      }
    }
  }

  /**
   * Cancel any pending debounced validation.
   */
  function cancel(): void {
    if (debounceTimer) {
      clearTimeout(debounceTimer)
      debounceTimer = null
    }
    validationGeneration++
  }

  /**
   * Clear all validation state and cache.
   */
  function reset(): void {
    cancel()
    isValidating.value = false
    lastValidation.value = null
    lastValidatedSource = ''
    lastValidatedModule = ''
  }

  /**
   * Enable or disable validation.
   */
  function setEnabled(value: boolean): void {
    enabled.value = value
    if (!value) {
      reset()
    }
  }

  // ── Cleanup ──

  onUnmounted(() => {
    cancel()
  })

  // ── Return API ──

  return {
    // Reactive state
    isValidating,
    lastValidation,
    markers,

    // Status
    status,
    errorCount,
    warningCount,

    // Methods
    validate,
    validateNow,
    cancel,
    reset,
    setEnabled,
  }
}
