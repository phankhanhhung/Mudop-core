/**
 * useMessageManager - Vue composable for easy access to the MessageManager singleton.
 *
 * Provides all reactive state and bound action methods so components can
 * simply destructure what they need:
 *
 *   const { addError, hasErrors, messages } = useMessageManager()
 */

import { messageManager } from '@/odata/MessageManager'

export function useMessageManager() {
  return {
    // Direct access to reactive state
    messages: messageManager.messages,
    errorMessages: messageManager.errorMessages,
    warningMessages: messageManager.warningMessages,
    infoMessages: messageManager.infoMessages,
    successMessages: messageManager.successMessages,
    errorCount: messageManager.errorCount,
    warningCount: messageManager.warningCount,
    totalCount: messageManager.totalCount,
    unreadCount: messageManager.unreadCount,
    hasErrors: messageManager.hasErrors,
    hasWarnings: messageManager.hasWarnings,
    hasMessages: messageManager.hasMessages,

    // Bound action methods
    addMessage: messageManager.addMessage.bind(messageManager),
    addError: messageManager.addError.bind(messageManager),
    addWarning: messageManager.addWarning.bind(messageManager),
    addInfo: messageManager.addInfo.bind(messageManager),
    addSuccess: messageManager.addSuccess.bind(messageManager),
    addFromApiError: messageManager.addFromApiError.bind(messageManager),
    addValidationErrors: messageManager.addValidationErrors.bind(messageManager),

    markAsRead: messageManager.markAsRead.bind(messageManager),
    markAllAsRead: messageManager.markAllAsRead.bind(messageManager),

    removeMessage: messageManager.removeMessage.bind(messageManager),
    removeByTarget: messageManager.removeByTarget.bind(messageManager),
    removeBySource: messageManager.removeBySource.bind(messageManager),
    clearTransient: messageManager.clearTransient.bind(messageManager),
    clearAll: messageManager.clearAll.bind(messageManager),

    getMessagesForTarget: messageManager.getMessagesForTarget.bind(messageManager),
    getHighestSeverity: messageManager.getHighestSeverity.bind(messageManager),
  }
}
