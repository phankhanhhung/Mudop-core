import { describe, it, expect } from 'vitest'
import { useDeepOperation } from '../useDeepOperation'
import type { EntityMetadata } from '@/types/metadata'

const metadata: EntityMetadata = {
  name: 'Order',
  namespace: 'test',
  keys: ['Id'],
  fields: [
    { name: 'Id', type: 'UUID', isRequired: true, isReadOnly: false, isComputed: true, annotations: {} },
    { name: 'CustomerName', type: 'String', isRequired: true, isReadOnly: false, isComputed: false, annotations: {} },
    { name: 'Total', type: 'Decimal', isRequired: false, isReadOnly: false, isComputed: false, annotations: {} },
  ],
  associations: [
    {
      name: 'Items',
      targetEntity: 'OrderItem',
      cardinality: 'Many',
      isComposition: true,
      foreignKey: 'OrderId',
    },
    {
      name: 'Notes',
      targetEntity: 'OrderNote',
      cardinality: 'Many',
      isComposition: true,
      foreignKey: 'OrderId',
    },
    {
      name: 'Customer',
      targetEntity: 'Customer',
      cardinality: 'ZeroOrOne',
      isComposition: false,
      foreignKey: 'CustomerId',
    },
  ],
  annotations: {},
}

describe('useDeepOperation', () => {
  // =========================================================================
  // Initialization
  // =========================================================================

  describe('initialization', () => {
    it('initializes composition states from metadata associations', () => {
      const { compositions } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      // Should have entries for composition associations only (Items and Notes)
      expect(compositions.value.has('Items')).toBe(true)
      expect(compositions.value.has('Notes')).toBe(true)
      // Non-composition (Customer) should NOT be in compositions
      expect(compositions.value.has('Customer')).toBe(false)
    })

    it('compositions start with no items', () => {
      const { getCompositionItems, hasCompositionChanges } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      expect(getCompositionItems('Items')).toEqual([])
      expect(getCompositionItems('Notes')).toEqual([])
      expect(hasCompositionChanges.value).toBe(false)
    })
  })

  // =========================================================================
  // initFromEntity
  // =========================================================================

  describe('initFromEntity()', () => {
    it('populates items from entity data', () => {
      const { initFromEntity, getCompositionItems } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      const itemsData = [
        { Id: '10', Product: 'Widget', Qty: 5, OrderId: '1' },
        { Id: '11', Product: 'Gadget', Qty: 2, OrderId: '1' },
      ]

      initFromEntity({ Id: '1', CustomerName: 'Alice', Items: itemsData })

      const items = getCompositionItems('Items')
      expect(items).toHaveLength(2)
      expect(items[0]).toEqual(itemsData[0])
      expect(items[1]).toEqual(itemsData[1])
    })

    it('populates originalItems for reset support', () => {
      const { initFromEntity, compositions } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({
        Id: '1',
        Items: [{ Id: '10', Product: 'Widget', Qty: 5 }],
      })

      const state = compositions.value.get('Items')!
      expect(state.originalItems).toHaveLength(1)
      expect(state.originalItems[0]).toEqual({ Id: '10', Product: 'Widget', Qty: 5 })
    })

    it('handles missing navigation data (sets empty arrays)', () => {
      const { initFromEntity, getCompositionItems } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({ Id: '1', CustomerName: 'Alice' })

      expect(getCompositionItems('Items')).toEqual([])
      expect(getCompositionItems('Notes')).toEqual([])
    })

    it('resets tracking state on re-init', () => {
      const { initFromEntity, addCompositionItem, getCompositionItems, hasCompositionChanges } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      // First init
      initFromEntity({ Id: '1', Items: [{ Id: '10', Product: 'Widget', Qty: 5 }] })

      // Add an item to create dirty state
      addCompositionItem('Items', { Id: 'tmp1', Product: 'Foo', Qty: 1 })
      expect(getCompositionItems('Items')).toHaveLength(2)

      // Re-init from entity — should reset everything
      initFromEntity({ Id: '1', Items: [{ Id: '20', Product: 'NewWidget', Qty: 3 }] })

      const items = getCompositionItems('Items')
      expect(items).toHaveLength(1)
      expect(items[0].Product).toBe('NewWidget')
      // After re-init, dirty state is cleared
      expect(hasCompositionChanges.value).toBe(false)
    })
  })

  // =========================================================================
  // addCompositionItem
  // =========================================================================

  describe('addCompositionItem()', () => {
    it('adds item to composition items list', () => {
      const { initFromEntity, addCompositionItem, getCompositionItems } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({ Id: '1', Items: [] })

      addCompositionItem('Items', { Product: 'NewWidget', Qty: 3 })

      const items = getCompositionItems('Items')
      expect(items).toHaveLength(1)
      expect(items[0]).toMatchObject({ Product: 'NewWidget', Qty: 3 })
    })

    it('added items appear in deep insert payload', () => {
      const { initFromEntity, addCompositionItem, buildDeepInsertPayload } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({ Id: '1', Items: [] })
      addCompositionItem('Items', { Product: 'Widget', Qty: 5 })

      const payload = buildDeepInsertPayload({ CustomerName: 'Alice' })
      expect(payload.Items).toBeDefined()
      const items = payload.Items as Record<string, unknown>[]
      expect(items).toHaveLength(1)
      expect(items[0].Product).toBe('Widget')
    })

    it('auto-populates FK when parentKey provided', () => {
      const { initFromEntity, addCompositionItem, getCompositionItems } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
        parentKey: 'order-123',
      })

      initFromEntity({ Id: 'order-123', Items: [] })
      addCompositionItem('Items', { Product: 'Widget', Qty: 1 })

      const items = getCompositionItems('Items')
      expect(items[0].OrderId).toBe('order-123')
    })

    it('does not populate FK when no parentKey', () => {
      const { initFromEntity, addCompositionItem, getCompositionItems } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({ Id: '1', Items: [] })
      addCompositionItem('Items', { Product: 'Widget', Qty: 1 })

      const items = getCompositionItems('Items')
      expect(items[0].OrderId).toBeUndefined()
    })

    it('ignores unknown association name', () => {
      const { initFromEntity, addCompositionItem, getCompositionItems } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({ Id: '1', Items: [] })
      addCompositionItem('NonExistent', { foo: 'bar' })

      // No crash, Items remain empty
      expect(getCompositionItems('Items')).toEqual([])
    })
  })

  // =========================================================================
  // updateCompositionItem
  // =========================================================================

  describe('updateCompositionItem()', () => {
    it('modifies item in the items list', () => {
      const { initFromEntity, updateCompositionItem, getCompositionItems } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({
        Id: '1',
        Items: [{ Id: '10', Product: 'Widget', Qty: 5 }],
      })

      updateCompositionItem('Items', '10', { Qty: 10 })

      const items = getCompositionItems('Items')
      expect(items[0].Qty).toBe(10)
      expect(items[0].Product).toBe('Widget') // unchanged field preserved
    })

    it('merges multiple updates for the same item', () => {
      const { initFromEntity, updateCompositionItem, getCompositionItems } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({
        Id: '1',
        Items: [{ Id: '10', Product: 'Widget', Qty: 5, Price: 9.99 }],
      })

      updateCompositionItem('Items', '10', { Qty: 10 })
      updateCompositionItem('Items', '10', { Price: 12.99 })

      const items = getCompositionItems('Items')
      expect(items[0].Qty).toBe(10)
      expect(items[0].Price).toBe(12.99)
    })

    it('does nothing for unknown item key', () => {
      const { initFromEntity, updateCompositionItem, getCompositionItems } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({
        Id: '1',
        Items: [{ Id: '10', Product: 'Widget', Qty: 5 }],
      })

      updateCompositionItem('Items', 'nonexistent', { Qty: 10 })

      const items = getCompositionItems('Items')
      expect(items[0].Qty).toBe(5) // unchanged
    })
  })

  // =========================================================================
  // removeCompositionItem
  // =========================================================================

  describe('removeCompositionItem()', () => {
    it('on existing item removes from items list', () => {
      const { initFromEntity, removeCompositionItem, getCompositionItems } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({
        Id: '1',
        Items: [
          { Id: '10', Product: 'Widget', Qty: 5 },
          { Id: '11', Product: 'Gadget', Qty: 2 },
        ],
      })

      removeCompositionItem('Items', '10')

      const items = getCompositionItems('Items')
      expect(items).toHaveLength(1)
      expect(items[0].Id).toBe('11')
    })

    it('on newly added item just removes without marking for server delete', () => {
      const { initFromEntity, addCompositionItem, removeCompositionItem, getCompositionItems } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({ Id: '1', Items: [{ Id: '10', Product: 'Existing', Qty: 1 }] })

      addCompositionItem('Items', { Id: 'tmp-1', Product: 'Temp', Qty: 1 })
      expect(getCompositionItems('Items')).toHaveLength(2)

      removeCompositionItem('Items', 'tmp-1')

      expect(getCompositionItems('Items')).toHaveLength(1)
      expect(getCompositionItems('Items')[0].Product).toBe('Existing')
    })
  })

  // =========================================================================
  // buildDeepInsertPayload
  // =========================================================================

  describe('buildDeepInsertPayload()', () => {
    it('includes compositions, strips keys and @-metadata', () => {
      const { initFromEntity, addCompositionItem, buildDeepInsertPayload } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({ Id: '1', Items: [] })

      addCompositionItem('Items', {
        Id: 'tmp-1',
        Product: 'Widget',
        Qty: 5,
        '@odata.type': '#OrderItem',
      })
      addCompositionItem('Items', {
        ID: 'tmp-2',
        Product: 'Gadget',
        Qty: 2,
      })

      const payload = buildDeepInsertPayload({ CustomerName: 'Alice', Total: 100 })

      expect(payload.CustomerName).toBe('Alice')
      expect(payload.Total).toBe(100)
      expect(payload.Items).toBeDefined()

      const items = payload.Items as Record<string, unknown>[]
      expect(items).toHaveLength(2)

      // Keys should be stripped
      expect(items[0]).not.toHaveProperty('Id')
      expect(items[0]).not.toHaveProperty('@odata.type')
      expect(items[0].Product).toBe('Widget')

      expect(items[1]).not.toHaveProperty('ID')
      expect(items[1].Product).toBe('Gadget')
    })

    it('does not include empty compositions in payload', () => {
      const { initFromEntity, buildDeepInsertPayload } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({ Id: '1', Items: [], Notes: [] })

      const payload = buildDeepInsertPayload({ CustomerName: 'Alice' })

      expect(payload.Items).toBeUndefined()
      expect(payload.Notes).toBeUndefined()
    })

    it('preserves parent data fields in payload', () => {
      const { initFromEntity, buildDeepInsertPayload } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({ Id: '1', Items: [] })

      const payload = buildDeepInsertPayload({
        CustomerName: 'Alice',
        Total: 250.50,
        ExtraField: 'value',
      })

      expect(payload.CustomerName).toBe('Alice')
      expect(payload.Total).toBe(250.50)
      expect(payload.ExtraField).toBe('value')
    })

    it('includes items from initFromEntity in the payload', () => {
      const { initFromEntity, buildDeepInsertPayload } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({
        Id: '1',
        Items: [
          { Id: '10', Product: 'Widget', Qty: 5 },
          { Id: '11', Product: 'Gadget', Qty: 2 },
        ],
      })

      const payload = buildDeepInsertPayload({ CustomerName: 'Alice' })
      expect(payload.Items).toBeDefined()
      const items = payload.Items as Record<string, unknown>[]
      expect(items).toHaveLength(2)
    })
  })

  // =========================================================================
  // hasCompositionChanges
  // =========================================================================

  describe('hasCompositionChanges', () => {
    it('is false initially', () => {
      const { hasCompositionChanges } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      expect(hasCompositionChanges.value).toBe(false)
    })

    it('is false after initFromEntity (no mutations)', () => {
      const { initFromEntity, hasCompositionChanges } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({ Id: '1', Items: [{ Id: '10', Product: 'Widget', Qty: 5 }] })
      expect(hasCompositionChanges.value).toBe(false)
    })

    it('deep insert payload includes items when added', () => {
      const { initFromEntity, addCompositionItem, buildDeepInsertPayload } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({ Id: '1', Items: [] })
      addCompositionItem('Items', { Product: 'New', Qty: 1 })

      const payload = buildDeepInsertPayload({ CustomerName: 'Alice' })
      expect(payload.Items).toBeDefined()
      expect((payload.Items as unknown[]).length).toBe(1)
    })

    it('getCompositionItems reflects updates after modification', () => {
      const { initFromEntity, updateCompositionItem, getCompositionItems } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({ Id: '1', Items: [{ Id: '10', Product: 'Widget', Qty: 5 }] })
      updateCompositionItem('Items', '10', { Qty: 10 })

      const items = getCompositionItems('Items')
      expect(items[0].Qty).toBe(10)
    })

    it('getCompositionItems reflects removal', () => {
      const { initFromEntity, removeCompositionItem, getCompositionItems } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({
        Id: '1',
        Items: [
          { Id: '10', Product: 'Widget', Qty: 5 },
          { Id: '11', Product: 'Gadget', Qty: 2 },
        ],
      })
      removeCompositionItem('Items', '10')

      const items = getCompositionItems('Items')
      expect(items).toHaveLength(1)
      expect(items[0].Product).toBe('Gadget')
    })
  })

  // =========================================================================
  // resetCompositions
  // =========================================================================

  describe('resetCompositions()', () => {
    it('restores original items after add/update/remove', () => {
      const { initFromEntity, addCompositionItem, updateCompositionItem, removeCompositionItem, resetCompositions, getCompositionItems } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({
        Id: '1',
        Items: [
          { Id: '10', Product: 'Widget', Qty: 5 },
          { Id: '11', Product: 'Gadget', Qty: 2 },
        ],
      })

      addCompositionItem('Items', { Product: 'New', Qty: 1 })
      updateCompositionItem('Items', '10', { Qty: 99 })
      removeCompositionItem('Items', '11')

      // Items should be modified
      expect(getCompositionItems('Items')).toHaveLength(2) // Widget(modified) + New

      resetCompositions()

      const items = getCompositionItems('Items')
      expect(items).toHaveLength(2)
      expect(items[0].Product).toBe('Widget')
      expect(items[0].Qty).toBe(5) // original value restored
      expect(items[1].Product).toBe('Gadget')
    })

    it('after reset, compositions are clean', () => {
      const { initFromEntity, addCompositionItem, resetCompositions, hasCompositionChanges } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({ Id: '1', Items: [{ Id: '10', Product: 'Widget', Qty: 5 }] })
      addCompositionItem('Items', { Product: 'New', Qty: 1 })

      resetCompositions()

      expect(hasCompositionChanges.value).toBe(false)
    })
  })

  // =========================================================================
  // getCompositionItems
  // =========================================================================

  describe('getCompositionItems()', () => {
    it('returns current items for a composition', () => {
      const { initFromEntity, getCompositionItems } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({
        Id: '1',
        Items: [{ Id: '10', Product: 'Widget', Qty: 5 }],
      })

      const items = getCompositionItems('Items')
      expect(items).toHaveLength(1)
      expect(items[0].Product).toBe('Widget')
    })

    it('returns empty array for unknown association', () => {
      const { getCompositionItems } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      expect(getCompositionItems('NonExistent')).toEqual([])
    })

    it('reflects additions', () => {
      const { initFromEntity, addCompositionItem, getCompositionItems } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({ Id: '1', Items: [{ Id: '10', Product: 'Widget', Qty: 5 }] })
      addCompositionItem('Items', { Product: 'NewItem', Qty: 3 })

      const items = getCompositionItems('Items')
      expect(items).toHaveLength(2)
      expect(items[1]).toMatchObject({ Product: 'NewItem', Qty: 3 })
    })

    it('reflects removals', () => {
      const { initFromEntity, removeCompositionItem, getCompositionItems } = useDeepOperation({
        module: 'myapp',
        entitySet: 'Orders',
        metadata,
      })

      initFromEntity({
        Id: '1',
        Items: [
          { Id: '10', Product: 'Widget', Qty: 5 },
          { Id: '11', Product: 'Gadget', Qty: 2 },
        ],
      })

      removeCompositionItem('Items', '10')

      const items = getCompositionItems('Items')
      expect(items).toHaveLength(1)
      expect(items[0].Product).toBe('Gadget')
    })
  })
})
