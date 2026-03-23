import type { FieldType } from '@/types/metadata'

export interface FormLayoutField {
  name: string
  width: 'full' | 'half' | 'third'
  visible: boolean
}

export interface FormLayoutSection {
  id: string
  title: string
  icon?: string
  collapsed: boolean
  fields: FormLayoutField[]
}

export interface FormLayoutSettings {
  version: 1
  sections: FormLayoutSection[]
  columns: 1 | 2 | 3
}

export interface DesignerField {
  name: string
  displayName: string
  type: FieldType
  isRequired: boolean
  isKey: boolean
  width: 'full' | 'half' | 'third'
}

export interface DesignerSection {
  id: string
  title: string
  icon?: string
  collapsed: boolean
  fields: DesignerField[]
}
