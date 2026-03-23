<script setup lang="ts">
import { computed } from 'vue'
import type { SmartColumnConfig } from '@/composables/useSmartTable'
import { Badge } from '@/components/ui/badge'
import ObjectStatus, { type ObjectStatusState } from './ObjectStatus.vue'
import ObjectNumber from './ObjectNumber.vue'
import InfoLabel from './InfoLabel.vue'
import { formatDate, formatDateTime, formatDecimal, formatInteger } from '@/utils/formatting'

interface Props {
  column: SmartColumnConfig
  value: unknown
  row: Record<string, unknown>
}

const props = defineProps<Props>()

const isNull = computed(() => props.value === null || props.value === undefined)

const displayValue = computed<string>(() => {
  if (isNull.value) return '-'

  // Use custom format function if provided
  if (props.column.format) {
    return props.column.format(props.value)
  }

  switch (props.column.type) {
    case 'UUID':
      return String(props.value)
    case 'Boolean':
      return '' // handled by badge template
    case 'Enum': {
      if (props.column.enumValues) {
        const match = props.column.enumValues.find((e) => e.value === props.value)
        return match?.displayName ?? match?.name ?? String(props.value)
      }
      return String(props.value)
    }
    case 'Date':
      return formatDate(props.value as string)
    case 'DateTime':
    case 'Timestamp':
      return formatDateTime(props.value as string)
    case 'Decimal':
      return typeof props.value === 'number' ? formatDecimal(props.value) : String(props.value)
    case 'Integer':
      return typeof props.value === 'number' ? formatInteger(props.value) : String(props.value)
    case 'String':
    default:
      return String(props.value)
  }
})

const truncatedUuid = computed(() => {
  if (props.column.type !== 'UUID' || isNull.value) return ''
  const str = String(props.value)
  return str.length > 8 ? str.substring(0, 8) + '...' : str
})

const isBoolean = computed(() => props.column.type === 'Boolean')
const booleanValue = computed(() => Boolean(props.value))
const isEnum = computed(() => props.column.type === 'Enum')
const isNumeric = computed(() => props.column.type === 'Integer' || props.column.type === 'Decimal')
const isUuid = computed(() => props.column.type === 'UUID')
const isLongString = computed(() => {
  if (props.column.type !== 'String' || isNull.value) return false
  return String(props.value).length > 50
})
const truncatedString = computed(() => {
  if (!isLongString.value) return displayValue.value
  return String(props.value).substring(0, 50) + '...'
})

// ── Semantic status for enum fields ──
// Maps common status-like enum values to ObjectStatus states
const enumStatusState = computed<ObjectStatusState | null>(() => {
  if (!isEnum.value || isNull.value || !props.column.enumValues) return null
  const match = props.column.enumValues.find((e) => e.value === props.value)
  if (!match) return null
  const name = (match.displayName ?? match.name).toLowerCase()
  if (/^(active|approved|completed|confirmed|success|paid|resolved|enabled|open)$/i.test(name)) return 'Success'
  if (/^(pending|draft|in.?progress|processing|review|hold|waiting)$/i.test(name)) return 'Warning'
  if (/^(inactive|rejected|failed|error|cancelled|canceled|closed|blocked|suspended|overdue|expired)$/i.test(name)) return 'Error'
  if (/^(new|info|created|scheduled|planned)$/i.test(name)) return 'Information'
  return null
})

// ── InfoLabel color scheme for non-status enum values ──
// Assigns a consistent color (1–10) based on the enum value's index position
const enumColorScheme = computed<1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10>(() => {
  if (!props.column.enumValues) return 1
  const idx = props.column.enumValues.findIndex((e) => e.value === props.value)
  return ((idx >= 0 ? idx : 0) % 10 + 1) as 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10
})

// ── Semantic state for numeric fields (via column config) ──
const numericUnit = computed(() => props.column.unit ?? undefined)
</script>

<template>
  <span v-if="isNull" class="text-muted-foreground">-</span>

  <!-- UUID: monospace, truncated with tooltip -->
  <span
    v-else-if="isUuid"
    class="font-mono text-xs text-muted-foreground"
    :title="String(value)"
  >
    {{ truncatedUuid }}
  </span>

  <!-- Boolean: badge -->
  <template v-else-if="isBoolean">
    <Badge v-if="booleanValue" variant="default" class="bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-200 border-green-200 dark:border-green-800">
      Yes
    </Badge>
    <Badge v-else variant="secondary">
      No
    </Badge>
  </template>

  <!-- Enum: ObjectStatus for status-like enums, plain Badge otherwise -->
  <ObjectStatus
    v-else-if="isEnum && enumStatusState"
    :text="displayValue"
    :state="enumStatusState"
    :inverted="true"
  />
  <InfoLabel
    v-else-if="isEnum"
    :text="displayValue"
    :colorScheme="enumColorScheme"
    :displayOnly="true"
    size="sm"
  />

  <!-- Numeric: ObjectNumber for numeric fields -->
  <ObjectNumber
    v-else-if="isNumeric && typeof value === 'number'"
    :number="value"
    :unit="numericUnit"
  />

  <!-- Long string: truncated with tooltip -->
  <span
    v-else-if="isLongString"
    :title="String(value)"
    class="truncate block"
  >
    {{ truncatedString }}
  </span>

  <!-- Default -->
  <span v-else>{{ displayValue }}</span>
</template>
