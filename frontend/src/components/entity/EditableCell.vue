<script setup lang="ts">
import { ref, nextTick, watch } from 'vue'
import { Pencil } from 'lucide-vue-next'
import { Spinner } from '@/components/ui/spinner'
import type { FieldType, EnumValue } from '@/types/metadata'

interface Props {
  value: unknown
  fieldType: FieldType
  fieldName: string
  isEditing: boolean
  isSaving?: boolean
  error?: string | null
  enumValues?: EnumValue[]
  maxLength?: number
  isReadOnly?: boolean
  isComputed?: boolean
  displayValue?: string
}

const props = withDefaults(defineProps<Props>(), {
  isSaving: false,
  error: null,
  enumValues: () => [],
  maxLength: undefined,
  isReadOnly: false,
  isComputed: false,
  displayValue: undefined
})

const emit = defineEmits<{
  'start-edit': []
  'update-value': [value: unknown]
  'commit': []
  'cancel': []
}>()

const inputRef = ref<HTMLInputElement | HTMLSelectElement | null>(null)

const canEdit = !props.isReadOnly && !props.isComputed

// Auto-focus input when entering edit mode
watch(
  () => props.isEditing,
  async (editing) => {
    if (editing) {
      await nextTick()
      inputRef.value?.focus()
    }
  }
)

function handleDoubleClick() {
  if (!canEdit) return
  emit('start-edit')
}

function handleInput(event: Event) {
  const target = event.target as HTMLInputElement | HTMLSelectElement

  let newValue: unknown

  switch (props.fieldType) {
    case 'Boolean':
      newValue = (target as HTMLInputElement).checked
      break
    case 'Integer':
      newValue = target.value === '' ? null : parseInt(target.value, 10)
      break
    case 'Decimal':
      newValue = target.value === '' ? null : parseFloat(target.value)
      break
    default:
      newValue = target.value === '' ? null : target.value
      break
  }

  emit('update-value', newValue)
}

function handleKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter') {
    event.preventDefault()
    emit('commit')
  } else if (event.key === 'Escape') {
    event.preventDefault()
    emit('cancel')
  }
}

function handleBlur() {
  // Small delay to allow click events on other elements to fire first
  setTimeout(() => {
    emit('commit')
  }, 150)
}

function getInputType(): string {
  switch (props.fieldType) {
    case 'Integer':
      return 'number'
    case 'Decimal':
      return 'number'
    case 'Date':
      return 'date'
    case 'Time':
      return 'time'
    case 'DateTime':
    case 'Timestamp':
      return 'datetime-local'
    default:
      return 'text'
  }
}

function getInputStep(): string | undefined {
  switch (props.fieldType) {
    case 'Integer':
      return '1'
    case 'Decimal':
      return '0.01'
    default:
      return undefined
  }
}

function formatValueForInput(val: unknown): string {
  if (val === null || val === undefined) return ''
  return String(val)
}

function getDisplayText(): string {
  if (props.displayValue !== undefined) return props.displayValue
  if (props.value === null || props.value === undefined) return ''
  if (props.fieldType === 'Boolean') return props.value ? 'Yes' : 'No'
  return String(props.value)
}
</script>

<template>
  <div
    class="relative group min-h-[2rem] flex items-center"
    :class="{
      'cursor-pointer': canEdit && !isEditing,
      'cursor-default': !canEdit
    }"
    @dblclick="handleDoubleClick"
  >
    <!-- Display mode -->
    <template v-if="!isEditing">
      <span class="truncate text-sm">{{ getDisplayText() }}</span>
      <Pencil
        v-if="canEdit"
        class="ml-1 h-3 w-3 text-muted-foreground opacity-0 group-hover:opacity-100 transition-opacity flex-shrink-0"
      />
    </template>

    <!-- Edit mode -->
    <template v-else>
      <div class="w-full relative">
        <!-- Boolean: checkbox -->
        <template v-if="fieldType === 'Boolean'">
          <input
            ref="inputRef"
            type="checkbox"
            :checked="!!value"
            class="h-4 w-4 rounded border-input ring-offset-background focus:ring-2 focus:ring-ring focus:ring-offset-2"
            @change="handleInput"
            @keydown="handleKeydown"
            @blur="handleBlur"
          />
        </template>

        <!-- Enum: select -->
        <template v-else-if="fieldType === 'Enum' && enumValues && enumValues.length > 0">
          <select
            ref="inputRef"
            :value="formatValueForInput(value)"
            class="w-full rounded-md border border-input bg-background px-2 py-1 text-sm ring-2 ring-primary ring-offset-1 focus:outline-none"
            @change="handleInput"
            @keydown="handleKeydown"
            @blur="handleBlur"
          >
            <option value="">-- Select --</option>
            <option
              v-for="opt in enumValues"
              :key="String(opt.value)"
              :value="String(opt.value)"
            >
              {{ opt.displayName || opt.name }}
            </option>
          </select>
        </template>

        <!-- UUID: read-only text input -->
        <template v-else-if="fieldType === 'UUID'">
          <input
            ref="inputRef"
            type="text"
            :value="formatValueForInput(value)"
            readonly
            class="w-full rounded-md border border-input bg-muted px-2 py-1 text-sm ring-2 ring-primary ring-offset-1 focus:outline-none cursor-not-allowed"
            @keydown="handleKeydown"
            @blur="handleBlur"
          />
        </template>

        <!-- All other types -->
        <template v-else>
          <input
            ref="inputRef"
            :type="getInputType()"
            :step="getInputStep()"
            :value="formatValueForInput(value)"
            :maxlength="maxLength"
            class="w-full rounded-md border border-input bg-background px-2 py-1 text-sm ring-2 ring-primary ring-offset-1 focus:outline-none"
            @input="handleInput"
            @keydown="handleKeydown"
            @blur="handleBlur"
          />
        </template>

        <!-- Saving overlay -->
        <div
          v-if="isSaving"
          class="absolute inset-0 flex items-center justify-center bg-background/60 rounded-md"
        >
          <Spinner size="sm" />
        </div>
      </div>

      <!-- Error message -->
      <p v-if="error" class="absolute top-full left-0 mt-1 text-xs text-destructive whitespace-nowrap z-10">
        {{ error }}
      </p>
    </template>
  </div>
</template>
