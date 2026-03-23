import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'

vi.mock('@/lib/plugins', () => ({
  pluginRegistry: {
    getDetailSections: vi.fn().mockReturnValue([]),
    getCustomViews: vi.fn().mockReturnValue([]),
    getColumnRenderer: vi.fn().mockReturnValue(null),
    runBeforeHooks: vi.fn().mockResolvedValue(true),
    runAfterHooks: vi.fn().mockResolvedValue(undefined),
  },
}))

import { pluginRegistry } from '@/lib/plugins'
import { usePlugins } from '../usePlugins'
import { ref } from 'vue'

const MockComponent = { template: '<div />' }

describe('usePlugins', () => {
  beforeEach(() => vi.clearAllMocks())

  afterEach(() => {})

  describe('detailSections', () => {
    it('calls pluginRegistry.getDetailSections with the current entityType', () => {
      const entityType = ref('Customer')
      const module = ref('crm')
      const { detailSections } = usePlugins(entityType, module)

      // Access computed to trigger evaluation
      void detailSections.value

      expect(pluginRegistry.getDetailSections).toHaveBeenCalledWith('Customer')
    })

    it('is reactive and updates when entityType ref changes', () => {
      const mockGetDetailSections = vi.mocked(pluginRegistry.getDetailSections)
      const section1 = { id: 's1', label: 'Section 1', entityType: 'Customer', component: MockComponent, pluginId: 'p1', position: 'after' as const, order: 1 }
      const section2 = { id: 's2', label: 'Section 2', entityType: 'Order', component: MockComponent, pluginId: 'p1', position: 'after' as const, order: 1 }
      mockGetDetailSections.mockImplementation((et) => et === 'Customer' ? [section1] : [section2])

      const entityType = ref('Customer')
      const module = ref('crm')
      const { detailSections } = usePlugins(entityType, module)

      expect(detailSections.value).toEqual([section1])

      entityType.value = 'Order'
      expect(detailSections.value).toEqual([section2])
    })
  })

  describe('customViews', () => {
    it('calls pluginRegistry.getCustomViews with the current entityType', () => {
      const view = { id: 'v1', label: 'View 1', entityType: 'Customer', component: MockComponent, pluginId: 'p1' }
      vi.mocked(pluginRegistry.getCustomViews).mockReturnValue([view])

      const entityType = ref('Customer')
      const module = ref('crm')
      const { customViews } = usePlugins(entityType, module)

      void customViews.value

      expect(pluginRegistry.getCustomViews).toHaveBeenCalledWith('Customer')
      expect(customViews.value).toEqual([view])
    })
  })

  describe('getColumnRenderer', () => {
    it('calls pluginRegistry.getColumnRenderer with fieldType, entityType, and fieldName and returns component', () => {
      vi.mocked(pluginRegistry.getColumnRenderer).mockReturnValue({
        pluginId: 'p1',
        fieldType: 'Enum',
        component: MockComponent,
        priority: 10,
      })

      const entityType = ref('Customer')
      const module = ref('crm')
      const { getColumnRenderer } = usePlugins(entityType, module)

      const result = getColumnRenderer('Enum', 'status')

      expect(pluginRegistry.getColumnRenderer).toHaveBeenCalledWith('Enum', 'Customer', 'status')
      expect(result).toBe(MockComponent)
    })

    it('returns null when registry returns null', () => {
      vi.mocked(pluginRegistry.getColumnRenderer).mockReturnValue(null)

      const entityType = ref('Product')
      const module = ref('inventory')
      const { getColumnRenderer } = usePlugins(entityType, module)

      const result = getColumnRenderer('String', 'name')

      expect(result).toBeNull()
    })

    it('passes fieldName to registry when provided', () => {
      vi.mocked(pluginRegistry.getColumnRenderer).mockReturnValue(null)

      const entityType = ref('Invoice')
      const module = ref('finance')
      const { getColumnRenderer } = usePlugins(entityType, module)

      getColumnRenderer('Decimal', 'totalAmount')

      expect(pluginRegistry.getColumnRenderer).toHaveBeenCalledWith('Decimal', 'Invoice', 'totalAmount')
    })

    it('passes undefined fieldName when not provided', () => {
      vi.mocked(pluginRegistry.getColumnRenderer).mockReturnValue(null)

      const entityType = ref('Invoice')
      const module = ref('finance')
      const { getColumnRenderer } = usePlugins(entityType, module)

      getColumnRenderer('Boolean')

      expect(pluginRegistry.getColumnRenderer).toHaveBeenCalledWith('Boolean', 'Invoice', undefined)
    })
  })

  describe('runBeforeHooks', () => {
    it('calls pluginRegistry.runBeforeHooks with correct context including entityType, module, operation, data, and entityId', async () => {
      vi.mocked(pluginRegistry.runBeforeHooks).mockResolvedValue(true)

      const entityType = ref('Customer')
      const module = ref('crm')
      const { runBeforeHooks } = usePlugins(entityType, module)
      const data = { name: 'Acme Corp' }

      await runBeforeHooks('create', data, 'ent-123')

      expect(pluginRegistry.runBeforeHooks).toHaveBeenCalledWith('Customer', 'create', {
        entityType: 'Customer',
        module: 'crm',
        operation: 'create',
        data,
        entityId: 'ent-123',
      })
    })

    it('returns true when registry returns true', async () => {
      vi.mocked(pluginRegistry.runBeforeHooks).mockResolvedValue(true)

      const entityType = ref('Order')
      const module = ref('sales')
      const { runBeforeHooks } = usePlugins(entityType, module)

      const result = await runBeforeHooks('update')

      expect(result).toBe(true)
    })

    it('returns false when registry returns false', async () => {
      vi.mocked(pluginRegistry.runBeforeHooks).mockResolvedValue(false)

      const entityType = ref('Order')
      const module = ref('sales')
      const { runBeforeHooks } = usePlugins(entityType, module)

      const result = await runBeforeHooks('delete', undefined, 'ent-456')

      expect(result).toBe(false)
    })

    it('uses current entityType and module values from the refs in the context', async () => {
      vi.mocked(pluginRegistry.runBeforeHooks).mockResolvedValue(true)

      const entityType = ref('Customer')
      const module = ref('crm')
      const { runBeforeHooks } = usePlugins(entityType, module)

      entityType.value = 'Product'
      module.value = 'inventory'

      await runBeforeHooks('read')

      expect(pluginRegistry.runBeforeHooks).toHaveBeenCalledWith('Product', 'read', expect.objectContaining({
        entityType: 'Product',
        module: 'inventory',
      }))
    })
  })

  describe('runAfterHooks', () => {
    it('calls pluginRegistry.runAfterHooks with correct context including result', async () => {
      vi.mocked(pluginRegistry.runAfterHooks).mockResolvedValue(undefined)

      const entityType = ref('Customer')
      const module = ref('crm')
      const { runAfterHooks } = usePlugins(entityType, module)
      const result = { id: 'cust-1', name: 'Acme' }
      const data = { name: 'Acme' }

      await runAfterHooks('create', result, 'cust-1', data)

      expect(pluginRegistry.runAfterHooks).toHaveBeenCalledWith('Customer', 'create', {
        entityType: 'Customer',
        module: 'crm',
        operation: 'create',
        data,
        entityId: 'cust-1',
        result,
      })
    })

    it('passes undefined result in ctx when result is not provided', async () => {
      vi.mocked(pluginRegistry.runAfterHooks).mockResolvedValue(undefined)

      const entityType = ref('Order')
      const module = ref('sales')
      const { runAfterHooks } = usePlugins(entityType, module)

      await runAfterHooks('delete', undefined, 'order-99')

      expect(pluginRegistry.runAfterHooks).toHaveBeenCalledWith('Order', 'delete', expect.objectContaining({
        result: undefined,
      }))
    })
  })
})
