import type {
  PluginDefinition,
  PluginRegistrationOptions,
  RegisteredActionHook,
  RegisteredColumnRenderer,
  RegisteredDetailSection,
  RegisteredCustomView,
  ThemeExtension,
  CrudOperation,
  HookTiming,
  BeforeHookHandler,
  AfterHookHandler,
} from './types'

export class PluginRegistry {
  private readonly plugins = new Map<string, PluginDefinition>()
  private columnRenderers: RegisteredColumnRenderer[] = []
  private detailSections: RegisteredDetailSection[] = []
  private actionHooks: RegisteredActionHook[] = []
  private customViews: RegisteredCustomView[] = []
  private themeExtensions: ThemeExtension[] = []

  // ── Registration ────────────────────────────────────────────────────────────

  register(options: PluginRegistrationOptions): void {
    const { plugin } = options
    if (this.plugins.has(plugin.id)) {
      console.warn(`[PluginRegistry] Plugin "${plugin.id}" is already registered. Skipping.`)
      return
    }
    this.plugins.set(plugin.id, plugin)

    for (const r of options.columnRenderers ?? []) {
      this.columnRenderers.push({ ...r, pluginId: plugin.id })
    }
    for (const s of options.detailSections ?? []) {
      this.detailSections.push({ ...s, pluginId: plugin.id })
    }
    for (const h of options.actionHooks ?? []) {
      this.actionHooks.push({ ...h, pluginId: plugin.id })
    }
    for (const v of options.customViews ?? []) {
      this.customViews.push({ ...v, pluginId: plugin.id })
    }
    for (const t of options.themeExtensions ?? []) {
      this.themeExtensions.push({ ...t, pluginId: plugin.id })
    }
  }

  unregister(pluginId: string): void {
    this.plugins.delete(pluginId)
    this.columnRenderers  = this.columnRenderers.filter(r => r.pluginId !== pluginId)
    this.detailSections   = this.detailSections.filter(s => s.pluginId !== pluginId)
    this.actionHooks      = this.actionHooks.filter(h => h.pluginId !== pluginId)
    this.customViews      = this.customViews.filter(v => v.pluginId !== pluginId)
    this.themeExtensions  = this.themeExtensions.filter(t => t.pluginId !== pluginId)
  }

  // ── Lookup ──────────────────────────────────────────────────────────────────

  getPlugin(pluginId: string): PluginDefinition | undefined {
    return this.plugins.get(pluginId)
  }

  getPlugins(): PluginDefinition[] {
    return Array.from(this.plugins.values())
  }

  /**
   * Get the best-matching column renderer for a field.
   * Priority: fieldName match > entityType + fieldType > fieldType > '*'
   * Higher `priority` value wins among equal specificity.
   */
  getColumnRenderer(
    fieldType: string,
    entityType?: string,
    fieldName?: string,
  ): RegisteredColumnRenderer | null {
    const candidates = this.columnRenderers.filter(r => {
      if (r.entityType && r.entityType !== entityType) return false
      if (r.fieldName) return r.fieldName === fieldName
      return r.fieldType === fieldType || r.fieldType === '*'
    })
    if (candidates.length === 0) return null
    // Sort by specificity descending, then priority descending
    candidates.sort((a, b) => {
      const specA = (a.fieldName ? 2 : 0) + (a.entityType ? 1 : 0)
      const specB = (b.fieldName ? 2 : 0) + (b.entityType ? 1 : 0)
      if (specB !== specA) return specB - specA
      return (b.priority ?? 0) - (a.priority ?? 0)
    })
    return candidates[0]
  }

  /**
   * Get all detail sections applicable to the given entityType.
   * Returns sections for '*' and for the exact entityType, sorted by order.
   */
  getDetailSections(entityType: string): RegisteredDetailSection[] {
    return this.detailSections
      .filter(s => s.entityType === '*' || s.entityType === entityType)
      .sort((a, b) => a.order - b.order)
  }

  /**
   * Get action hooks matching entity, operation and timing, sorted by priority.
   */
  getActionHooks(
    entityType: string,
    operation: CrudOperation,
    timing: HookTiming,
  ): RegisteredActionHook[] {
    return this.actionHooks
      .filter(h =>
        (h.entityType === '*' || h.entityType === entityType) &&
        h.operation === operation &&
        h.timing === timing,
      )
      .sort((a, b) => (a.priority ?? 0) - (b.priority ?? 0))
  }

  /**
   * Run all before-hooks for the given context.
   * Returns false if any hook explicitly returns false (cancels the operation).
   */
  async runBeforeHooks(
    entityType: string,
    operation: CrudOperation,
    ctx: Parameters<BeforeHookHandler>[0],
  ): Promise<boolean> {
    const hooks = this.getActionHooks(entityType, operation, 'before')
    for (const hook of hooks) {
      const result = await (hook.handler as BeforeHookHandler)(ctx)
      if (result === false) return false
    }
    return true
  }

  /**
   * Run all after-hooks for the given context.
   */
  async runAfterHooks(
    entityType: string,
    operation: CrudOperation,
    ctx: Parameters<AfterHookHandler>[0],
  ): Promise<void> {
    const hooks = this.getActionHooks(entityType, operation, 'after')
    for (const hook of hooks) {
      await (hook.handler as AfterHookHandler)(ctx)
    }
  }

  getCustomViews(entityType: string): RegisteredCustomView[] {
    return this.customViews.filter(
      v => v.entityType === '*' || v.entityType === entityType,
    )
  }

  getThemeExtensions(): ThemeExtension[] {
    return [...this.themeExtensions]
  }

  /** Merge all registered CSS variables into a flat object */
  getMergedCssVariables(): Record<string, string> {
    const merged: Record<string, string> = {}
    for (const ext of this.themeExtensions) {
      Object.assign(merged, ext.cssVariables)
    }
    return merged
  }

  /** Remove all registrations (useful for testing) */
  clear(): void {
    this.plugins.clear()
    this.columnRenderers = []
    this.detailSections = []
    this.actionHooks = []
    this.customViews = []
    this.themeExtensions = []
  }
}

/** Singleton instance — import this throughout the app */
export const pluginRegistry = new PluginRegistry()
