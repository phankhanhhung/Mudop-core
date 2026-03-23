<script setup lang="ts">
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Select } from '@/components/ui/select'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Label } from '@/components/ui/label'
import {
  Play,
  Plus,
  X,
  Layers,
  Calculator,
  Filter as FilterIcon,
  ChevronDown,
  ChevronUp
} from 'lucide-vue-next'
import type { FieldMetadata } from '@/types/metadata'
import type { AggregateFunction, AggregationConfig, AggregationItem } from '@/types/aggregation'

interface Props {
  fields: FieldMetadata[]
  isLoading?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  isLoading: false
})

const emit = defineEmits<{
  execute: [config: AggregationConfig]
}>()

const { t } = useI18n()

// Group By state
const selectedGroupByField = ref('')
const groupByFields = ref<string[]>([])

// Aggregation items
const aggregations = ref<AggregationItem[]>([])
let aggregationCounter = 0

// Filter
const showFilter = ref(false)
const filterExpression = ref('')

// Numeric fields for aggregate targets
const numericFields = computed(() =>
  props.fields.filter((f) => f.type === 'Integer' || f.type === 'Decimal')
)

const aggregateFunctions: { value: AggregateFunction; label: string }[] = [
  { value: 'count', label: 'Count' },
  { value: 'countdistinct', label: 'Count Distinct' },
  { value: 'sum', label: 'Sum' },
  { value: 'avg', label: 'Average' },
  { value: 'min', label: 'Min' },
  { value: 'max', label: 'Max' }
]

// Available fields for group by (exclude already selected)
const availableGroupByFields = computed(() =>
  props.fields.filter(f => !groupByFields.value.includes(f.name))
)

function addGroupByField() {
  if (selectedGroupByField.value && !groupByFields.value.includes(selectedGroupByField.value)) {
    groupByFields.value = [...groupByFields.value, selectedGroupByField.value]
    selectedGroupByField.value = ''
  }
}

function removeGroupByField(field: string) {
  groupByFields.value = groupByFields.value.filter(f => f !== field)
}

function getFieldDisplayName(fieldName: string): string {
  const field = props.fields.find(f => f.name === fieldName)
  return field?.displayName || fieldName
}

function addAggregation() {
  aggregationCounter++
  const newItem: AggregationItem = {
    id: crypto.randomUUID(),
    func: 'count',
    field: '',
    alias: `Value${aggregationCounter}`
  }
  aggregations.value = [...aggregations.value, newItem]
}

function removeAggregation(id: string) {
  aggregations.value = aggregations.value.filter(a => a.id !== id)
}

function updateAggregation(id: string, updates: Partial<AggregationItem>) {
  aggregations.value = aggregations.value.map(a =>
    a.id === id ? { ...a, ...updates } : a
  )
  // If function changed to count, clear field
  const agg = aggregations.value.find(a => a.id === id)
  if (agg && updates.func === 'count') {
    aggregations.value = aggregations.value.map(a =>
      a.id === id ? { ...a, field: '' } : a
    )
  }
}

function needsField(func: AggregateFunction): boolean {
  return func !== 'count'
}

const canExecute = computed(() => {
  if (groupByFields.value.length === 0) return false
  if (aggregations.value.length === 0) return false
  for (const agg of aggregations.value) {
    if (needsField(agg.func) && !agg.field) return false
    if (!agg.alias.trim()) return false
  }
  return true
})

function handleExecute() {
  if (!canExecute.value) return
  emit('execute', {
    groupByFields: [...groupByFields.value],
    aggregations: aggregations.value.map(a => ({ ...a })),
    filter: filterExpression.value.trim() || undefined
  })
}

function applyConfig(cfg: AggregationConfig) {
  groupByFields.value = [...cfg.groupByFields]
  aggregations.value = cfg.aggregations.map(a => ({ ...a }))
  filterExpression.value = cfg.filter || ''
}

defineExpose({ applyConfig })
</script>

<template>
  <div class="space-y-5" aria-label="Aggregation configuration">
    <!-- Group By Section -->
    <div role="group" aria-labelledby="group-by-heading">
      <div class="flex items-center gap-2 mb-3">
        <div class="h-7 w-7 rounded-md bg-blue-500/10 flex items-center justify-center">
          <Layers class="h-3.5 w-3.5 text-blue-500" />
        </div>
        <Label id="group-by-heading" class="text-sm font-semibold">{{ t('analytics.groupBy') }}</Label>
      </div>

      <!-- Selected group-by chips -->
      <div v-if="groupByFields.length > 0" class="flex flex-wrap gap-1.5 mb-3">
        <Badge
          v-for="field in groupByFields"
          :key="field"
          variant="secondary"
          class="pl-2.5 pr-1 py-1 gap-1 text-xs font-medium"
        >
          {{ getFieldDisplayName(field) }}
          <button
            @click="removeGroupByField(field)"
            :aria-label="`Remove ${getFieldDisplayName(field)} from group by`"
            class="ml-0.5 rounded-sm hover:bg-muted-foreground/20 p-0.5 transition-colors"
          >
            <X class="h-3 w-3" aria-hidden="true" />
          </button>
        </Badge>
      </div>

      <!-- Add group-by field -->
      <div class="flex items-center gap-2">
        <Select
          v-model="selectedGroupByField"
          :placeholder="t('analytics.selectGroupByField')"
          aria-label="Select group by field"
          class="w-56"
        >
          <option v-for="field in availableGroupByFields" :key="field.name" :value="field.name">
            {{ field.displayName || field.name }}
          </option>
        </Select>
        <Button
          variant="outline"
          size="sm"
          aria-label="Add group by field"
          @click="addGroupByField"
          :disabled="!selectedGroupByField"
        >
          <Plus class="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
          {{ t('analytics.addField') }}
        </Button>
      </div>
    </div>

    <!-- Divider -->
    <div class="border-t" />

    <!-- Aggregation Functions Section -->
    <div role="group" aria-labelledby="aggregations-heading">
      <div class="flex items-center justify-between mb-3">
        <div class="flex items-center gap-2">
          <div class="h-7 w-7 rounded-md bg-emerald-500/10 flex items-center justify-center">
            <Calculator class="h-3.5 w-3.5 text-emerald-500" />
          </div>
          <Label id="aggregations-heading" class="text-sm font-semibold">{{ t('analytics.aggregations') }}</Label>
        </div>
        <Button variant="outline" size="sm" aria-label="Add aggregation" @click="addAggregation">
          <Plus class="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
          {{ t('analytics.addAggregation') }}
        </Button>
      </div>

      <!-- Empty state -->
      <div
        v-if="aggregations.length === 0"
        class="border border-dashed rounded-lg p-6 text-center"
      >
        <Calculator class="h-8 w-8 text-muted-foreground/50 mx-auto mb-2" />
        <p class="text-sm text-muted-foreground">{{ t('analytics.noAggregations') }}</p>
        <Button variant="outline" size="sm" class="mt-3" aria-label="Add aggregation" @click="addAggregation">
          <Plus class="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
          {{ t('analytics.addFirstAggregation') }}
        </Button>
      </div>

      <!-- Aggregation items -->
      <div v-else class="space-y-2.5">
        <div
          v-for="(agg, index) in aggregations"
          :key="agg.id"
          :aria-label="`Aggregation ${index + 1}: ${agg.func}${agg.field ? ` of ${getFieldDisplayName(agg.field)}` : ''}`"
          class="flex flex-wrap items-end gap-3 p-3 rounded-lg border bg-muted/30"
        >
          <!-- Function -->
          <div class="flex flex-col gap-1.5">
            <label
              :for="`agg-func-${agg.id}`"
              class="text-xs font-medium text-muted-foreground uppercase tracking-wider"
            >
              {{ t('analytics.function') }}
            </label>
            <Select
              :id="`agg-func-${agg.id}`"
              :modelValue="agg.func"
              @update:modelValue="(v: string | number) => updateAggregation(agg.id, { func: v as AggregateFunction })"
              class="w-40"
            >
              <option v-for="fn in aggregateFunctions" :key="fn.value" :value="fn.value">
                {{ fn.label }}
              </option>
            </Select>
          </div>

          <!-- Field (hidden for count) -->
          <div v-if="needsField(agg.func)" class="flex flex-col gap-1.5">
            <label
              :for="`agg-field-${agg.id}`"
              class="text-xs font-medium text-muted-foreground uppercase tracking-wider"
            >
              {{ t('analytics.field') }}
            </label>
            <Select
              :id="`agg-field-${agg.id}`"
              :modelValue="agg.field"
              @update:modelValue="(v: string | number) => updateAggregation(agg.id, { field: String(v) })"
              :placeholder="t('analytics.selectField')"
              class="w-48"
            >
              <option
                v-for="field in (agg.func === 'sum' || agg.func === 'avg' ? numericFields : fields)"
                :key="field.name"
                :value="field.name"
              >
                {{ field.displayName || field.name }}
              </option>
            </Select>
          </div>

          <!-- Alias -->
          <div class="flex flex-col gap-1.5">
            <label
              :for="`agg-alias-${agg.id}`"
              class="text-xs font-medium text-muted-foreground uppercase tracking-wider"
            >
              {{ t('analytics.alias') }}
            </label>
            <Input
              :id="`agg-alias-${agg.id}`"
              :modelValue="agg.alias"
              @update:modelValue="(v: string | number) => updateAggregation(agg.id, { alias: String(v) })"
              :placeholder="t('analytics.aliasPlaceholder')"
              class="w-36 h-10"
            />
          </div>

          <!-- Remove -->
          <Button
            variant="ghost"
            size="sm"
            :aria-label="`Remove aggregation ${index + 1}`"
            class="h-10 w-10 p-0 text-muted-foreground hover:text-destructive shrink-0"
            @click="removeAggregation(agg.id)"
          >
            <X class="h-4 w-4" aria-hidden="true" />
          </Button>
        </div>
      </div>
    </div>

    <!-- Divider -->
    <div class="border-t" />

    <!-- Pre-Filter Section (collapsible) -->
    <div>
      <button
        @click="showFilter = !showFilter"
        :aria-expanded="showFilter"
        aria-controls="pre-filter-panel"
        class="flex items-center gap-2 text-sm font-medium text-muted-foreground hover:text-foreground transition-colors w-full"
      >
        <div class="h-7 w-7 rounded-md bg-amber-500/10 flex items-center justify-center">
          <FilterIcon class="h-3.5 w-3.5 text-amber-500" aria-hidden="true" />
        </div>
        <span class="font-semibold text-foreground">{{ t('analytics.preFilter') }}</span>
        <Badge v-if="filterExpression.trim()" variant="default" class="text-xs ml-1">
          {{ t('analytics.active') }}
        </Badge>
        <ChevronDown v-if="!showFilter" class="h-4 w-4 ml-auto" aria-hidden="true" />
        <ChevronUp v-else class="h-4 w-4 ml-auto" aria-hidden="true" />
      </button>
      <div v-if="showFilter" id="pre-filter-panel" class="mt-3 space-y-2">
        <Input
          v-model="filterExpression"
          :placeholder="t('analytics.filterPlaceholder')"
          aria-label="Pre-filter expression"
          class="font-mono text-sm"
        />
        <p class="text-xs text-muted-foreground">
          {{ t('analytics.filterHint') }}
        </p>
      </div>
    </div>

    <!-- Divider -->
    <div class="border-t" />

    <!-- Execute -->
    <div class="flex items-center justify-end">
      <Button
        :disabled="!canExecute || isLoading"
        @click="handleExecute"
        size="lg"
        class="min-w-[160px]"
      >
        <Play class="mr-2 h-4 w-4" />
        {{ t('analytics.runAggregation') }}
      </Button>
    </div>
  </div>
</template>
