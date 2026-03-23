<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { Label } from '@/components/ui/label'
import { formatCurrency } from '@/utils/formatting'
import type { FieldMetadata } from '@/types/metadata'

interface Props {
  field: FieldMetadata
  modelValue?: number | null
  readonly?: boolean
  currencyCode?: string
}

const props = withDefaults(defineProps<Props>(), {
  readonly: false,
  currencyCode: 'USD'
})

const emit = defineEmits<{
  'update:modelValue': [value: number | null]
}>()

const isFocused = ref(false)
const rawInput = ref('')

// Resolve currency symbol for prefix display
const currencySymbol = computed(() => {
  try {
    const parts = new Intl.NumberFormat('en', {
      style: 'currency',
      currency: props.currencyCode,
      currencyDisplay: 'narrowSymbol'
    }).formatToParts(0)
    const symbolPart = parts.find((p) => p.type === 'currency')
    return symbolPart?.value ?? props.currencyCode
  } catch {
    return props.currencyCode
  }
})

// Formatted display value
const formattedValue = computed(() => {
  return formatCurrency(props.modelValue, props.currencyCode)
})

// Step based on field scale
const step = computed(() => {
  if (props.field.scale) {
    return Math.pow(10, -props.field.scale)
  }
  return 0.01
})

function handleFocus(): void {
  isFocused.value = true
  // Show raw number value for editing
  rawInput.value = props.modelValue != null ? String(props.modelValue) : ''
}

function handleBlur(): void {
  isFocused.value = false
  // Parse and emit the final value
  if (rawInput.value === '') {
    emit('update:modelValue', null)
    return
  }
  const num = parseFloat(rawInput.value)
  emit('update:modelValue', isNaN(num) ? null : num)
}

function handleInput(event: Event): void {
  const target = event.target as HTMLInputElement
  rawInput.value = target.value
}

// Keep rawInput in sync when modelValue changes externally while not focused
watch(
  () => props.modelValue,
  (val) => {
    if (!isFocused.value) {
      rawInput.value = val != null ? String(val) : ''
    }
  }
)
</script>

<template>
  <div class="space-y-2">
    <Label :for="field.name">
      {{ field.displayName || field.name }}
      <span v-if="field.isRequired" class="text-destructive">*</span>
    </Label>

    <!-- Readonly display -->
    <p v-if="readonly || field.isReadOnly" class="text-sm py-2">
      {{ formattedValue }}
    </p>

    <!-- Editable input -->
    <div v-else class="relative">
      <!-- Currency symbol prefix -->
      <span
        class="absolute left-3 top-1/2 -translate-y-1/2 text-sm text-muted-foreground pointer-events-none select-none"
      >
        {{ currencySymbol }}
      </span>

      <!-- When focused: raw number input -->
      <input
        v-if="isFocused"
        :id="field.name"
        type="number"
        :value="rawInput"
        :step="step"
        :placeholder="field.description ?? '0.00'"
        :disabled="field.isReadOnly"
        class="flex h-10 w-full rounded-md border border-input bg-background py-2 pr-3 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
        :style="{ paddingLeft: `${currencySymbol.length * 0.6 + 1.2}rem` }"
        @input="handleInput"
        @blur="handleBlur"
      />

      <!-- When blurred: formatted display -->
      <input
        v-else
        :id="field.name"
        type="text"
        :value="modelValue != null ? formattedValue : ''"
        :placeholder="field.description ?? '0.00'"
        :disabled="field.isReadOnly"
        readonly
        class="flex h-10 w-full rounded-md border border-input bg-background py-2 pr-3 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50 cursor-text"
        :style="{ paddingLeft: `${currencySymbol.length * 0.6 + 1.2}rem` }"
        @focus="handleFocus"
      />
    </div>

    <p v-if="field.description" class="text-xs text-muted-foreground">
      {{ field.description }}
    </p>
  </div>
</template>
