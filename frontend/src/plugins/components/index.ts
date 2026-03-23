import type { Component } from 'vue'

/**
 * Registry of plugin-provided Vue components.
 *
 * Each key matches the "component" field in PluginPageDefinition.
 * Components are lazy-loaded via dynamic imports.
 *
 * This is "Approach A" (built-in components) from the plugin architecture plan.
 * All plugin UI components are bundled in the frontend; the manifest maps
 * component names to lazy-loaded imports here.
 */
export const pluginComponents: Record<string, () => Promise<Component>> = {
  // Multi-Tenancy Plugin
  PluginTenantList: () => import('./PluginTenantList.vue'),
  PluginTenantCreate: () => import('./PluginTenantCreate.vue'),

  // Generic settings form (renders from plugin settings schema)
  PluginSettingsForm: () => import('./PluginSettingsForm.vue'),

  // Infrastructure plugin components
  PluginAuditLogList: () => import('./PluginAuditLogList.vue'),
  PluginOutboxList: () => import('./PluginOutboxList.vue'),
  PluginWebhookList: () => import('./PluginWebhookList.vue'),
  PluginWebhookCreate: () => import('./PluginWebhookCreate.vue'),
  PluginReportList: () => import('./PluginReportList.vue'),
  PluginChangeRequestList: () => import('./PluginChangeRequestList.vue'),
  PluginUserList: () => import('./PluginUserList.vue'),
  PluginUserCreate: () => import('./PluginUserCreate.vue'),
}
