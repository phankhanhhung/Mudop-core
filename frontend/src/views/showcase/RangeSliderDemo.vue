<script setup lang="ts">
import { ref } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import RangeSlider from '@/components/smart/RangeSlider.vue'
import { ArrowLeft, SlidersHorizontal } from 'lucide-vue-next'
import { useRouter } from 'vue-router'

const router = useRouter()

// ─── Demo 1: Basic ──────────────────────────────────────────────────────

const basicValue = ref(50)

// ─── Demo 2: Temperature with min/max labels ────────────────────────────

const tempValue = ref(22)

// ─── Demo 3: Range (dual thumb) ─────────────────────────────────────────

const priceRange = ref<[number, number]>([200, 800])

// ─── Demo 4: With ticks ─────────────────────────────────────────────────

const ratingValue = ref(7)

// ─── Demo 5: Custom format (percentage) ─────────────────────────────────

const percentValue = ref(65)
const percentFormat = (v: number) => v + '%'

// ─── Demo 6: Disabled ───────────────────────────────────────────────────

const disabledValue = ref(40)

// ─── Demo 7: Fine control (decimal) ─────────────────────────────────────

const decimalValue = ref(0.5)
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
            <SlidersHorizontal class="h-6 w-6" />
            Range Slider
          </h1>
          <p class="text-muted-foreground mt-1">
            Single and dual-thumb slider with drag, keyboard, ticks, and tooltips.
          </p>
        </div>
      </div>

      <!-- Demo 1: Basic -->
      <Card>
        <CardHeader>
          <CardTitle>Basic Slider</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Default slider from 0 to 100 with step 1. Click the track or drag the thumb. Tooltip appears on drag.
          </p>
          <div class="max-w-md space-y-2">
            <RangeSlider
              v-model="basicValue"
              label="Volume"
            />
            <div class="text-sm text-muted-foreground">
              Value: <Badge variant="secondary">{{ basicValue }}</Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 2: Temperature with min/max labels -->
      <Card>
        <CardHeader>
          <CardTitle>Min / Max Labels</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Temperature selector from -20 to 50 with min/max labels shown. Custom format appends the degree symbol.
          </p>
          <div class="max-w-md space-y-2">
            <RangeSlider
              v-model="tempValue"
              :min="-20"
              :max="50"
              show-min-max
              label="Temperature"
              :format-value="(v: number) => v + '\u00B0C'"
            />
            <div class="text-sm text-muted-foreground">
              Value: <Badge variant="secondary">{{ tempValue }}&deg;C</Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 3: Range (dual thumb) -->
      <Card>
        <CardHeader>
          <CardTitle>Range Slider (Dual Thumb)</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Pass a two-element array as modelValue to activate range mode. Thumbs cannot cross each other.
            Step is $10, range $0 to $1000.
          </p>
          <div class="max-w-md space-y-2">
            <RangeSlider
              v-model="priceRange"
              :min="0"
              :max="1000"
              :step="10"
              show-min-max
              label="Price Range"
              :format-value="(v: number) => '$' + v"
            />
            <div class="text-sm text-muted-foreground">
              Range: <Badge variant="secondary">${{ priceRange[0] }}</Badge>
              &ndash;
              <Badge variant="secondary">${{ priceRange[1] }}</Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 4: With ticks -->
      <Card>
        <CardHeader>
          <CardTitle>Tick Marks</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Rating from 1 to 10 with tick marks at each step. Keyboard: Arrow keys step by 1, Shift+Arrow by 10, Home/End jump to min/max.
          </p>
          <div class="max-w-md space-y-2">
            <RangeSlider
              v-model="ratingValue"
              :min="1"
              :max="10"
              :step="1"
              show-ticks
              show-min-max
              label="Rating"
            />
            <div class="text-sm text-muted-foreground">
              Value: <Badge variant="secondary">{{ ratingValue }}</Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 5: Custom format (percentage) -->
      <Card>
        <CardHeader>
          <CardTitle>Custom Format</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            A custom formatValue function formats the displayed value as a percentage. Applied to tooltip, value display, and min/max labels.
          </p>
          <div class="max-w-md space-y-2">
            <RangeSlider
              v-model="percentValue"
              :min="0"
              :max="100"
              :step="5"
              show-min-max
              label="Progress"
              :format-value="percentFormat"
            />
            <div class="text-sm text-muted-foreground">
              Value: <Badge variant="secondary">{{ percentValue }}%</Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 6: Disabled -->
      <Card>
        <CardHeader>
          <CardTitle>Disabled</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            A disabled slider prevents all interaction: drag, track click, and keyboard are all disabled.
          </p>
          <div class="max-w-md space-y-2">
            <RangeSlider
              v-model="disabledValue"
              disabled
              label="Disabled Slider"
            />
            <div class="text-sm text-muted-foreground">
              Value: <Badge variant="secondary">{{ disabledValue }}</Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 7: Fine control (decimal) -->
      <Card>
        <CardHeader>
          <CardTitle>Fine Control (Decimal)</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Step 0.01 over the range 0.0 to 1.0. Demonstrates precise decimal snapping without floating-point artifacts.
          </p>
          <div class="max-w-md space-y-2">
            <RangeSlider
              v-model="decimalValue"
              :min="0"
              :max="1"
              :step="0.01"
              show-min-max
              label="Opacity"
            />
            <div class="text-sm text-muted-foreground">
              Value: <Badge variant="secondary">{{ decimalValue }}</Badge>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
