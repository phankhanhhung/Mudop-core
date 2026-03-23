import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import type { FieldMetadata } from '@/types/metadata'

export interface SmartFieldConfig {
  /** Whether the field should be visible in the UI */
  isVisible: boolean
  /** Whether the field is editable in the current mode */
  isEditable: boolean
  /** Whether the field is mandatory (required) */
  isMandatory: boolean
  /** Whether the field should render as a multiline textarea */
  isMultiLine: boolean
  /** Optional display format hint from annotations */
  displayFormat?: string
  /** How to arrange text and ID for association fields */
  textArrangement?: 'TextOnly' | 'TextFirst' | 'TextLast' | 'TextSeparate'
  /** Whether the field is a currency amount (dispatch to CurrencyField) */
  isCurrency: boolean
  /** ISO 4217 currency code from @Semantics.CurrencyCode annotation */
  currencyCode: string
  /** Whether the field has an input mask (dispatch to MaskedInput) */
  hasMask: boolean
  /** Mask pattern from @UI.InputMask annotation */
  maskPattern: string
  /** Whether the field is a date range picker (dispatch to DateRangeField) */
  isDateRange: boolean
  /** Whether the field is a multi-select (dispatch to MultiComboBox) */
  isMultiSelect: boolean
}

/**
 * Processes field metadata and annotations to determine field behavior
 * in a given mode (display, edit, or create).
 *
 * Annotation keys checked:
 * - `Org.OData.Core.V1.Computed` -- always readonly
 * - `Org.OData.Core.V1.Immutable` -- readonly in edit mode, editable in create
 * - `@UI.Hidden` -- field is hidden
 * - `@UI.MultiLineText` -- force textarea rendering
 * - `@UI.TextArrangement` -- association display arrangement
 * - `@UI.DisplayFormat` -- optional display format hint
 * - `@Semantics.CurrencyCode` -- currency field with ISO 4217 code
 * - `@UI.InputMask` -- masked input pattern
 * - `@UI.DateRange` -- date range picker
 * - `@UI.MultiSelect` -- multi-select combo box
 */
export function useSmartField(
  field: MaybeRefOrGetter<FieldMetadata>,
  mode: MaybeRefOrGetter<'edit' | 'display' | 'create'>,
  overrides?: {
    visible?: MaybeRefOrGetter<boolean | undefined>
    editable?: MaybeRefOrGetter<boolean | undefined>
    mandatory?: MaybeRefOrGetter<boolean | undefined>
  }
) {
  const config = computed<SmartFieldConfig>(() => {
    const f = toValue(field)
    const m = toValue(mode)
    const annotations = f.annotations ?? {}

    // --- Visibility ---
    const overrideVisible = overrides?.visible ? toValue(overrides.visible) : undefined
    const isHidden = Boolean(
      annotations['@UI.Hidden'] ??
      annotations['UI.Hidden'] ??
      false
    )
    const isVisible = overrideVisible !== undefined ? overrideVisible : !isHidden

    // --- Editability ---
    const overrideEditable = overrides?.editable ? toValue(overrides.editable) : undefined

    const isComputed = Boolean(
      annotations['Org.OData.Core.V1.Computed'] ??
      annotations['Core.Computed'] ??
      f.isComputed
    )

    const isImmutable = Boolean(
      annotations['Org.OData.Core.V1.Immutable'] ??
      annotations['Core.Immutable'] ??
      false
    )

    let isEditable: boolean
    if (overrideEditable !== undefined) {
      isEditable = overrideEditable
    } else if (m === 'display') {
      isEditable = false
    } else if (isComputed) {
      // Computed fields are never editable
      isEditable = false
    } else if (isImmutable) {
      // Immutable fields are editable only in create mode
      isEditable = m === 'create'
    } else if (f.isReadOnly) {
      isEditable = false
    } else {
      isEditable = true
    }

    // --- Mandatory ---
    const overrideMandatory = overrides?.mandatory ? toValue(overrides.mandatory) : undefined
    const isMandatory = overrideMandatory !== undefined ? overrideMandatory : f.isRequired

    // --- MultiLine ---
    const isMultiLine = Boolean(
      annotations['@UI.MultiLineText'] ??
      annotations['UI.MultiLineText'] ??
      false
    )

    // --- Display format ---
    const displayFormat = (
      annotations['@UI.DisplayFormat'] ??
      annotations['UI.DisplayFormat'] ??
      undefined
    ) as string | undefined

    // --- Text arrangement ---
    const textArrangement = (
      annotations['@UI.TextArrangement'] ??
      annotations['UI.TextArrangement'] ??
      undefined
    ) as SmartFieldConfig['textArrangement'] | undefined

    // --- Currency ---
    const currencyAnnotation = (
      annotations['@Semantics.CurrencyCode'] ??
      annotations['Semantics.CurrencyCode'] ??
      undefined
    ) as string | undefined
    const isCurrency = currencyAnnotation !== undefined
    const currencyCode = currencyAnnotation ?? 'USD'

    // --- Input mask ---
    const maskAnnotation = (
      annotations['@UI.InputMask'] ??
      annotations['UI.InputMask'] ??
      undefined
    ) as string | undefined
    const hasMask = maskAnnotation !== undefined
    const maskPattern = maskAnnotation ?? ''

    // --- Date range ---
    const isDateRange = Boolean(
      annotations['@UI.DateRange'] ??
      annotations['UI.DateRange'] ??
      false
    )

    // --- Multi-select ---
    const isMultiSelect = Boolean(
      annotations['@UI.MultiSelect'] ??
      annotations['UI.MultiSelect'] ??
      false
    )

    return {
      isVisible,
      isEditable,
      isMandatory,
      isMultiLine,
      displayFormat,
      textArrangement,
      isCurrency,
      currencyCode,
      hasMask,
      maskPattern,
      isDateRange,
      isMultiSelect,
    }
  })

  return config
}
