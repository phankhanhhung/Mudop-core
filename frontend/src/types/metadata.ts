// Metadata types for dynamic entity handling

export interface EntityMetadata {
  name: string
  namespace: string
  displayName?: string
  description?: string
  fields: FieldMetadata[]
  keys: string[]
  associations: AssociationMetadata[]
  annotations: Record<string, unknown>
  hasStream?: boolean
  isTemporal?: boolean
  isAbstract?: boolean
  isSingleton?: boolean
  parentEntityName?: string
  boundActions?: ActionMetadata[]
  boundFunctions?: FunctionMetadata[]
}

export interface FieldMetadata {
  name: string
  type: FieldType
  displayName?: string
  description?: string
  isRequired: boolean
  isReadOnly: boolean
  isComputed: boolean
  maxLength?: number
  precision?: number
  scale?: number
  defaultValue?: unknown
  enumValues?: EnumValue[]
  annotations: Record<string, unknown>
}

export type FieldType =
  | 'String'
  | 'Integer'
  | 'Decimal'
  | 'Boolean'
  | 'Date'
  | 'Time'
  | 'DateTime'
  | 'Timestamp'
  | 'UUID'
  | 'Binary'
  | 'Enum'
  | 'Array'

export interface EnumValue {
  name: string
  value: number | string
  displayName?: string
}

export interface AssociationMetadata {
  name: string
  targetEntity: string
  cardinality: Cardinality
  foreignKey?: string
  isComposition: boolean
}

export type Cardinality = 'ZeroOrOne' | 'One' | 'Many' | 'OneOrMore'

export interface ServiceMetadata {
  name: string
  namespace: string
  entities: EntitySetMetadata[]
  actions: ActionMetadata[]
  functions: FunctionMetadata[]
}

export interface EntitySetMetadata {
  name: string
  entityType: string
}

export interface ActionMetadata {
  name: string
  parameters: ParameterMetadata[]
  returnType?: string
  isBound: boolean
  bindingParameter?: string
}

export interface FunctionMetadata {
  name: string
  parameters: ParameterMetadata[]
  returnType: string
  isBound: boolean
  bindingParameter?: string
}

export interface ParameterMetadata {
  name: string
  type: FieldType
  isRequired: boolean
}

// Module metadata
export interface ModuleMetadata {
  name: string
  version: string
  description?: string
  services: ServiceMetadata[]
}
