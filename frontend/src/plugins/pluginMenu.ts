import type { PluginManifest } from '@/services/pluginService'

export interface MenuItem {
  label: string
  route: string
  icon: string
  section: string
  order: number
  badge?: string
  isPlugin?: boolean
  pluginName?: string
}

/**
 * Extract menu items from all enabled plugins in the manifest.
 * Returns items sorted by order, tagged with isPlugin=true and pluginName.
 */
export function getPluginMenuItems(manifest: Readonly<PluginManifest>): MenuItem[] {
  return manifest.plugins
    .filter(p => p.status === 'enabled')
    .flatMap(plugin =>
      plugin.menuItems.map(item => ({
        label: item.label,
        route: item.route,
        icon: item.icon,
        section: item.section,
        order: item.order,
        badge: item.badge,
        isPlugin: true,
        pluginName: plugin.name,
      }))
    )
    .sort((a, b) => a.order - b.order)
}

/**
 * Merge core menu items with plugin menu items.
 * Items are sorted by section (main < admin < tools) then by order within each section.
 */
export function mergeMenuItems(coreItems: MenuItem[], pluginItems: MenuItem[]): MenuItem[] {
  const sectionOrder: Record<string, number> = { main: 0, admin: 1, system: 2, tools: 3 }
  const allItems = [...coreItems, ...pluginItems]

  return allItems.sort((a, b) => {
    const sa = sectionOrder[a.section] ?? 99
    const sb = sectionOrder[b.section] ?? 99
    if (sa !== sb) return sa - sb
    return a.order - b.order
  })
}
