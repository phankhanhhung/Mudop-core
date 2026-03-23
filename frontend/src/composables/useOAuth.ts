import { ref, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { authService } from '@/services/authService'

export type OAuthProvider = 'google' | 'microsoft' | 'apple'

// Module-level tracking for active popup polling so cleanup can reach it
let activePopupInterval: ReturnType<typeof setInterval> | null = null
let activePopupTimeout: ReturnType<typeof setTimeout> | null = null

export function useOAuth() {
  const router = useRouter()
  const authStore = useAuthStore()
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const activeProvider = ref<OAuthProvider | null>(null)

  const googleClientId = import.meta.env.VITE_GOOGLE_CLIENT_ID || ''
  const microsoftClientId = import.meta.env.VITE_MICROSOFT_CLIENT_ID || ''
  const appleClientId = import.meta.env.VITE_APPLE_CLIENT_ID || ''

  function getProviderConfig(provider: OAuthProvider) {
    switch (provider) {
      case 'google':
        return {
          clientId: googleClientId,
          authUrl: 'https://accounts.google.com/o/oauth2/v2/auth',
          scope: 'openid email profile',
          responseType: 'id_token',
        }
      case 'microsoft':
        return {
          clientId: microsoftClientId,
          authUrl: 'https://login.microsoftonline.com/common/oauth2/v2.0/authorize',
          scope: 'openid email profile',
          responseType: 'id_token',
        }
      case 'apple':
        return {
          clientId: appleClientId,
          authUrl: 'https://appleid.apple.com/auth/authorize',
          scope: 'name email',
          responseType: 'id_token',
        }
    }
  }

  function openOAuthPopup(provider: string, config: ReturnType<typeof getProviderConfig>): Promise<string | null> {
    return new Promise((resolve) => {
      const redirectUri = `${window.location.origin}/auth/oauth-callback`
      const state = crypto.randomUUID()
      const nonce = crypto.randomUUID()

      const params = new URLSearchParams({
        client_id: config.clientId,
        redirect_uri: redirectUri,
        response_type: config.responseType,
        scope: config.scope,
        state,
        nonce,
        response_mode: 'fragment',
      })

      const popup = window.open(
        `${config.authUrl}?${params.toString()}`,
        `oauth-${provider}`,
        'width=500,height=600,scrollbars=yes'
      )

      // If popup was blocked, resolve immediately
      if (!popup) {
        resolve(null)
        return
      }

      const interval = setInterval(() => {
        // Early exit if popup reference is gone or already closed
        if (!popup || popup.closed) {
          clearInterval(interval)
          activePopupInterval = null
          clearTimeout(timeout)
          activePopupTimeout = null
          resolve(null)
          return
        }

        try {
          const hash = popup.location.hash
          if (hash && hash.includes('id_token=')) {
            clearInterval(interval)
            activePopupInterval = null
            clearTimeout(timeout)
            activePopupTimeout = null
            const fragmentParams = new URLSearchParams(hash.substring(1))
            const idToken = fragmentParams.get('id_token')
            const returnedState = fragmentParams.get('state')
            popup.close()

            if (returnedState !== state) {
              resolve(null)
              return
            }

            // Validate nonce in id_token to prevent replay attacks
            if (idToken) {
              try {
                const parts = idToken.split('.')
                if (parts.length < 3) {
                  resolve(null)
                  return
                }
                let b64 = parts[1]
                b64 += '='.repeat((4 - (b64.length % 4)) % 4)
                let payload: any
                try {
                  payload = JSON.parse(atob(b64))
                } catch {
                  resolve(null)
                  return
                }
                if (typeof payload.nonce !== 'string' || payload.nonce !== nonce) {
                  resolve(null)
                  return
                }
              } catch {
                resolve(null)
                return
              }
            }

            resolve(idToken)
          }
        } catch {
          // Cross-origin error - popup hasn't redirected yet
        }
      }, 500)
      activePopupInterval = interval

      // Timeout after 5 minutes
      const timeout = setTimeout(() => {
        clearInterval(interval)
        activePopupInterval = null
        if (!popup.closed) popup.close()
        resolve(null)
      }, 300000)
      activePopupTimeout = timeout
    })
  }

  async function loginWithProvider(provider: OAuthProvider) {
    error.value = null
    activeProvider.value = provider
    isLoading.value = true

    try {
      const config = getProviderConfig(provider)
      if (!config.clientId) {
        throw new Error(`${provider} login is not configured`)
      }

      const idToken = await openOAuthPopup(provider, config)
      if (!idToken) {
        throw new Error('Authentication was cancelled')
      }

      const response = await authService.externalLogin({ provider, idToken })
      authStore.user = response.user
      router.push('/dashboard')
    } catch (e: any) {
      error.value = e?.message || `Failed to login with ${provider}`
    } finally {
      isLoading.value = false
      activeProvider.value = null
    }
  }

  async function linkProvider(provider: OAuthProvider) {
    error.value = null
    activeProvider.value = provider
    isLoading.value = true

    try {
      const config = getProviderConfig(provider)
      if (!config.clientId) {
        throw new Error(`${provider} is not configured`)
      }

      const idToken = await openOAuthPopup(provider, config)
      if (!idToken) {
        throw new Error('Linking was cancelled')
      }

      await authService.linkProvider({ provider, idToken })
    } catch (e: any) {
      error.value = e?.message || `Failed to link ${provider}`
      throw e
    } finally {
      isLoading.value = false
      activeProvider.value = null
    }
  }

  // Clean up popup polling on component unmount
  onUnmounted(() => {
    if (activePopupInterval) {
      clearInterval(activePopupInterval)
      activePopupInterval = null
    }
    if (activePopupTimeout) {
      clearTimeout(activePopupTimeout)
      activePopupTimeout = null
    }
  })

  const hasGoogle = !!googleClientId
  const hasMicrosoft = !!microsoftClientId
  const hasApple = !!appleClientId
  const hasAnyProvider = hasGoogle || hasMicrosoft || hasApple

  return {
    isLoading,
    error,
    activeProvider,
    loginWithProvider,
    linkProvider,
    hasGoogle,
    hasMicrosoft,
    hasApple,
    hasAnyProvider,
  }
}
