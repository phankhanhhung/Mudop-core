import type { AxiosError } from 'axios'

/**
 * Structured representation of a parsed OData error response.
 */
export interface ParsedODataError {
  /** The primary error message */
  message: string
  /** OData error code (e.g., "ValidationError") */
  code?: string
  /** Field-specific error messages, keyed by lowercase field name */
  fieldErrors: Record<string, string[]>
  /** Whether this error contains field-level validation failures */
  isValidationError: boolean
  /** Whether the request failed due to a network issue (no response received) */
  isNetworkError: boolean
  /** Whether the request timed out */
  isTimeout: boolean
  /** HTTP status code, if a response was received */
  status?: number
}

/**
 * OData v4 error response body structure.
 */
interface ODataErrorBody {
  error?: {
    code?: string
    message?: string
    details?: Array<{
      code?: string
      message?: string
      target?: string
    }>
    innererror?: unknown
  }
}

/**
 * Simple server error body structure (non-OData format).
 */
interface SimpleErrorBody {
  message?: string
  details?: Record<string, string[]>
}

/**
 * Normalizes a field name to lowercase for case-insensitive matching.
 */
function normalizeFieldName(name: string): string {
  return name.toLowerCase()
}

/**
 * Parses an OData v4 error body into field errors.
 */
function parseODataErrorBody(body: ODataErrorBody): {
  message: string
  code?: string
  fieldErrors: Record<string, string[]>
} {
  const error = body.error
  if (!error) {
    return { message: 'An unknown error occurred', fieldErrors: {} }
  }

  const fieldErrors: Record<string, string[]> = {}

  if (error.details && Array.isArray(error.details)) {
    for (const detail of error.details) {
      if (detail.target) {
        const key = normalizeFieldName(detail.target)
        if (!fieldErrors[key]) {
          fieldErrors[key] = []
        }
        if (detail.message) {
          fieldErrors[key].push(detail.message)
        }
      }
    }
  }

  return {
    message: error.message || 'An error occurred',
    code: error.code,
    fieldErrors
  }
}

/**
 * Parses a simple server error body into field errors.
 * Expected format: { message: "...", details: { FieldName: ["error1", "error2"] } }
 */
function parseSimpleErrorBody(body: SimpleErrorBody): {
  message: string
  fieldErrors: Record<string, string[]>
} {
  const fieldErrors: Record<string, string[]> = {}

  if (body.details && typeof body.details === 'object' && !Array.isArray(body.details)) {
    for (const [fieldName, errors] of Object.entries(body.details)) {
      if (Array.isArray(errors)) {
        const key = normalizeFieldName(fieldName)
        fieldErrors[key] = errors.filter((e): e is string => typeof e === 'string')
      }
    }
  }

  return {
    message: body.message || 'An error occurred',
    fieldErrors
  }
}

/**
 * Parses an axios error (or any unknown error) into a structured ParsedODataError.
 *
 * Handles three response body formats:
 * 1. OData v4: `{ error: { code, message, details: [{ target, message }] } }`
 * 2. Simple:   `{ message, details: { FieldName: ["error1"] } }`
 * 3. Fallback: extracts message from axios error or generic string
 *
 * Also detects network errors (no response) and timeouts.
 */
export function parseODataError(error: unknown): ParsedODataError {
  // Handle non-axios errors
  if (!error || typeof error !== 'object') {
    return {
      message: typeof error === 'string' ? error : 'An unknown error occurred',
      fieldErrors: {},
      isValidationError: false,
      isNetworkError: false,
      isTimeout: false
    }
  }

  const axiosError = error as AxiosError

  // Check for timeout
  if (axiosError.code === 'ECONNABORTED' || axiosError.message?.includes('timeout')) {
    return {
      message: 'The request timed out. Please try again.',
      fieldErrors: {},
      isValidationError: false,
      isNetworkError: false,
      isTimeout: true
    }
  }

  // Check for network error (no response received)
  if (!axiosError.response) {
    return {
      message: axiosError.message || 'Network error. Please check your connection.',
      fieldErrors: {},
      isValidationError: false,
      isNetworkError: true,
      isTimeout: false
    }
  }

  const status = axiosError.response.status
  const responseData = axiosError.response.data as Record<string, unknown> | undefined

  if (!responseData || typeof responseData !== 'object') {
    return {
      message: `Request failed with status ${status}`,
      fieldErrors: {},
      isValidationError: false,
      isNetworkError: false,
      isTimeout: false,
      status
    }
  }

  let message: string
  let code: string | undefined
  let fieldErrors: Record<string, string[]> = {}

  // Try OData v4 format first: { error: { code, message, details } }
  if ('error' in responseData && responseData.error && typeof responseData.error === 'object') {
    const parsed = parseODataErrorBody(responseData as ODataErrorBody)
    message = parsed.message
    code = parsed.code
    fieldErrors = parsed.fieldErrors
  }
  // Try simple format: { message, details: { Field: [...] } }
  else if ('message' in responseData || 'details' in responseData) {
    const parsed = parseSimpleErrorBody(responseData as SimpleErrorBody)
    message = parsed.message
    fieldErrors = parsed.fieldErrors
  }
  // Fallback
  else {
    message = `Request failed with status ${status}`
  }

  const hasFieldErrors = Object.keys(fieldErrors).length > 0
  const isValidationError =
    hasFieldErrors ||
    status === 400 ||
    status === 422 ||
    code === 'ValidationError'

  return {
    message,
    code,
    fieldErrors,
    isValidationError,
    isNetworkError: false,
    isTimeout: false,
    status
  }
}

/**
 * Gets the first error message for a given field name (case-insensitive).
 *
 * @param fieldErrors - The field errors record from ParsedODataError
 * @param fieldName - The field name to look up
 * @returns The first error message, or undefined if no errors for this field
 */
export function getFirstFieldError(
  fieldErrors: Record<string, string[]>,
  fieldName: string
): string | undefined {
  const key = normalizeFieldName(fieldName)
  const errors = fieldErrors[key]
  return errors && errors.length > 0 ? errors[0] : undefined
}
