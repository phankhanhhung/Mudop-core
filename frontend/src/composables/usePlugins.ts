import { computed, type Ref } from 'vue'
import { pluginRegistry } from '@/lib/plugins'
import type { ActionHookContext, CrudOperation } from '@/lib/plugins'
import type { Component } from 'vue'

export function usePlugins(entityType: Ref<string>, module: Ref<string>) {
  /** Detail sections registered for this entity type */
  const detailSections = computed(() => pluginRegistry.getDetailSections(entityType.value))

  /** Custom views (additional toolbar views) for this entity */
  const customViews = computed(() => pluginRegistry.getCustomViews(entityType.value))

  /** Get a custom cell renderer component for a field, or null if none registered */
  function getColumnRenderer(fieldType: string, fieldName?: string): Component | null {
    const renderer = pluginRegistry.getColumnRenderer(fieldType, entityType.value, fieldName)
    return renderer?.component ?? null
  }

  /**
   * Run before-hooks for a CRUD operation.
   * Returns false if any hook cancelled the operation.
   */
  async function runBeforeHooks(
    operation: CrudOperation,
    data?: Record<string, unknown>,
    entityId?: string,
  ): Promise<boolean> {
    const ctx: ActionHookContext = {
      entityType: entityType.value,
      module: module.value,
      operation,
      data,
      entityId,
    }
    return pluginRegistry.runBeforeHooks(entityType.value, operation, ctx)
  }

  /**
   * Run after-hooks for a CRUD operation.
   */
  async function runAfterHooks(
    operation: CrudOperation,
    result?: Record<string, unknown>,
    entityId?: string,
    data?: Record<string, unknown>,
  ): Promise<void> {
    const ctx: ActionHookContext = {
      entityType: entityType.value,
      module: module.value,
      operation,
      data,
      entityId,
      result,
    }
    return pluginRegistry.runAfterHooks(entityType.value, operation, ctx)
  }

  return {
    detailSections,
    customViews,
    getColumnRenderer,
    runBeforeHooks,
    runAfterHooks,
  }
}
