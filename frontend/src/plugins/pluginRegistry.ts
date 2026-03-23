import { ref, readonly } from 'vue'
import type { PluginManifest, PluginManifestEntry, PluginManifestSettings } from '@/services/pluginService'
import { pluginService } from '@/services/pluginService'

/**
 * PluginInfo is the view-model used by PluginManagementView.
 * Derived from PluginManifestEntry with computed dependents list.
 */
export interface PluginInfo {
  name: string
  status: 'available' | 'installed' | 'enabled' | 'disabled'
  version?: number
  description?: string
  dependsOn: string[]
  dependents: string[]
  capabilities: string[]
  settings: PluginManifestSettings | null
}

const manifest = ref<PluginManifest | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)

/**
 * Central plugin registry composable.
 * Fetches and caches the plugin manifest from the backend.
 * Uses module-level refs so state is shared across all consumers.
 */
export function usePluginRegistry() {
  /**
   * Load the plugin manifest from the backend.
   * Returns the cached manifest if already loaded.
   * On error, returns an empty manifest for graceful degradation.
   */
  async function loadManifest(): Promise<PluginManifest> {
    if (manifest.value) return manifest.value

    loading.value = true
    error.value = null

    try {
      manifest.value = await pluginService.getManifest()
      return manifest.value
    } catch (e: unknown) {
      error.value = e instanceof Error ? e.message : String(e)
      // Return empty manifest on error (graceful degradation)
      return { plugins: [] } as PluginManifest
    } finally {
      loading.value = false
    }
  }

  /**
   * Get all plugins with status 'enabled'.
   */
  function getEnabledPlugins(): PluginManifestEntry[] {
    return manifest.value?.plugins.filter(p => p.status === 'enabled') ?? []
  }

  /**
   * Check if a specific plugin is enabled by name.
   */
  function isPluginEnabled(name: string): boolean {
    return getEnabledPlugins().some(p => p.name === name)
  }

  /**
   * Invalidate the cached manifest, forcing a re-fetch on next loadManifest() call.
   */
  function invalidateCache(): void {
    manifest.value = null
  }

  /**
   * Load all plugins as PluginInfo view-models for the management view.
   * Fetches a fresh manifest and computes dependents from the dependency graph.
   */
  async function loadPlugins(): Promise<PluginInfo[]> {
    invalidateCache()
    const m = await loadManifest()
    return toPluginInfoList(m.plugins)
  }

  /**
   * Install a plugin by name.
   */
  async function installPlugin(name: string): Promise<void> {
    await pluginService.installPlugin(name)
    invalidateCache()
  }

  /**
   * Enable an installed/disabled plugin.
   */
  async function enablePlugin(name: string): Promise<void> {
    await pluginService.enablePlugin(name)
    invalidateCache()
  }

  /**
   * Disable an enabled plugin.
   */
  async function disablePlugin(name: string): Promise<void> {
    await pluginService.disablePlugin(name)
    invalidateCache()
  }

  /**
   * Uninstall a disabled plugin.
   */
  async function uninstallPlugin(name: string): Promise<void> {
    await pluginService.uninstallPlugin(name)
    invalidateCache()
  }

  /**
   * Update settings for a plugin.
   */
  async function updatePluginSettings(name: string, settings: Record<string, unknown>): Promise<void> {
    await pluginService.updateSettings(name, settings)
    invalidateCache()
  }

  return {
    manifest: readonly(manifest),
    loading: readonly(loading),
    error: readonly(error),
    loadManifest,
    getEnabledPlugins,
    isPluginEnabled,
    invalidateCache,
    loadPlugins,
    installPlugin,
    enablePlugin,
    disablePlugin,
    uninstallPlugin,
    updatePluginSettings,
  }
}

/**
 * Convert manifest entries to PluginInfo view-models,
 * computing the reverse dependency (dependents) list.
 */
function toPluginInfoList(entries: PluginManifestEntry[]): PluginInfo[] {
  // Build reverse dependency map
  const dependentsMap = new Map<string, string[]>()
  for (const entry of entries) {
    for (const dep of entry.dependsOn) {
      if (!dependentsMap.has(dep)) dependentsMap.set(dep, [])
      dependentsMap.get(dep)!.push(entry.name)
    }
  }

  return entries.map(entry => ({
    name: entry.name,
    status: entry.status,
    dependsOn: [...entry.dependsOn],
    dependents: dependentsMap.get(entry.name) ?? [],
    capabilities: [...entry.capabilities],
    settings: entry.settings,
  }))
}
