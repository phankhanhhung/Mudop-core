import type { RouteRecordRaw } from 'vue-router'
import type { PluginManifest } from '@/services/pluginService'
import { pluginComponents } from './components'

// Fallback component for unknown plugin pages
const PluginFallback = () => import('@/plugins/components/PluginFallback.vue')

/**
 * Generate Vue Router routes from the plugin manifest.
 * Only enabled plugins contribute routes.
 * Unknown component names fall back to PluginFallback.
 */
export function generatePluginRoutes(manifest: PluginManifest): RouteRecordRaw[] {
  return manifest.plugins
    .filter(p => p.status === 'enabled')
    .flatMap(plugin =>
      plugin.pages.map(page => ({
        path: page.route,
        name: `plugin-${plugin.name}-${page.route.replace(/\//g, '-')}`,
        component: pluginComponents[page.component] ?? PluginFallback,
        meta: {
          title: page.title,
          requiresAuth: true,
          plugin: plugin.name,
          icon: page.icon,
          ...page.meta,
        },
      }))
    )
}
