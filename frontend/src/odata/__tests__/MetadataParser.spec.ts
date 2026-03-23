import { describe, it, expect, vi, beforeEach } from 'vitest'

vi.mock('@/services/odataService', () => ({
  odataService: {
    getMetadata: vi.fn(),
  },
}))

import { MetadataParser } from '../MetadataParser'
import { odataService } from '@/services/odataService'

// ---------------------------------------------------------------------------
// Sample CSDL XML
// ---------------------------------------------------------------------------

const SAMPLE_CSDL = `<?xml version="1.0" encoding="utf-8"?>
<edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
  <edmx:DataServices>
    <Schema Namespace="myapp" xmlns="http://docs.oasis-open.org/odata/ns/edm">
      <EnumType Name="CustomerStatus">
        <Member Name="Active" Value="1"/>
        <Member Name="Inactive" Value="2"/>
      </EnumType>
      <EntityType Name="Customer">
        <Key><PropertyRef Name="Id"/></Key>
        <Property Name="Id" Type="Edm.Guid" Nullable="false">
          <Annotation Term="Org.OData.Core.V1.Computed" Bool="true"/>
        </Property>
        <Property Name="Name" Type="Edm.String" MaxLength="100" Nullable="false"/>
        <Property Name="Email" Type="Edm.String" MaxLength="255"/>
        <Property Name="Balance" Type="Edm.Decimal" Precision="18" Scale="2"/>
        <Property Name="Status" Type="myapp.CustomerStatus" DefaultValue="Active"/>
        <Property Name="IsActive" Type="Edm.Boolean"/>
        <Property Name="CreatedAt" Type="Edm.DateTimeOffset"/>
        <NavigationProperty Name="Orders" Type="Collection(myapp.Order)" Partner="Customer" ContainsTarget="true"/>
        <NavigationProperty Name="Account" Type="myapp.Account">
          <ReferentialConstraint Property="AccountId" ReferencedProperty="Id"/>
        </NavigationProperty>
      </EntityType>
      <EntityType Name="Order">
        <Key><PropertyRef Name="Id"/></Key>
        <Property Name="Id" Type="Edm.Guid" Nullable="false"/>
        <Property Name="Total" Type="Edm.Decimal" Precision="18" Scale="2"/>
        <NavigationProperty Name="Customer" Type="myapp.Customer" Partner="Orders"/>
      </EntityType>
      <EntityType Name="Account">
        <Key><PropertyRef Name="Id"/></Key>
        <Property Name="Id" Type="Edm.Guid" Nullable="false"/>
        <Property Name="Code" Type="Edm.String" MaxLength="20"/>
      </EntityType>
      <EntityContainer Name="Container">
        <EntitySet Name="Customers" EntityType="myapp.Customer">
          <NavigationPropertyBinding Path="Account" Target="Accounts"/>
        </EntitySet>
        <EntitySet Name="Orders" EntityType="myapp.Order"/>
        <EntitySet Name="Accounts" EntityType="myapp.Account"/>
      </EntityContainer>
    </Schema>
  </edmx:DataServices>
</edmx:Edmx>`

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('MetadataParser', () => {
  beforeEach(() => {
    MetadataParser.clearCache()
    vi.clearAllMocks()
  })

  // =========================================================================
  // parseXml — Schema-level parsing
  // =========================================================================

  describe('parseXml', () => {
    it('parses namespace from Schema element', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      expect(schema.namespace).toBe('myapp')
    })

    it('parses all EntityTypes', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      expect(schema.entityTypes.size).toBe(3)
      expect(schema.entityTypes.has('Customer')).toBe(true)
      expect(schema.entityTypes.has('Order')).toBe(true)
      expect(schema.entityTypes.has('Account')).toBe(true)
    })

    it('parses all EnumTypes', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      expect(schema.enumTypes.size).toBe(1)
      expect(schema.enumTypes.has('CustomerStatus')).toBe(true)
    })

    it('parses EntityContainer', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      expect(schema.entityContainer).toBeDefined()
      expect(schema.entityContainer!.name).toBe('Container')
      expect(schema.entityContainer!.entitySets).toHaveLength(3)
    })
  })

  // =========================================================================
  // EntityType parsing
  // =========================================================================

  describe('EntityType parsing', () => {
    it('parses entity key', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      expect(customer.key).toEqual(['Id'])
    })

    it('parses entity name', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      expect(customer.name).toBe('Customer')
    })

    it('parses all properties', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      expect(customer.properties).toHaveLength(7)
      const names = customer.properties.map(p => p.name)
      expect(names).toContain('Id')
      expect(names).toContain('Name')
      expect(names).toContain('Email')
      expect(names).toContain('Balance')
      expect(names).toContain('Status')
      expect(names).toContain('IsActive')
      expect(names).toContain('CreatedAt')
    })

    it('parses all navigation properties', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      expect(customer.navigationProperties).toHaveLength(2)
      const navNames = customer.navigationProperties.map(n => n.name)
      expect(navNames).toContain('Orders')
      expect(navNames).toContain('Account')
    })
  })

  // =========================================================================
  // Property attributes
  // =========================================================================

  describe('property attributes', () => {
    it('parses type', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const idProp = customer.properties.find(p => p.name === 'Id')!
      expect(idProp.type).toBe('Edm.Guid')
    })

    it('parses nullable=false', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const idProp = customer.properties.find(p => p.name === 'Id')!
      expect(idProp.nullable).toBe(false)
    })

    it('defaults nullable to true when not specified', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const emailProp = customer.properties.find(p => p.name === 'Email')!
      expect(emailProp.nullable).toBe(true)
    })

    it('parses maxLength', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const nameProp = customer.properties.find(p => p.name === 'Name')!
      expect(nameProp.maxLength).toBe(100)
      const emailProp = customer.properties.find(p => p.name === 'Email')!
      expect(emailProp.maxLength).toBe(255)
    })

    it('leaves maxLength undefined when not specified', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const idProp = customer.properties.find(p => p.name === 'Id')!
      expect(idProp.maxLength).toBeUndefined()
    })

    it('parses precision and scale', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const balanceProp = customer.properties.find(p => p.name === 'Balance')!
      expect(balanceProp.precision).toBe(18)
      expect(balanceProp.scale).toBe(2)
    })

    it('leaves precision and scale undefined when not specified', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const nameProp = customer.properties.find(p => p.name === 'Name')!
      expect(nameProp.precision).toBeUndefined()
      expect(nameProp.scale).toBeUndefined()
    })

    it('parses defaultValue', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const statusProp = customer.properties.find(p => p.name === 'Status')!
      expect(statusProp.defaultValue).toBe('Active')
    })

    it('leaves defaultValue undefined when not specified', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const nameProp = customer.properties.find(p => p.name === 'Name')!
      expect(nameProp.defaultValue).toBeUndefined()
    })

    it('parses enum type reference', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const statusProp = customer.properties.find(p => p.name === 'Status')!
      expect(statusProp.type).toBe('myapp.CustomerStatus')
    })
  })

  // =========================================================================
  // NavigationProperty attributes
  // =========================================================================

  describe('navigation property attributes', () => {
    it('detects collection navigation property', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const ordersNav = customer.navigationProperties.find(n => n.name === 'Orders')!
      expect(ordersNav.isCollection).toBe(true)
      expect(ordersNav.type).toBe('Collection(myapp.Order)')
    })

    it('detects single-valued navigation property', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const accountNav = customer.navigationProperties.find(n => n.name === 'Account')!
      expect(accountNav.isCollection).toBe(false)
      expect(accountNav.type).toBe('myapp.Account')
    })

    it('parses partner attribute', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const ordersNav = customer.navigationProperties.find(n => n.name === 'Orders')!
      expect(ordersNav.partner).toBe('Customer')
    })

    it('leaves partner undefined when not specified', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const accountNav = customer.navigationProperties.find(n => n.name === 'Account')!
      expect(accountNav.partner).toBeUndefined()
    })

    it('parses referential constraints', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const accountNav = customer.navigationProperties.find(n => n.name === 'Account')!
      expect(accountNav.referentialConstraints).toHaveLength(1)
      expect(accountNav.referentialConstraints[0]).toEqual({
        property: 'AccountId',
        referencedProperty: 'Id',
      })
    })

    it('has empty referential constraints when none specified', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const ordersNav = customer.navigationProperties.find(n => n.name === 'Orders')!
      expect(ordersNav.referentialConstraints).toEqual([])
    })

    it('parses containsTarget=true', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const ordersNav = customer.navigationProperties.find(n => n.name === 'Orders')!
      expect(ordersNav.containsTarget).toBe(true)
    })

    it('defaults containsTarget to false', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const accountNav = customer.navigationProperties.find(n => n.name === 'Account')!
      expect(accountNav.containsTarget).toBe(false)
    })
  })

  // =========================================================================
  // EnumType parsing
  // =========================================================================

  describe('EnumType parsing', () => {
    it('parses enum name', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const enumType = schema.enumTypes.get('CustomerStatus')!
      expect(enumType.name).toBe('CustomerStatus')
    })

    it('parses enum members', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const enumType = schema.enumTypes.get('CustomerStatus')!
      expect(enumType.members).toHaveLength(2)
      expect(enumType.members[0]).toEqual({ name: 'Active', value: '1' })
      expect(enumType.members[1]).toEqual({ name: 'Inactive', value: '2' })
    })
  })

  // =========================================================================
  // EntityContainer parsing
  // =========================================================================

  describe('EntityContainer parsing', () => {
    it('parses container name', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      expect(schema.entityContainer!.name).toBe('Container')
    })

    it('parses entity sets', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const sets = schema.entityContainer!.entitySets
      expect(sets).toHaveLength(3)

      const customers = sets.find(s => s.name === 'Customers')!
      expect(customers.entityType).toBe('myapp.Customer')

      const orders = sets.find(s => s.name === 'Orders')!
      expect(orders.entityType).toBe('myapp.Order')

      const accounts = sets.find(s => s.name === 'Accounts')!
      expect(accounts.entityType).toBe('myapp.Account')
    })

    it('parses navigation property bindings', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customers = schema.entityContainer!.entitySets.find(s => s.name === 'Customers')!
      expect(customers.navigationPropertyBindings).toHaveLength(1)
      expect(customers.navigationPropertyBindings[0]).toEqual({
        path: 'Account',
        target: 'Accounts',
      })
    })

    it('has empty bindings when none specified', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const orders = schema.entityContainer!.entitySets.find(s => s.name === 'Orders')!
      expect(orders.navigationPropertyBindings).toEqual([])
    })
  })

  // =========================================================================
  // Annotation parsing
  // =========================================================================

  describe('annotation parsing', () => {
    it('parses Bool annotation on property', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const idProp = customer.properties.find(p => p.name === 'Id')!
      expect(idProp.annotations['Org.OData.Core.V1.Computed']).toBe(true)
    })

    it('parses String annotation', () => {
      const xml = `<?xml version="1.0" encoding="utf-8"?>
        <edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
          <edmx:DataServices>
            <Schema Namespace="test" xmlns="http://docs.oasis-open.org/odata/ns/edm">
              <EntityType Name="Foo">
                <Key><PropertyRef Name="Id"/></Key>
                <Property Name="Id" Type="Edm.Guid" Nullable="false">
                  <Annotation Term="Org.OData.Core.V1.Description" String="Primary key"/>
                </Property>
              </EntityType>
            </Schema>
          </edmx:DataServices>
        </edmx:Edmx>`
      const schema = MetadataParser.parseXml(xml)
      const foo = schema.entityTypes.get('Foo')!
      const idProp = foo.properties.find(p => p.name === 'Id')!
      expect(idProp.annotations['Org.OData.Core.V1.Description']).toBe('Primary key')
    })

    it('parses Int annotation', () => {
      const xml = `<?xml version="1.0" encoding="utf-8"?>
        <edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
          <edmx:DataServices>
            <Schema Namespace="test" xmlns="http://docs.oasis-open.org/odata/ns/edm">
              <EntityType Name="Foo">
                <Key><PropertyRef Name="Id"/></Key>
                <Property Name="Id" Type="Edm.Guid" Nullable="false">
                  <Annotation Term="Custom.Priority" Int="5"/>
                </Property>
              </EntityType>
            </Schema>
          </edmx:DataServices>
        </edmx:Edmx>`
      const schema = MetadataParser.parseXml(xml)
      const foo = schema.entityTypes.get('Foo')!
      const idProp = foo.properties.find(p => p.name === 'Id')!
      expect(idProp.annotations['Custom.Priority']).toBe(5)
    })

    it('parses EnumMember annotation', () => {
      const xml = `<?xml version="1.0" encoding="utf-8"?>
        <edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
          <edmx:DataServices>
            <Schema Namespace="test" xmlns="http://docs.oasis-open.org/odata/ns/edm">
              <EntityType Name="Foo">
                <Key><PropertyRef Name="Id"/></Key>
                <Property Name="Id" Type="Edm.Guid" Nullable="false">
                  <Annotation Term="Custom.Permission" EnumMember="Custom.Access/ReadWrite"/>
                </Property>
              </EntityType>
            </Schema>
          </edmx:DataServices>
        </edmx:Edmx>`
      const schema = MetadataParser.parseXml(xml)
      const foo = schema.entityTypes.get('Foo')!
      const idProp = foo.properties.find(p => p.name === 'Id')!
      expect(idProp.annotations['Custom.Permission']).toBe('Custom.Access/ReadWrite')
    })

    it('parses Collection annotation', () => {
      const xml = `<?xml version="1.0" encoding="utf-8"?>
        <edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
          <edmx:DataServices>
            <Schema Namespace="test" xmlns="http://docs.oasis-open.org/odata/ns/edm">
              <EntityType Name="Foo">
                <Key><PropertyRef Name="Id"/></Key>
                <Property Name="Id" Type="Edm.Guid" Nullable="false"/>
                <Annotation Term="Custom.Tags">
                  <Collection>
                    <String>tag1</String>
                    <String>tag2</String>
                  </Collection>
                </Annotation>
              </EntityType>
            </Schema>
          </edmx:DataServices>
        </edmx:Edmx>`
      const schema = MetadataParser.parseXml(xml)
      const foo = schema.entityTypes.get('Foo')!
      // The Collection annotation is parsed as an array from String text elements
      const tags = foo.annotations['Custom.Tags']
      expect(Array.isArray(tags)).toBe(true)
      expect(tags).toHaveLength(2)
    })

    it('parses Record annotation', () => {
      const xml = `<?xml version="1.0" encoding="utf-8"?>
        <edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
          <edmx:DataServices>
            <Schema Namespace="test" xmlns="http://docs.oasis-open.org/odata/ns/edm">
              <EntityType Name="Foo">
                <Key><PropertyRef Name="Id"/></Key>
                <Property Name="Id" Type="Edm.Guid" Nullable="false"/>
                <Annotation Term="Custom.DisplayInfo">
                  <Record>
                    <PropertyValue Property="Label" String="Identifier"/>
                    <PropertyValue Property="Order" Int="1"/>
                  </Record>
                </Annotation>
              </EntityType>
            </Schema>
          </edmx:DataServices>
        </edmx:Edmx>`
      const schema = MetadataParser.parseXml(xml)
      const foo = schema.entityTypes.get('Foo')!
      const displayInfo = foo.annotations['Custom.DisplayInfo'] as Record<string, unknown>
      expect(displayInfo).toBeDefined()
      expect(displayInfo.Label).toBe('Identifier')
      expect(displayInfo.Order).toBe(1)
    })

    it('parses standalone Annotations element', () => {
      const xml = `<?xml version="1.0" encoding="utf-8"?>
        <edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
          <edmx:DataServices>
            <Schema Namespace="test" xmlns="http://docs.oasis-open.org/odata/ns/edm">
              <EntityType Name="Foo">
                <Key><PropertyRef Name="Id"/></Key>
                <Property Name="Id" Type="Edm.Guid" Nullable="false"/>
              </EntityType>
              <Annotations Target="test.Foo">
                <Annotation Term="Org.OData.Core.V1.Description" String="A foo entity"/>
              </Annotations>
            </Schema>
          </edmx:DataServices>
        </edmx:Edmx>`
      const schema = MetadataParser.parseXml(xml)
      expect(schema.annotations).toHaveLength(1)
      expect(schema.annotations[0].target).toBe('test.Foo')
      expect(schema.annotations[0].term).toBe('Org.OData.Core.V1.Description')
      expect(schema.annotations[0].value).toBe('A foo entity')
    })
  })

  // =========================================================================
  // toEntityMetadata
  // =========================================================================

  describe('toEntityMetadata', () => {
    it('converts properties to fields with correct types', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const meta = MetadataParser.toEntityMetadata(customer, schema)

      expect(meta.name).toBe('Customer')
      expect(meta.namespace).toBe('myapp')
      expect(meta.keys).toEqual(['Id'])

      const idField = meta.fields.find(f => f.name === 'Id')!
      expect(idField.type).toBe('UUID')
      expect(idField.isRequired).toBe(true)
      expect(idField.isComputed).toBe(true)

      const nameField = meta.fields.find(f => f.name === 'Name')!
      expect(nameField.type).toBe('String')
      expect(nameField.isRequired).toBe(true)
      expect(nameField.maxLength).toBe(100)

      const emailField = meta.fields.find(f => f.name === 'Email')!
      expect(emailField.type).toBe('String')
      expect(emailField.isRequired).toBe(false)
      expect(emailField.maxLength).toBe(255)

      const balanceField = meta.fields.find(f => f.name === 'Balance')!
      expect(balanceField.type).toBe('Decimal')
      expect(balanceField.precision).toBe(18)
      expect(balanceField.scale).toBe(2)
    })

    it('maps enum fields and resolves enum values', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const meta = MetadataParser.toEntityMetadata(customer, schema)

      const statusField = meta.fields.find(f => f.name === 'Status')!
      expect(statusField.type).toBe('Enum')
      expect(statusField.defaultValue).toBe('Active')
      expect(statusField.enumValues).toBeDefined()
      expect(statusField.enumValues).toHaveLength(2)
      expect(statusField.enumValues![0]).toEqual({ name: 'Active', value: '1' })
      expect(statusField.enumValues![1]).toEqual({ name: 'Inactive', value: '2' })
    })

    it('marks Computed fields correctly', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const meta = MetadataParser.toEntityMetadata(customer, schema)

      const idField = meta.fields.find(f => f.name === 'Id')!
      expect(idField.isComputed).toBe(true)

      const nameField = meta.fields.find(f => f.name === 'Name')!
      expect(nameField.isComputed).toBe(false)
    })

    it('marks Immutable fields as readOnly', () => {
      const xml = `<?xml version="1.0" encoding="utf-8"?>
        <edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
          <edmx:DataServices>
            <Schema Namespace="test" xmlns="http://docs.oasis-open.org/odata/ns/edm">
              <EntityType Name="Foo">
                <Key><PropertyRef Name="Id"/></Key>
                <Property Name="Id" Type="Edm.Guid" Nullable="false">
                  <Annotation Term="Org.OData.Core.V1.Immutable" Bool="true"/>
                </Property>
              </EntityType>
            </Schema>
          </edmx:DataServices>
        </edmx:Edmx>`
      const schema = MetadataParser.parseXml(xml)
      const foo = schema.entityTypes.get('Foo')!
      const meta = MetadataParser.toEntityMetadata(foo, schema)

      const idField = meta.fields.find(f => f.name === 'Id')!
      expect(idField.isReadOnly).toBe(true)
    })

    it('converts navigation properties to associations', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const meta = MetadataParser.toEntityMetadata(customer, schema)

      expect(meta.associations).toHaveLength(2)

      const ordersAssoc = meta.associations.find(a => a.name === 'Orders')!
      expect(ordersAssoc.targetEntity).toBe('Order')
      expect(ordersAssoc.cardinality).toBe('Many')
      expect(ordersAssoc.isComposition).toBe(true)
      expect(ordersAssoc.foreignKey).toBeUndefined()

      const accountAssoc = meta.associations.find(a => a.name === 'Account')!
      expect(accountAssoc.targetEntity).toBe('Account')
      expect(accountAssoc.cardinality).toBe('ZeroOrOne')
      expect(accountAssoc.isComposition).toBe(false)
      expect(accountAssoc.foreignKey).toBe('AccountId')
    })

    it('preserves entity-level annotations', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const meta = MetadataParser.toEntityMetadata(customer, schema)
      expect(meta.annotations).toBeDefined()
      expect(typeof meta.annotations).toBe('object')
    })

    it('maps Boolean field type', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const meta = MetadataParser.toEntityMetadata(customer, schema)

      const isActiveField = meta.fields.find(f => f.name === 'IsActive')!
      expect(isActiveField.type).toBe('Boolean')
    })

    it('maps DateTimeOffset to Timestamp', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const meta = MetadataParser.toEntityMetadata(customer, schema)

      const createdAtField = meta.fields.find(f => f.name === 'CreatedAt')!
      expect(createdAtField.type).toBe('Timestamp')
    })
  })

  // =========================================================================
  // generateSmartFilterFields
  // =========================================================================

  describe('generateSmartFilterFields', () => {
    it('generates filter fields for non-computed properties', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const fields = MetadataParser.generateSmartFilterFields(customer, schema)

      // Id is Computed and should be skipped
      const idField = fields.find(f => f.name === 'Id')
      expect(idField).toBeUndefined()

      // Name should be present
      const nameField = fields.find(f => f.name === 'Name')
      expect(nameField).toBeDefined()
    })

    it('assigns text widget for String type', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const fields = MetadataParser.generateSmartFilterFields(customer, schema)

      const nameField = fields.find(f => f.name === 'Name')!
      expect(nameField.widgetType).toBe('text')
      expect(nameField.defaultOperator).toBe('contains')
    })

    it('assigns decimal widget for Decimal type', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const fields = MetadataParser.generateSmartFilterFields(customer, schema)

      const balanceField = fields.find(f => f.name === 'Balance')!
      expect(balanceField.widgetType).toBe('decimal')
      expect(balanceField.defaultOperator).toBe('eq')
    })

    it('assigns enum widget with enum values', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const fields = MetadataParser.generateSmartFilterFields(customer, schema)

      const statusField = fields.find(f => f.name === 'Status')!
      expect(statusField.widgetType).toBe('enum')
      expect(statusField.enumValues).toHaveLength(2)
      expect(statusField.enumValues![0]).toEqual({ name: 'Active', value: '1' })
    })

    it('assigns boolean widget for Boolean type', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const fields = MetadataParser.generateSmartFilterFields(customer, schema)

      const isActiveField = fields.find(f => f.name === 'IsActive')!
      expect(isActiveField.widgetType).toBe('boolean')
      expect(isActiveField.defaultOperator).toBe('eq')
    })

    it('assigns datetime widget for DateTimeOffset type', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const fields = MetadataParser.generateSmartFilterFields(customer, schema)

      const createdAtField = fields.find(f => f.name === 'CreatedAt')!
      expect(createdAtField.widgetType).toBe('datetime')
    })

    it('adds association fields for single-valued nav props with FK', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const fields = MetadataParser.generateSmartFilterFields(customer, schema)

      const accountField = fields.find(f => f.name === 'AccountId')!
      expect(accountField).toBeDefined()
      expect(accountField.widgetType).toBe('association')
      expect(accountField.associationTarget).toBe('Account')
      expect(accountField.label).toBe('Account')
      expect(accountField.defaultOperator).toBe('eq')
    })

    it('does not add association fields for collection nav props', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const fields = MetadataParser.generateSmartFilterFields(customer, schema)

      // Orders is a collection, should not appear
      const ordersField = fields.find(f => f.label === 'Orders' && f.widgetType === 'association')
      expect(ordersField).toBeUndefined()
    })

    it('skips non-filterable properties from FilterRestrictions', () => {
      const xml = `<?xml version="1.0" encoding="utf-8"?>
        <edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
          <edmx:DataServices>
            <Schema Namespace="test" xmlns="http://docs.oasis-open.org/odata/ns/edm">
              <EntityType Name="Foo">
                <Key><PropertyRef Name="Id"/></Key>
                <Property Name="Id" Type="Edm.Guid" Nullable="false"/>
                <Property Name="Name" Type="Edm.String" MaxLength="100"/>
                <Property Name="Internal" Type="Edm.String"/>
                <Annotation Term="Org.OData.Capabilities.V1.FilterRestrictions">
                  <Record>
                    <PropertyValue Property="NonFilterableProperties">
                      <Collection>
                        <String>Internal</String>
                      </Collection>
                    </PropertyValue>
                  </Record>
                </Annotation>
              </EntityType>
            </Schema>
          </edmx:DataServices>
        </edmx:Edmx>`
      const schema = MetadataParser.parseXml(xml)
      const foo = schema.entityTypes.get('Foo')!
      const fields = MetadataParser.generateSmartFilterFields(foo, schema)

      const internalField = fields.find(f => f.name === 'Internal')
      expect(internalField).toBeUndefined()

      // Name should still be present
      const nameField = fields.find(f => f.name === 'Name')
      expect(nameField).toBeDefined()
    })

    it('sets all filter fields as sortable by default', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const fields = MetadataParser.generateSmartFilterFields(customer, schema)

      for (const field of fields) {
        expect(field.sortable).toBe(true)
      }
    })

    it('uses property name as label by default', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const customer = schema.entityTypes.get('Customer')!
      const fields = MetadataParser.generateSmartFilterFields(customer, schema)

      const nameField = fields.find(f => f.name === 'Name')!
      expect(nameField.label).toBe('Name')
    })
  })

  // =========================================================================
  // EDM type mapping
  // =========================================================================

  describe('EDM type mapping', () => {
    function buildXmlWithProperty(type: string): string {
      return `<?xml version="1.0" encoding="utf-8"?>
        <edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
          <edmx:DataServices>
            <Schema Namespace="test" xmlns="http://docs.oasis-open.org/odata/ns/edm">
              <EntityType Name="TestEntity">
                <Key><PropertyRef Name="Id"/></Key>
                <Property Name="Id" Type="Edm.Guid" Nullable="false"/>
                <Property Name="TestField" Type="${type}"/>
              </EntityType>
            </Schema>
          </edmx:DataServices>
        </edmx:Edmx>`
    }

    function getTestFieldType(edmType: string): string {
      const schema = MetadataParser.parseXml(buildXmlWithProperty(edmType))
      const entity = schema.entityTypes.get('TestEntity')!
      const meta = MetadataParser.toEntityMetadata(entity, schema)
      const field = meta.fields.find(f => f.name === 'TestField')!
      return field.type
    }

    it('maps Edm.String to String', () => {
      expect(getTestFieldType('Edm.String')).toBe('String')
    })

    it('maps Edm.Int32 to Integer', () => {
      expect(getTestFieldType('Edm.Int32')).toBe('Integer')
    })

    it('maps Edm.Int16 to Integer', () => {
      expect(getTestFieldType('Edm.Int16')).toBe('Integer')
    })

    it('maps Edm.Int64 to Integer', () => {
      expect(getTestFieldType('Edm.Int64')).toBe('Integer')
    })

    it('maps Edm.Byte to Integer', () => {
      expect(getTestFieldType('Edm.Byte')).toBe('Integer')
    })

    it('maps Edm.SByte to Integer', () => {
      expect(getTestFieldType('Edm.SByte')).toBe('Integer')
    })

    it('maps Edm.Decimal to Decimal', () => {
      expect(getTestFieldType('Edm.Decimal')).toBe('Decimal')
    })

    it('maps Edm.Double to Decimal', () => {
      expect(getTestFieldType('Edm.Double')).toBe('Decimal')
    })

    it('maps Edm.Single to Decimal', () => {
      expect(getTestFieldType('Edm.Single')).toBe('Decimal')
    })

    it('maps Edm.Boolean to Boolean', () => {
      expect(getTestFieldType('Edm.Boolean')).toBe('Boolean')
    })

    it('maps Edm.Date to Date', () => {
      expect(getTestFieldType('Edm.Date')).toBe('Date')
    })

    it('maps Edm.TimeOfDay to Time', () => {
      expect(getTestFieldType('Edm.TimeOfDay')).toBe('Time')
    })

    it('maps Edm.DateTime to DateTime', () => {
      expect(getTestFieldType('Edm.DateTime')).toBe('DateTime')
    })

    it('maps Edm.DateTimeOffset to Timestamp', () => {
      expect(getTestFieldType('Edm.DateTimeOffset')).toBe('Timestamp')
    })

    it('maps Edm.Guid to UUID', () => {
      expect(getTestFieldType('Edm.Guid')).toBe('UUID')
    })

    it('maps Edm.Binary to Binary', () => {
      expect(getTestFieldType('Edm.Binary')).toBe('Binary')
    })

    it('maps unknown types to String as fallback', () => {
      expect(getTestFieldType('Edm.Unknown')).toBe('String')
    })
  })

  // =========================================================================
  // Cache behavior
  // =========================================================================

  describe('cache behavior', () => {
    it('caches parsed result and returns it on second call', async () => {
      const mockGetMetadata = vi.mocked(odataService.getMetadata)
      mockGetMetadata.mockResolvedValue(SAMPLE_CSDL)

      const schema1 = await MetadataParser.parse('testModule')
      const schema2 = await MetadataParser.parse('testModule')

      expect(schema1).toBe(schema2) // Same reference
      expect(mockGetMetadata).toHaveBeenCalledTimes(1)
    })

    it('fetches again after clearCache for specific module', async () => {
      const mockGetMetadata = vi.mocked(odataService.getMetadata)
      mockGetMetadata.mockResolvedValue(SAMPLE_CSDL)

      await MetadataParser.parse('testModule')
      MetadataParser.clearCache('testModule')
      await MetadataParser.parse('testModule')

      expect(mockGetMetadata).toHaveBeenCalledTimes(2)
    })

    it('fetches again after clearCache for all modules', async () => {
      const mockGetMetadata = vi.mocked(odataService.getMetadata)
      mockGetMetadata.mockResolvedValue(SAMPLE_CSDL)

      await MetadataParser.parse('module1')
      await MetadataParser.parse('module2')
      MetadataParser.clearCache()
      await MetadataParser.parse('module1')

      expect(mockGetMetadata).toHaveBeenCalledTimes(3)
    })

    it('bypasses cache with forceRefresh=true', async () => {
      const mockGetMetadata = vi.mocked(odataService.getMetadata)
      mockGetMetadata.mockResolvedValue(SAMPLE_CSDL)

      await MetadataParser.parse('testModule')
      await MetadataParser.parse('testModule', true)

      expect(mockGetMetadata).toHaveBeenCalledTimes(2)
    })

    it('caches different modules independently', async () => {
      const mockGetMetadata = vi.mocked(odataService.getMetadata)
      mockGetMetadata.mockResolvedValue(SAMPLE_CSDL)

      await MetadataParser.parse('module1')
      await MetadataParser.parse('module2')
      await MetadataParser.parse('module1') // should be cached

      expect(mockGetMetadata).toHaveBeenCalledTimes(2) // only module1 + module2
    })

    it('clearCache for one module does not affect others', async () => {
      const mockGetMetadata = vi.mocked(odataService.getMetadata)
      mockGetMetadata.mockResolvedValue(SAMPLE_CSDL)

      await MetadataParser.parse('module1')
      await MetadataParser.parse('module2')
      MetadataParser.clearCache('module1')

      await MetadataParser.parse('module1') // re-fetch
      await MetadataParser.parse('module2') // still cached

      expect(mockGetMetadata).toHaveBeenCalledTimes(3) // module1 (initial) + module2 + module1 (re-fetch)
    })
  })

  // =========================================================================
  // getAnnotation helper
  // =========================================================================

  describe('getAnnotation', () => {
    it('returns annotation value for matching target and term', () => {
      const xml = `<?xml version="1.0" encoding="utf-8"?>
        <edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
          <edmx:DataServices>
            <Schema Namespace="test" xmlns="http://docs.oasis-open.org/odata/ns/edm">
              <EntityType Name="Foo">
                <Key><PropertyRef Name="Id"/></Key>
                <Property Name="Id" Type="Edm.Guid" Nullable="false"/>
              </EntityType>
              <Annotations Target="test.Foo">
                <Annotation Term="Org.OData.Core.V1.Description" String="A foo entity"/>
              </Annotations>
            </Schema>
          </edmx:DataServices>
        </edmx:Edmx>`
      const schema = MetadataParser.parseXml(xml)
      const desc = MetadataParser.getAnnotation(schema, 'test.Foo', 'Org.OData.Core.V1.Description')
      expect(desc).toBe('A foo entity')
    })

    it('returns undefined for non-matching target', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const result = MetadataParser.getAnnotation(schema, 'nonexistent.Target', 'Org.OData.Core.V1.Description')
      expect(result).toBeUndefined()
    })
  })

  // =========================================================================
  // Edge cases
  // =========================================================================

  describe('edge cases', () => {
    it('handles XML with no EntityTypes', () => {
      const xml = `<?xml version="1.0" encoding="utf-8"?>
        <edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
          <edmx:DataServices>
            <Schema Namespace="empty" xmlns="http://docs.oasis-open.org/odata/ns/edm">
            </Schema>
          </edmx:DataServices>
        </edmx:Edmx>`
      const schema = MetadataParser.parseXml(xml)
      expect(schema.namespace).toBe('empty')
      expect(schema.entityTypes.size).toBe(0)
      expect(schema.enumTypes.size).toBe(0)
      expect(schema.entityContainer).toBeUndefined()
    })

    it('handles EntityType with no key', () => {
      const xml = `<?xml version="1.0" encoding="utf-8"?>
        <edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
          <edmx:DataServices>
            <Schema Namespace="test" xmlns="http://docs.oasis-open.org/odata/ns/edm">
              <EntityType Name="KeylessEntity">
                <Property Name="Name" Type="Edm.String"/>
              </EntityType>
            </Schema>
          </edmx:DataServices>
        </edmx:Edmx>`
      const schema = MetadataParser.parseXml(xml)
      const entity = schema.entityTypes.get('KeylessEntity')!
      expect(entity.key).toEqual([])
      expect(entity.properties).toHaveLength(1)
    })

    it('handles EntityType with composite key', () => {
      const xml = `<?xml version="1.0" encoding="utf-8"?>
        <edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
          <edmx:DataServices>
            <Schema Namespace="test" xmlns="http://docs.oasis-open.org/odata/ns/edm">
              <EntityType Name="CompositeKeyEntity">
                <Key>
                  <PropertyRef Name="Key1"/>
                  <PropertyRef Name="Key2"/>
                </Key>
                <Property Name="Key1" Type="Edm.Guid" Nullable="false"/>
                <Property Name="Key2" Type="Edm.String" Nullable="false"/>
              </EntityType>
            </Schema>
          </edmx:DataServices>
        </edmx:Edmx>`
      const schema = MetadataParser.parseXml(xml)
      const entity = schema.entityTypes.get('CompositeKeyEntity')!
      expect(entity.key).toEqual(['Key1', 'Key2'])
    })

    it('handles Order entity correctly from sample CSDL', () => {
      const schema = MetadataParser.parseXml(SAMPLE_CSDL)
      const order = schema.entityTypes.get('Order')!
      expect(order.key).toEqual(['Id'])
      expect(order.properties).toHaveLength(2)
      expect(order.navigationProperties).toHaveLength(1)

      const customerNav = order.navigationProperties[0]
      expect(customerNav.name).toBe('Customer')
      expect(customerNav.isCollection).toBe(false)
      expect(customerNav.partner).toBe('Orders')
    })
  })
})
