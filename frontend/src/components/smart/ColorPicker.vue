<script setup lang="ts">
import { ref, watch, onBeforeUnmount } from 'vue'
import { cn } from '@/lib/utils'
import { Label } from '@/components/ui/label'
import { Input } from '@/components/ui/input'
import { ChevronDown } from 'lucide-vue-next'
import { PopoverRoot, PopoverTrigger, PopoverContent, PopoverPortal } from 'radix-vue'
import { useColorPicker } from '@/composables/useColorPicker'

interface Props {
  modelValue?: string // hex color like '#3b82f6'
  presetColors?: string[] // palette of preset colors
  showInput?: boolean // show hex input (default true)
  showRgb?: boolean // show RGB inputs (default false)
  disabled?: boolean
  label?: string
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  modelValue: '#000000',
  presetColors: () => [
    '#ef4444', '#f97316', '#f59e0b', '#eab308', '#84cc16', '#22c55e',
    '#14b8a6', '#06b6d4', '#3b82f6', '#6366f1', '#8b5cf6', '#a855f7',
    '#d946ef', '#ec4899', '#f43f5e', '#000000', '#6b7280', '#ffffff',
  ],
  showInput: true,
  showRgb: false,
  disabled: false,
})

const emit = defineEmits<{
  'update:modelValue': [color: string]
  'change': [color: string]
}>()

// ── Color state ───────────────────────────────────────────────────────────

const { hex, rgb, hsv, setHex, setRGB, setHSV } = useColorPicker({
  initialColor: props.modelValue,
})

const hexInput = ref(props.modelValue)

// Sync from parent prop into composable
watch(() => props.modelValue, (val) => {
  if (val && val.toLowerCase() !== hex.value.toLowerCase()) {
    setHex(val)
    hexInput.value = hex.value
  }
})

// Sync composable state out to parent
watch(hex, (val) => {
  hexInput.value = val
  emit('update:modelValue', val)
  emit('change', val)
})

// ── Hex input handling ────────────────────────────────────────────────────

function onHexInput() {
  const raw = hexInput.value ?? ''
  const cleaned = raw.startsWith('#') ? raw : '#' + raw
  if (/^#[0-9a-fA-F]{3}([0-9a-fA-F]{3})?$/.test(cleaned)) {
    setHex(cleaned)
  }
}

// ── Preset selection ──────────────────────────────────────────────────────

function selectPreset(color: string) {
  setHex(color)
}

// ── SV area drag ──────────────────────────────────────────────────────────

const svAreaRef = ref<HTMLDivElement | null>(null)
let isDraggingSv = false

function updateSv(clientX: number, clientY: number) {
  const el = svAreaRef.value
  if (!el) return
  const rect = el.getBoundingClientRect()
  const x = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width))
  const y = Math.max(0, Math.min(1, (clientY - rect.top) / rect.height))
  setHSV(hsv.value.h, Math.round(x * 100), Math.round((1 - y) * 100))
}

function onSvMouseDown(e: MouseEvent) {
  isDraggingSv = true
  updateSv(e.clientX, e.clientY)
  document.addEventListener('mousemove', onSvMouseMove)
  document.addEventListener('mouseup', onSvMouseUp)
}

function onSvMouseMove(e: MouseEvent) {
  if (!isDraggingSv) return
  e.preventDefault()
  updateSv(e.clientX, e.clientY)
}

function onSvMouseUp() {
  isDraggingSv = false
  document.removeEventListener('mousemove', onSvMouseMove)
  document.removeEventListener('mouseup', onSvMouseUp)
}

// ── Hue slider drag ──────────────────────────────────────────────────────

const hueRef = ref<HTMLDivElement | null>(null)
let isDraggingHue = false

function updateHue(clientX: number) {
  const el = hueRef.value
  if (!el) return
  const rect = el.getBoundingClientRect()
  const x = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width))
  setHSV(Math.round(x * 360), hsv.value.s, hsv.value.v)
}

function onHueMouseDown(e: MouseEvent) {
  isDraggingHue = true
  updateHue(e.clientX)
  document.addEventListener('mousemove', onHueMouseMove)
  document.addEventListener('mouseup', onHueMouseUp)
}

function onHueMouseMove(e: MouseEvent) {
  if (!isDraggingHue) return
  e.preventDefault()
  updateHue(e.clientX)
}

function onHueMouseUp() {
  isDraggingHue = false
  document.removeEventListener('mousemove', onHueMouseMove)
  document.removeEventListener('mouseup', onHueMouseUp)
}

// ── Cleanup ───────────────────────────────────────────────────────────────

onBeforeUnmount(() => {
  document.removeEventListener('mousemove', onSvMouseMove)
  document.removeEventListener('mouseup', onSvMouseUp)
  document.removeEventListener('mousemove', onHueMouseMove)
  document.removeEventListener('mouseup', onHueMouseUp)
})
</script>

<template>
  <div :class="cn('color-picker-root', props.class)">
    <Label v-if="label" class="mb-1.5 block">{{ label }}</Label>

    <PopoverRoot>
      <PopoverTrigger as-child>
        <button
          class="flex items-center gap-2 h-9 px-3 border rounded-md bg-background hover:bg-muted/50 transition-colors disabled:cursor-not-allowed disabled:opacity-50"
          :disabled="disabled"
        >
          <!-- Color swatch -->
          <div
            class="w-6 h-6 rounded border"
            :style="{ backgroundColor: modelValue || '#000000' }"
          />
          <span class="text-sm font-mono">{{ modelValue || '#000000' }}</span>
          <ChevronDown class="h-4 w-4 ml-auto text-muted-foreground" />
        </button>
      </PopoverTrigger>

      <PopoverPortal>
        <PopoverContent
          class="w-64 p-3 space-y-3 rounded-md border bg-popover text-popover-foreground shadow-md z-50"
          side="bottom"
          align="start"
          :side-offset="4"
        >
          <!-- SV (Saturation-Value) picker area -->
          <div
            ref="svAreaRef"
            class="relative w-full h-40 rounded cursor-crosshair select-none"
            :style="{
              background: `linear-gradient(to top, #000, transparent), linear-gradient(to right, #fff, hsl(${hsv.h}, 100%, 50%))`,
            }"
            @mousedown="onSvMouseDown"
          >
            <!-- Picker circle -->
            <div
              class="absolute w-4 h-4 rounded-full border-2 border-white shadow -translate-x-1/2 -translate-y-1/2 pointer-events-none"
              :style="{
                left: hsv.s + '%',
                top: (100 - hsv.v) + '%',
              }"
            />
          </div>

          <!-- Hue slider -->
          <div
            ref="hueRef"
            class="relative h-3 rounded-full cursor-pointer select-none"
            style="background: linear-gradient(to right, #f00, #ff0, #0f0, #0ff, #00f, #f0f, #f00)"
            @mousedown="onHueMouseDown"
          >
            <div
              class="absolute top-1/2 -translate-y-1/2 -translate-x-1/2 w-4 h-4 rounded-full border-2 border-white shadow pointer-events-none"
              :style="{ left: (hsv.h / 360) * 100 + '%' }"
            />
          </div>

          <!-- Preview + Hex input -->
          <div v-if="showInput" class="flex gap-2 items-center">
            <div
              class="w-9 h-9 rounded border flex-shrink-0"
              :style="{ backgroundColor: hex }"
            />
            <Input
              v-model="hexInput"
              class="font-mono text-sm"
              placeholder="#000000"
              @change="onHexInput"
              @keydown.enter="onHexInput"
            />
          </div>

          <!-- RGB inputs -->
          <div v-if="showRgb" class="grid grid-cols-3 gap-2">
            <div>
              <Label class="text-xs">R</Label>
              <Input
                type="number"
                :model-value="rgb.r"
                :min="0"
                :max="255"
                class="text-sm"
                @update:model-value="(v: string | number) => setRGB(Number(v), rgb.g, rgb.b)"
              />
            </div>
            <div>
              <Label class="text-xs">G</Label>
              <Input
                type="number"
                :model-value="rgb.g"
                :min="0"
                :max="255"
                class="text-sm"
                @update:model-value="(v: string | number) => setRGB(rgb.r, Number(v), rgb.b)"
              />
            </div>
            <div>
              <Label class="text-xs">B</Label>
              <Input
                type="number"
                :model-value="rgb.b"
                :min="0"
                :max="255"
                class="text-sm"
                @update:model-value="(v: string | number) => setRGB(rgb.r, rgb.g, Number(v))"
              />
            </div>
          </div>

          <!-- Preset colors -->
          <div v-if="presetColors?.length" class="grid grid-cols-9 gap-1">
            <button
              v-for="color in presetColors"
              :key="color"
              class="w-6 h-6 rounded border hover:scale-110 transition-transform"
              :class="{ 'ring-2 ring-ring ring-offset-1': color.toLowerCase() === hex.toLowerCase() }"
              :style="{ backgroundColor: color }"
              @click="selectPreset(color)"
            />
          </div>
        </PopoverContent>
      </PopoverPortal>
    </PopoverRoot>
  </div>
</template>
