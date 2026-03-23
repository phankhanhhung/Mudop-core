import { ref, onMounted, onUnmounted } from 'vue'

interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>
}

const needsUpdate = ref(false)
const isInstallable = ref(false)

let deferredPrompt: BeforeInstallPromptEvent | null = null
let registration: ServiceWorkerRegistration | null = null

export function usePwa() {
  let updateFoundHandler: (() => void) | null = null
  let stateChangeHandler: (() => void) | null = null
  let trackedWorker: ServiceWorker | null = null

  function onBeforeInstallPrompt(e: Event) {
    e.preventDefault()
    deferredPrompt = e as BeforeInstallPromptEvent
    isInstallable.value = true
  }

  function onAppInstalled() {
    deferredPrompt = null
    isInstallable.value = false
  }

  onMounted(() => {
    window.addEventListener('beforeinstallprompt', onBeforeInstallPrompt)
    window.addEventListener('appinstalled', onAppInstalled)

    // Listen for service worker updates
    if ('serviceWorker' in navigator) {
      navigator.serviceWorker.ready.then((reg) => {
        registration = reg
        updateFoundHandler = () => {
          const newWorker = reg.installing
          if (newWorker) {
            // Clean up previous worker listener if any
            if (trackedWorker && stateChangeHandler) {
              trackedWorker.removeEventListener('statechange', stateChangeHandler)
            }
            trackedWorker = newWorker
            stateChangeHandler = () => {
              if (newWorker.state === 'installed' && navigator.serviceWorker.controller) {
                needsUpdate.value = true
              }
            }
            newWorker.addEventListener('statechange', stateChangeHandler)
          }
        }
        reg.addEventListener('updatefound', updateFoundHandler)
      })
    }
  })

  onUnmounted(() => {
    window.removeEventListener('beforeinstallprompt', onBeforeInstallPrompt)
    window.removeEventListener('appinstalled', onAppInstalled)
    if (registration && updateFoundHandler) {
      registration.removeEventListener('updatefound', updateFoundHandler)
    }
    if (trackedWorker && stateChangeHandler) {
      trackedWorker.removeEventListener('statechange', stateChangeHandler)
    }
  })

  async function updateApp() {
    if (registration?.waiting) {
      registration.waiting.postMessage({ type: 'SKIP_WAITING' })
    }
    window.location.reload()
  }

  async function installApp() {
    if (!deferredPrompt) return
    await deferredPrompt.prompt()
    const { outcome } = await deferredPrompt.userChoice
    if (outcome === 'accepted') {
      deferredPrompt = null
      isInstallable.value = false
    }
  }

  return {
    needsUpdate,
    isInstallable,
    updateApp,
    installApp
  }
}
