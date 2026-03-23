import api from './api'

export interface PluginManifest {
  plugins: PluginManifestEntry[]
}

export interface PluginBmmdlModuleInfo {
  name: string
  autoInstall: boolean
  initSchema: boolean
}

export interface ModuleInstallResultDto {
  success: boolean
  entityCount: number
  serviceCount: number
  errors: string[]
  warnings: string[]
  schemaResult?: string
}

export interface PluginModuleInstallResponse {
  pluginName: string
  results: ModuleInstallResultDto[]
}

export interface PluginManifestEntry {
  name: string
  status: 'available' | 'installed' | 'enabled' | 'disabled'
  dependsOn: string[]
  capabilities: string[]
  isGloballyActive: boolean
  menuItems: PluginMenuItem[]
  pages: PluginPageDefinition[]
  settings: PluginManifestSettings | null
  bmmdlModules: PluginBmmdlModuleInfo[]
}

export interface PluginMenuItem {
  label: string
  route: string
  icon: string
  section: 'main' | 'admin' | 'system' | 'tools'
  order: number
  badge?: string
  requiredPermission?: string
}

export interface PluginPageDefinition {
  route: string
  title: string
  component: string
  icon?: string
  parentRoute?: string
  meta?: Record<string, unknown>
}

export interface PluginManifestSettings {
  schema: PluginSettingsSchema
  values: Record<string, unknown>
}

export interface PluginSettingsSchema {
  groupLabel: string
  settings: PluginSettingDef[]
}

export interface PluginSettingDef {
  key: string
  label: string
  type: 'boolean' | 'integer' | 'string' | 'select'
  defaultValue: unknown
  required: boolean
  description?: string
  options?: string[]
}

// ── Staging Types ────────────────────────────────────────────

export interface ValidationCheckResultDto {
  checkName: string
  passed: boolean
  severity: 'info' | 'warning' | 'error'
  message: string
  details?: string
}

export interface PluginStagingResponse {
  id: number
  name: string
  version: string
  description?: string
  author?: string
  fileHash: string
  fileSize: number
  fileName: string
  validationStatus: 'pending' | 'valid' | 'invalid' | 'approved' | 'rejected'
  uploadedAt: string
  approvedAt?: string
  validationResults: ValidationCheckResultDto[]
}

export interface PluginLoadResponse {
  name: string
  version: string
  description?: string
  featureCount: number
  features: string[]
}

/**
 * Plugin management service.
 * Uses the shared API client with auth/tenant/admin-key interceptors.
 */
export const pluginService = {
  /**
   * Fetch the aggregated plugin manifest (menu items + pages for all plugins)
   */
  async getManifest(): Promise<PluginManifest> {
    const response = await api.get<PluginManifest>('/plugins/manifest')
    return response.data
  },

  /**
   * List all registered plugins with their current state
   */
  async listPlugins(): Promise<PluginManifestEntry[]> {
    const response = await api.get<PluginManifestEntry[]>('/plugins')
    return response.data
  },

  /**
   * Install a plugin (run migrations, set status=installed)
   */
  async installPlugin(name: string): Promise<void> {
    await api.post(`/plugins/${encodeURIComponent(name)}/install`)
  },

  /**
   * Enable an installed plugin (set status=enabled, call OnEnabledAsync)
   */
  async enablePlugin(name: string): Promise<void> {
    await api.post(`/plugins/${encodeURIComponent(name)}/enable`)
  },

  /**
   * Disable an enabled plugin (set status=disabled, call OnDisabledAsync)
   */
  async disablePlugin(name: string): Promise<void> {
    await api.post(`/plugins/${encodeURIComponent(name)}/disable`)
  },

  /**
   * Uninstall a plugin (run down-migrations, call OnUninstalledAsync)
   */
  async uninstallPlugin(name: string): Promise<void> {
    await api.delete(`/plugins/${encodeURIComponent(name)}`)
  },

  /**
   * Update plugin settings
   */
  async updateSettings(name: string, settings: Record<string, unknown>): Promise<void> {
    await api.put(`/plugins/${encodeURIComponent(name)}/settings`, settings)
  },

  /**
   * Install BMMDL modules bundled with a plugin into the Registry
   */
  async installPluginModules(name: string, force = false): Promise<PluginModuleInstallResponse> {
    const response = await api.post<PluginModuleInstallResponse>(
      `/plugins/${encodeURIComponent(name)}/install-modules`,
      null,
      { params: { force } }
    )
    return response.data
  },

  // ── Staging API ──────────────────────────────────────────────

  /**
   * Upload a plugin zip for staging and validation (NOT direct install).
   */
  async uploadPlugin(file: File): Promise<PluginStagingResponse> {
    const formData = new FormData()
    formData.append('file', file)
    const response = await api.post<PluginStagingResponse>('/plugins/upload', formData, {
      headers: { 'Content-Type': 'multipart/form-data' },
    })
    return response.data
  },

  /**
   * List all staged (uploaded but not yet installed) plugins.
   */
  async listStagedPlugins(): Promise<PluginStagingResponse[]> {
    const response = await api.get<{ value: PluginStagingResponse[] }>('/plugins/staging')
    return response.data.value
  },

  /**
   * Get details of a specific staged plugin.
   */
  async getStagedPlugin(id: number): Promise<PluginStagingResponse> {
    const response = await api.get<PluginStagingResponse>(`/plugins/staging/${id}`)
    return response.data
  },

  /**
   * Re-run validation on a staged plugin.
   */
  async revalidateStagedPlugin(id: number): Promise<PluginStagingResponse> {
    const response = await api.post<PluginStagingResponse>(`/plugins/staging/${id}/validate`)
    return response.data
  },

  /**
   * Approve a staged plugin (move to live plugins directory and load).
   */
  async approveStagedPlugin(id: number): Promise<PluginLoadResponse> {
    const response = await api.post<PluginLoadResponse>(`/plugins/staging/${id}/approve`)
    return response.data
  },

  /**
   * Reject a staged plugin (cleanup files and remove record).
   */
  async rejectStagedPlugin(id: number): Promise<void> {
    await api.delete(`/plugins/staging/${id}`)
  },
}

export default pluginService
