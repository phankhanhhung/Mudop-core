export interface SequenceDefinition {
  name: string
  forEntity: string
  forField: string
  pattern: string
  scope: 'Tenant' | 'Company' | 'Global'
  resetOn: 'Never' | 'Monthly' | 'Yearly'
  startValue: number
  increment: number
  padding: number
  maxValue?: number
}

export interface SequenceValue {
  sequenceName: string
  currentValue: number
  formattedValue: string
}
