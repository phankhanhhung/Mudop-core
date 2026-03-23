<script setup lang="ts">
import { computed } from 'vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { X } from 'lucide-vue-next'
import type { FilterCondition, FilterOperator } from '@/types/odata'
import type { FieldMetadata, EnumValue } from '@/types/metadata'

interface Props {
  filters: FilterCondition[]
  fields: FieldMetadata[]
}

const props = defineProps<Props>()

const emit = defineEmits<{
  remove: [field: string]
  'clear-all': []
}>()

// Group filters by field so "between" (ge + le on same field) shows as one chip
interface FilterChip {
  field: string
  displayName: string
  description: string
}

const operatorLabels: Record<FilterOperator, string> = {
  eq: 'equals',
  ne: 'not equals',
  gt: 'greater than',
  ge: 'at least',
  lt: 'less than',
  le: 'at most',
  contains: 'contains',
  startswith: 'starts with',
  endswith: 'ends with'
}

function getFieldDisplayName(fieldName: string): string {
  const field = props.fields.find((f) => f.name === fieldName)
  if (field?.displayName) return field.displayName
  // Convert PascalCase/camelCase to spaced form
  return fieldName.replace(/([A-Z])/g, ' $1').trim()
}

function getEnumLabel(fieldName: string, value: unknown): string {
  const field = props.fields.find((f) => f.name === fieldName)
  if (!field?.enumValues) return String(value)
  const enumVal = field.enumValues.find(
    (ev: EnumValue) => String(ev.value) === String(value) || ev.name === String(value)
  )
  return enumVal?.displayName ?? enumVal?.name ?? String(value)
}

function formatValue(fieldName: string, value: unknown): string {
  const field = props.fields.find((f) => f.name === fieldName)
  if (field?.type === 'Enum') {
    return getEnumLabel(fieldName, value)
  }
  if (field?.type === 'Boolean') {
    return value === true || value === 'true' ? 'Yes' : 'No'
  }
  return String(value ?? '')
}

const chips = computed<FilterChip[]>(() => {
  // Group by field
  const grouped = new Map<string, FilterCondition[]>()
  for (const filter of props.filters) {
    const existing = grouped.get(filter.field) ?? []
    existing.push(filter)
    grouped.set(filter.field, existing)
  }

  const result: FilterChip[] = []
  for (const [field, conditions] of grouped) {
    const displayName = getFieldDisplayName(field)

    // Check for "between" pattern: one ge and one le for the same field
    const geCondition = conditions.find((c) => c.operator === 'ge')
    const leCondition = conditions.find((c) => c.operator === 'le')

    if (conditions.length === 2 && geCondition && leCondition) {
      result.push({
        field,
        displayName,
        description: `between ${formatValue(field, geCondition.value)} and ${formatValue(field, leCondition.value)}`
      })
    } else {
      // Render each condition individually
      for (const condition of conditions) {
        const opLabel = operatorLabels[condition.operator] ?? condition.operator
        result.push({
          field,
          displayName,
          description: `${opLabel} ${formatValue(field, condition.value)}`
        })
      }
    }
  }

  return result
})

const showClearAll = computed(() => {
  const uniqueFields = new Set(props.filters.map((f) => f.field))
  return uniqueFields.size >= 2
})
</script>

<template>
  <div v-if="filters.length > 0" class="flex flex-wrap items-center gap-2">
    <Badge
      v-for="chip in chips"
      :key="`${chip.field}-${chip.description}`"
      variant="secondary"
      class="flex items-center gap-1 pl-2.5 pr-1 py-1"
    >
      <span class="text-xs">
        <span class="font-semibold">{{ chip.displayName }}</span>
        {{ ' ' }}
        <span class="text-muted-foreground">{{ chip.description }}</span>
      </span>
      <button
        class="ml-0.5 rounded-full p-0.5 hover:bg-muted-foreground/20 transition-colors"
        :aria-label="`Remove filter: ${chip.displayName}`"
        @click="emit('remove', chip.field)"
      >
        <X class="h-3 w-3" aria-hidden="true" />
      </button>
    </Badge>

    <Button
      v-if="showClearAll"
      variant="ghost"
      size="sm"
      class="h-6 px-2 text-xs text-muted-foreground"
      @click="emit('clear-all')"
    >
      Clear all
    </Button>
  </div>
</template>
