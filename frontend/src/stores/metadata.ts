import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { metadataService } from '@/services'
import type { ModuleMetadata, EntityMetadata, ServiceMetadata } from '@/types/metadata'

export const useMetadataStore = defineStore('metadata', () => {
  // State
  const modules = ref<ModuleMetadata[]>([])
  const entityCache = ref<Map<string, EntityMetadata>>(new Map())
  const serviceCache = ref<Map<string, ServiceMetadata>>(new Map())
  const isLoading = ref(false)
  const error = ref<string | null>(null)

  // Getters
  const moduleNames = computed(() => modules.value.map((m) => m.name))
  const hasModules = computed(() => modules.value.length > 0)

  // Helper to create cache key
  function entityKey(moduleName: string, entityName: string): string {
    return `${moduleName}:${entityName}`
  }

  function serviceKey(moduleName: string, serviceName: string): string {
    return `${moduleName}:${serviceName}`
  }

  // Actions
  async function fetchModules(): Promise<void> {
    isLoading.value = true
    error.value = null
    try {
      modules.value = await metadataService.getModules()
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to fetch modules'
      throw e
    } finally {
      isLoading.value = false
    }
  }

  async function fetchModule(moduleName: string): Promise<ModuleMetadata> {
    isLoading.value = true
    error.value = null
    try {
      const module = await metadataService.getModule(moduleName)
      const index = modules.value.findIndex((m) => m.name === moduleName)
      if (index >= 0) {
        modules.value[index] = module
      } else {
        modules.value.push(module)
      }
      return module
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to fetch module'
      throw e
    } finally {
      isLoading.value = false
    }
  }

  async function fetchEntity(
    moduleName: string,
    entityName: string,
    force = false
  ): Promise<EntityMetadata> {
    const key = entityKey(moduleName, entityName)

    // Return cached if available and not forcing
    if (!force && entityCache.value.has(key)) {
      return entityCache.value.get(key)!
    }

    isLoading.value = true
    error.value = null
    try {
      const entity = await metadataService.getEntity(moduleName, entityName)
      entityCache.value.set(key, entity)
      return entity
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to fetch entity metadata'
      throw e
    } finally {
      isLoading.value = false
    }
  }

  async function fetchService(
    moduleName: string,
    serviceName: string,
    force = false
  ): Promise<ServiceMetadata> {
    const key = serviceKey(moduleName, serviceName)

    // Return cached if available and not forcing
    if (!force && serviceCache.value.has(key)) {
      return serviceCache.value.get(key)!
    }

    isLoading.value = true
    error.value = null
    try {
      const service = await metadataService.getService(moduleName, serviceName)
      serviceCache.value.set(key, service)
      return service
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to fetch service metadata'
      throw e
    } finally {
      isLoading.value = false
    }
  }

  function getEntity(moduleName: string, entityName: string): EntityMetadata | undefined {
    return entityCache.value.get(entityKey(moduleName, entityName))
  }

  function getService(moduleName: string, serviceName: string): ServiceMetadata | undefined {
    return serviceCache.value.get(serviceKey(moduleName, serviceName))
  }

  function getModule(moduleName: string): ModuleMetadata | undefined {
    return modules.value.find((m) => m.name === moduleName)
  }

  function clearCache(): void {
    entityCache.value.clear()
    serviceCache.value.clear()
  }

  function clearError(): void {
    error.value = null
  }

  function reset(): void {
    modules.value = []
    entityCache.value.clear()
    serviceCache.value.clear()
    error.value = null
  }

  return {
    // State
    modules,
    entityCache,
    serviceCache,
    isLoading,
    error,
    // Getters
    moduleNames,
    hasModules,
    // Actions
    fetchModules,
    fetchModule,
    fetchEntity,
    fetchService,
    getEntity,
    getService,
    getModule,
    clearCache,
    clearError,
    reset
  }
})
