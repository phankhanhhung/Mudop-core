<script setup lang="ts">
import { ref, computed, nextTick } from 'vue'
import { cn } from '@/lib/utils'
import { Badge } from '@/components/ui/badge'
import { Label } from '@/components/ui/label'
import { X } from 'lucide-vue-next'

interface Props {
  modelValue?: string[]
  placeholder?: string
  disabled?: boolean
  readonly?: boolean
  maxTokens?: number
  allowDuplicates?: boolean
  label?: string
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  modelValue: () => [],
  placeholder: 'Type and press Enter',
  disabled: false,
  readonly: false,
  allowDuplicates: false,
})

const emit = defineEmits<{
  'update:modelValue': [value: string[]]
  'token-add': [value: string]
  'token-remove': [value: string]
}>()

// ── Internal state ──────────────────────────────────────────────────────

const inputValue = ref('')
const inputRef = ref<HTMLInputElement | null>(null)
const focusedTokenIndex = ref<number | null>(null)

// ── Computed ────────────────────────────────────────────────────────────

const tokens = computed(() => props.modelValue ?? [])

const isAtLimit = computed(() =>
  props.maxTokens != null && tokens.value.length >= props.maxTokens
)

const isInteractive = computed(() => !props.disabled && !props.readonly)

// ── Token operations ────────────────────────────────────────────────────

function addToken(raw: string) {
  const value = raw.trim()
  if (!value) return
  if (isAtLimit.value) return

  if (!props.allowDuplicates) {
    const lower = value.toLowerCase()
    if (tokens.value.some(t => t.toLowerCase() === lower)) {
      inputValue.value = ''
      return
    }
  }

  const updated = [...tokens.value, value]
  emit('update:modelValue', updated)
  emit('token-add', value)
  inputValue.value = ''
}

function removeToken(index: number) {
  if (!isInteractive.value) return
  const removed = tokens.value[index]
  const updated = tokens.value.filter((_, i) => i !== index)
  emit('update:modelValue', updated)
  emit('token-remove', removed)

  // Reset focused token index
  if (focusedTokenIndex.value !== null) {
    if (focusedTokenIndex.value >= updated.length) {
      focusedTokenIndex.value = updated.length > 0 ? updated.length - 1 : null
    }
  }

  nextTick(() => inputRef.value?.focus())
}

// ── Input event handlers ────────────────────────────────────────────────

function onInputKeydown(event: KeyboardEvent) {
  if (!isInteractive.value) return

  if (event.key === 'Enter' || event.key === ',') {
    event.preventDefault()
    const raw = event.key === ',' ? inputValue.value : inputValue.value
    addToken(raw)
    return
  }

  if (event.key === 'Backspace' && inputValue.value === '' && tokens.value.length > 0) {
    event.preventDefault()
    // Focus the last token
    focusedTokenIndex.value = tokens.value.length - 1
    nextTick(() => {
      const tokenEls = document.querySelectorAll('[data-token-item]')
      const last = tokenEls[tokenEls.length - 1] as HTMLElement
      last?.focus()
    })
    return
  }

  if (event.key === 'ArrowLeft' && inputValue.value === '' && tokens.value.length > 0) {
    event.preventDefault()
    focusedTokenIndex.value = tokens.value.length - 1
    nextTick(() => {
      const tokenEls = document.querySelectorAll('[data-token-item]')
      const last = tokenEls[tokenEls.length - 1] as HTMLElement
      last?.focus()
    })
  }
}

function onTokenKeydown(event: KeyboardEvent, index: number) {
  if (!isInteractive.value) return

  if (event.key === 'Backspace' || event.key === 'Delete') {
    event.preventDefault()
    removeToken(index)
    return
  }

  if (event.key === 'ArrowLeft') {
    event.preventDefault()
    if (index > 0) {
      focusedTokenIndex.value = index - 1
      nextTick(() => {
        const tokenEls = document.querySelectorAll('[data-token-item]')
        ;(tokenEls[index - 1] as HTMLElement)?.focus()
      })
    }
    return
  }

  if (event.key === 'ArrowRight') {
    event.preventDefault()
    if (index < tokens.value.length - 1) {
      focusedTokenIndex.value = index + 1
      nextTick(() => {
        const tokenEls = document.querySelectorAll('[data-token-item]')
        ;(tokenEls[index + 1] as HTMLElement)?.focus()
      })
    } else {
      // Move focus back to input
      focusedTokenIndex.value = null
      nextTick(() => inputRef.value?.focus())
    }
    return
  }
}

// ── Container click ─────────────────────────────────────────────────────

function onContainerClick() {
  if (isInteractive.value) {
    focusedTokenIndex.value = null
    inputRef.value?.focus()
  }
}
</script>

<template>
  <div :class="cn('space-y-1.5', props.class)">
    <Label v-if="label">{{ label }}</Label>

    <div
      :class="cn(
        'flex flex-wrap items-center gap-1.5 rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background transition-colors',
        'focus-within:ring-2 focus-within:ring-ring focus-within:ring-offset-2',
        disabled && 'cursor-not-allowed opacity-50',
        readonly && 'cursor-default',
        !disabled && !readonly && 'cursor-text',
      )"
      @click="onContainerClick"
    >
      <!-- Token list -->
      <div role="listbox" :aria-label="label || 'Tokens'" class="contents">
        <Badge
          v-for="(token, index) in tokens"
          :key="`${token}-${index}`"
          variant="secondary"
          role="option"
          :aria-selected="focusedTokenIndex === index"
          :tabindex="isInteractive ? 0 : -1"
          data-token-item
          :class="cn(
            'gap-1 pr-1 select-none',
            focusedTokenIndex === index && 'ring-2 ring-ring ring-offset-1',
            isInteractive && 'cursor-pointer',
          )"
          @keydown="onTokenKeydown($event, index)"
          @focus="focusedTokenIndex = index"
          @blur="focusedTokenIndex = null"
        >
          {{ token }}
          <button
            v-if="isInteractive"
            type="button"
            :aria-label="`Remove ${token}`"
            class="ml-0.5 rounded-full p-0.5 hover:bg-muted-foreground/20 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
            tabindex="-1"
            @click.stop="removeToken(index)"
          >
            <X class="h-3 w-3" />
          </button>
        </Badge>
      </div>

      <!-- Input -->
      <input
        v-if="!isAtLimit && !readonly"
        ref="inputRef"
        v-model="inputValue"
        type="text"
        :placeholder="tokens.length === 0 ? placeholder : ''"
        :disabled="disabled"
        class="flex-1 min-w-[80px] bg-transparent outline-none placeholder:text-muted-foreground disabled:cursor-not-allowed"
        @keydown="onInputKeydown"
        @focus="focusedTokenIndex = null"
      />
    </div>
  </div>
</template>
