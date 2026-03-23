import { onMounted, onUnmounted } from 'vue'
import { usePreferences } from '@/utils/preferences'

export interface ShortcutDefinition {
  key: string
  ctrl?: boolean
  shift?: boolean
  alt?: boolean
  meta?: boolean
  handler: () => void
  description?: string
}

function isEditableElement(el: EventTarget | null): boolean {
  if (!el || !(el instanceof HTMLElement)) return false
  const tag = el.tagName.toLowerCase()
  if (tag === 'input' || tag === 'textarea' || tag === 'select') return true
  if (el.isContentEditable) return true
  return false
}

function matchesShortcut(e: KeyboardEvent, shortcut: ShortcutDefinition): boolean {
  const key = shortcut.key.toLowerCase()
  if (e.key.toLowerCase() !== key) return false
  if (!!shortcut.ctrl !== (e.ctrlKey || e.metaKey)) return false
  if (!!shortcut.shift !== e.shiftKey) return false
  if (!!shortcut.alt !== e.altKey) return false
  return true
}

export function useKeyboardShortcuts(shortcuts: ShortcutDefinition[]) {
  const { preferences } = usePreferences()

  function handleKeydown(e: KeyboardEvent) {
    if (!preferences.value.keyboardShortcutsEnabled) return
    if (isEditableElement(e.target)) return

    for (const shortcut of shortcuts) {
      if (matchesShortcut(e, shortcut)) {
        e.preventDefault()
        e.stopPropagation()
        shortcut.handler()
        return
      }
    }
  }

  onMounted(() => {
    window.addEventListener('keydown', handleKeydown)
  })

  onUnmounted(() => {
    window.removeEventListener('keydown', handleKeydown)
  })
}

/**
 * Register a single global keydown listener that always fires,
 * even when focused in input fields (useful for Escape, Ctrl+K).
 */
export function useGlobalShortcut(
  key: string,
  handler: () => void,
  options?: { ctrl?: boolean; meta?: boolean }
) {
  const { preferences } = usePreferences()

  function handleKeydown(e: KeyboardEvent) {
    if (!preferences.value.keyboardShortcutsEnabled) return
    if (e.key.toLowerCase() !== key.toLowerCase()) return
    if (options?.ctrl && !(e.ctrlKey || e.metaKey)) return
    if (!options?.ctrl && (e.ctrlKey || e.metaKey)) return
    e.preventDefault()
    handler()
  }

  onMounted(() => {
    window.addEventListener('keydown', handleKeydown)
  })

  onUnmounted(() => {
    window.removeEventListener('keydown', handleKeydown)
  })
}
