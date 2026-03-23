/**
 * CSDL Metadata Parser — parses OData $metadata XML into structured types.
 *
 * Features:
 * - Parse EntityType, NavigationProperty, Annotations from CSDL XML
 * - Build type registry: field types, constraints, annotations
 * - Extract OData annotations: Computed, Immutable, Capabilities.*
 * - Convert CSDL types to BMMDL FieldType
 * - Cache parsed results per module
 *
 * Inspired by OpenUI5's ODataMetaModel which parses $metadata on initialization.
 */

import type {
  CsdlSchema,
  CsdlEntityType,
  CsdlProperty,
  CsdlNavigationProperty,
  CsdlEnumType,
  CsdlEntityContainer,
  CsdlAnnotation,
  SmartFilterField,
  SmartFilterWidgetType,
} from './types'
import type { EntityMetadata, FieldMetadata, AssociationMetadata, FieldType } from '@/types/metadata'
import { odataService } from '@/services/odataService'

// ---------------------------------------------------------------------------
// EDM → FieldType mapping
// ---------------------------------------------------------------------------

const EDM_TYPE_MAP: Record<string, FieldType> = {
  'Edm.String': 'String',
  'Edm.Int16': 'Integer',
  'Edm.Int32': 'Integer',
  'Edm.Int64': 'Integer',
  'Edm.Decimal': 'Decimal',
  'Edm.Double': 'Decimal',
  'Edm.Single': 'Decimal',
  'Edm.Boolean': 'Boolean',
  'Edm.Date': 'Date',
  'Edm.TimeOfDay': 'Time',
  'Edm.DateTime': 'DateTime',
  'Edm.DateTimeOffset': 'Timestamp',
  'Edm.Guid': 'UUID',
  'Edm.Binary': 'Binary',
  'Edm.Byte': 'Integer',
  'Edm.SByte': 'Integer',
}

// ---------------------------------------------------------------------------
// Parsed result cache
// ---------------------------------------------------------------------------

const schemaCache = new Map<string, CsdlSchema>()

// ---------------------------------------------------------------------------
// MetadataParser
// ---------------------------------------------------------------------------

export class MetadataParser {
  /**
   * Parse $metadata CSDL XML for a module.
   * Results are cached per module name.
   */
  static async parse(module: string, forceRefresh = false): Promise<CsdlSchema> {
    if (!forceRefresh && schemaCache.has(module)) {
      return schemaCache.get(module)!
    }

    const xml = await odataService.getMetadata(module)
    const schema = MetadataParser.parseXml(xml)
    schemaCache.set(module, schema)
    return schema
  }

  /**
   * Parse CSDL XML string into structured schema.
   */
  static parseXml(xml: string): CsdlSchema {
    const parser = new DOMParser()
    const doc = parser.parseFromString(xml, 'application/xml')

    const schemaEl = doc.querySelector('Schema')
    const namespace = schemaEl?.getAttribute('Namespace') ?? ''

    const schema: CsdlSchema = {
      namespace,
      entityTypes: new Map(),
      enumTypes: new Map(),
      annotations: [],
    }

    // Parse EnumTypes
    const enumEls = doc.querySelectorAll('EnumType')
    for (const el of enumEls) {
      const enumType = MetadataParser.parseEnumType(el)
      schema.enumTypes.set(enumType.name, enumType)
    }

    // Parse EntityTypes
    const entityTypeEls = doc.querySelectorAll('EntityType')
    for (const el of entityTypeEls) {
      const entityType = MetadataParser.parseEntityType(el, namespace)
      schema.entityTypes.set(entityType.name, entityType)
    }

    // Parse EntityContainer
    const containerEl = doc.querySelector('EntityContainer')
    if (containerEl) {
      schema.entityContainer = MetadataParser.parseEntityContainer(containerEl)
    }

    // Parse standalone Annotations
    const annotationsEls = doc.querySelectorAll('Annotations')
    for (const el of annotationsEls) {
      const target = el.getAttribute('Target') ?? ''
      const annotationEls = el.querySelectorAll(':scope > Annotation')
      for (const annEl of annotationEls) {
        schema.annotations.push(MetadataParser.parseAnnotation(target, annEl))
      }
    }

    return schema
  }

  /**
   * Convert a CSDL EntityType to BMMDL EntityMetadata format.
   */
  static toEntityMetadata(
    entityType: CsdlEntityType,
    schema: CsdlSchema
  ): EntityMetadata {
    const fields: FieldMetadata[] = entityType.properties.map(prop => {
      const fieldType = MetadataParser.resolveFieldType(prop.type, schema)
      const isComputed = prop.annotations['Org.OData.Core.V1.Computed'] === true
      const isImmutable = prop.annotations['Org.OData.Core.V1.Immutable'] === true

      const field: FieldMetadata = {
        name: prop.name,
        type: fieldType,
        isRequired: !prop.nullable,
        isReadOnly: isImmutable,
        isComputed,
        maxLength: prop.maxLength,
        precision: prop.precision,
        scale: prop.scale,
        annotations: prop.annotations,
      }

      if (prop.defaultValue !== undefined) {
        field.defaultValue = prop.defaultValue
      }

      // Resolve enum values
      if (fieldType === 'Enum') {
        const enumType = schema.enumTypes.get(prop.type.replace(`${schema.namespace}.`, ''))
        if (enumType) {
          field.enumValues = enumType.members.map(m => ({
            name: m.name,
            value: m.value,
          }))
        }
      }

      return field
    })

    const associations: AssociationMetadata[] = entityType.navigationProperties.map(nav => ({
      name: nav.name,
      targetEntity: nav.type.replace(`Collection(${schema.namespace}.`, '').replace(`${schema.namespace}.`, '').replace(')', ''),
      cardinality: nav.isCollection ? 'Many' : 'ZeroOrOne',
      foreignKey: nav.referentialConstraints[0]?.property,
      isComposition: nav.containsTarget,
    }))

    return {
      name: entityType.name,
      namespace: schema.namespace,
      fields,
      keys: entityType.key,
      associations,
      annotations: entityType.annotations,
    }
  }

  /**
   * Generate SmartFilter field definitions from entity metadata.
   */
  static generateSmartFilterFields(
    entityType: CsdlEntityType,
    schema: CsdlSchema
  ): SmartFilterField[] {
    const fields: SmartFilterField[] = []

    for (const prop of entityType.properties) {
      // Skip computed and key fields from filter
      const isComputed = prop.annotations['Org.OData.Core.V1.Computed'] === true

      // Check filter restrictions
      const filterRestrictions = entityType.annotations['Org.OData.Capabilities.V1.FilterRestrictions'] as
        | { NonFilterableProperties?: string[] }
        | undefined
      const isNonFilterable = filterRestrictions?.NonFilterableProperties?.includes(prop.name)

      if (isComputed || isNonFilterable) continue

      const fieldType = MetadataParser.resolveFieldType(prop.type, schema)
      const widgetType = MetadataParser.fieldTypeToWidgetType(fieldType)

      const field: SmartFilterField = {
        name: prop.name,
        label: prop.name, // Can be overridden by UI annotations
        widgetType,
        filterable: !isNonFilterable,
        sortable: true,
        defaultOperator: MetadataParser.defaultOperatorForWidget(widgetType),
      }

      // Add enum values if applicable
      if (fieldType === 'Enum') {
        const enumType = schema.enumTypes.get(prop.type.replace(`${schema.namespace}.`, ''))
        if (enumType) {
          field.enumValues = enumType.members.map(m => ({
            name: m.name,
            value: m.value,
          }))
        }
      }

      fields.push(field)
    }

    // Add association fields for value-help style filtering
    for (const nav of entityType.navigationProperties) {
      if (!nav.isCollection && nav.referentialConstraints.length > 0) {
        fields.push({
          name: nav.referentialConstraints[0].property,
          label: nav.name,
          widgetType: 'association',
          filterable: true,
          sortable: true,
          associationTarget: nav.type.replace(`${schema.namespace}.`, ''),
          defaultOperator: 'eq',
        })
      }
    }

    return fields
  }

  /**
   * Get annotation value for a specific target and term.
   */
  static getAnnotation(schema: CsdlSchema, target: string, term: string): unknown | undefined {
    const ann = schema.annotations.find(a => a.target === target && a.term === term)
    return ann?.value
  }

  /**
   * Clear the metadata cache for a module (or all modules).
   */
  static clearCache(module?: string): void {
    if (module) {
      schemaCache.delete(module)
    } else {
      schemaCache.clear()
    }
  }

  // =========================================================================
  // Private parsers
  // =========================================================================

  private static parseEntityType(el: Element, _namespace: string): CsdlEntityType {
    const name = el.getAttribute('Name') ?? ''
    const key: string[] = []

    // Parse key
    const keyEl = el.querySelector('Key')
    if (keyEl) {
      const propRefEls = keyEl.querySelectorAll('PropertyRef')
      for (const ref of propRefEls) {
        const keyName = ref.getAttribute('Name')
        if (keyName) key.push(keyName)
      }
    }

    // Parse properties
    const properties: CsdlProperty[] = []
    const propEls = el.querySelectorAll(':scope > Property')
    for (const propEl of propEls) {
      properties.push(MetadataParser.parseProperty(propEl))
    }

    // Parse navigation properties
    const navigationProperties: CsdlNavigationProperty[] = []
    const navEls = el.querySelectorAll(':scope > NavigationProperty')
    for (const navEl of navEls) {
      navigationProperties.push(MetadataParser.parseNavigationProperty(navEl))
    }

    // Parse inline annotations
    const annotations: Record<string, unknown> = {}
    const annEls = el.querySelectorAll(':scope > Annotation')
    for (const annEl of annEls) {
      const term = annEl.getAttribute('Term') ?? ''
      annotations[term] = MetadataParser.parseAnnotationValue(annEl)
    }

    return { name, key, properties, navigationProperties, annotations }
  }

  private static parseProperty(el: Element): CsdlProperty {
    const annotations: Record<string, unknown> = {}
    const annEls = el.querySelectorAll(':scope > Annotation')
    for (const annEl of annEls) {
      const term = annEl.getAttribute('Term') ?? ''
      annotations[term] = MetadataParser.parseAnnotationValue(annEl)
    }

    return {
      name: el.getAttribute('Name') ?? '',
      type: el.getAttribute('Type') ?? 'Edm.String',
      nullable: el.getAttribute('Nullable') !== 'false',
      maxLength: el.hasAttribute('MaxLength')
        ? parseInt(el.getAttribute('MaxLength')!, 10)
        : undefined,
      precision: el.hasAttribute('Precision')
        ? parseInt(el.getAttribute('Precision')!, 10)
        : undefined,
      scale: el.hasAttribute('Scale')
        ? parseInt(el.getAttribute('Scale')!, 10)
        : undefined,
      defaultValue: el.getAttribute('DefaultValue') ?? undefined,
      annotations,
    }
  }

  private static parseNavigationProperty(el: Element): CsdlNavigationProperty {
    const type = el.getAttribute('Type') ?? ''
    const isCollection = type.startsWith('Collection(')

    const referentialConstraints: Array<{ property: string; referencedProperty: string }> = []
    const refConstraintEls = el.querySelectorAll('ReferentialConstraint')
    for (const rcEl of refConstraintEls) {
      referentialConstraints.push({
        property: rcEl.getAttribute('Property') ?? '',
        referencedProperty: rcEl.getAttribute('ReferencedProperty') ?? '',
      })
    }

    return {
      name: el.getAttribute('Name') ?? '',
      type,
      isCollection,
      partner: el.getAttribute('Partner') ?? undefined,
      referentialConstraints,
      containsTarget: el.getAttribute('ContainsTarget') === 'true',
    }
  }

  private static parseEnumType(el: Element): CsdlEnumType {
    const name = el.getAttribute('Name') ?? ''
    const members: Array<{ name: string; value: string | number }> = []

    const memberEls = el.querySelectorAll('Member')
    for (const memberEl of memberEls) {
      members.push({
        name: memberEl.getAttribute('Name') ?? '',
        value: memberEl.getAttribute('Value') ?? '',
      })
    }

    return { name, members }
  }

  private static parseEntityContainer(el: Element): CsdlEntityContainer {
    const name = el.getAttribute('Name') ?? ''
    const entitySets: CsdlEntityContainer['entitySets'] = []

    const entitySetEls = el.querySelectorAll('EntitySet')
    for (const esEl of entitySetEls) {
      const bindings: Array<{ path: string; target: string }> = []
      const bindingEls = esEl.querySelectorAll('NavigationPropertyBinding')
      for (const bEl of bindingEls) {
        bindings.push({
          path: bEl.getAttribute('Path') ?? '',
          target: bEl.getAttribute('Target') ?? '',
        })
      }

      entitySets.push({
        name: esEl.getAttribute('Name') ?? '',
        entityType: esEl.getAttribute('EntityType') ?? '',
        navigationPropertyBindings: bindings,
      })
    }

    return { name, entitySets }
  }

  private static parseAnnotation(target: string, el: Element): CsdlAnnotation {
    return {
      target,
      term: el.getAttribute('Term') ?? '',
      value: MetadataParser.parseAnnotationValue(el),
    }
  }

  private static parseAnnotationValue(el: Element): unknown {
    // Bool attribute shorthand
    if (el.hasAttribute('Bool')) {
      return el.getAttribute('Bool') === 'true'
    }
    // String attribute shorthand
    if (el.hasAttribute('String')) {
      return el.getAttribute('String')
    }
    // Int attribute shorthand
    if (el.hasAttribute('Int')) {
      return parseInt(el.getAttribute('Int')!, 10)
    }
    // EnumMember attribute shorthand
    if (el.hasAttribute('EnumMember')) {
      return el.getAttribute('EnumMember')
    }

    // Check for Collection
    const collectionEl = el.querySelector(':scope > Collection')
    if (collectionEl) {
      const items: unknown[] = []
      for (const child of collectionEl.children) {
        items.push(MetadataParser.parseAnnotationValue(child))
      }
      return items
    }

    // Check for Record
    const recordEl = el.querySelector(':scope > Record')
    if (recordEl) {
      return MetadataParser.parseRecordAnnotation(recordEl)
    }

    // Check for PropertyValue children (record without Record wrapper)
    const pvEls = el.querySelectorAll(':scope > PropertyValue')
    if (pvEls.length > 0) {
      const record: Record<string, unknown> = {}
      for (const pvEl of pvEls) {
        const propName = pvEl.getAttribute('Property') ?? ''
        record[propName] = MetadataParser.parseAnnotationValue(pvEl)
      }
      return record
    }

    // Simple text content
    return el.textContent?.trim() ?? true
  }

  private static parseRecordAnnotation(el: Element): Record<string, unknown> {
    const record: Record<string, unknown> = {}
    const pvEls = el.querySelectorAll(':scope > PropertyValue')
    for (const pvEl of pvEls) {
      const propName = pvEl.getAttribute('Property') ?? ''
      record[propName] = MetadataParser.parseAnnotationValue(pvEl)
    }
    return record
  }

  // =========================================================================
  // Type resolution
  // =========================================================================

  private static resolveFieldType(edmType: string, schema: CsdlSchema): FieldType {
    // Direct EDM type mapping
    if (EDM_TYPE_MAP[edmType]) {
      return EDM_TYPE_MAP[edmType]
    }

    // Collection type
    if (edmType.startsWith('Collection(')) {
      return 'Array'
    }

    // Enum type (namespace.EnumName)
    const enumName = edmType.replace(`${schema.namespace}.`, '')
    if (schema.enumTypes.has(enumName)) {
      return 'Enum'
    }

    // Default to String
    return 'String'
  }

  private static fieldTypeToWidgetType(fieldType: FieldType): SmartFilterWidgetType {
    switch (fieldType) {
      case 'String': return 'text'
      case 'Integer': return 'number'
      case 'Decimal': return 'decimal'
      case 'Boolean': return 'boolean'
      case 'Date': return 'date'
      case 'DateTime':
      case 'Timestamp': return 'datetime'
      case 'UUID': return 'uuid'
      case 'Enum': return 'enum'
      default: return 'text'
    }
  }

  private static defaultOperatorForWidget(widget: SmartFilterWidgetType): import('./types').FilterOperator {
    switch (widget) {
      case 'text': return 'contains'
      case 'number':
      case 'decimal': return 'eq'
      case 'boolean': return 'eq'
      case 'date':
      case 'datetime': return 'eq'
      case 'enum': return 'eq'
      case 'uuid': return 'eq'
      case 'association': return 'eq'
      default: return 'eq'
    }
  }
}
