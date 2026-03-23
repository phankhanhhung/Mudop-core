import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { VitePWA } from 'vite-plugin-pwa'
import monacoEditorPlugin from 'vite-plugin-monaco-editor'

export default defineConfig({
  plugins: [
    vue(),
    (monacoEditorPlugin as any).default({
      languageWorkers: ['editorWorkerService'],
    }),
    VitePWA({
      registerType: 'autoUpdate',
      manifest: false,
      devOptions: { enabled: false },
      workbox: {
        maximumFileSizeToCacheInBytes: 10 * 1024 * 1024,
        globPatterns: ['**/*.{js,css,html,ico,png,svg,woff,woff2}'],
        navigateFallback: '/index.html',
        navigateFallbackDenylist: [/^\/api\//, /^\/odata\//, /^\/hubs\//],
        runtimeCaching: [
          {
            urlPattern: /\.(?:js|css|woff2?)$/,
            handler: 'CacheFirst',
            options: {
              cacheName: 'static-assets',
              expiration: { maxEntries: 100, maxAgeSeconds: 30 * 24 * 60 * 60 }
            }
          },
          {
            urlPattern: /\.(?:png|jpg|jpeg|svg|gif|ico|webp)$/,
            handler: 'CacheFirst',
            options: {
              cacheName: 'images',
              expiration: { maxEntries: 60, maxAgeSeconds: 30 * 24 * 60 * 60 }
            }
          },
          {
            urlPattern: /^.*\/api\/.*/,
            handler: 'NetworkFirst',
            options: {
              cacheName: 'api-cache',
              networkTimeoutSeconds: 30,
              expiration: { maxEntries: 50, maxAgeSeconds: 5 * 60 }
            }
          }
        ]
      }
    })
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  },
  build: {
    // Target modern browsers for smaller output
    target: 'es2020',
    rollupOptions: {
      output: {
        manualChunks: {
          // Core framework — cached long-term, rarely changes
          'vue-core': ['vue', 'vue-router', 'pinia', 'vue-i18n'],
          // UI library — separate chunk for shadcn/radix primitives
          'ui-lib': ['radix-vue', 'class-variance-authority', 'clsx', 'tailwind-merge'],
          // Icons — large module, deduplicated into own chunk
          'icons': ['lucide-vue-next'],
          // Data utilities — CSV parsing, virtual scroll
          'data-utils': ['papaparse', '@tanstack/vue-virtual'],
          // Network — axios, signalr
          'network': ['axios', '@microsoft/signalr'],
          'monaco': ['monaco-editor']
        }
      }
    }
  },
  server: {
    port: 5173,
    proxy: {
      // SignalR hub (WebSocket + long polling)
      '/hubs': {
        target: 'http://localhost:5175',
        changeOrigin: true,
        ws: true
      },
      // Registry API (admin endpoints, compile, etc.)
      '/api/admin': {
        target: 'http://localhost:51742',
        changeOrigin: true
      },
      // Runtime API (auth, tenants, odata, etc.)
      '/api': {
        target: 'http://localhost:5175',
        changeOrigin: true
      }
    }
  }
})
