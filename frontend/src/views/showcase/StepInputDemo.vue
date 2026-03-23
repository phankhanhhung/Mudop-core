<script setup lang="ts">
import { ref, computed } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Label } from '@/components/ui/label'
import { Input } from '@/components/ui/input'
import StepInput from '@/components/smart/StepInput.vue'
import { ArrowLeft, Hash } from 'lucide-vue-next'
import { useRouter } from 'vue-router'

const router = useRouter()

// ─── Demo 1: Basic ──────────────────────────────────────────────────────

const basicValue = ref(0)

// ─── Demo 2: With min/max ───────────────────────────────────────────────

const boundedValue = ref(50)

// ─── Demo 3: Decimal ────────────────────────────────────────────────────

const decimalValue = ref(9.99)

// ─── Demo 4: Large step ─────────────────────────────────────────────────

const quantityValue = ref(25)

// ─── Demo 5: Sizes ──────────────────────────────────────────────────────

const sizeSmValue = ref(10)
const sizeMdValue = ref(20)
const sizeLgValue = ref(30)

// ─── Demo 6: Disabled / Readonly ────────────────────────────────────────

const disabledValue = ref(42)
const readonlyValue = ref(77)

// ─── Demo 7: Custom display format ─────────────────────────────────────

const currencyValue = ref(1250.00)
const currencyFormat = (v: number) => '$' + v.toFixed(2)

// ─── Demo 8: Validation error ───────────────────────────────────────────

const errorValue = ref(150)
const errorMessage = computed(() =>
  errorValue.value > 100 ? 'Value must not exceed 100' : ''
)

// ─── Demo 9: Interactive playground ─────────────────────────────────────

const playMin = ref(0)
const playMax = ref(100)
const playStep = ref(1)
const playPrecision = ref(0)
const playValue = ref(50)
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
            <Hash class="h-6 w-6" />
            Step Input
          </h1>
          <p class="text-muted-foreground mt-1">
            Numeric input with increment/decrement buttons and keyboard support.
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
            Default StepInput with no constraints. Step is 1, precision is 0.
          </p>
          <div class="flex items-end gap-6">
            <StepInput v-model="basicValue" label="Counter" />
            <div class="text-sm text-muted-foreground pb-1">
              Value: <Badge variant="secondary">{{ basicValue }}</Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 2: With min/max -->
      <Card>
        <CardHeader>
          <CardTitle>Min / Max Bounds</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Clamped between 0 and 100 with step of 5. Buttons disable at boundaries.
            Press Home/End to jump to min/max.
          </p>
          <div class="flex items-end gap-6">
            <StepInput
              v-model="boundedValue"
              :min="0"
              :max="100"
              :step="5"
              label="Percentage"
              description="Value between 0 and 100, step 5"
            />
            <div class="text-sm text-muted-foreground pb-1">
              Value: <Badge variant="secondary">{{ boundedValue }}</Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 3: Decimal -->
      <Card>
        <CardHeader>
          <CardTitle>Decimal Precision</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Step of 0.01 with 2 decimal places. Precision is auto-calculated from the step value.
          </p>
          <div class="flex items-end gap-6">
            <StepInput
              v-model="decimalValue"
              :step="0.01"
              :min="0"
              label="Price"
              required
            />
            <div class="text-sm text-muted-foreground pb-1">
              Value: <Badge variant="secondary">{{ decimalValue }}</Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 4: Large step -->
      <Card>
        <CardHeader>
          <CardTitle>Large Step (Shift+Arrow)</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Step of 1 with largeStep of 10. Hold Shift while pressing Arrow keys to step by 10 instead of 1.
          </p>
          <div class="flex items-end gap-6">
            <StepInput
              v-model="quantityValue"
              :step="1"
              :large-step="10"
              :min="0"
              :max="1000"
              label="Quantity"
              description="Arrow: +/-1, Shift+Arrow: +/-10"
            />
            <div class="text-sm text-muted-foreground pb-1">
              Value: <Badge variant="secondary">{{ quantityValue }}</Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 5: Sizes -->
      <Card>
        <CardHeader>
          <CardTitle>Sizes</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Three sizes: sm, md (default), and lg. Each size adjusts button dimensions, input width, and font size.
          </p>
          <div class="flex items-end gap-8">
            <StepInput v-model="sizeSmValue" size="sm" label="Small" />
            <StepInput v-model="sizeMdValue" size="md" label="Medium" />
            <StepInput v-model="sizeLgValue" size="lg" label="Large" />
          </div>
        </CardContent>
      </Card>

      <!-- Demo 6: Disabled / Readonly -->
      <Card>
        <CardHeader>
          <CardTitle>Disabled and Readonly</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Disabled prevents all interaction. Readonly allows focus but not editing.
          </p>
          <div class="flex items-end gap-8">
            <StepInput v-model="disabledValue" disabled label="Disabled" />
            <StepInput v-model="readonlyValue" readonly label="Readonly" />
          </div>
        </CardContent>
      </Card>

      <!-- Demo 7: Custom display format -->
      <Card>
        <CardHeader>
          <CardTitle>Custom Display Format</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            A custom displayFormat function formats the value as currency. When focused, the raw number is shown for editing.
          </p>
          <div class="flex items-end gap-6">
            <StepInput
              v-model="currencyValue"
              :step="50"
              :min="0"
              :precision="2"
              :display-format="currencyFormat"
              label="Budget"
            />
            <div class="text-sm text-muted-foreground pb-1">
              Raw: <Badge variant="secondary">{{ currencyValue }}</Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 8: Validation error -->
      <Card>
        <CardHeader>
          <CardTitle>Validation Error</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            When an error message is provided, all three parts (buttons and input) show a destructive border.
            Try increasing the value above 100.
          </p>
          <div class="flex items-end gap-6">
            <StepInput
              v-model="errorValue"
              :step="10"
              label="Score"
              :error="errorMessage"
            />
            <div class="text-sm text-muted-foreground pb-1">
              Value: <Badge variant="secondary">{{ errorValue }}</Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 9: Interactive playground -->
      <Card>
        <CardHeader>
          <CardTitle>Interactive Playground</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Adjust min, max, step, and precision to see how the component responds dynamically.
          </p>
          <div class="grid grid-cols-2 md:grid-cols-4 gap-4 mb-6">
            <div class="space-y-1.5">
              <Label>Min</Label>
              <Input
                type="number"
                :model-value="playMin"
                @update:model-value="playMin = Number($event)"
              />
            </div>
            <div class="space-y-1.5">
              <Label>Max</Label>
              <Input
                type="number"
                :model-value="playMax"
                @update:model-value="playMax = Number($event)"
              />
            </div>
            <div class="space-y-1.5">
              <Label>Step</Label>
              <Input
                type="number"
                :model-value="playStep"
                @update:model-value="playStep = Number($event)"
              />
            </div>
            <div class="space-y-1.5">
              <Label>Precision</Label>
              <Input
                type="number"
                :model-value="playPrecision"
                @update:model-value="playPrecision = Number($event)"
              />
            </div>
          </div>

          <div class="flex items-end gap-6">
            <StepInput
              v-model="playValue"
              :min="playMin"
              :max="playMax"
              :step="playStep"
              :precision="playPrecision"
              label="Playground Value"
              :description="`Range: ${playMin} - ${playMax}, Step: ${playStep}, Precision: ${playPrecision}`"
            />
            <div class="text-sm text-muted-foreground pb-1">
              Value: <Badge variant="secondary">{{ playValue }}</Badge>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
