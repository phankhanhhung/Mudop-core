import type { Component } from 'vue'

// ── Operation types ──────────────────────────────────────────────────────────

export type CrudOperation = 'create' | 'read' | 'update' | 'delete'
export type HookTiming = 'before' | 'after'

// ── Action hook context ───────────────────────────────────────────────────────

export interface ActionHookContext {
  entityType: string
  module: string
  operation: CrudOperation
  data?: Record<string, unknown>       // Input data (create/update)
  entityId?: string                    // Target id (read/update/delete)
  result?: Record<string, unknown>     // Result data (after hooks)
}

// ── Before hook can cancel by returning false ─────────────────────────────────

export type BeforeHookHandler = (ctx: ActionHookContext) => boolean | void | Promise<boolean | void>
export type AfterHookHandler  = (ctx: ActionHookContext) => void | Promise<void>

// ── Registered types ──────────────────────────────────────────────────────────

export interface RegisteredActionHook {
  pluginId: string
  entityType: string   // '*' = all entities
  operation: CrudOperation
  timing: HookTiming
  handler: BeforeHookHandler | AfterHookHandler
  priority: number     // lower = runs first
}

export interface RegisteredColumnRenderer {
  pluginId: string
  fieldType: string    // FieldType value e.g. 'String', 'Enum', or '*'
  entityType?: string  // undefined = applies to all entities
  fieldName?: string   // optional: specific field name override
  component: Component
  priority: number     // higher = preferred
}

export interface RegisteredDetailSection {
  pluginId: string
  id: string
  label: string
  entityType: string   // '*' = all entities, or specific entity name
  component: Component
  position: 'before' | 'after' | 'append'
  order: number
}

export interface RegisteredCustomView {
  pluginId: string
  id: string
  label: string
  entityType: string   // '*' = all, or specific
  component: Component
  icon?: Component
}

export interface ThemeExtension {
  pluginId: string
  id: string
  name: string
  cssVariables: Record<string, string>  // CSS custom property name → value
}

// ── Plugin registration options ───────────────────────────────────────────────

export interface PluginDefinition {
  id: string
  name: string
  version: string
  description?: string
  author?: string
}

export interface PluginRegistrationOptions {
  plugin: PluginDefinition
  columnRenderers?: Omit<RegisteredColumnRenderer, 'pluginId'>[]
  detailSections?: Omit<RegisteredDetailSection, 'pluginId'>[]
  actionHooks?: Omit<RegisteredActionHook, 'pluginId'>[]
  customViews?: Omit<RegisteredCustomView, 'pluginId'>[]
  themeExtensions?: Omit<ThemeExtension, 'pluginId'>[]
}
