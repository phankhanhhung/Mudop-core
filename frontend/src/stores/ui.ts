import { defineStore } from 'pinia'
import { ref, computed, watch } from 'vue'
import { usePreferredDark } from '@vueuse/core'

export type Theme = 'light' | 'dark' | 'system'

export interface Toast {
  id: string
  type: 'success' | 'error' | 'warning' | 'info'
  title: string
  message?: string
  duration?: number
}

export const useUiStore = defineStore('ui', () => {
  // State
  const theme = ref<Theme>('system')
  const sidebarCollapsed = ref(false)
  const sidebarMobileOpen = ref(false)
  const toasts = ref<Toast[]>([])
  const globalLoading = ref(false)
  const globalLoadingMessage = ref<string | null>(null)

  // System dark mode preference
  const prefersDark = usePreferredDark()

  // Getters
  const isDark = computed(() => {
    if (theme.value === 'system') {
      return prefersDark.value
    }
    return theme.value === 'dark'
  })

  // Watch and apply theme to document
  watch(
    isDark,
    (dark) => {
      if (dark) {
        document.documentElement.classList.add('dark')
      } else {
        document.documentElement.classList.remove('dark')
      }
    },
    { immediate: true }
  )

  // Load theme from localStorage on init
  function initTheme(): void {
    const saved = localStorage.getItem('bmmdl_theme') as Theme | null
    if (saved && ['light', 'dark', 'system'].includes(saved)) {
      theme.value = saved
    }
  }

  // Actions
  function setTheme(newTheme: Theme): void {
    theme.value = newTheme
    localStorage.setItem('bmmdl_theme', newTheme)
  }

  function toggleTheme(): void {
    const themes: Theme[] = ['light', 'dark', 'system']
    const currentIndex = themes.indexOf(theme.value)
    const nextIndex = (currentIndex + 1) % themes.length
    setTheme(themes[nextIndex])
  }

  function toggleSidebar(): void {
    sidebarCollapsed.value = !sidebarCollapsed.value
  }

  function setSidebarCollapsed(collapsed: boolean): void {
    sidebarCollapsed.value = collapsed
  }

  function toggleMobileSidebar(): void {
    sidebarMobileOpen.value = !sidebarMobileOpen.value
  }

  function closeMobileSidebar(): void {
    sidebarMobileOpen.value = false
  }

  function showToast(toast: Omit<Toast, 'id'>): string {
    const id = `toast-${Date.now()}-${Math.random().toString(36).substring(2, 9)}`
    const newToast: Toast = {
      id,
      duration: 5000,
      ...toast
    }
    toasts.value.push(newToast)

    // Auto remove after duration
    if (newToast.duration && newToast.duration > 0) {
      setTimeout(() => {
        removeToast(id)
      }, newToast.duration)
    }

    return id
  }

  function removeToast(id: string): void {
    const index = toasts.value.findIndex((t) => t.id === id)
    if (index >= 0) {
      toasts.value.splice(index, 1)
    }
  }

  function clearToasts(): void {
    toasts.value = []
  }

  // Convenience toast methods
  function success(title: string, message?: string): string {
    return showToast({ type: 'success', title, message })
  }

  function error(title: string, message?: string): string {
    return showToast({ type: 'error', title, message, duration: 10000 })
  }

  function warning(title: string, message?: string): string {
    return showToast({ type: 'warning', title, message })
  }

  function info(title: string, message?: string): string {
    return showToast({ type: 'info', title, message })
  }

  function setGlobalLoading(loading: boolean, message?: string): void {
    globalLoading.value = loading
    globalLoadingMessage.value = message ?? null
  }

  return {
    // State
    theme,
    sidebarCollapsed,
    sidebarMobileOpen,
    toasts,
    globalLoading,
    globalLoadingMessage,
    // Getters
    isDark,
    // Actions
    initTheme,
    setTheme,
    toggleTheme,
    toggleSidebar,
    setSidebarCollapsed,
    toggleMobileSidebar,
    closeMobileSidebar,
    showToast,
    removeToast,
    clearToasts,
    success,
    error,
    warning,
    info,
    setGlobalLoading
  }
})
