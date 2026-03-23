/**
 * MessageManager - Reactive message collection and lifecycle manager.
 *
 * Inspired by SAP UI5's MessageManager, this collects messages from various
 * sources (API errors, validation, user actions) and provides them to the UI
 * via Vue reactivity.
 *
 * Usage:
 *   import { messageManager } from '@/odata/MessageManager'
 *   messageManager.addError('Something went wrong', 'Details here')
 *   messageManager.addFromApiError(axiosError, 'Loading customers')
 */

import { ref, computed, type Ref, type ComputedRef } from 'vue'

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export type MessageType = 'error' | 'warning' | 'info' | 'success'

export interface Message {
  id: string
  type: MessageType
  title: string
  /** Optional longer description */
  description?: string
  /** Target field path (e.g., 'name', 'Customers/email') for field-level messages */
  target?: string
  /** Technical details (e.g., HTTP status, error code) */
  technical?: string
  /** Whether this message has been read/acknowledged */
  read: boolean
  /** Timestamp */
  timestamp: Date
  /** Source of the message */
  source: 'api' | 'validation' | 'system' | 'user'
  /** Whether this message is persistent (survives navigation) or transient */
  persistent: boolean
}

// ---------------------------------------------------------------------------
// Helper
// ---------------------------------------------------------------------------

function generateId(): string {
  return `msg-${Date.now()}-${Math.random().toString(36).slice(2)}`
}

// ---------------------------------------------------------------------------
// MessageManager
// ---------------------------------------------------------------------------

export class MessageManager {
  /** All messages (reactive) */
  readonly messages: Ref<Message[]>

  /** Messages grouped by type */
  readonly errorMessages: ComputedRef<Message[]>
  readonly warningMessages: ComputedRef<Message[]>
  readonly infoMessages: ComputedRef<Message[]>
  readonly successMessages: ComputedRef<Message[]>

  /** Counts */
  readonly errorCount: ComputedRef<number>
  readonly warningCount: ComputedRef<number>
  readonly totalCount: ComputedRef<number>
  readonly unreadCount: ComputedRef<number>

  /** Has any errors / warnings / messages */
  readonly hasErrors: ComputedRef<boolean>
  readonly hasWarnings: ComputedRef<boolean>
  readonly hasMessages: ComputedRef<boolean>

  constructor() {
    this.messages = ref<Message[]>([])

    // Grouped by type
    this.errorMessages = computed(() =>
      this.messages.value.filter((m) => m.type === 'error'),
    )
    this.warningMessages = computed(() =>
      this.messages.value.filter((m) => m.type === 'warning'),
    )
    this.infoMessages = computed(() =>
      this.messages.value.filter((m) => m.type === 'info'),
    )
    this.successMessages = computed(() =>
      this.messages.value.filter((m) => m.type === 'success'),
    )

    // Counts
    this.errorCount = computed(() => this.errorMessages.value.length)
    this.warningCount = computed(() => this.warningMessages.value.length)
    this.totalCount = computed(() => this.messages.value.length)
    this.unreadCount = computed(
      () => this.messages.value.filter((m) => !m.read).length,
    )

    // Booleans
    this.hasErrors = computed(() => this.errorCount.value > 0)
    this.hasWarnings = computed(() => this.warningCount.value > 0)
    this.hasMessages = computed(() => this.totalCount.value > 0)
  }

  // -----------------------------------------------------------------------
  // Add messages
  // -----------------------------------------------------------------------

  /** Add a message with full control over all fields. */
  addMessage(msg: Omit<Message, 'id' | 'read' | 'timestamp'>): Message {
    const message: Message = {
      ...msg,
      id: generateId(),
      read: false,
      timestamp: new Date(),
    }
    this.messages.value = [message, ...this.messages.value]
    return message
  }

  /** Shorthand: add an error message. */
  addError(title: string, description?: string, target?: string): Message {
    return this.addMessage({
      type: 'error',
      title,
      description,
      target,
      source: 'user',
      persistent: false,
    })
  }

  /** Shorthand: add a warning message. */
  addWarning(title: string, description?: string, target?: string): Message {
    return this.addMessage({
      type: 'warning',
      title,
      description,
      target,
      source: 'user',
      persistent: false,
    })
  }

  /** Shorthand: add an info message. */
  addInfo(title: string, description?: string): Message {
    return this.addMessage({
      type: 'info',
      title,
      description,
      source: 'user',
      persistent: false,
    })
  }

  /** Shorthand: add a success message. */
  addSuccess(title: string, description?: string): Message {
    return this.addMessage({
      type: 'success',
      title,
      description,
      source: 'user',
      persistent: false,
    })
  }

  // -----------------------------------------------------------------------
  // API / Validation error parsing
  // -----------------------------------------------------------------------

  /**
   * Parse an API error (typically an Axios error with response) and add
   * one or more messages. Returns the added messages.
   */
  addFromApiError(error: unknown, context?: string): Message[] {
    const msgs: Message[] = []

    const axiosErr = error as {
      response?: {
        status?: number
        data?: {
          error?: {
            message?: string
            details?: Array<{ message: string; target?: string }>
          }
        }
      }
      message?: string
    }

    if (axiosErr.response?.data?.error) {
      const oerr = axiosErr.response.data.error

      // Main error message
      msgs.push(
        this.addMessage({
          type: 'error',
          title: context
            ? `${context}: ${oerr.message}`
            : oerr.message || 'Request failed',
          technical: `HTTP ${axiosErr.response.status}`,
          source: 'api',
          persistent: false,
        }),
      )

      // Detail errors (field-level from OData validation)
      if (oerr.details) {
        for (const detail of oerr.details) {
          msgs.push(
            this.addMessage({
              type: 'error',
              title: detail.message,
              target: detail.target,
              source: 'api',
              persistent: false,
            }),
          )
        }
      }
    } else if (axiosErr.message) {
      msgs.push(
        this.addError(
          context
            ? `${context}: ${(error as Error).message}`
            : (error as Error).message,
        ),
      )
    } else {
      msgs.push(this.addError(context || 'An unexpected error occurred'))
    }

    return msgs
  }

  /**
   * Add messages from a server-side validation errors map.
   * The map keys are field names, values are arrays of error strings.
   */
  addValidationErrors(errors: Record<string, string[]>): Message[] {
    const msgs: Message[] = []
    for (const [field, fieldErrors] of Object.entries(errors)) {
      for (const errMsg of fieldErrors) {
        msgs.push(
          this.addMessage({
            type: 'error',
            title: errMsg,
            target: field,
            source: 'validation',
            persistent: false,
          }),
        )
      }
    }
    return msgs
  }

  // -----------------------------------------------------------------------
  // Read / acknowledge
  // -----------------------------------------------------------------------

  /** Mark a single message as read. */
  markAsRead(id: string): void {
    const msg = this.messages.value.find((m) => m.id === id)
    if (msg) {
      msg.read = true
      // Trigger reactivity by replacing the array
      this.messages.value = [...this.messages.value]
    }
  }

  /** Mark all messages as read. */
  markAllAsRead(): void {
    let changed = false
    for (const msg of this.messages.value) {
      if (!msg.read) {
        msg.read = true
        changed = true
      }
    }
    if (changed) {
      this.messages.value = [...this.messages.value]
    }
  }

  // -----------------------------------------------------------------------
  // Remove messages
  // -----------------------------------------------------------------------

  /** Remove a single message by ID. */
  removeMessage(id: string): void {
    this.messages.value = this.messages.value.filter((m) => m.id !== id)
  }

  /** Remove all messages for a specific target/field. */
  removeByTarget(target: string): void {
    this.messages.value = this.messages.value.filter(
      (m) => m.target !== target,
    )
  }

  /** Remove all messages from a specific source. */
  removeBySource(source: Message['source']): void {
    this.messages.value = this.messages.value.filter(
      (m) => m.source !== source,
    )
  }

  /** Remove non-persistent (transient) messages. */
  clearTransient(): void {
    this.messages.value = this.messages.value.filter((m) => m.persistent)
  }

  /** Remove all messages. */
  clearAll(): void {
    this.messages.value = []
  }

  // -----------------------------------------------------------------------
  // Query
  // -----------------------------------------------------------------------

  /** Get all messages for a specific target/field path. */
  getMessagesForTarget(target: string): Message[] {
    return this.messages.value.filter((m) => m.target === target)
  }

  /** Get the highest severity message type currently present, or null. */
  getHighestSeverity(): MessageType | null {
    if (this.errorCount.value > 0) return 'error'
    if (this.warningCount.value > 0) return 'warning'
    if (this.infoMessages.value.length > 0) return 'info'
    if (this.successMessages.value.length > 0) return 'success'
    return null
  }
}

// ---------------------------------------------------------------------------
// Singleton instance
// ---------------------------------------------------------------------------

export const messageManager = new MessageManager()
