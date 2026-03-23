<script setup lang="ts">
import { ref, computed, nextTick } from 'vue'
import { Label } from '@/components/ui/label'
import type { FieldMetadata } from '@/types/metadata'

interface Props {
  field: FieldMetadata
  modelValue?: string | null
  readonly?: boolean
  maskPattern?: string
}

const props = withDefaults(defineProps<Props>(), {
  readonly: false,
  maskPattern: ''
})

const emit = defineEmits<{
  'update:modelValue': [value: string | null]
}>()

const inputRef = ref<HTMLInputElement | null>(null)

const builtInPatterns: Record<string, string> = {
  phone: '(###) ###-####',
  creditCard: '#### #### #### ####',
  postalCode: '#####',
  ssn: '###-##-####'
}

const resolvedPattern = computed(() => {
  const p = props.maskPattern
  if (!p) return ''
  return builtInPatterns[p] ?? p
})

const placeholder = computed(() => {
  const pattern = resolvedPattern.value
  if (!pattern) return props.field.description ?? ''
  return pattern
    .replace(/#/g, '_')
    .replace(/A/g, '_')
    .replace(/\*/g, '_')
})

function isMaskSlot(char: string): boolean {
  return char === '#' || char === 'A' || char === '*'
}

function matchesSlot(char: string, slot: string): boolean {
  if (slot === '#') return /\d/.test(char)
  if (slot === 'A') return /[a-zA-Z]/.test(char)
  if (slot === '*') return true
  return false
}



function applyMask(raw: string): string {
  const pattern = resolvedPattern.value
  if (!pattern) return raw
  let result = ''
  let rawIdx = 0
  for (let i = 0; i < pattern.length && rawIdx < raw.length; i++) {
    if (isMaskSlot(pattern[i])) {
      if (matchesSlot(raw[rawIdx], pattern[i])) {
        result += raw[rawIdx]
        rawIdx++
      } else {
        // Skip invalid char
        rawIdx++
        i-- // Retry this pattern slot with the next raw char
      }
    } else {
      result += pattern[i]
    }
  }
  return result
}



const displayValue = computed(() => {
  if (!props.modelValue) return ''
  return applyMask(props.modelValue)
})

function handleInput(event: Event) {
  const input = event.target as HTMLInputElement
  const cursorPos = input.selectionStart ?? 0
  const inputValue = input.value
  const pattern = resolvedPattern.value

  if (!pattern) {
    emit('update:modelValue', inputValue === '' ? null : inputValue)
    return
  }

  // Extract only valid chars from user input
  const rawChars: string[] = []
  for (const char of inputValue) {
    // Check if it matches any mask slot type
    if (/\d/.test(char) || /[a-zA-Z]/.test(char)) {
      rawChars.push(char)
    }
  }

  // Filter raw chars to only match their corresponding pattern slots
  const filteredRaw: string[] = []
  let patternIdx = 0
  for (const char of rawChars) {
    // Find next slot in pattern
    while (patternIdx < pattern.length && !isMaskSlot(pattern[patternIdx])) {
      patternIdx++
    }
    if (patternIdx >= pattern.length) break
    if (matchesSlot(char, pattern[patternIdx])) {
      filteredRaw.push(char)
      patternIdx++
    }
  }

  const rawValue = filteredRaw.join('')
  const formatted = applyMask(rawValue)

  emit('update:modelValue', rawValue === '' ? null : rawValue)

  // Correct cursor position
  nextTick(() => {
    if (!inputRef.value) return
    inputRef.value.value = formatted

    // Calculate where the cursor should be based on how many raw chars precede the cursor
    let newCursorPos = formatted.length
    // Count how many raw-char positions are before the original cursor
    let rawCountBeforeCursor = 0
    for (let i = 0; i < cursorPos && i < inputValue.length; i++) {
      const c = inputValue[i]
      if (/\d/.test(c) || /[a-zA-Z]/.test(c)) {
        rawCountBeforeCursor++
      }
    }
    // Map that count back to formatted position
    let rawSeen = 0
    for (let i = 0; i < formatted.length; i++) {
      if (isMaskSlot(pattern[i])) {
        rawSeen++
        if (rawSeen === rawCountBeforeCursor) {
          newCursorPos = i + 1
          // Skip any trailing literals
          while (newCursorPos < formatted.length && newCursorPos < pattern.length && !isMaskSlot(pattern[newCursorPos])) {
            newCursorPos++
          }
          break
        }
      }
    }

    inputRef.value.setSelectionRange(newCursorPos, newCursorPos)
  })
}

function handleKeydown(e: KeyboardEvent) {
  const pattern = resolvedPattern.value
  if (!pattern) return

  // Allow control keys
  if (e.ctrlKey || e.metaKey || e.key === 'Tab' || e.key === 'Escape') return

  // For Backspace, allow default browser behavior + reformat via handleInput
  if (e.key === 'Backspace' || e.key === 'Delete' || e.key === 'ArrowLeft' || e.key === 'ArrowRight') return
}
</script>

<template>
  <div class="space-y-2">
    <Label :for="field.name">
      {{ field.displayName || field.name }}
      <span v-if="field.isRequired" class="text-destructive">*</span>
    </Label>

    <!-- Readonly display -->
    <div
      v-if="readonly || field.isReadOnly"
      class="flex h-10 w-full items-center rounded-md border border-input bg-background px-3 py-2 text-sm opacity-50"
    >
      {{ displayValue || '-' }}
    </div>

    <!-- Editable input -->
    <input
      v-else
      ref="inputRef"
      :id="field.name"
      type="text"
      :value="displayValue"
      :placeholder="placeholder"
      :maxlength="resolvedPattern ? resolvedPattern.length : field.maxLength"
      class="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background file:border-0 file:bg-transparent file:text-sm file:font-medium placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
      @input="handleInput"
      @keydown="handleKeydown"
    />

    <p v-if="field.description" class="text-xs text-muted-foreground">
      {{ field.description }}
    </p>
  </div>
</template>
