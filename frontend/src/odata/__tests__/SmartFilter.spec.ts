import { describe, it, expect, beforeEach } from 'vitest'
import { SmartFilter } from '../SmartFilter'
import type { EntityMetadata } from '@/types/metadata'
import type { SmartFilterField } from '../types'

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

function createTestMetadata(
  overrides: Partial<EntityMetadata> = {}
): EntityMetadata {
  return {
    name: 'Customer',
    namespace: 'myapp',
    fields: [
      {
        name: 'Id',
        type: 'UUID',
        isRequired: true,
        isReadOnly: false,
        isComputed: true,
        annotations: {},
      },
      {
        name: 'Name',
        type: 'String',
        isRequired: true,
        isReadOnly: false,
        isComputed: false,
        maxLength: 100,
        annotations: {},
      },
      {
        name: 'Email',
        type: 'String',
        isRequired: false,
        isReadOnly: false,
        isComputed: false,
        annotations: {},
      },
      {
        name: 'Age',
        type: 'Integer',
        isRequired: false,
        isReadOnly: false,
        isComputed: false,
        annotations: {},
      },
      {
        name: 'Balance',
        type: 'Decimal',
        isRequired: false,
        isReadOnly: false,
        isComputed: false,
        precision: 18,
        scale: 2,
        annotations: {},
      },
      {
        name: 'IsActive',
        type: 'Boolean',
        isRequired: false,
        isReadOnly: false,
        isComputed: false,
        annotations: {},
      },
      {
        name: 'BirthDate',
        type: 'Date',
        isRequired: false,
        isReadOnly: false,
        isComputed: false,
        annotations: {},
      },
      {
        name: 'CreatedAt',
        type: 'Timestamp',
        isRequired: false,
        isReadOnly: false,
        isComputed: false,
        annotations: {},
      },
      {
        name: 'Status',
        type: 'Enum',
        isRequired: false,
        isReadOnly: false,
        isComputed: false,
        enumValues: [
          { name: 'Active', value: 1 },
          { name: 'Inactive', value: 2 },
        ],
        annotations: {},
      },
    ],
    keys: ['Id'],
    associations: [
      {
        name: 'Account',
        targetEntity: 'Account',
        cardinality: 'ZeroOrOne',
        foreignKey: 'AccountId',
        isComposition: false,
      },
      {
        name: 'Orders',
        targetEntity: 'Order',
        cardinality: 'Many',
        isComposition: true,
      },
    ],
    annotations: {},
    ...overrides,
  }
}

function findField(fields: SmartFilterField[], name: string): SmartFilterField | undefined {
  return fields.find(f => f.name === name)
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('SmartFilter', () => {
  let smartFilter: SmartFilter

  beforeEach(() => {
    smartFilter = new SmartFilter()
    // Clear localStorage before each test
    localStorage.clear()
  })

  // =========================================================================
  // generateFields()
  // =========================================================================

  describe('generateFields()', () => {
    it('generates the correct number of fields (skipping computed + key fields)', () => {
      const metadata = createTestMetadata()
      const fields = smartFilter.generateFields(metadata)

      // Total fields in metadata: 9
      // Id is key + computed => skip
      // Remaining entity fields: Name, Email, Age, Balance, IsActive, BirthDate, CreatedAt, Status = 8
      // Association FK fields added: AccountId (Account assoc has foreignKey)
      // Composition: Orders is a composition, so NOT added
      // Total: 8 + 1 = 9
      expect(fields).toHaveLength(9)

      // Verify Id is not present
      expect(findField(fields, 'Id')).toBeUndefined()
    })

    it('maps String fields to widgetType "text" with defaultOperator "contains"', () => {
      const metadata = createTestMetadata()
      const fields = smartFilter.generateFields(metadata)
      const nameField = findField(fields, 'Name')

      expect(nameField).toBeDefined()
      expect(nameField!.widgetType).toBe('text')
      expect(nameField!.defaultOperator).toBe('contains')
    })

    it('maps Integer fields to widgetType "number" with defaultOperator "eq"', () => {
      const metadata = createTestMetadata()
      const fields = smartFilter.generateFields(metadata)
      const ageField = findField(fields, 'Age')

      expect(ageField).toBeDefined()
      expect(ageField!.widgetType).toBe('number')
      expect(ageField!.defaultOperator).toBe('eq')
    })

    it('maps Decimal fields to widgetType "decimal" with defaultOperator "eq"', () => {
      const metadata = createTestMetadata()
      const fields = smartFilter.generateFields(metadata)
      const balanceField = findField(fields, 'Balance')

      expect(balanceField).toBeDefined()
      expect(balanceField!.widgetType).toBe('decimal')
      expect(balanceField!.defaultOperator).toBe('eq')
    })

    it('maps Boolean fields to widgetType "boolean" with defaultOperator "eq"', () => {
      const metadata = createTestMetadata()
      const fields = smartFilter.generateFields(metadata)
      const isActiveField = findField(fields, 'IsActive')

      expect(isActiveField).toBeDefined()
      expect(isActiveField!.widgetType).toBe('boolean')
      expect(isActiveField!.defaultOperator).toBe('eq')
    })

    it('maps Date fields to widgetType "date" with defaultOperator "eq"', () => {
      const metadata = createTestMetadata()
      const fields = smartFilter.generateFields(metadata)
      const birthDateField = findField(fields, 'BirthDate')

      expect(birthDateField).toBeDefined()
      expect(birthDateField!.widgetType).toBe('date')
      expect(birthDateField!.defaultOperator).toBe('eq')
    })

    it('maps Timestamp fields to widgetType "datetime" with defaultOperator "eq"', () => {
      const metadata = createTestMetadata()
      const fields = smartFilter.generateFields(metadata)
      const createdAtField = findField(fields, 'CreatedAt')

      expect(createdAtField).toBeDefined()
      expect(createdAtField!.widgetType).toBe('datetime')
      expect(createdAtField!.defaultOperator).toBe('eq')
    })

    it('maps Enum fields to widgetType "enum" and includes enumValues', () => {
      const metadata = createTestMetadata()
      const fields = smartFilter.generateFields(metadata)
      const statusField = findField(fields, 'Status')

      expect(statusField).toBeDefined()
      expect(statusField!.widgetType).toBe('enum')
      expect(statusField!.defaultOperator).toBe('eq')
      expect(statusField!.enumValues).toBeDefined()
      expect(statusField!.enumValues).toHaveLength(2)
      expect(statusField!.enumValues![0]).toEqual({
        name: 'Active',
        value: 1,
        displayName: 'Active',
      })
      expect(statusField!.enumValues![1]).toEqual({
        name: 'Inactive',
        value: 2,
        displayName: 'Inactive',
      })
    })

    it('maps UUID fields to widgetType "uuid" (non-key UUID fields)', () => {
      const metadata = createTestMetadata({
        fields: [
          ...createTestMetadata().fields,
          {
            name: 'ExternalRef',
            type: 'UUID',
            isRequired: false,
            isReadOnly: false,
            isComputed: false,
            annotations: {},
          },
        ],
      })
      const fields = smartFilter.generateFields(metadata)
      const externalRefField = findField(fields, 'ExternalRef')

      expect(externalRefField).toBeDefined()
      expect(externalRefField!.widgetType).toBe('uuid')

      // But key UUID (Id) should still be absent
      expect(findField(fields, 'Id')).toBeUndefined()
    })

    it('skips key UUID fields from filter generation', () => {
      const metadata = createTestMetadata()
      const fields = smartFilter.generateFields(metadata)

      expect(findField(fields, 'Id')).toBeUndefined()
    })

    it('adds association FK fields as "association" widget type', () => {
      const metadata = createTestMetadata()
      const fields = smartFilter.generateFields(metadata)
      const accountFkField = findField(fields, 'AccountId')

      expect(accountFkField).toBeDefined()
      expect(accountFkField!.widgetType).toBe('association')
      expect(accountFkField!.associationTarget).toBe('Account')
      expect(accountFkField!.label).toBe('Account')
      expect(accountFkField!.defaultOperator).toBe('eq')
      expect(accountFkField!.filterable).toBe(true)
      expect(accountFkField!.sortable).toBe(true)
    })

    it('does NOT add composition associations as filter fields', () => {
      const metadata = createTestMetadata()
      const fields = smartFilter.generateFields(metadata)

      // Orders is a composition - should not generate a filter field
      const ordersField = fields.find(
        f => f.name === 'Orders' || f.associationTarget === 'Order'
      )
      expect(ordersField).toBeUndefined()
    })

    it('uses field.displayName as label when available, falls back to field.name', () => {
      const metadata = createTestMetadata({
        fields: [
          {
            name: 'Id',
            type: 'UUID',
            isRequired: true,
            isReadOnly: false,
            isComputed: true,
            annotations: {},
          },
          {
            name: 'FullName',
            type: 'String',
            displayName: 'Full Name',
            isRequired: false,
            isReadOnly: false,
            isComputed: false,
            annotations: {},
          },
          {
            name: 'Code',
            type: 'String',
            isRequired: false,
            isReadOnly: false,
            isComputed: false,
            annotations: {},
          },
        ],
        keys: ['Id'],
        associations: [],
      })
      const fields = smartFilter.generateFields(metadata)

      const fullNameField = findField(fields, 'FullName')
      expect(fullNameField!.label).toBe('Full Name')

      const codeField = findField(fields, 'Code')
      expect(codeField!.label).toBe('Code')
    })

    it('caches results per entity - second call returns the same array instance', () => {
      const metadata = createTestMetadata()
      const first = smartFilter.generateFields(metadata)
      const second = smartFilter.generateFields(metadata)

      expect(first).toBe(second)
    })

    it('clearCache() forces regeneration on next call', () => {
      const metadata = createTestMetadata()
      const first = smartFilter.generateFields(metadata)

      smartFilter.clearCache()

      const second = smartFilter.generateFields(metadata)
      expect(first).not.toBe(second)
      // But they should be structurally equivalent
      expect(second).toEqual(first)
    })

    it('respects FilterRestrictions annotation (NonFilterableProperties)', () => {
      const metadata = createTestMetadata({
        annotations: {
          'Org.OData.Capabilities.V1.FilterRestrictions': {
            NonFilterableProperties: ['Email', 'Age'],
          },
        },
      })
      const fields = smartFilter.generateFields(metadata)

      expect(findField(fields, 'Email')).toBeUndefined()
      expect(findField(fields, 'Age')).toBeUndefined()
      // Other fields should still be present
      expect(findField(fields, 'Name')).toBeDefined()
    })

    it('respects Filterable: false annotation (returns empty array)', () => {
      const metadata = createTestMetadata({
        annotations: {
          'Org.OData.Capabilities.V1.FilterRestrictions': {
            Filterable: false,
          },
        },
      })
      const fields = smartFilter.generateFields(metadata)

      expect(fields).toHaveLength(0)
    })

    it('respects SortRestrictions (NonSortableProperties marks sortable: false)', () => {
      const metadata = createTestMetadata({
        annotations: {
          'Org.OData.Capabilities.V1.SortRestrictions': {
            NonSortableProperties: ['Email', 'BirthDate'],
          },
        },
      })
      const fields = smartFilter.generateFields(metadata)

      const emailField = findField(fields, 'Email')
      expect(emailField).toBeDefined()
      expect(emailField!.sortable).toBe(false)

      const birthDateField = findField(fields, 'BirthDate')
      expect(birthDateField).toBeDefined()
      expect(birthDateField!.sortable).toBe(false)

      // Other fields should still be sortable
      const nameField = findField(fields, 'Name')
      expect(nameField!.sortable).toBe(true)
    })

    it('does not duplicate FK field when it already exists as an entity field', () => {
      const metadata = createTestMetadata({
        fields: [
          ...createTestMetadata().fields,
          {
            name: 'AccountId',
            type: 'UUID',
            isRequired: false,
            isReadOnly: false,
            isComputed: false,
            annotations: {},
          },
        ],
      })
      const fields = smartFilter.generateFields(metadata)

      const accountIdFields = fields.filter(f => f.name === 'AccountId')
      expect(accountIdFields).toHaveLength(1)
    })

    it('uses different cache keys for entities with same name in different namespaces', () => {
      const meta1 = createTestMetadata({ namespace: 'ns1' })
      const meta2 = createTestMetadata({
        namespace: 'ns2',
        fields: [
          {
            name: 'Id',
            type: 'UUID',
            isRequired: true,
            isReadOnly: false,
            isComputed: true,
            annotations: {},
          },
          {
            name: 'OnlyField',
            type: 'String',
            isRequired: false,
            isReadOnly: false,
            isComputed: false,
            annotations: {},
          },
        ],
        keys: ['Id'],
        associations: [],
      })

      const fields1 = smartFilter.generateFields(meta1)
      const fields2 = smartFilter.generateFields(meta2)

      expect(fields1).not.toBe(fields2)
      expect(fields1.length).not.toBe(fields2.length)
    })
  })

  // =========================================================================
  // buildFilterString()
  // =========================================================================

  describe('buildFilterString()', () => {
    it('returns empty string for empty filters array', () => {
      const result = smartFilter.buildFilterString([])
      expect(result).toBe('')
    })

    it('builds a correct OData $filter for a single condition', () => {
      const result = smartFilter.buildFilterString([
        { field: 'Name', operator: 'contains', value: 'Alice' },
      ])
      expect(result).toBe("contains(Name, 'Alice')")
    })

    it('joins multiple filters with "and" by default', () => {
      const result = smartFilter.buildFilterString([
        { field: 'Name', operator: 'contains', value: 'A' },
        { field: 'Age', operator: 'gt', value: 18 },
      ])
      expect(result).toBe("contains(Name, 'A') and Age gt 18")
    })

    it('joins multiple filters with "or" when specified', () => {
      const result = smartFilter.buildFilterString(
        [
          { field: 'Status', operator: 'eq', value: 1 },
          { field: 'Status', operator: 'eq', value: 2 },
        ],
        'or'
      )
      expect(result).toBe('Status eq 1 or Status eq 2')
    })

    it('handles boolean filter values', () => {
      const result = smartFilter.buildFilterString([
        { field: 'IsActive', operator: 'eq', value: true },
      ])
      expect(result).toBe('IsActive eq true')
    })

    it('handles null filter values', () => {
      const result = smartFilter.buildFilterString([
        { field: 'Email', operator: 'eq', value: null },
      ])
      expect(result).toBe('Email eq null')
    })

    it('handles string values with single quotes (escaping)', () => {
      const result = smartFilter.buildFilterString([
        { field: 'Name', operator: 'eq', value: "O'Brien" },
      ])
      expect(result).toBe("Name eq 'O''Brien'")
    })
  })

  // =========================================================================
  // createFilterForField()
  // =========================================================================

  describe('createFilterForField()', () => {
    it('uses the field defaultOperator when no operator is provided', () => {
      const metadata = createTestMetadata()
      const fields = smartFilter.generateFields(metadata)
      const nameField = findField(fields, 'Name')!

      const condition = smartFilter.createFilterForField(nameField, 'John')

      expect(condition).toEqual({
        field: 'Name',
        operator: 'contains',
        value: 'John',
      })
    })

    it('uses an explicit operator when provided', () => {
      const metadata = createTestMetadata()
      const fields = smartFilter.generateFields(metadata)
      const nameField = findField(fields, 'Name')!

      const condition = smartFilter.createFilterForField(nameField, 'John', 'eq')

      expect(condition).toEqual({
        field: 'Name',
        operator: 'eq',
        value: 'John',
      })
    })

    it('returns the correct FilterCondition shape', () => {
      const field: SmartFilterField = {
        name: 'Age',
        label: 'Age',
        widgetType: 'number',
        filterable: true,
        sortable: true,
        defaultOperator: 'eq',
      }

      const condition = smartFilter.createFilterForField(field, 25, 'gt')

      expect(condition).toHaveProperty('field', 'Age')
      expect(condition).toHaveProperty('operator', 'gt')
      expect(condition).toHaveProperty('value', 25)
    })
  })

  // =========================================================================
  // createRangeFilter()
  // =========================================================================

  describe('createRangeFilter()', () => {
    const rangeField: SmartFilterField = {
      name: 'Age',
      label: 'Age',
      widgetType: 'number',
      filterable: true,
      sortable: true,
      defaultOperator: 'eq',
    }

    it('returns [ge, le] conditions when both min and max are provided', () => {
      const conditions = smartFilter.createRangeFilter(rangeField, 18, 65)

      expect(conditions).toHaveLength(2)
      expect(conditions[0]).toEqual({ field: 'Age', operator: 'ge', value: 18 })
      expect(conditions[1]).toEqual({ field: 'Age', operator: 'le', value: 65 })
    })

    it('returns only [ge] condition when max is null', () => {
      const conditions = smartFilter.createRangeFilter(rangeField, 18, null)

      expect(conditions).toHaveLength(1)
      expect(conditions[0]).toEqual({ field: 'Age', operator: 'ge', value: 18 })
    })

    it('returns only [le] condition when min is null', () => {
      const conditions = smartFilter.createRangeFilter(rangeField, null, 65)

      expect(conditions).toHaveLength(1)
      expect(conditions[0]).toEqual({ field: 'Age', operator: 'le', value: 65 })
    })

    it('returns empty array when both min and max are null', () => {
      const conditions = smartFilter.createRangeFilter(rangeField, null, null)
      expect(conditions).toHaveLength(0)
    })

    it('returns empty array when both min and max are empty strings', () => {
      const conditions = smartFilter.createRangeFilter(rangeField, '', '')
      expect(conditions).toHaveLength(0)
    })

    it('treats 0 as a valid boundary (not skipped)', () => {
      const conditions = smartFilter.createRangeFilter(rangeField, 0, 100)

      expect(conditions).toHaveLength(2)
      expect(conditions[0]).toEqual({ field: 'Age', operator: 'ge', value: 0 })
      expect(conditions[1]).toEqual({ field: 'Age', operator: 'le', value: 100 })
    })
  })

  // =========================================================================
  // getSearchableFields()
  // =========================================================================

  describe('getSearchableFields()', () => {
    it('returns only text-type fields', () => {
      const metadata = createTestMetadata()
      const searchable = smartFilter.getSearchableFields(metadata)

      // Name and Email are String → text
      expect(searchable).toHaveLength(2)
      expect(searchable.every(f => f.widgetType === 'text')).toBe(true)

      const names = searchable.map(f => f.name)
      expect(names).toContain('Name')
      expect(names).toContain('Email')
    })

    it('returns empty array when entity has no string fields', () => {
      const metadata = createTestMetadata({
        fields: [
          {
            name: 'Id',
            type: 'UUID',
            isRequired: true,
            isReadOnly: false,
            isComputed: true,
            annotations: {},
          },
          {
            name: 'Count',
            type: 'Integer',
            isRequired: false,
            isReadOnly: false,
            isComputed: false,
            annotations: {},
          },
        ],
        keys: ['Id'],
        associations: [],
      })
      const searchable = smartFilter.getSearchableFields(metadata)
      expect(searchable).toHaveLength(0)
    })
  })

  // =========================================================================
  // getSortableFields()
  // =========================================================================

  describe('getSortableFields()', () => {
    it('returns all fields where sortable is true', () => {
      const metadata = createTestMetadata()
      const sortable = smartFilter.getSortableFields(metadata)

      // All generated fields should be sortable by default (no SortRestrictions set)
      expect(sortable.length).toBeGreaterThan(0)
      expect(sortable.every(f => f.sortable === true)).toBe(true)
    })

    it('excludes fields marked as non-sortable via SortRestrictions', () => {
      const metadata = createTestMetadata({
        annotations: {
          'Org.OData.Capabilities.V1.SortRestrictions': {
            NonSortableProperties: ['Age', 'Balance'],
          },
        },
      })

      // Generate fields first so the cache populates
      smartFilter.generateFields(metadata)
      const sortable = smartFilter.getSortableFields(metadata)

      const sortableNames = sortable.map(f => f.name)
      expect(sortableNames).not.toContain('Age')
      expect(sortableNames).not.toContain('Balance')
      expect(sortableNames).toContain('Name')
    })
  })

  // =========================================================================
  // Static helpers
  // =========================================================================

  describe('static fieldTypeToWidget()', () => {
    it('maps all known field types to the correct widget type', () => {
      expect(SmartFilter.fieldTypeToWidget('String')).toBe('text')
      expect(SmartFilter.fieldTypeToWidget('Integer')).toBe('number')
      expect(SmartFilter.fieldTypeToWidget('Decimal')).toBe('decimal')
      expect(SmartFilter.fieldTypeToWidget('Boolean')).toBe('boolean')
      expect(SmartFilter.fieldTypeToWidget('Date')).toBe('date')
      expect(SmartFilter.fieldTypeToWidget('DateTime')).toBe('datetime')
      expect(SmartFilter.fieldTypeToWidget('Timestamp')).toBe('datetime')
      expect(SmartFilter.fieldTypeToWidget('UUID')).toBe('uuid')
      expect(SmartFilter.fieldTypeToWidget('Enum')).toBe('enum')
    })

    it('falls back to "text" for unknown types', () => {
      expect(SmartFilter.fieldTypeToWidget('Binary')).toBe('text')
      expect(SmartFilter.fieldTypeToWidget('Array')).toBe('text')
      expect(SmartFilter.fieldTypeToWidget('Time')).toBe('text')
    })
  })

  describe('static defaultOperator()', () => {
    it('returns "contains" for text widgets', () => {
      expect(SmartFilter.defaultOperator('text')).toBe('contains')
    })

    it('returns "eq" for number widgets', () => {
      expect(SmartFilter.defaultOperator('number')).toBe('eq')
    })

    it('returns "eq" for decimal widgets', () => {
      expect(SmartFilter.defaultOperator('decimal')).toBe('eq')
    })

    it('returns "eq" for boolean widgets', () => {
      expect(SmartFilter.defaultOperator('boolean')).toBe('eq')
    })

    it('returns "eq" for date widgets', () => {
      expect(SmartFilter.defaultOperator('date')).toBe('eq')
    })

    it('returns "eq" for datetime widgets', () => {
      expect(SmartFilter.defaultOperator('datetime')).toBe('eq')
    })

    it('returns "eq" for enum widgets', () => {
      expect(SmartFilter.defaultOperator('enum')).toBe('eq')
    })

    it('returns "eq" for uuid widgets', () => {
      expect(SmartFilter.defaultOperator('uuid')).toBe('eq')
    })

    it('returns "eq" for association widgets', () => {
      expect(SmartFilter.defaultOperator('association')).toBe('eq')
    })
  })
})
