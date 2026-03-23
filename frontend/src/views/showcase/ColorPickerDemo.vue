<script setup lang="ts">
import { ref } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import ColorPicker from '@/components/smart/ColorPicker.vue'
import { ArrowLeft, Palette } from 'lucide-vue-next'
import { useRouter } from 'vue-router'

const router = useRouter()

// ─── Demo 1: Basic ──────────────────────────────────────────────────────

const basicColor = ref('#3b82f6')

// ─── Demo 2: With RGB inputs ────────────────────────────────────────────

const rgbColor = ref('#22c55e')

// ─── Demo 3: Custom presets ─────────────────────────────────────────────

const brandColor = ref('#1e40af')
const brandPresets = [
  '#1e40af', '#1e3a8a', '#1d4ed8', '#2563eb', '#3b82f6',
  '#60a5fa', '#93c5fd', '#bfdbfe', '#dbeafe', '#eff6ff',
  '#0f172a', '#1e293b', '#334155', '#475569', '#64748b',
  '#94a3b8', '#cbd5e1', '#f8fafc',
]

// ─── Demo 4: With label ─────────────────────────────────────────────────

const bgColor = ref('#f0f9ff')
const textColor = ref('#0f172a')

// ─── Demo 5: Disabled ───────────────────────────────────────────────────

const disabledColor = ref('#6366f1')

// ─── Demo 6: Live preview ───────────────────────────────────────────────

const previewBg = ref('#1e293b')
const previewText = ref('#f8fafc')
const previewAccent = ref('#3b82f6')
</script>

<template>
  <DefaultLayout>
    <div class="space-y-8">
      <!-- Header -->
      <div class="flex items-center gap-4">
        <Button variant="ghost" size="icon" @click="router.push('/showcase')">
          <ArrowLeft class="h-5 w-5" />
        </Button>
        <div>
          <h1 class="text-2xl font-bold tracking-tight flex items-center gap-2">
            <Palette class="h-6 w-6" />
            Color Picker
          </h1>
          <p class="text-muted-foreground mt-1">
            Color selection with SV area, hue slider, hex/RGB inputs, and presets.
          </p>
        </div>
      </div>

      <!-- Demo 1: Basic -->
      <Card>
        <CardHeader>
          <CardTitle>Basic</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Color picker with default preset palette. Click the trigger to open the popover
            with a saturation-value area, hue slider, hex input, and preset colors.
          </p>
          <div class="flex items-end gap-6">
            <ColorPicker v-model="basicColor" />
            <div class="text-sm text-muted-foreground pb-1">
              Value: <Badge variant="secondary" class="font-mono">{{ basicColor }}</Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 2: With RGB inputs -->
      <Card>
        <CardHeader>
          <CardTitle>With RGB Inputs</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Enable <code class="text-xs bg-muted px-1 py-0.5 rounded">showRgb</code> to display
            individual R, G, B number inputs alongside the hex input. All representations stay in sync.
          </p>
          <div class="flex items-end gap-6">
            <ColorPicker v-model="rgbColor" show-rgb />
            <div class="text-sm text-muted-foreground pb-1">
              Value: <Badge variant="secondary" class="font-mono">{{ rgbColor }}</Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 3: Custom presets -->
      <Card>
        <CardHeader>
          <CardTitle>Custom Presets</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Pass a custom <code class="text-xs bg-muted px-1 py-0.5 rounded">presetColors</code>
            array to show brand-specific colors. This example uses a blue/slate palette.
          </p>
          <div class="flex items-end gap-6">
            <ColorPicker v-model="brandColor" :preset-colors="brandPresets" />
            <div class="text-sm text-muted-foreground pb-1">
              Value: <Badge variant="secondary" class="font-mono">{{ brandColor }}</Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 4: With label -->
      <Card>
        <CardHeader>
          <CardTitle>With Label</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Use the <code class="text-xs bg-muted px-1 py-0.5 rounded">label</code> prop for
            accessible labeling. Multiple pickers can be used side by side.
          </p>
          <div class="flex items-end gap-8">
            <ColorPicker v-model="bgColor" label="Background Color" />
            <ColorPicker v-model="textColor" label="Text Color" />
          </div>
        </CardContent>
      </Card>

      <!-- Demo 5: Disabled -->
      <Card>
        <CardHeader>
          <CardTitle>Disabled</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Disabled state prevents opening the popover. The trigger appears grayed out.
          </p>
          <div class="flex items-end gap-6">
            <ColorPicker v-model="disabledColor" disabled label="Locked Color" />
            <div class="text-sm text-muted-foreground pb-1">
              Value: <Badge variant="secondary" class="font-mono">{{ disabledColor }}</Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 6: Live preview -->
      <Card>
        <CardHeader>
          <CardTitle>Live Preview</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Three color pickers control a live card preview. Change background, text, and accent
            colors to see the result in real-time.
          </p>
          <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
            <!-- Controls -->
            <div class="space-y-4">
              <ColorPicker v-model="previewBg" label="Background" show-rgb />
              <ColorPicker v-model="previewText" label="Text" show-rgb />
              <ColorPicker v-model="previewAccent" label="Accent" show-rgb />
            </div>
            <!-- Preview card -->
            <div
              class="rounded-lg border p-6 transition-colors"
              :style="{ backgroundColor: previewBg, color: previewText }"
            >
              <h3 class="text-lg font-semibold mb-2">Preview Card</h3>
              <p class="text-sm opacity-80 mb-4">
                This card updates in real-time as you change the color pickers on the left.
                The background, text, and accent colors are all configurable.
              </p>
              <div class="flex gap-2">
                <span
                  class="inline-flex items-center px-3 py-1 rounded-full text-sm font-medium text-white"
                  :style="{ backgroundColor: previewAccent }"
                >
                  Accent Button
                </span>
                <span
                  class="inline-flex items-center px-3 py-1 rounded-full text-sm font-medium border"
                  :style="{ borderColor: previewAccent, color: previewAccent }"
                >
                  Outline Button
                </span>
              </div>
              <div class="mt-4 pt-4 border-t text-xs font-mono opacity-60" :style="{ borderColor: previewAccent }">
                bg: {{ previewBg }} / text: {{ previewText }} / accent: {{ previewAccent }}
              </div>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
