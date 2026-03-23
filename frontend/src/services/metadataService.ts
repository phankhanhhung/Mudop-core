import api from './api'
import type { ModuleMetadata, EntityMetadata, ServiceMetadata } from '@/types/metadata'

export const metadataService = {
  /**
   * Get all available modules (from Runtime OData metadata)
   */
  async getModules(): Promise<ModuleMetadata[]> {
    const response = await api.get<ModuleMetadata[]>('/odata/metadata')
    return response.data
  },

  /**
   * Get metadata for a specific module
   */
  async getModule(moduleName: string): Promise<ModuleMetadata> {
    const modules = await this.getModules()
    const module = modules.find((m) => m.name === moduleName)
    if (!module) throw new Error(`Module '${moduleName}' not found`)
    return module
  },

  /**
   * Get entity metadata (fields, keys, associations)
   */
  async getEntity(moduleName: string, entityName: string): Promise<EntityMetadata> {
    const response = await api.get<EntityMetadata>(
      `/odata/metadata/${encodeURIComponent(moduleName)}/entities/${encodeURIComponent(entityName)}`
    )
    return response.data
  },

  /**
   * Get service metadata
   */
  async getService(moduleName: string, serviceName: string): Promise<ServiceMetadata> {
    const module = await this.getModule(moduleName)
    const service = module.services.find((s) => s.name === serviceName)
    if (!service) throw new Error(`Service '${serviceName}' not found in module '${moduleName}'`)
    return service
  },

  /**
   * Get all entities in a module
   */
  async getEntities(moduleName: string): Promise<EntityMetadata[]> {
    const module = await this.getModule(moduleName)
    const entities: EntityMetadata[] = []
    for (const service of module.services) {
      for (const entity of service.entities) {
        try {
          const meta = await this.getEntity(moduleName, entity.entityType)
          entities.push(meta)
        } catch {
          // Skip entities that fail to load
        }
      }
    }
    return entities
  },

  /**
   * Get all services in a module
   */
  async getServices(moduleName: string): Promise<ServiceMetadata[]> {
    const module = await this.getModule(moduleName)
    return module.services
  }
}

export default metadataService
