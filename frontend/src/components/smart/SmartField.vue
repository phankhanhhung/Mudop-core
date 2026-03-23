<script setup lang="ts">
import { ref, computed, getCurrentInstance, type Component } from 'vue'
import { Label } from '@/components/ui/label'
import { Input } from '@/components/ui/input'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Search, X } from 'lucide-vue-next'
import StringField from '@/components/entity/fields/StringField.vue'
import NumberField from '@/components/entity/fields/NumberField.vue'
import BooleanField from '@/components/entity/fields/BooleanField.vue'
import DateField from '@/components/entity/fields/DateField.vue'
import EnumField from '@/components/entity/fields/EnumField.vue'
import UuidField from '@/components/entity/fields/UuidField.vue'
import TimePickerField from '@/components/entity/fields/TimePickerField.vue'
import DateRangeField from '@/components/entity/fields/DateRangeField.vue'
import MultiComboBox from '@/components/entity/fields/MultiComboBox.vue'
import CurrencyField from '@/components/entity/fields/CurrencyField.vue'
import MaskedInput from '@/components/entity/fields/MaskedInput.vue'
import TokenInput from '@/components/smart/TokenInput.vue'
import FileReferenceField from '@/components/entity/fields/FileReferenceField.vue'
import UploadCollection from '@/components/smart/UploadCollection.vue'
import ValueHelpDialog from '@/components/smart/ValueHelpDialog.vue'
import { fileService } from '@/services/fileService'
import { useSmartField } from '@/composables/useSmartField'
import { formatCurrency } from '@/utils/formatting'
import type { FieldMetadata, AssociationMetadata } from '@/types/metadata'

interface Props {
  field: FieldMetadata
  modelValue?: unknown
  mode?: 'edit' | 'display' | 'create'
  module?: string
  entitySet?: string
  /** Association metadata when this field is an FK for an association */
  association?: AssociationMetadata
  /** Display label resolved for the current FK value (e.g. "Acme Corp") */
  associationDisplayValue?: string
  // Annotation overrides
  visible?: boolean
  editable?: boolean
  mandatory?: boolean
  /** Show field label in display mode (default: true). Edit mode always shows label. */
  showLabel?: boolean
  // Value help
  valueHelpEnabled?: boolean
  // Validation
  error?: string
}

const props = withDefaults(defineProps<Props>(), {
  mode: 'edit',
  valueHelpEnabled: true,
  showLabel: true,
})

const emit = defineEmits<{
  'update:modelValue': [value: unknown]
}>()

// --------------------------------------------------------------------------
// Smart field config (annotation-driven behavior)
// --------------------------------------------------------------------------
// Vue's boolean casting makes absent boolean props = false instead of undefined.
// Detect which override props were actually passed by the parent template so we
// only forward genuine overrides to useSmartField.
const _vnodeProps = getCurrentInstance()?.vnode?.props ?? {}
const _hasVisible = 'visible' in _vnodeProps
const _hasEditable = 'editable' in _vnodeProps
const _hasMandatory = 'mandatory' in _vnodeProps

const smartConfig = useSmartField(
  computed(() => props.field),
  computed(() => props.mode),
  {
    visible: _hasVisible ? computed(() => props.visible) : undefined,
    editable: _hasEditable ? computed(() => props.editable) : undefined,
    mandatory: _hasMandatory ? computed(() => props.mandatory) : undefined,
  }
)

// --------------------------------------------------------------------------
// Value help dialog state
// --------------------------------------------------------------------------
const showValueHelp = ref(false)

// --------------------------------------------------------------------------
// Computed helpers
// --------------------------------------------------------------------------
const isAssociationField = computed(() => {
  return props.association !== undefined && props.valueHelpEnabled
})

const associationTarget = computed(() => {
  return props.association?.targetEntity ?? ''
})

const isRequired = computed(() => smartConfig.value.isMandatory)
const isEditable = computed(() => smartConfig.value.isEditable)
const isFileReference = computed(() => props.field.type === 'FileReference' || props.field.name.endsWith('Attachment') || props.field.name.endsWith('File'))

// Array<String> without enum values → use TokenInput for free-form string arrays
const isArrayStringField = computed(() => {
  return props.field.type === 'Array' && !smartConfig.value.isMultiSelect && !props.field.enumValues?.length
})

// UploadCollection adapter: wraps fileService.upload for the UploadCollection uploadFn signature
const uploadFn = computed(() => {
  if (!isFileReference.value || !props.module || !props.entitySet) return undefined
  return async (file: File, onProgress: (pct: number) => void) => {
    onProgress(10)
    const result = await fileService.upload(
      props.module!,
      props.entitySet!,
      String(props.modelValue ?? ''),
      props.field.name,
      file
    )
    onProgress(100)
    return result.key ?? ''
  }
})

/** The underlying edit component for this field type */
const fieldComponent = computed<Component | null>(() => {
  const sc = smartConfig.value

  // Annotation-driven dispatch takes priority
  if (sc.isCurrency) return CurrencyField
  if (sc.hasMask) return MaskedInput
  if (sc.isDateRange) return DateRangeField
  if (sc.isMultiSelect) return MultiComboBox

  // MultiLine override for strings
  if (sc.isMultiLine && props.field.type === 'String') {
    return StringField
  }

  switch (props.field.type) {
    case 'String':
      return StringField
    case 'Integer':
    case 'Decimal':
      return NumberField
    case 'Boolean':
      return BooleanField
    case 'Date':
    case 'DateTime':
    case 'Timestamp':
      return DateField
    case 'Time':
      return TimePickerField
    case 'Enum':
      return EnumField
    case 'UUID':
      return UuidField
    case 'Array':
      // Use TokenInput for string arrays without enum options
      if (!props.field.enumValues?.length) return TokenInput
      return MultiComboBox
    default:
      return StringField
  }
})

/** Extra props to pass to annotation-driven field components */
const extraFieldProps = computed<Record<string, unknown>>(() => {
  const sc = smartConfig.value
  const extra: Record<string, unknown> = {}
  if (sc.isCurrency) extra.currencyCode = sc.currencyCode
  if (sc.hasMask) extra.maskPattern = sc.maskPattern
  return extra
})

// --------------------------------------------------------------------------
// Display mode formatting
// --------------------------------------------------------------------------
const displayValue = computed<string>(() => {
  const val = props.modelValue

  if (val === null || val === undefined || val === '') {
    return '-'
  }

  const sc = smartConfig.value

  // For association FK fields, prefer the resolved display name
  if (isAssociationField.value && props.associationDisplayValue) {
    return props.associationDisplayValue
  }

  // Currency (annotation-driven) — must check before Decimal type
  if (sc.isCurrency && typeof val === 'number') {
    return formatCurrency(val, sc.currencyCode)
  }

  // Masked input — display formatted value
  if (sc.hasMask && typeof val === 'string') {
    const builtInPatterns: Record<string, string> = {
      phone: '(###) ###-####',
      creditCard: '#### #### #### ####',
      postalCode: '#####',
      ssn: '###-##-####'
    }
    const pattern = builtInPatterns[sc.maskPattern] ?? sc.maskPattern
    if (pattern) {
      let result = ''
      let rawIdx = 0
      for (let i = 0; i < pattern.length && rawIdx < val.length; i++) {
        const slot = pattern[i]
        if (slot === '#' || slot === 'A' || slot === '*') {
          result += val[rawIdx++]
        } else {
          result += slot
        }
      }
      return result
    }
  }

  // Date range — display formatted range
  if (sc.isDateRange && typeof val === 'object' && val !== null) {
    const range = val as { from?: string; to?: string }
    const fmt = (s: string) => {
      try { return new Date(s + 'T00:00:00').toLocaleDateString('default', { month: 'short', day: 'numeric', year: 'numeric' }) }
      catch { return s }
    }
    if (range.from && range.to) return `${fmt(range.from)} \u2013 ${fmt(range.to)}`
    if (range.from) return `From ${fmt(range.from)}`
    if (range.to) return `To ${fmt(range.to)}`
    return '-'
  }

  // Multi-select — join labels
  if (sc.isMultiSelect && Array.isArray(val)) {
    if (val.length === 0) return '-'
    return val.map((v) => {
      const enumVal = props.field.enumValues?.find(
        (e) => e.value === v || String(e.value) === String(v)
      )
      return enumVal?.displayName ?? enumVal?.name ?? String(v)
    }).join(', ')
  }

  // Boolean
  if (props.field.type === 'Boolean') {
    return val ? 'Yes' : 'No'
  }

  // Enum
  if (props.field.type === 'Enum' && props.field.enumValues) {
    const enumVal = props.field.enumValues.find(
      (e) => e.value === val || String(e.value) === String(val)
    )
    return enumVal?.displayName ?? enumVal?.name ?? String(val)
  }

  // Date/DateTime formatting
  if (
    (props.field.type === 'Date' ||
      props.field.type === 'DateTime' ||
      props.field.type === 'Timestamp') &&
    typeof val === 'string'
  ) {
    try {
      const date = new Date(val)
      if (props.field.type === 'Date') {
        return date.toLocaleDateString()
      }
      return date.toLocaleString()
    } catch {
      return String(val)
    }
  }

  // Time
  if (props.field.type === 'Time' && typeof val === 'string') {
    return val
  }

  // Decimal formatting
  if (props.field.type === 'Decimal' && typeof val === 'number') {
    return val.toLocaleString(undefined, {
      minimumFractionDigits: props.field.scale ?? 0,
      maximumFractionDigits: props.field.scale ?? 2,
    })
  }

  return String(val)
})

const truncatedUuid = computed(() => {
  const val = props.modelValue
  if (typeof val !== 'string') return String(val ?? '-')
  if (val.length > 20) return val.substring(0, 8) + '...'
  return val
})

const booleanBadgeVariant = computed<'default' | 'secondary'>(() => {
  return props.modelValue ? 'default' : 'secondary'
})

// --------------------------------------------------------------------------
// MultiLine field override: produce a patched FieldMetadata that forces textarea
// --------------------------------------------------------------------------
const effectiveField = computed<FieldMetadata>(() => {
  if (
    smartConfig.value.isMultiLine &&
    props.field.type === 'String' &&
    (props.field.maxLength === undefined || props.field.maxLength <= 255)
  ) {
    return {
      ...props.field,
      maxLength: 256, // StringField uses textarea when maxLength > 255
    }
  }
  return props.field
})

// --------------------------------------------------------------------------
// Event handlers
// --------------------------------------------------------------------------
function handleUpdate(value: unknown) {
  emit('update:modelValue', value)
}

function handleValueHelpSelect(selected: { key: string; label: string }) {
  emit('update:modelValue', selected.key)
}

function clearValue() {
  emit('update:modelValue', null)
}
</script>

<template>
  <div v-if="smartConfig.isVisible">
    <!-- ================================================================ -->
    <!-- Display mode: formatted read-only value                          -->
    <!-- ================================================================ -->
    <div v-if="mode === 'display'" class="space-y-1">
      <Label v-if="showLabel" class="text-xs font-medium text-muted-foreground uppercase tracking-wider">
        {{ field.displayName || field.name }}
      </Label>
      <div class="text-sm">
        <!-- Boolean -->
        <Badge
          v-if="field.type === 'Boolean'"
          :variant="booleanBadgeVariant"
        >
          {{ displayValue }}
        </Badge>
        <!-- Enum -->
        <Badge
          v-else-if="field.type === 'Enum' && !smartConfig.isMultiSelect"
          variant="secondary"
        >
          {{ displayValue }}
        </Badge>
        <!-- Multi-select display -->
        <div
          v-else-if="smartConfig.isMultiSelect && Array.isArray(modelValue)"
          class="flex flex-wrap gap-1"
        >
          <Badge
            v-for="v in (modelValue as (string | number)[])"
            :key="String(v)"
            variant="secondary"
          >
            {{ field.enumValues?.find(e => e.value === v || String(e.value) === String(v))?.displayName ?? field.enumValues?.find(e => e.value === v || String(e.value) === String(v))?.name ?? String(v) }}
          </Badge>
          <span v-if="(modelValue as unknown[]).length === 0" class="text-muted-foreground">-</span>
        </div>
        <!-- Array<String> tokens display -->
        <div
          v-else-if="isArrayStringField && Array.isArray(modelValue)"
          class="flex flex-wrap gap-1"
        >
          <Badge
            v-for="(v, idx) in (modelValue as string[])"
            :key="idx"
            variant="secondary"
          >
            {{ v }}
          </Badge>
          <span v-if="(modelValue as string[]).length === 0" class="text-muted-foreground">-</span>
        </div>
        <!-- UUID -->
        <span
          v-else-if="field.type === 'UUID'"
          class="font-mono text-xs text-muted-foreground"
          :title="String(modelValue ?? '')"
        >
          {{ truncatedUuid }}
        </span>
        <!-- Default -->
        <span v-else>{{ displayValue }}</span>
      </div>
    </div>

    <!-- ================================================================ -->
    <!-- Edit / Create mode                                               -->
    <!-- ================================================================ -->
    <div v-else class="space-y-1">
      <!-- Association FK field with value help trigger (needs its own Label since it uses raw Input) -->
      <template v-if="isAssociationField">
        <Label :for="field.name">
          {{ field.displayName || field.name }}
          <span v-if="isRequired" class="text-destructive">*</span>
        </Label>
      </template>

      <div v-if="isAssociationField" class="flex gap-2">
        <Input
          :modelValue="associationDisplayValue || String(modelValue ?? '')"
          :readonly="true"
          :placeholder="`Select ${association?.targetEntity ?? ''}...`"
          class="flex-1"
        />
        <Button
          v-if="isEditable"
          size="icon"
          variant="outline"
          type="button"
          @click="showValueHelp = true"
        >
          <Search class="h-4 w-4" />
        </Button>
        <Button
          v-if="modelValue && isEditable"
          size="icon"
          variant="ghost"
          type="button"
          @click="clearValue"
        >
          <X class="h-4 w-4" />
        </Button>
      </div>

      <!-- FileReference: UploadCollection with drag-drop -->
      <UploadCollection
        v-else-if="isFileReference && isEditable"
        :title="field.displayName || field.name"
        :maxFiles="1"
        :uploadFn="uploadFn"
        @upload-complete="() => {}"
      />

      <!-- Array<String> without enum values: TokenInput for free-form tags -->
      <TokenInput
        v-else-if="isArrayStringField"
        :modelValue="(Array.isArray(modelValue) ? modelValue : []) as string[]"
        :label="field.displayName || field.name"
        :disabled="!isEditable"
        :readonly="!isEditable"
        :placeholder="`Type and press Enter`"
        @update:modelValue="handleUpdate"
      />

      <!-- Standard field types - delegate to existing field components -->
      <component
        v-else
        :is="fieldComponent"
        :field="effectiveField"
        :modelValue="modelValue"
        :readonly="!isEditable"
        v-bind="extraFieldProps"
        @update:modelValue="handleUpdate"
      />

      <!-- Help text -->
      <p v-if="field.description" class="text-xs text-muted-foreground">
        {{ field.description }}
      </p>

      <!-- Validation error -->
      <p v-if="error" class="text-sm text-destructive">
        {{ error }}
      </p>

      <!-- Value help dialog -->
      <ValueHelpDialog
        v-if="isAssociationField"
        v-model:open="showValueHelp"
        :module="module ?? ''"
        :targetEntity="associationTarget"
        @select="handleValueHelpSelect"
      />
    </div>
  </div>
</template>
